using System;
using System.Collections.Generic;
using TsMap.FileSystem;
using TsMap.Helpers;
using TsMap.Helpers.Logger;

namespace TsMap
{
    public struct TsPrefabNode
    {
        public int id;
        public float X;
        public float Y;
        public float Z;
        public float RotX;
        public float RotY;
        public float RotZ;
        public int LaneCount;
        public List<int> InputPoints;
        public List<int> OutputPoints;
    }

    public struct TsMapPoint
    {
        public float X;
        public float Y;
        public float Z;
        public int LaneOffset;
        public int LaneCount;
        public bool Hidden;
        public byte PrefabColorFlags;
        public int NeighbourCount;
        public List<int> Neighbours;
        public sbyte ControlNodeIndex;
    }

    public class TsSpawnPoint
    {
        public float X;
        public float Y;
        public float Z;
        public TsSpawnPointType Type;
        public uint Unk; /* from version 24 */
    }

    public class TsTriggerPoint
    {
        public uint TriggerId;
        public ulong TriggerActionToken;
        public float X;
        public float Y;
        public float Z;
    }

    public struct TsPrefabCurve
    {
        public int id;
        public int idNode;
        public float start_X;
        public float start_Y;
        public float start_Z;
        public float end_X;
        public float end_Y;
        public float end_Z;
        public float lenght;
        public List<int> nextLines;
        public List<int> prevLines;
    }

    public class TsPrefab
    {
        private const int NodeBlockSize = 0x68;
        private const int MapPointBlockSize = 0x30;
        private const int SpawnPointBlockSize = 0x20;
        private const int SpawnPointV24BlockSize = 0x24;
        private const int TriggerPointBlockSize = 0x30;
        private const int PrefabCurveSize = 0x84;

        public string FilePath { get; }
        public ulong Token { get; }
        public string Category { get; }

        private byte[] _stream;

        public bool ValidRoad { get; private set; }

        public List<TsPrefabNode> PrefabNodes { get; private set; }
        public List<TsSpawnPoint> SpawnPoints { get; private set; }
        public List<TsMapPoint> MapPoints { get; private set; }
        public List<TsTriggerPoint> TriggerPoints { get; private set; }

        public List<TsPrefabCurve> PrefabCurves { get; private set; }
        public List<TsPrefabCurve> ActualCurves { get; private set; }
        public Dictionary<Tuple<TsPrefabNode, TsPrefabNode>, Tuple<List<TsPrefabCurve>, float>> NavigationRoutes { get; private set; }

        public TsPrefab(string filePath, ulong token, string category)
        {
            FilePath = filePath;
            Token = token;
            Category = category;

            PrefabCurves = new List<TsPrefabCurve>();
            ActualCurves = new List<TsPrefabCurve>();
            NavigationRoutes = new Dictionary<Tuple<TsPrefabNode, TsPrefabNode>, Tuple<List<TsPrefabCurve>, float>>();

            var file = UberFileSystem.Instance.GetFile(FilePath);

            if (file == null) return;

            _stream = file.Entry.Read();

            Parse();
        }

