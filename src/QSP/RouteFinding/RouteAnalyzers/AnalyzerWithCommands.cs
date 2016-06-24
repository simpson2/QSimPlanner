﻿using QSP.AviationTools.Coordinates;
using QSP.RouteFinding.Airports;
using QSP.RouteFinding.AirwayStructure;
using QSP.RouteFinding.Containers;
using QSP.RouteFinding.RouteAnalyzers.Extractors;
using QSP.RouteFinding.Routes;
using QSP.RouteFinding.TerminalProcedures.Sid;
using QSP.RouteFinding.TerminalProcedures.Star;
using System.Collections.Generic;
using System.Linq;

namespace QSP.RouteFinding.RouteAnalyzers
{
    // The accepted formats are similar to those of StandardRouteAnalyzer,
    // except:
    //
    // (1) The first (or the one right after Origin), last (or the one 
    //     right before Dest.), or any entry between two waypoints, 
    //     can be "AUTO" or "RAND".
    //
    // (2) "AUTO" finds the shortest route between the specified waypoints.
    //     If it's the first entry, then a route between departure runway 
    //     and first waypoint is found. The case for last entry is similar.
    //
    // (3) Similarly, "RAND" finds a random route.
    //

    public class AnalyzerWithCommands
    {
        private WaypointList wptList;
        private AirportManager airportList;
        private SidCollection sids;
        private StarCollection stars;

        private string origIcao;
        private string origRwy;
        private string destIcao;
        private string destRwy;
        private string[] route;

        private Waypoint origRwyWpt;
        private Waypoint destRwyWpt;

        public AnalyzerWithCommands(
            string[] route,
            string origIcao,
            string origRwy,
            string destIcao,
            string destRwy,
            AirportManager airportList,
            WaypointList wptList,
            SidCollection sids,
            StarCollection stars)
        {
            this.route = route;
            this.origIcao = origIcao;
            this.origRwy = origRwy;
            this.destIcao = destIcao;
            this.destRwy = destRwy;
            this.airportList = airportList;
            this.wptList = wptList;
            this.sids = sids;
            this.stars = stars;
        }

        public Route Analyze()
        {
            SetRwyWpts();
            var subRoutes = SplitEntries(route);
            var analyzed = ComputeRoutes(subRoutes);
            FillCommands(subRoutes, analyzed);
            return ConnectAll(analyzed);
        }

        private static List<List<string>> SplitEntries(string[] route)
        {
            var subRoutes = new List<List<string>>();
            var tmp = new List<string>();

            foreach (var i in route)
            {
                if (i == "AUTO" || i == "RAND")
                {
                    AddIfNonEmpty(subRoutes, ref tmp);
                    subRoutes.Add(new List<string> { i });
                }
                else
                {
                    tmp.Add(i);
                }
            }
            AddIfNonEmpty(subRoutes, ref tmp);

            return subRoutes;
        }

        private static void AddIfNonEmpty(
            List<List<string>> subRoutes, ref List<string> tmp)
        {
            if (tmp.Count > 0)
            {
                subRoutes.Add(tmp);
                tmp = new List<string>();
            }
        }

        private void SetRwyWpts()
        {
            origRwyWpt = new Waypoint(
                origIcao + origRwy,
                airportList.RwyLatLon(origIcao, origRwy));

            destRwyWpt = new Waypoint(
                destIcao + destRwy,
                airportList.RwyLatLon(destIcao, destRwy));
        }

        private List<Route> ComputeRoutes(List<List<string>> subRoutes)
        {
            var result = new List<Route>();

            for (int i = 0; i < subRoutes.Count; i++)
            {
                var route = new LinkedList<string>(subRoutes[i]);

                if (route.Count == 1 &&
                    (route.First.Value == "AUTO" ||
                     route.First.Value == "RAND"))
                {
                    result.Add(null);
                }
                else
                {
                    Route origRoute = null;
                    Route destRoute = null;

                    if (i == 0)
                    {
                        origRoute = new SidExtractor(
                            route, origIcao, origRwy, origRwyWpt, wptList, sids)
                            .Extract();
                    }

                    if (i == subRoutes.Count - 1)
                    {
                        destRoute = new StarExtractor(
                            route, destIcao, destRwy, destRwyWpt, wptList, stars)
                            .Extract();
                    }

                    var mainRoute = new AutoSelectAnalyzer(
                        route.ToArray(), origRwyWpt.Lat, origRwyWpt.Lon, wptList)
                        .Analyze();

                    result.Add(
                        AppendRoute(origRoute, AppendRoute(mainRoute, destRoute)));
                }
            }
            return result;
        }

