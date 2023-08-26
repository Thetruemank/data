using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TsMap.Common;
using TsMap.FileSystem;
using TsMap.Helpers;
using TsMap.Helpers.Logger;
using TsMap.Map.Overlays;
using TsMap.TsItem;

namespace TsMap
{
    public class TsMapper
    {
        private readonly string _gameDir;
        private List<Mod> _mods;

        public bool IsEts2 = true;

        private List<string> _sectorFiles;

        internal MapOverlayManager OverlayManager { get; private set; }
        public LocalizationManager Localization { get; private set; }

        private readonly Dictionary<ulong, TsPrefab> _prefabLookup = new Dictionary<ulong, TsPrefab>();
        private readonly Dictionary<ulong, TsCity> _citiesLookup = new Dictionary<ulong, TsCity>();
        private readonly Dictionary<ulong, TsCountry> _countriesLookup = new Dictionary<ulong, TsCountry>();
        private readonly Dictionary<ulong, TsRoadLook> _roadLookup = new Dictionary<ulong, TsRoadLook>();

        public readonly List<TsFerryConnection> FerryConnectionLookup = new List<TsFerryConnection>();
        public readonly List<TsRoadItem> Roads = new List<TsRoadItem>();
        public readonly List<TsPrefabItem> Prefabs = new List<TsPrefabItem>();
        public readonly List<TsMapAreaItem> MapAreas = new List<TsMapAreaItem>();
        public readonly List<TsCityItem> Cities = new List<TsCityItem>();
        public readonly List<TsFerryItem> FerryConnections = new List<TsFerryItem>();

        public readonly Dictionary<ulong, TsItem.TsItem> Items = new Dictionary<ulong, TsItem.TsItem>();
        public readonly Dictionary<ulong, TsNode> Nodes = new Dictionary<ulong, TsNode>();

        public float minX = float.MaxValue;
        public float maxX = float.MinValue;
        public float minZ = float.MaxValue;
        public float maxZ = float.MinValue;

        private List<TsSector> Sectors { get; set; }

        internal readonly List<TsItem.TsItem> MapItems = new List<TsItem.TsItem>();

        public List<TsRoadItem> RouteRoads = new List<TsRoadItem>();
        public List<TsPrefabItem> RoutePrefabs = new List<TsPrefabItem>();
        public Dictionary<TsFerryItem, TsFerryItem> RouteFerryPorts = new Dictionary<TsFerryItem, TsFerryItem>();
        public Dictionary<TsPrefabItem, List<TsPrefabCurve>> PrefabNav = new Dictionary<TsPrefabItem, List<TsPrefabCurve>>();

        public readonly Dictionary<ulong, TsFerryItem> FerryPortbyId = new Dictionary<ulong, TsFerryItem>();

        public TsMapper(string gameDir, List<Mod> mods)
        {
            _gameDir = gameDir;
            _mods = mods;
            Sectors = new List<TsSector>();

            OverlayManager = new MapOverlayManager();
            Localization = new LocalizationManager();
        }

        public Dictionary<ulong, TsPrefab> GetPrefabs()
        {
            return _prefabLookup;
        }

        public List<DlcGuard> GetDlcGuardsForCurrentGame()
        {
            return IsEts2
                ? Consts.DefaultEts2DlcGuards
                : Consts.DefaultAtsDlcGuards;
        }

        private void ParseCityFiles()
        {
            var defDirectory = UberFileSystem.Instance.GetDirectory("def");
            if (defDirectory == null)
            {
                Logger.Instance.Error("Could not read 'def' dir");
                return;
            }

            foreach (var cityFileName in defDirectory.GetFiles("city"))
            {
                var cityFile = UberFileSystem.Instance.GetFile($"def/{cityFileName}");

                var data = cityFile.Entry.Read();
                var lines = Encoding.UTF8.GetString(data).Split('\n');
                foreach (var line in lines)
                {
                    if (line.TrimStart().StartsWith("#")) continue;
                    if (line.Contains("@include"))
                    {
                        var path = PathHelper.GetFilePath(line.Split('"')[1], "def");
                        var city = new TsCity(path);
                        if (city.Token != 0 && !_citiesLookup.ContainsKey(city.Token))
                        {
                            _citiesLookup.Add(city.Token, city);
                        }
                    }
                }
            }
        }

        private void ParseCountryFiles()
        {
            var defDirectory = UberFileSystem.Instance.GetDirectory("def");
            if (defDirectory == null)
            {
                Logger.Instance.Error("Could not read 'def' dir");
                return;
            }

            foreach (var countryFilePath in defDirectory.GetFiles("country"))
            {
                var countryFile = UberFileSystem.Instance.GetFile($"def/{countryFilePath}");

                var data = countryFile.Entry.Read();
                var lines = Encoding.UTF8.GetString(data).Split('\n');
                foreach (var line in lines)
                {
                    if (line.TrimStart().StartsWith("#")) continue;
                    if (line.Contains("@include"))
                    {
                        var path = PathHelper.GetFilePath(line.Split('"')[1], "def");
                        var country = new TsCountry(path);
                        if (country.Token != 0 && !_countriesLookup.ContainsKey(country.Token))
                        {
                            _countriesLookup.Add(country.Token, country);
                        }
                    }
                }
            }
        }

