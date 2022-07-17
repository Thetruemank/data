using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TsMap.Helpers.Logger;
using TsMap.TsItem;

namespace TsMap
{
    public class Exporter
    {
        public static void Export(TsMapper mapper)
        {
            JsonSerializer serializer = new JsonSerializer();
            serializer.NullValueHandling = NullValueHandling.Include;
            serializer.Formatting = Formatting.Indented;

            Dictionary<ulong, ActualPrefabModel> actualPrefabModels = new Dictionary<ulong, ActualPrefabModel>();
            mapper.Prefabs.ForEach(p =>
            {
                var pref = p.Prefab;
                if (actualPrefabModels.ContainsKey(pref.Token)) return;

                var pm = new ActualPrefabModel
                {
                    FilePath = pref.FilePath,
                    Token = pref.Token,
                    Category = pref.Category,
                    ValidRoad = pref.ValidRoad,
                    PrefabNodes = pref.PrefabNodes,
                    SpawnPoints = pref.SpawnPoints,
                    MapPoints = pref.MapPoints,
                    TriggerPoints = pref.TriggerPoints,
                    PrefabCurves = pref.PrefabCurves,
                    NavigationRoutes = new Dictionary<string, PrefabNavItem>()
                };
                if (pref.NavigationRoutes != null)
                    foreach (KeyValuePair<Tuple<TsPrefabNode, TsPrefabNode>, Tuple<List<TsPrefabCurve>, float>> kv in pref.NavigationRoutes)
                    {
                        var pfi = new PrefabNavItem
                        {
                            CurveIds = new List<int>(),
                            Distance = 0
                        };
                        kv.Value.Item1.ForEach(c => pfi.CurveIds.Add(c.id));
                        pfi.Distance = kv.Value.Item2;
                        pm.NavigationRoutes.Add(kv.Key.Item1.id + "/" + kv.Key.Item2.id, pfi);
                    }
                actualPrefabModels.Add(pm.Token, pm);
            });
            using (StreamWriter sw = new StreamWriter("./prefabs.json"))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, actualPrefabModels);
            }