        private static Route AppendRoute(Route original, Route routeToAppend)
        {
            if (original == null)
            {
                return routeToAppend;
            }

            if (routeToAppend == null)
            {
                return original;
            }

            original.Merge(routeToAppend);
            return original;
        }

        private void FillCommands(
            List<List<string>> subRoutes, List<Route> analyzed)
        {
            for (int i = 0; i < subRoutes.Count; i++)
            {
                if (analyzed[i] == null)
                {
                    var startEnd = GetStartEndWpts(analyzed, i);

                    if (subRoutes[i][0] == "AUTO")
                    {
                        analyzed[i] = FindRoute(analyzed, i);
                    }
                    else
                    {
                        // RAND
                        var randRoute = RandRouteToRoute(
                             RandomRoutes.Instance.GetInstance()
                            .Find(startEnd.Start, startEnd.End)
                            .Select(p => p.LatLon).ToList());

                        RandRouteAddOrigDest(randRoute, analyzed, i);
                        analyzed[i] = randRoute;
                    }
                }
            }
        }

        private Route FindRoute(List<Route> analyzed, int index)
        {
            var routeFinder = new RouteFinder(wptList);

            if (index == 0)
            {
                if (index == analyzed.Count - 1)
                {
                    return new RouteFinderFacade(wptList, airportList, "")
                           .FindRoute(origIcao, origRwy, sids, sids.GetSidList(origRwy),
                                      destIcao, destRwy, stars, stars.GetStarList(destRwy));
                }
                else
                {
                    int wptTo = wptList.FindByWaypoint(analyzed[index + 1].FirstWaypoint);

                    return new RouteFinderFacade(wptList, airportList, "")
                          .FindRoute(origIcao, origRwy, sids, sids.GetSidList(origRwy), wptTo);
                }
            }
            else
            {
                if (index == analyzed.Count - 1)
                {
                    int wptFrom = wptList.FindByWaypoint(analyzed[index - 1].LastWaypoint);

                    return new RouteFinderFacade(wptList, airportList, "")
                         .FindRoute(wptFrom, destIcao, destRwy, stars, stars.GetStarList(destRwy));
                }
                else
                {
                    int wptFrom = wptList.FindByWaypoint(analyzed[index - 1].LastWaypoint);
                    int wptTo = wptList.FindByWaypoint(analyzed[index + 1].FirstWaypoint);

                    return routeFinder.FindRoute(wptFrom, wptTo);
                }
            }
        }

        private void RandRouteAddOrigDest(Route route, List<Route> analyzed, int index)
        {
            if (index == 0)
            {
                route.AddFirstWaypoint(origRwyWpt, "DCT");
            }

            if (index == analyzed.Count - 1)
            {
                route.AddLastWaypoint(destRwyWpt, "DCT");
            }
        }

        private static Route RandRouteToRoute(List<LatLon> randRoute)
        {
            var result = new Route();

            if (randRoute.Count > 2)
            {
                for (int i = 1; i < randRoute.Count - 1; i++)
                {
                    double lat = randRoute[i].Lat;
                    double lon = randRoute[i].Lon;

                    string latLonTxt = Format5Letter.To5LetterFormat(lat, lon);
                    var wpt = new Waypoint(latLonTxt, lat, lon);
                    result.AddLastWaypoint(wpt, "DCT");
                }
            }

            return result;
        }

        private WptPair GetStartEndWpts(List<Route> subRoutes, int index)
        {
            var start = index == 0
                ? origRwyWpt
                : subRoutes[index - 1].LastWaypoint;

            var end = index == subRoutes.Count - 1
                ? destRwyWpt
                : subRoutes[index + 1].FirstWaypoint;

            return new WptPair(start, end);
        }

        private static Route ConnectAll(List<Route> subRoutes)
        {
            var route = subRoutes[0];

            for (int i = 1; i < subRoutes.Count; i++)
            {
                route.Merge(subRoutes[i]);
            }
            return route;
        }

        private class WptPair
        {
            public Waypoint Start { get; private set; }
            public Waypoint End { get; private set; }

            public WptPair(Waypoint Start, Waypoint End)
            {
                this.Start = Start;
                this.End = End;
            }
        }
    }
}