        private void ParsePrefabFiles()
        {
            var worldDirectory = UberFileSystem.Instance.GetDirectory("def/world");
            if (worldDirectory == null)
            {
                Logger.Instance.Error("Could not read 'def/world' dir");
                return;
            }

            foreach (var prefabFileName in worldDirectory.GetFiles("prefab"))
            {
                if (!prefabFileName.StartsWith("prefab")) continue;
                var prefabFile = UberFileSystem.Instance.GetFile($"def/world/{prefabFileName}");

                var data = prefabFile.Entry.Read();
                var lines = Encoding.UTF8.GetString(data).Split('\n');

                var token = 0UL;
                var path = "";
                var category = "";
                foreach (var line in lines)
                {
                    var (validLine, key, value) = SiiHelper.ParseLine(line);
                    if (validLine)
                    {
                        if (key == "prefab_model")
                        {
                            token = ScsToken.StringToToken(SiiHelper.Trim(value.Split('.')[1]));
                        }
                        else if (key == "prefab_desc")
                        {
                            path = PathHelper.EnsureLocalPath(value.Split('"')[1]);
                        }
                        else if (key == "category")
                        {
                            category = value.Contains('"') ? value.Split('"')[1] : value.Trim();
                        }
                    }

                    if (line.Contains("}") && token != 0 && path != "")
                    {
                        var prefab = new TsPrefab(path, token, category);
                        if (prefab.Token != 0 && !_prefabLookup.ContainsKey(prefab.Token))
                        {
                            _prefabLookup.Add(prefab.Token, prefab);
                        }

                        token = 0;
                        path = "";
                        category = "";
                    }
                }
            }
        }

        private void ParseRoadLookFiles()
        {
            var worldDirectory = UberFileSystem.Instance.GetDirectory("def/world");
            if (worldDirectory == null)
            {
                Logger.Instance.Error("Could not read 'def/world' dir");
                return;
            }

            foreach (var roadLookFileName in worldDirectory.GetFiles("road_look"))
            {
                if (!roadLookFileName.StartsWith("road")) continue;
                var roadLookFile = UberFileSystem.Instance.GetFile($"def/world/{roadLookFileName}");

                var data = roadLookFile.Entry.Read();
                var lines = Encoding.UTF8.GetString(data).Split('\n');
                TsRoadLook roadLook = null;

                foreach (var line in lines)
                {
                    var (validLine, key, value) = SiiHelper.ParseLine(line);
                    if (validLine)
                    {
                        if (key == "road_look")
                        {
                            roadLook = new TsRoadLook(ScsToken.StringToToken(SiiHelper.Trim(value.Split('.')[1].Trim('{'))));
                        }
                        if (roadLook == null) continue;
                        if (key == "lanes_left[]")
                        {
                            roadLook.LanesLeft.Add(value);
                            roadLook.IsLocal = (value.Equals("traffic_lane.road.local") || value.Equals("traffic_lane.road.local.tram") || value.Equals("traffic_lane.road.local.no_overtake"));
                            roadLook.IsExpress = (value.Equals("traffic_lane.road.expressway") || value.Equals("traffic_lane.road.divided"));
                            roadLook.IsHighway = (value.Equals("traffic_lane.road.motorway") || value.Equals("traffic_lane.road.motorway.low_density") ||
                                                value.Equals("traffic_lane.road.freeway") || value.Equals("traffic_lane.road.freeway.low_density") ||
                                                value.Equals("traffic_lane.road.divided"));
                            roadLook.IsNoVehicles = (value.Equals("traffic_lane.no_vehicles"));
                        }
                        else if (key == "lanes_right[]")
                        {
                            roadLook.LanesRight.Add(value);
                            roadLook.IsLocal = (value.Equals("traffic_lane.road.local") || value.Equals("traffic_lane.road.local.tram") || value.Equals("traffic_lane.road.local.no_overtake"));
                            roadLook.IsExpress = (value.Equals("traffic_lane.road.expressway") || value.Equals("traffic_lane.road.divided"));
                            roadLook.IsHighway = (value.Equals("traffic_lane.road.motorway") || value.Equals("traffic_lane.road.motorway.low_density") ||
                                                value.Equals("traffic_lane.road.freeway") || value.Equals("traffic_lane.road.freeway.low_density") ||
                                                value.Equals("traffic_lane.road.divided"));
                            roadLook.IsNoVehicles = (value.Equals("traffic_lane.no_vehicles"));
                        }
                        else if (key == "road_offset")
                        {
                            roadLook.Offset = float.Parse(value, CultureInfo.InvariantCulture);
                        }
                    }

                    if (line.Contains("}") && roadLook != null)
                    {
                        if (roadLook.Token != 0 && !_roadLookup.ContainsKey(roadLook.Token))
                        {
                            _roadLookup.Add(roadLook.Token, roadLook);
                            roadLook = null;
                        }
                    }
                }
            }
        }