            Dictionary<ulong, PrefabModel> prefabModels = new Dictionary<ulong, PrefabModel>();
            mapper.Prefabs.ForEach(p =>
            {
                var pm = new PrefabModel
                {
                    Uid = p.Uid,
                    StartNodeUid = p.StartNodeUid,
                    EndNodeUid = p.EndNodeUid,
                    Nodes = p.Nodes,
                    BlockSize = p.BlockSize,
                    Valid = p.Valid,
                    Type = p.Type,
                    X = p.X,
                    Z = p.Z,
                    Hidden = p.Hidden,
                    Flags = p.Flags,
                    Navigation = new Dictionary<ulong, Tuple<float, List<SimpleItem>>>(),
                    Origin = p.Origin,
                    Prefab = p.Prefab.Token,
                    IsSecret = p.IsSecret
                };
                if (p.Navigation != null)
                {
                    foreach (KeyValuePair<TsPrefabItem, Tuple<float, List<TsItem.TsItem>>> entry in p.Navigation)
                    {
                        List<SimpleItem> itemIds = new List<SimpleItem>();
                        if (entry.Value.Item2 != null) entry.Value.Item2.ForEach(o => itemIds.Add(new SimpleItem
                        {
                            Uid = o.Uid,
                            Type = o.Type.ToString()
                        }));
                        pm.Navigation.Add(entry.Key.Uid, new Tuple<float, List<SimpleItem>>(entry.Value.Item1, itemIds));
                    }
                }
                prefabModels.Add(pm.Uid, pm);
            });
            using (StreamWriter sw = new StreamWriter("./prefab_items.json"))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, prefabModels);
            }

            Dictionary<ulong, TsFerryItem> ferryPorts = new Dictionary<ulong, TsFerryItem>();
            mapper.FerryConnections.ForEach(c => ferryPorts.Add(c.Uid, c));
            using (StreamWriter sw = new StreamWriter("./ferry_ports.json"))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, ferryPorts);
            }

            using (StreamWriter sw = new StreamWriter("./ferry_connections.json"))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, mapper.FerryConnectionLookup);
            }

            Dictionary<ulong, RoadModel> roadModels = new Dictionary<ulong, RoadModel>();
            mapper.Roads.ForEach(r =>
            {
                var rm = new RoadModel
                {
                    Uid = r.Uid,
                    StartNodeUid = r.StartNodeUid,
                    EndNodeUid = r.EndNodeUid,
                    Nodes = r.Nodes,
                    BlockSize = r.BlockSize,
                    Valid = r.Valid,
                    Type = r.Type,
                    X = r.X,
                    Z = r.Z,
                    Hidden = r.Hidden,
                    Flags = r.Flags,
                    Navigation = new Dictionary<ulong, Tuple<float, List<SimpleItem>>>(),
                    RoadLook = r.RoadLook,
                    Points = r._points,
                    IsSecret = r.IsSecret,
                };
                if (r.Navigation != null)
                {
                    foreach (KeyValuePair<TsPrefabItem, Tuple<float, List<TsItem.TsItem>>> entry in r.Navigation)
                    {
                        List<SimpleItem> itemIds = new List<SimpleItem>();
                        entry.Value.Item2.ForEach(o => itemIds.Add(new SimpleItem
                        {
                            Uid = o.Uid,
                            Type = o.Type.ToString()
                        }));
                        rm.Navigation.Add(entry.Key.Uid, new Tuple<float, List<SimpleItem>>(entry.Value.Item1, itemIds));
                    }
                }
                roadModels.Add(rm.Uid, rm);
            });
            using (StreamWriter sw = new StreamWriter("./roads.json"))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, roadModels);
            }

            List<NodeModel> nodeModels = new List<NodeModel>();
            foreach (var kv in mapper.Nodes)
            {
                nodeModels.Add(new NodeModel
                {
                    Uid = kv.Value.Uid,
                    X = kv.Value.X,
                    Z = kv.Value.Z,
                    Rotation = kv.Value.Rotation,
                    ForwardItem = kv.Value.ForwardItem == null ? null : new SimpleItem
                    {
                        Uid = kv.Value.ForwardItem.Uid,
                        Type = kv.Value.ForwardItem.Type.ToString(),
                    },
                    BackwardItem = kv.Value.BackwardItem == null ? null : new SimpleItem
                    {
                        Uid = kv.Value.BackwardItem.Uid,
                        Type = kv.Value.BackwardItem.Type.ToString(),
                    },
                });
            }
            using (StreamWriter sw = new StreamWriter("./nodes.json"))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, nodeModels);
            }
        }

        public class NodeModel
        {
            public ulong Uid;
            public float X;
            public float Z;
            public float Rotation;

            public SimpleItem ForwardItem;
            public SimpleItem BackwardItem;
        }

        public class SimpleItem
        {
            public ulong Uid;
            public string Type;
        }

        public class PrefabNavItem
        {
            public List<int> CurveIds;
            public float Distance;
        }

        public class ActualPrefabModel
        {
            public string FilePath;
            public ulong Token;
            public string Category;

            public bool ValidRoad;

            public List<TsPrefabNode> PrefabNodes;
            public List<TsSpawnPoint> SpawnPoints;
            public List<TsMapPoint> MapPoints;
            public List<TsTriggerPoint> TriggerPoints;

            public List<TsPrefabCurve> PrefabCurves;
            //< TsNodeId/TsNodeId, <List<TsPrefabCurveId>, float> >
            public Dictionary<string, PrefabNavItem> NavigationRoutes;
        }

        public class PrefabModel
        {
            public ulong Uid;
            public ulong StartNodeUid;
            public ulong EndNodeUid;

            public List<ulong> Nodes;

            public int BlockSize;

            public bool Valid;

            public TsItemType Type;
            public float X;
            public float Z;
            public bool Hidden;

            public uint Flags;

            public Dictionary<ulong, Tuple<float, List<SimpleItem>>> Navigation;

            public int Origin;
            public ulong Prefab;

            public bool IsSecret;
        }

        public class RoadModel
        {
            public ulong Uid;
            public ulong StartNodeUid;
            public ulong EndNodeUid;

            public List<ulong> Nodes;

            public int BlockSize;

            public bool Valid;

            public TsItemType Type;
            public float X;
            public float Z;
            public bool Hidden;

            public uint Flags;

            public Dictionary<ulong, Tuple<float, List<SimpleItem>>> Navigation;

            public TsRoadLook RoadLook;
            public List<PointF> Points;
            public bool IsSecret;
        }
    }
}