        private void Parse()
        {
            PrefabNodes = new List<TsPrefabNode>();
            SpawnPoints = new List<TsSpawnPoint>();
            MapPoints = new List<TsMapPoint>();
            TriggerPoints = new List<TsTriggerPoint>();

            var fileOffset = 0x0;

            var version = MemoryHelper.ReadInt32(_stream, fileOffset);

            if (version < 0x15)
            {
                Logger.Instance.Error($"{FilePath} file version ({version}) too low, min. is {0x15}");
                return;
            }

            var nodeCount = BitConverter.ToInt32(_stream, fileOffset += 0x04);
            var navCurveCount = BitConverter.ToInt32(_stream, fileOffset += 0x04);
            var curveCount = navCurveCount;
            ValidRoad = navCurveCount != 0;
            var spawnPointCount = BitConverter.ToInt32(_stream, fileOffset += 0x0C);
            var mapPointCount = BitConverter.ToInt32(_stream, fileOffset += 0x0C);
            var triggerPointCount = BitConverter.ToInt32(_stream, fileOffset += 0x04);

            if (version > 0x15) fileOffset += 0x04; // http://modding.scssoft.com/wiki/Games/ETS2/Modding_guides/1.30#Prefabs

            var nodeOffset = MemoryHelper.ReadInt32(_stream, fileOffset += 0x08);
            var curveOffset = BitConverter.ToInt32(_stream, fileOffset += 0x04);
            var spawnPointOffset = MemoryHelper.ReadInt32(_stream, fileOffset += 0x0C);
            var mapPointOffset = MemoryHelper.ReadInt32(_stream, fileOffset += 0x10);
            var triggerPointOffset = MemoryHelper.ReadInt32(_stream, fileOffset += 0x04);

            for (var i = 0; i < nodeCount; i++)
            {
                var nodeBaseOffset = nodeOffset + (i * NodeBlockSize);
                
                var listInput = new List<int>();
                var listOutput = new List<int>();
                for (var j = 0; j < 8; j++)
                {
                    var inVal = BitConverter.ToInt32(_stream, nodeBaseOffset + 0x28 + j * 4);
                    var outVal = BitConverter.ToInt32(_stream, nodeBaseOffset + 0x48 + j * 4);
                    if (inVal != -1)
                    {
                        listInput.Add(inVal);
                    }
                    if (outVal != -1)
                    {
                        listOutput.Add(outVal);
                    }
                }

                var node = new TsPrefabNode
                {
                    id = i,
                    X = MemoryHelper.ReadSingle(_stream, nodeBaseOffset + 0x10),
                    Y = MemoryHelper.ReadSingle(_stream, nodeBaseOffset + 0x14),
                    Z = MemoryHelper.ReadSingle(_stream, nodeBaseOffset + 0x18),
                    RotX = MemoryHelper.ReadSingle(_stream, nodeBaseOffset + 0x1C),
                    RotY = MemoryHelper.ReadSingle(_stream, nodeBaseOffset + 0x20),
                    RotZ = MemoryHelper.ReadSingle(_stream, nodeBaseOffset + 0x24),
                    InputPoints = listInput,
                    OutputPoints = listOutput
                };

                int laneCount = 0;
                var nodeFileOffset = nodeBaseOffset + 0x24;
                for (var j = 0; j < 8; j++)
                {
                    if (MemoryHelper.ReadInt32(_stream, nodeFileOffset += 0x04) != -1) laneCount++;
                }

                for (var j = 0; j < 8; j++)
                {
                    if (MemoryHelper.ReadInt32(_stream, nodeFileOffset += 0x04) != -1) laneCount++;
                }
                node.LaneCount = laneCount;

                PrefabNodes.Add(node);
            }


            for (var i = 0; i < curveCount; i++)
            {
                var curveBaseOffset = curveOffset + (i * PrefabCurveSize);
                var countNextLines = BitConverter.ToInt32(_stream, curveBaseOffset + 0x6C);
                var nextLinesList = new List<int>();
                for (var j = 0; j < countNextLines; j++)
                {
                    nextLinesList.Add(BitConverter.ToInt32(_stream, curveBaseOffset + 0x4C + j * 4));
                }
                var countPrevLines = BitConverter.ToInt32(_stream, curveBaseOffset + 0x70);
                var prevLinesList = new List<int>();
                for (var j = 0; j < countPrevLines; j++)
                {
                    prevLinesList.Add(BitConverter.ToInt32(_stream, curveBaseOffset + 0x5C + j * 4));
                }
                var curve = new TsPrefabCurve
                {
                    id = i,
                    idNode = BitConverter.ToInt32(_stream, curveBaseOffset + 0x0C), // 12
                    start_X = BitConverter.ToSingle(_stream, curveBaseOffset + 0x10), // 16
                    start_Y = BitConverter.ToSingle(_stream, curveBaseOffset + 0x14), // 20
                    start_Z = BitConverter.ToSingle(_stream, curveBaseOffset + 0x18), // 24
                    end_X = BitConverter.ToSingle(_stream, curveBaseOffset + 0x1C), // 28
                    end_Y = BitConverter.ToSingle(_stream, curveBaseOffset + 0x20), // 32
                    end_Z = BitConverter.ToSingle(_stream, curveBaseOffset + 0x24), // 36
                    lenght = BitConverter.ToSingle(_stream, curveBaseOffset + 0x44), // 68
                    nextLines = nextLinesList,
                    prevLines = prevLinesList
                };
                PrefabCurves.Add(curve);
            }

            var spawnPointBlockSize = version >= 24 ? SpawnPointV24BlockSize : SpawnPointBlockSize;

            for (var i = 0; i < spawnPointCount; i++)
            {
                var spawnPointBaseOffset = spawnPointOffset + (i * spawnPointBlockSize);
                var spawnPoint = new TsSpawnPoint
                {
                    X = MemoryHelper.ReadSingle(_stream, spawnPointBaseOffset),
                    Y = MemoryHelper.ReadSingle(_stream, spawnPointBaseOffset + 0x04),
                    Z = MemoryHelper.ReadSingle(_stream, spawnPointBaseOffset + 0x08),
                    Type = (TsSpawnPointType)MemoryHelper.ReadUInt32(_stream, spawnPointBaseOffset + 0x1C)
                };
                SpawnPoints.Add(spawnPoint);
                // Log.Msg($"Spawn point of type: {spawnPoint.Type} in {_filePath}");
            }

            foreach (var inputNode in PrefabNodes)
            {
                foreach (var outputNode in PrefabNodes)
                {
                    if (inputNode.id == outputNode.id) continue;
                    var defaultCurve = default(TsPrefabCurve);
                    defaultCurve.id = -1;
                    Dictionary<TsPrefabCurve, Tuple<int, TsPrefabCurve>> distances = new Dictionary<TsPrefabCurve, Tuple<int, TsPrefabCurve>>();
                    Dictionary<TsPrefabCurve, bool> visited = new Dictionary<TsPrefabCurve, bool>();
                    foreach (var curve in PrefabCurves)
                    {
                        distances[curve] = new Tuple<int, TsPrefabCurve>(Int32.MaxValue, defaultCurve);
                    }
                    foreach (var inputCurves in inputNode.InputPoints)
                    {
                        distances[PrefabCurves[inputCurves]] = new Tuple<int, TsPrefabCurve>(0, defaultCurve);
                    }
                    var actualCurve = defaultCurve;
                    while (!outputNode.OutputPoints.Contains(actualCurve.id))
                    {
                        var minVal = Int32.MaxValue;
                        var minCurve = defaultCurve;
                        foreach (var distance in distances)
                        {
                            if (!visited.ContainsKey(distance.Key))
                            {
                                if (distance.Value.Item1 < minVal)
                                {
                                    minVal = distance.Value.Item1;
                                    minCurve = distance.Key;
                                }
                            }
                        }
                        actualCurve = minCurve;
                        if (actualCurve.id == -1) break;
                        visited[actualCurve] = true;
                        foreach (var nextCurveId in actualCurve.nextLines)
                        {
                            var nextCurve = PrefabCurves[nextCurveId];
                            if (minVal + 1 < distances[nextCurve].Item1 && !visited.ContainsKey(nextCurve))
                            {
                                distances[nextCurve] = new Tuple<int, TsPrefabCurve>(minVal + 1, actualCurve);
                            }
                        }
                    }
                    if (actualCurve.id != -1)
                    {
                        List<TsPrefabCurve> path = new List<TsPrefabCurve>();
                        float length = (float)distances[actualCurve].Item1;
                        float distanceLength = 0.0f;
                        while (actualCurve.id != -1)
                        {
                            distanceLength += (float)Math.Sqrt(Math.Pow(actualCurve.start_X - actualCurve.end_X, 2) + Math.Pow(actualCurve.start_Y - actualCurve.end_Y, 2) + Math.Pow(actualCurve.start_Z - actualCurve.end_Z, 2));
                            ActualCurves.Add(actualCurve);
                            path.Add(actualCurve);
                            actualCurve = distances[actualCurve].Item2;
                        }
                        NavigationRoutes.Add(new Tuple<TsPrefabNode, TsPrefabNode>(inputNode, outputNode), new Tuple<List<TsPrefabCurve>, float>(path, distanceLength));
                    }
                }
            }

            for (var i = 0; i < mapPointCount; i++)
            {
                var mapPointBaseOffset = mapPointOffset + (i * MapPointBlockSize);
                var roadLookFlags = MemoryHelper.ReadUint8(_stream, mapPointBaseOffset + 0x01);
                var laneTypeFlags = (byte) (roadLookFlags & 0x0F);
                var laneOffsetFlags = (byte)(roadLookFlags >> 4);
                var controlNodeIndexFlags = MemoryHelper.ReadInt8(_stream, mapPointBaseOffset + 0x04);
                int laneOffset;
                switch (laneOffsetFlags)
                {
                    case 1: laneOffset = 1; break;
                    case 2: laneOffset = 2; break;
                    case 3: laneOffset = 5; break;
                    case 4: laneOffset = 10; break;
                    case 5: laneOffset = 15; break;
                    case 6: laneOffset = 20; break;
                    case 7: laneOffset = 25; break;
                    default: laneOffset = 0; break;

                }
                int laneCount;
                switch (laneTypeFlags) // TODO: Change these (not really used atm)
                {
                    case 0: laneCount = 1; break;
                    case 1: laneCount = 2; break;
                    case 2: laneCount = 4; break;
                    case 3: laneCount = 6; break;
                    case 4: laneCount = 8; break;
                    case 5: laneCount = 5; break;
                    case 6: laneCount = 7; break;
                    case 8: laneCount = 3; break;
                    case 13: laneCount = -1; break;
                    case 14: laneCount = -2; break; // auto
                    default:
                        laneCount = 1;
                        // Log.Msg($"Unknown LaneType: {laneTypeFlags}");
                        break;
                }
                sbyte controlNodeIndex = -1;
                switch (controlNodeIndexFlags)
                {
                    case 1: controlNodeIndex = 0; break;
                    case 2: controlNodeIndex = 1; break;
                    case 4: controlNodeIndex = 2; break;
                    case 8: controlNodeIndex = 3; break;
                    case 16: controlNodeIndex = 4; break;
                    case 32: controlNodeIndex = 5; break;
                }
                var prefabColorFlags = MemoryHelper.ReadUint8(_stream, mapPointBaseOffset + 0x02);

                var navFlags = MemoryHelper.ReadUint8(_stream, mapPointBaseOffset + 0x05);
                var hidden = (navFlags & 0x02) != 0; // Map Point is Control Node

                var point = new TsMapPoint
                {
                    LaneCount = laneCount,
                    LaneOffset = laneOffset,
                    Hidden = hidden,
                    PrefabColorFlags = prefabColorFlags,
                    X = MemoryHelper.ReadSingle(_stream, mapPointBaseOffset + 0x08),
                    Y = MemoryHelper.ReadSingle(_stream, mapPointBaseOffset + 0x0C),
                    Z = MemoryHelper.ReadSingle(_stream, mapPointBaseOffset + 0x10),
                    Neighbours = new List<int>(),
                    NeighbourCount = MemoryHelper.ReadInt32(_stream, mapPointBaseOffset + 0x14 + (0x04 * 6)),
                    ControlNodeIndex = controlNodeIndex
                };

                for (var x = 0; x < point.NeighbourCount; x++)
                {
                    point.Neighbours.Add(MemoryHelper.ReadInt32(_stream, mapPointBaseOffset + 0x14 + (x * 0x04)));
                }

                MapPoints.Add(point);
            }

            for (var i = 0; i < triggerPointCount; i++)
            {
                var triggerPointBaseOffset = triggerPointOffset + (i * TriggerPointBlockSize);
                var triggerPoint = new TsTriggerPoint
                {
                    TriggerId = MemoryHelper.ReadUInt32(_stream, triggerPointBaseOffset),
                    TriggerActionToken = MemoryHelper.ReadUInt64(_stream, triggerPointBaseOffset + 0x04),
                    X = MemoryHelper.ReadSingle(_stream, triggerPointBaseOffset + 0x1C),
                    Y = MemoryHelper.ReadSingle(_stream, triggerPointBaseOffset + 0x20),
                    Z = MemoryHelper.ReadSingle(_stream, triggerPointBaseOffset + 0x24),
                };
                TriggerPoints.Add(triggerPoint);
            }

            _stream = null;

        }
    }
}