        private void ParseFerryConnections()
        {
            var connectionDirectory = UberFileSystem.Instance.GetDirectory("def/ferry/connection");
            if (connectionDirectory == null)
            {
                Logger.Instance.Error("Could not read 'def/ferry/connection' dir");
                return;
            }

            foreach (var ferryConnectionFilePath in connectionDirectory.GetFilesByExtension("def/ferry/connection", ".sui", ".sii"))
            {
                var ferryConnectionFile = UberFileSystem.Instance.GetFile(ferryConnectionFilePath);

                var data = ferryConnectionFile.Entry.Read();
                var lines = Encoding.UTF8.GetString(data).Split('\n');

                TsFerryConnection conn = null;

                ulong startPortToken = 0;
                ulong endPortToken = 0;
                int price = 0;
                int time = 0;
                int distance = 0;

                foreach (var line in lines)
                {
                    var (validLine, key, value) = SiiHelper.ParseLine(line);
                    if (validLine)
                    {
                        if (conn != null)
                        {
                            if (key.Contains("connection_positions"))
                            {
                                var index = int.Parse(key.Split('[')[1].Split(']')[0]);
                                var vector = value.Split('(')[1].Split(')')[0];
                                var values = vector.Split(',');
                                var x = float.Parse(values[0], CultureInfo.InvariantCulture);
                                var z = float.Parse(values[2], CultureInfo.InvariantCulture);
                                conn.AddConnectionPosition(index, x, z);
                            }
                            else if (key.Contains("connection_directions"))
                            {
                                var index = int.Parse(key.Split('[')[1].Split(']')[0]);
                                var vector = value.Split('(')[1].Split(')')[0];
                                var values = vector.Split(',');
                                var x = float.Parse(values[0], CultureInfo.InvariantCulture);
                                var z = float.Parse(values[2], CultureInfo.InvariantCulture);
                                conn.AddRotation(index, Math.Atan2(z, x));
                            }

                        }

                        if (key == "ferry_connection")
                        {
                            var portIds = value.Split('.');

                            // StartPortToken = ScsToken.StringToToken(portIds[1]),
                            // EndPortToken = ScsToken.StringToToken(portIds[2].TrimEnd('{').Trim())

                            startPortToken = ScsToken.StringToToken(portIds[1]);
                            endPortToken = ScsToken.StringToToken(portIds[2].TrimEnd('{').Trim());

                            conn = new TsFerryConnection
                            {
                                StartPortToken = startPortToken,
                                EndPortToken = endPortToken,
                                Price = price,
                                Time = time,
                                Distance = distance
                            };
                        }

                        if (key.Contains("price"))
                        {
                            try
                            {
                                price = Int32.Parse(value);
                            }
                            catch
                            {
                                price = 0;
                            }
                        }

                        if (key.Contains("time"))
                        {
                            time = Int32.Parse(value);
                        }

                        if (key.Contains("distance"))
                        {
                            distance = Int32.Parse(value);
                        }
                    }

                    if (!line.Contains("}") || conn == null) continue;
                }

                var oldCon = conn;
                conn = new TsFerryConnection
                {
                    StartPortToken = startPortToken,
                    EndPortToken = endPortToken,
                    Price = price,
                    Time = time,
                    Distance = distance
                };
                if (oldCon != null)
                {
                    oldCon.Connections.ForEach(i =>
                    {
                        conn.Connections.Add(i);
                    });
                }

                var existingItem = FerryConnectionLookup.FirstOrDefault(item =>
                    (item.StartPortToken == conn.StartPortToken && item.EndPortToken == conn.EndPortToken) ||
                    (item.StartPortToken == conn.EndPortToken && item.EndPortToken == conn.StartPortToken)); // Check if connection already exists
                if (existingItem == null) FerryConnectionLookup.Add(conn);
            }
        }


        /// <summary>
        /// Parse all definition files
        /// </summary>
        private void ParseDefFiles()
        {
            var startTime = DateTime.Now.Ticks;
            ParseCityFiles();
            Logger.Instance.Info($"Loaded {_citiesLookup.Count} cities in {(DateTime.Now.Ticks - startTime) / TimeSpan.TicksPerMillisecond}ms");

            startTime = DateTime.Now.Ticks;
            ParseCountryFiles();
            Logger.Instance.Info($"Loaded {_countriesLookup.Count} countries in {(DateTime.Now.Ticks - startTime) / TimeSpan.TicksPerMillisecond}ms");

            startTime = DateTime.Now.Ticks;
            ParsePrefabFiles();
            Logger.Instance.Info($"Loaded {_prefabLookup.Count} prefabs in {(DateTime.Now.Ticks - startTime) / TimeSpan.TicksPerMillisecond}ms");

            startTime = DateTime.Now.Ticks;
            ParseRoadLookFiles();
            Logger.Instance.Info($"Loaded {_roadLookup.Count} roads in {(DateTime.Now.Ticks - startTime) / TimeSpan.TicksPerMillisecond}ms");

            startTime = DateTime.Now.Ticks;
            ParseFerryConnections();
            Logger.Instance.Info($"Loaded {FerryConnectionLookup.Count} ferry connections in {(DateTime.Now.Ticks - startTime) / TimeSpan.TicksPerMillisecond}ms");
        }

        /// <summary>
        /// Parse all .base files
        /// </summary>
        private void LoadSectorFiles()
        {
            var baseMapEntry = UberFileSystem.Instance.GetDirectory("map");
            if (baseMapEntry == null)
            {
                Logger.Instance.Error("Could not read 'map' dir");
                return;
            }

            var mbdFilePaths = baseMapEntry.GetFilesByExtension("map", ".mbd"); // Get the map names from the mbd files
            if (mbdFilePaths.Count == 0)
            {
                Logger.Instance.Error("Could not find mbd file");
                return;
            }

            _sectorFiles = new List<string>();

            foreach (var filePath in mbdFilePaths)
            {
                var mapName = PathHelper.GetFileNameWithoutExtensionFromPath(filePath);
                IsEts2 = !(mapName == "usa");

                var mapFileDir = UberFileSystem.Instance.GetDirectory($"map/{mapName}");
                if (mapFileDir == null)
                {
                    Logger.Instance.Error($"Could not read 'map/{mapName}' directory");
                    return;
                }

                _sectorFiles.AddRange(mapFileDir.GetFilesByExtension($"map/{mapName}", ".base"));
            }
        }

        /// <summary>
        /// Loads navigation inside TsItem objects
        /// </summary>
        private void LoadNavigation()
        {
            Logger.Instance.Info("=> " + Prefabs.Count + " prefabs");
            foreach (var prefab in Prefabs)
            {
                Logger.Instance.Info("=> => " + prefab.Uid + " = " + prefab.Nodes.Count + " nodes");
                foreach (var nodeStr in prefab.Nodes)
                {
                    var node = GetNodeByUid(nodeStr);
                    TsItem.TsItem road = null;
                    TsNode precnode = node;
                    TsItem.TsItem precitem = prefab;
                    TsNode nextnode;
                    TsItem.TsItem nextitem;
                    List<TsItem.TsItem> roads = new List<TsItem.TsItem>();
                    var totalLength = 0.0f;
                    if (node.ForwardItem != null && node.ForwardItem.Type == TsItemType.Road)
                    {
                        road = node.ForwardItem;
                    }
                    else if (node.BackwardItem != null && node.BackwardItem.Type == TsItemType.Road)
                    {
                        road = node.BackwardItem;
                    }
                    if (road != null)
                    {
                        int direction = 0;
                        if (road.EndNodeUid == node.Uid) direction = 1;
                        while (road != null && road.Type != TsItemType.Prefab && !road.Hidden)
                        {
                            var length = (float)Math.Sqrt(Math.Pow(GetNodeByUid(road.StartNodeUid).X - GetNodeByUid(road.EndNodeUid).X, 2) + Math.Pow(GetNodeByUid(road.StartNodeUid).Z - GetNodeByUid(road.EndNodeUid).Z, 2));
                            TsRoadItem roadObj = (TsRoadItem)road;
                            totalLength += length / roadObj.RoadLook.GetWidth();
                            /*if (roadObj.RoadLook.IsHighway) totalLength += (length / 2) / roadObj.RoadLook.GetWidth();
                            else if (roadObj.RoadLook.IsLocal) totalLength += (length / 1.75f) / roadObj.RoadLook.GetWidth();
                            else if (roadObj.RoadLook.IsExpress) totalLength += (length / 1.25f) / roadObj.RoadLook.GetWidth();
                            else length += length * 2;*/
                            roads.Add(road);
                            if (GetNodeByUid(road.StartNodeUid) == precnode)
                            {
                                nextnode = GetNodeByUid(road.EndNodeUid);
                                precnode = GetNodeByUid(road.EndNodeUid);
                            }
                            else
                            {
                                nextnode = GetNodeByUid(road.StartNodeUid);
                                precnode = GetNodeByUid(road.StartNodeUid);
                            }
                            if (nextnode.BackwardItem == road || nextnode.BackwardItem == precitem)
                            {
                                nextitem = nextnode.ForwardItem;
                                precitem = nextnode.ForwardItem;
                            }
                            else
                            {
                                nextitem = nextnode.BackwardItem;
                                precitem = nextnode.BackwardItem;
                            }
                            road = nextitem;
                        }
                        if (road != null && !road.Hidden)
                        {
                            TsPrefabItem prevPrefab = (TsPrefabItem)prefab;
                            TsPrefabItem nextPrefab = (TsPrefabItem)road;
                            TsRoadLook look = ((TsRoadItem)roads.LastOrDefault()).RoadLook;
                            if (prevPrefab.Hidden || nextPrefab.Hidden) continue;
                            if (prevPrefab.Navigation.ContainsKey(nextPrefab) == false && (look.IsBidirectional() || direction == 0))
                            {
                                prevPrefab.Navigation.Add(nextPrefab, new Tuple<float, List<TsItem.TsItem>>(totalLength, roads));
                            }
                            if (nextPrefab.Navigation.ContainsKey(prevPrefab) == false && (look.IsBidirectional() || direction == 1))
                            {
                                var reverse = new List<TsItem.TsItem>(roads);
                                reverse.Reverse();
                                nextPrefab.Navigation.Add(prevPrefab, new Tuple<float, List<TsItem.TsItem>>(totalLength, reverse));
                            }
                        }
                    }
                    else if (node.ForwardItem != null && node.BackwardItem != null)
                    {
                        TsPrefabItem forwardPrefab = (TsPrefabItem)node.ForwardItem;
                        TsPrefabItem backwardPrefab = (TsPrefabItem)node.BackwardItem;
                        if (forwardPrefab.Hidden || backwardPrefab.Hidden) continue;
                        if (forwardPrefab.Navigation.ContainsKey(backwardPrefab) == false)
                        {
                            forwardPrefab.Navigation.Add(backwardPrefab, new Tuple<float, List<TsItem.TsItem>>(0, null));
                        }
                        if (backwardPrefab.Navigation.ContainsKey(forwardPrefab) == false)
                        {
                            backwardPrefab.Navigation.Add(forwardPrefab, new Tuple<float, List<TsItem.TsItem>>(0, null));
                        }
                    }

                }
            }

            Dictionary<ulong, TsPrefabItem> ferryToPrefab = new Dictionary<ulong, TsPrefabItem>();
            foreach (var port in FerryConnections)
            {
                float min = float.MaxValue;
                TsPrefabItem closerPrefab = null;
                foreach (var prefab in Prefabs)
                {
                    float distance = (float)Math.Sqrt(Math.Pow(port.X - prefab.X, 2) + Math.Pow(port.Z - prefab.Z, 2));
                    if (distance < min && prefab.Navigation.Count > 1 && !prefab.Hidden)
                    {
                        min = distance;
                        closerPrefab = prefab;
                    }
                }
                ferryToPrefab[port.FerryPortId] = closerPrefab;
            }
            foreach (var port in FerryConnections)
            {
                foreach (var connection in LookupFerryConnection(port.FerryPortId))
                {
                    var ports = new List<TsItem.TsItem>();
                    ports.Add(FerryPortbyId[connection.StartPortToken]);
                    ports.Add(FerryPortbyId[connection.EndPortToken]);
                    ferryToPrefab[connection.StartPortToken].Navigation.Add(ferryToPrefab[connection.EndPortToken], new Tuple<float, List<TsItem.TsItem>>(connection.Distance, ports));
                    ports.Reverse();
                    ferryToPrefab[connection.EndPortToken].Navigation.Add(ferryToPrefab[connection.StartPortToken], new Tuple<float, List<TsItem.TsItem>>(connection.Distance, ports));
                }
            }
        }

        /// <summary>
        /// Calculate path between two prefabs with Dijkstra's shortest path algorithm (Needs improvement with A* Algorithm)
        /// </summary>
        private void CalculatePath(TsPrefabItem Start, TsPrefabItem End)
        {
            Dictionary<TsPrefabItem, Tuple<float, TsPrefabItem>> nodesToWalk = new Dictionary<TsPrefabItem, Tuple<float, TsPrefabItem>>();
            Dictionary<TsPrefabItem, Tuple<float, TsPrefabItem>> walkedNodes = new Dictionary<TsPrefabItem, Tuple<float, TsPrefabItem>>();

            foreach (var node in Prefabs)
            {
                nodesToWalk.Add(node, new Tuple<float, TsPrefabItem>(float.MaxValue, null));
            }

            if (!nodesToWalk.ContainsKey((TsPrefabItem)Start)) return;
            if (!nodesToWalk.ContainsKey((TsPrefabItem)End)) return;

            nodesToWalk[Start] = new Tuple<float, TsPrefabItem>(0, null);

            var s1start = DateTime.Now.Ticks;

            // Walk all nodes
            while (walkedNodes.Count != nodesToWalk.Count)
            {
                // Get node that has been walked the least
                float distanceWalked = float.MaxValue;
                TsPrefabItem toWalk = null;
                foreach (var node in nodesToWalk)
                {
                    var dTmp = node.Value.Item1;
                    if (distanceWalked > dTmp)
                    {
                        distanceWalked = dTmp;
                        toWalk = node.Key;
                    }
                }
                // Break if not found
                if (toWalk == null) break;

                // Add to walked nodes list, remove from nodes to walk list
                walkedNodes[toWalk] = nodesToWalk[toWalk];
                nodesToWalk.Remove(toWalk);

                // Break if we reached the end
                if (toWalk.Uid == End.Uid) break;

                // Get weight (distance)
                var currentWeight = walkedNodes[toWalk].Item1;

                foreach (var jump in toWalk.Navigation)
                {
                    var newWeight = jump.Value.Item1 + currentWeight;
                    TsPrefabItem newNode = jump.Key;

                    if (walkedNodes[toWalk].Item2 != null)
                    {
                        TsPrefabItem precPrefab = walkedNodes[toWalk].Item2;
                        TsPrefabItem middlePrefab = toWalk;
                        List<TsItem.TsItem> precRoad = null;
                        while (precRoad == null && precPrefab != null)
                        {
                            precRoad = precPrefab.Navigation[middlePrefab].Item2;
                            middlePrefab = precPrefab;
                            precPrefab = walkedNodes[precPrefab].Item2;
                        }
                        var nextRoad = toWalk.Navigation[newNode].Item2;
                        if (precRoad != null && nextRoad != null && (precRoad.Count != 0 && nextRoad.Count != 0)
                            && precRoad.LastOrDefault() is TsRoadItem && nextRoad[0] is TsRoadItem)
                        {
                            var result = SetInternalRoutePrefab((TsRoadItem)precRoad.LastOrDefault(), (TsRoadItem)nextRoad[0]);
                            if (!result.Item1) continue;
                            else newWeight += result.Item2;
                        }
                    }

                    if (!walkedNodes.ContainsKey(newNode) && nodesToWalk[newNode].Item1 > newWeight) nodesToWalk[newNode] = new Tuple<float, TsPrefabItem>(newWeight, toWalk);
                }
            }

            var s1end = DateTime.Now.Ticks - s1start;
            Logger.Instance.Info(">> S1: " + s1end / TimeSpan.TicksPerMillisecond);
            var s2start = DateTime.Now.Ticks;

            TsPrefabItem route = End;

            while (route != null)
            {
                TsPrefabItem gotoNew;
                if (walkedNodes.ContainsKey(route)) gotoNew = walkedNodes[route].Item2;
                else gotoNew = nodesToWalk[route].Item2;

                if (gotoNew == null) break;
                if (gotoNew.Navigation.ContainsKey(route) && gotoNew.Navigation[route].Item2 != null)
                {
                    if (gotoNew.Navigation[route].Item2.Count == 2 && gotoNew.Navigation[route].Item2[0] is TsFerryItem && gotoNew.Navigation[route].Item2[1] is TsFerryItem)
                    {
                        var startPort = (TsFerryItem)gotoNew.Navigation[route].Item2[0];
                        var endPort = (TsFerryItem)gotoNew.Navigation[route].Item2[1];
                        if (!RouteFerryPorts.ContainsKey(startPort)) RouteFerryPorts.Add(startPort, endPort);
                    }
                    else
                    {
                        for (int i = gotoNew.Navigation[route].Item2.Count - 1; i >= 0; i--)
                        {
                            RouteRoads.Add((TsRoadItem)gotoNew.Navigation[route].Item2[i]);
                        }
                    }
                }

                route = gotoNew;
            }
            var s2end = DateTime.Now.Ticks - s2start;
            Logger.Instance.Info(">> S2: "+ s2end / TimeSpan.TicksPerMillisecond);

            RouteRoads.Reverse();
        }

        /// <summary>
        /// Even if prefabs roads are already been calculated these could be dirty so it recalculate them using the roads
        /// NEEDS IMPROVEMENT
        /// </summary>
        public void CalculatePrefabsPath()
        {
            RoutePrefabs.Clear();
            PrefabNav.Clear();
            for (int i = 0; i < RouteRoads.Count - 1; i++)
            {
                SetInternalRoutePrefab(RouteRoads[i], RouteRoads[i + 1]);
            }
        }

        /// <summary>
        /// Add Items in Items Dictionary
        /// </summary>
        private void SetItems()
        {
            foreach (var item in Roads) Items.Add(item.Uid, item);
            foreach (var item in Prefabs) Items.Add(item.Uid, item);
            foreach (var item in Cities) Items.Add(item.Uid, item);
            foreach (var item in FerryConnections) try { Items.Add(item.Uid, item); } catch { };
        }

        /// <summary>
        /// Set ForwardItem and BackwardItem in TsNodes
        /// </summary>
        private void SetForwardBackward()
        {
            foreach (var node in Nodes)
            {
                TsItem.TsItem item = null;
                if (Items.TryGetValue(node.Value.ForwardItemUID, out item))
                {
                    node.Value.ForwardItem = item;
                }
                item = null;
                if (Items.TryGetValue(node.Value.BackwardItemUID, out item))
                {
                    node.Value.BackwardItem = item;
                }

            }
        }

        /// <summary>
        /// Given two roads it search for a path that are inside prefabs between them using a DFS search
        /// </summary>
        private Tuple<bool, float> SetInternalRoutePrefab(TsRoadItem Start, TsRoadItem End)
        {
            TsNode startNode = null;
            Dictionary<TsPrefabItem, bool> visited = new Dictionary<TsPrefabItem, bool>();
            Stack<List<Tuple<TsNode, TsPrefabItem>>> prefabsToCheck = new Stack<List<Tuple<TsNode, TsPrefabItem>>>();
            List<List<Tuple<TsNode, TsPrefabItem>>> possiblePaths = new List<List<Tuple<TsNode, TsPrefabItem>>>();
            if (GetNodeByUid(Start.StartNodeUid).BackwardItem.Type == TsItemType.Prefab || GetNodeByUid(Start.StartNodeUid).ForwardItem.Type == TsItemType.Prefab)
            {
                startNode = GetNodeByUid(Start.StartNodeUid);
                var prefab = startNode.BackwardItem.Type == TsItemType.Prefab ? (TsPrefabItem)startNode.BackwardItem : (TsPrefabItem)startNode.ForwardItem;
                var temp = new List<Tuple<TsNode, TsPrefabItem>>();
                temp.Add(new Tuple<TsNode, TsPrefabItem>(startNode, prefab));
                prefabsToCheck.Push(temp);
            }
            if (GetNodeByUid(Start.EndNodeUid).BackwardItem.Type == TsItemType.Prefab || GetNodeByUid(Start.EndNodeUid).ForwardItem.Type == TsItemType.Prefab)
            {
                startNode = GetNodeByUid(Start.EndNodeUid);
                var prefab = startNode.BackwardItem.Type == TsItemType.Prefab ? (TsPrefabItem)startNode.BackwardItem : (TsPrefabItem)startNode.ForwardItem;
                var temp = new List<Tuple<TsNode, TsPrefabItem>>();
                temp.Add(new Tuple<TsNode, TsPrefabItem>(startNode, prefab));
                prefabsToCheck.Push(temp);
            }
            while (prefabsToCheck.Count != 0)
            {
                List<Tuple<TsNode, TsPrefabItem>> actualPath = prefabsToCheck.Pop();
                Tuple<TsNode, TsPrefabItem> actualPrefab = actualPath.LastOrDefault();

                if (visited.ContainsKey(actualPrefab.Item2)) continue;
                visited[actualPrefab.Item2] = true;

                var lastNode = actualPrefab.Item2.NodeIteminPrefab(this, End);
                if (lastNode != null)
                {
                    actualPath.Add(new Tuple<TsNode, TsPrefabItem>(lastNode, null));
                    possiblePaths.Add(actualPath);
                    continue;
                }

                foreach (var prefab in actualPrefab.Item2.NodePrefabinPrefab(this))
                {
                    var newPath = new List<Tuple<TsNode, TsPrefabItem>>(actualPath);
                    newPath.Add(prefab);
                    prefabsToCheck.Push(newPath);
                }
            }

            var returnValue = new Tuple<bool, float>(false, 0);
            foreach (var path in possiblePaths)
            {
                bool success = true;
                float totalLength = 0.0f;
                for (int i = 0; i < path.Count - 1; i++)
                {
                    var tempData = AddPrefabPath(path[i].Item2, path[i].Item1, path[i + 1].Item1);
                    if (!tempData.Item1)
                    {
                        success = false;
                        break;
                    }
                    totalLength += tempData.Item2;
                }
                if (success && path.Count >= 1) return new Tuple<bool, float>(true, totalLength / Start.RoadLook.GetWidth());
            }
            return returnValue;
        }

        public Tuple<bool, float> AddPrefabPath(TsPrefabItem prefab, TsNode startNode, TsNode endNode)
        {
            //Optional - some prefabs, like gas stastions will be completely selected instead of selecting a sigle road
            //if (prefab.Prefab.PrefabNodes.Count <= 2) {
            //    RoutePrefabs.Add(prefab);
            //    return true;
            //}
            var returnValue = new Tuple<bool, float>(false, 0);
            var s = prefab.GetNearestNode(this, startNode, 0);
            var e = prefab.GetNearestNode(this, endNode, 1);
            if (s.id == -1 || e.id == -1) return returnValue;
            var key = new Tuple<TsPrefabNode, TsPrefabNode>(s, e);
            if (prefab.Prefab.NavigationRoutes.ContainsKey(key))
            {
                PrefabNav[prefab] = prefab.Prefab.NavigationRoutes[key].Item1;
                returnValue = new Tuple<bool, float>(true, prefab.Prefab.NavigationRoutes[key].Item2);
            }
            return returnValue;
            // TODO: Add the possibility to return also the weight of the path to be used in Dijkstra's Algorithm
        }

        /// <summary>
        /// Parse through all .scs files and retrieve all necessary files
        /// </summary>
        public void Parse()
        {
            var startTime = DateTime.Now.Ticks;

            if (!Directory.Exists(_gameDir))
            {
                Logger.Instance.Error("Could not find Game directory.");
                return;
            }

            UberFileSystem.Instance.AddSourceDirectory(_gameDir);

            _mods.Reverse(); // Highest priority mods (top) need to be loaded last

            foreach (var mod in _mods)
            {
                if (mod.Load) UberFileSystem.Instance.AddSourceFile(mod.ModPath);
            }

            UberFileSystem.Instance.AddSourceFile(Path.Combine(Environment.CurrentDirectory,
                "custom_resources.zip"));

            Logger.Instance.Info($"Loaded all .scs files in {(DateTime.Now.Ticks - startTime) / TimeSpan.TicksPerMillisecond}ms");

            ParseDefFiles();
            LoadSectorFiles();

            var preLocaleTime = DateTime.Now.Ticks;
            Localization.LoadLocaleValues();
            Logger.Instance.Info($"It took {(DateTime.Now.Ticks - preLocaleTime) / TimeSpan.TicksPerMillisecond} ms to read all locale files");

            if (_sectorFiles == null) return;
            var preMapParseTime = DateTime.Now.Ticks;
            Sectors = _sectorFiles.Select(file => new TsSector(this, file)).ToList();
            Sectors.ForEach(sec => sec.Parse());
            Sectors.ForEach(sec => sec.ClearFileData());
            SetItems();
            SetForwardBackward();
            Logger.Instance.Info($"It took {(DateTime.Now.Ticks - preMapParseTime) / TimeSpan.TicksPerMillisecond} ms to parse all (*.base) files");

            foreach (var mapItem in MapItems)
            {
                mapItem.Update();
            }

            var invalidFerryConnections = FerryConnectionLookup.Where(x => x.StartPortLocation == PointF.Empty || x.EndPortLocation == PointF.Empty).ToList();
            foreach (var invalidFerryConnection in invalidFerryConnections)
            {
                FerryConnectionLookup.Remove(invalidFerryConnection);
                Logger.Instance.Debug($"Ignored ferry connection " +
                    $"'{ScsToken.TokenToString(invalidFerryConnection.StartPortToken)}-{ScsToken.TokenToString(invalidFerryConnection.EndPortToken)}' " +
                    $"due to not having Start/End location set.");
            }

            Logger.Instance.Info($"Loaded {OverlayManager.GetOverlayImagesCount()} overlay images, with {OverlayManager.GetOverlays().Count} overlays on the map");
            //Logger.Instance.Info($"It took {(DateTime.Now.Ticks - startTime) / TimeSpan.TicksPerMillisecond} ms to fully load.");

            Logger.Instance.Info("Loading navigation data...");
            LoadNavigation();

            //while (Prefabs[firstP].Hidden || Prefabs[firstP].Navigation.Count < 1) firstP++;
            //while (Prefabs[secondP].Hidden || Prefabs[secondP].Navigation.Count < 1) secondP++;
            Logger.Instance.Info("Starting Calculating Path...");
            //CalculatePath(Prefabs[firstP], Prefabs[secondP]);

            // CHANGE THIS
            PointF startCoord = new PointF(-10109.01f, 45631.55f);
            PointF endCoord = new PointF(-8042.469f, -44051.7f);

            TsPrefabItem firstPfItm = null;
            foreach (var i in Prefabs)
            {
                var x = i.X - startCoord.X;
                var z = i.Z - startCoord.Y;
                if ((x <= 1 && x >= -1) && (z <= 1 && z >= -1))
                {
                    firstPfItm = i;
                    break;
                }
            }

            TsPrefabItem secondPfItm = null;
            foreach (var i in Prefabs)
            {
                var x = i.X - endCoord.X;
                var z = i.Z - endCoord.Y;
                if ((x <= 10 && x >= -10) && (z <= 10 && z >= -10))
                {
                    secondPfItm = i;
                    break;
                }
            }

            CalculatePath(firstPfItm, secondPfItm);
            Logger.Instance.Info("Starting Calculating Path inside Prefabs...");
            CalculatePrefabsPath();

            Logger.Instance.Info("Selected Roads: " + RouteRoads.Count + " - Selected Prefabs: " + PrefabNav.Count);
            Logger.Instance.Info("Start Location: X -> " + firstPfItm.X + ";Z -> " + firstPfItm.Z);
            Logger.Instance.Info("End Location: X -> " + secondPfItm.X + ";Z -> " + secondPfItm.Z);
            Logger.Instance.Info($"It took {(DateTime.Now.Ticks - preMapParseTime) / TimeSpan.TicksPerMillisecond} ms to parse all (*.base)" +
                    $" map files and {(DateTime.Now.Ticks - startTime) / TimeSpan.TicksPerMillisecond} ms total.");


        }

        public void ExportInfo(ExportFlags exportFlags, string exportPath)
        {
            if (exportFlags.IsActive(ExportFlags.CityList)) ExportCities(exportFlags, exportPath);
            if (exportFlags.IsActive(ExportFlags.CountryList)) ExportCountries(exportFlags, exportPath);
            if (exportFlags.IsActive(ExportFlags.OverlayList)) ExportOverlays(exportFlags, exportPath);
        }

        /// <summary>
        /// Creates a json file with the positions and names (w/ localizations) of all cities
        /// </summary>
        public void ExportCities(ExportFlags exportFlags, string path)
        {
            if (!Directory.Exists(path)) return;
            var citiesJArr = new JArray();
            foreach (var city in Cities)
            {
                if (city.Hidden) continue;
                var cityJObj = JObject.FromObject(city.City);
                cityJObj["X"] = city.X;
                cityJObj["Y"] = city.Z;
                if (_countriesLookup.ContainsKey(ScsToken.StringToToken(city.City.Country)))
                {
                    var country = _countriesLookup[ScsToken.StringToToken(city.City.Country)];
                    cityJObj["CountryId"] = country.CountryId;
                }
                else
                {
                    Logger.Instance.Warning($"Could not find country for {city.City.Name}");
                }

                if (exportFlags.IsActive(ExportFlags.CityLocalizedNames))
                {
                    cityJObj["LocalizedNames"] = new JObject();
                    foreach (var locale in Localization.GetLocales())
                    {
                        var locCityName = Localization.GetLocaleValue(city.City.LocalizationToken, locale);
                        if (locCityName != null)
                        {
                            cityJObj["LocalizedNames"][locale] = locCityName;
                        }
                    }
                }

                citiesJArr.Add(cityJObj);
            }
            File.WriteAllText(Path.Combine(path, "Cities.json"), citiesJArr.ToString(Formatting.Indented));
        }
        /// <summary>
        /// Creates a json file with the positions and names (w/ localizations) of all countries
        /// </summary>
        public void ExportCountries(ExportFlags exportFlags, string path)
        {
            if (!Directory.Exists(path)) return;
            var countriesJArr = new JArray();
            foreach (var country in _countriesLookup.Values)
            {
                var countryJObj = JObject.FromObject(country);
                if (exportFlags.IsActive(ExportFlags.CountryLocalizedNames))
                {
                    countryJObj["LocalizedNames"] = new JObject();
                    foreach (var locale in Localization.GetLocales())
                    {
                        var locCountryName = Localization.GetLocaleValue(country.LocalizationToken, locale);
                        if (locCountryName != null)
                        {
                            countryJObj["LocalizedNames"][locale] = locCountryName;
                        }
                    }
                }
                countriesJArr.Add(countryJObj);
            }
            File.WriteAllText(Path.Combine(path, "Countries.json"), countriesJArr.ToString(Formatting.Indented));
        }

        /// <summary>
        /// Saves all overlays as .png images.
        /// Creates a json file with all positions of said overlays
        /// </summary>
        /// <remarks>
        /// ZoomLevelVisibility flags: Multiple can be selected at the same time,
        /// eg. if value is 3 then 0 and 1 are both selected
        /// Selected = hidden (0-7 => numbers in game editor)
        /// 1 = (Nav map, 3D view, zoom 0) (0)
        /// 2 = (Nav map, 3D view, zoom 1) (1)
        /// 4 = (Nav map, 2D view, zoom 0) (2)
        /// 8 = (Nav map, 2D view, zoom 1) (3)
        /// 16 = (World map, zoom 0) (4)
        /// 32 = (World map, zoom 1) (5)
        /// 64 = (World map, zoom 2) (6)
        /// 128 = (World map, zoom 3) (7)
        /// </remarks>
        /// <param name="path"></param>
        public void ExportOverlays(ExportFlags exportFlags, string path)
        {
            if (!Directory.Exists(path)) return;

            var saveAsPNG = exportFlags.IsActive(ExportFlags.OverlayPNGs);

            var overlayPath = Path.Combine(path, "Overlays");
            if (saveAsPNG) Directory.CreateDirectory(overlayPath);

            var overlaysJArr = new JArray();
            foreach (var mapOverlay in OverlayManager.GetOverlays())
            {
                var b = mapOverlay.GetBitmap();
                if (b == null) continue;

                var overlayJObj = new JObject
                {
                    ["X"] = mapOverlay.Position.X,
                    ["Y"] = mapOverlay.Position.Y,
                    ["Name"] = mapOverlay.OverlayName,
                    ["Type"] = mapOverlay.TypeName,
                    ["Width"] = b.Width,
                    ["Height"] = b.Height,
                    ["DlcGuard"] = mapOverlay.DlcGuard,
                    ["IsSecret"] = mapOverlay.IsSecret,
                };

                if (mapOverlay.ZoomLevelVisibility != 0)
                    overlayJObj["ZoomLevelVisibility"] = mapOverlay.ZoomLevelVisibility;

                overlaysJArr.Add(overlayJObj);
                if (saveAsPNG && !File.Exists(Path.Combine(overlayPath, $"{mapOverlay.OverlayName}.png")))
                    b.Save(Path.Combine(overlayPath, $"{mapOverlay.OverlayName}.png"));
            }
            
            File.WriteAllText(Path.Combine(path, "Overlays.json"), overlaysJArr.ToString(Formatting.Indented));
        }

        public void UpdateEdgeCoords(TsNode node)
        {
            if (minX > node.X) minX = node.X;
            if (maxX < node.X) maxX = node.X;
            if (minZ > node.Z) minZ = node.Z;
            if (maxZ < node.Z) maxZ = node.Z;
        }

        public TsNode GetNodeByUid(ulong uid)
        {
            return Nodes.ContainsKey(uid) ? Nodes[uid] : null;
        }

        public TsCountry GetCountryByTokenName(string name)
        {
            var token = ScsToken.StringToToken(name);
            return _countriesLookup.ContainsKey(token) ? _countriesLookup[token] : null;
        }

        public TsRoadLook LookupRoadLook(ulong lookId)
        {
            return _roadLookup.ContainsKey(lookId) ? _roadLookup[lookId] : null;
        }

        public TsPrefab LookupPrefab(ulong prefabId)
        {
            return _prefabLookup.ContainsKey(prefabId) ? _prefabLookup[prefabId] : null;
        }

        public TsCity LookupCity(ulong cityId)
        {
            return _citiesLookup.ContainsKey(cityId) ? _citiesLookup[cityId] : null;
        }

        public List<TsFerryConnection> LookupFerryConnection(ulong ferryPortId)
        {
            return FerryConnectionLookup.Where(item => item.StartPortToken == ferryPortId).ToList();
        }

        public void AddFerryPortLocation(ulong ferryPortId, float x, float z)
        {
            var ferry = FerryConnectionLookup.Where(item => item.StartPortToken == ferryPortId || item.EndPortToken == ferryPortId);
            foreach (var connection in ferry)
            {
                connection.SetPortLocation(ferryPortId, x, z);
            }
        }
    }
}
