using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Policy;
using TsMap.Common;
using TsMap.Helpers;
using TsMap.Helpers.Logger;
using TsMap.Map.Overlays;
using TsMap.TsItem;

namespace TsMap
{
    public class TsMapRenderer
    {
        private readonly TsMapper _mapper;
        private const float itemDrawMargin = 1000f;
        private int[] zoomCaps = { 1000, 5000, 18500, 45000 };

        private readonly Font _defaultFont = new Font("Arial", 10.0f, FontStyle.Bold);
        private readonly SolidBrush _cityShadowColor = new SolidBrush(Color.FromArgb(210, 0, 0, 0));

        public TsMapRenderer(TsMapper mapper)
        {
            _mapper = mapper;
        }

        private bool exported;

        public void Render(Graphics g, Rectangle clip, float scale, PointF startPoint, MapPalette palette, RenderFlags renderFlags = RenderFlags.All)
        {
            var startTime = DateTime.Now.Ticks;
            g.ResetTransform();
            g.FillRectangle(palette.Background, new Rectangle(0, 0, clip.Width, clip.Height));

            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-startPoint.X, -startPoint.Y);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_mapper == null)
            {
                g.DrawString("Map object not initialized", _defaultFont, palette.Error, 5, 5);
                return;
            }

            var dlcGuards = _mapper.GetDlcGuardsForCurrentGame();

            var activeDlcGuards = dlcGuards.Where(x => x.Enabled).Select(x => x.Index).ToList();

            var centerX = startPoint.X;
            var centerY = startPoint.Y;

            float totalX, totalY;

            if (clip.Width > clip.Height)
            {
                totalX = scale;
                totalY = scale * clip.Height / clip.Width;
            }
            else
            {
                totalY = scale;
                totalX = scale * clip.Width / clip.Height;
            }

            var startX = clip.X + centerX - totalX;
            var endX = clip.X + centerX + totalX;
            var startY = clip.Y + centerY - totalY;
            var endY = clip.Y + centerY + totalY;

            var scaleX = clip.Width / (endX - startX);
            var scaleY = clip.Height / (endY - startY);

            var zoomIndex = RenderHelper.GetZoomIndex(clip, scale);

            var endPoint = new PointF(startPoint.X + clip.Width / scale, startPoint.Y + clip.Height / scale);

            var ferryStartTime = DateTime.Now.Ticks;
            if (renderFlags.IsActive(RenderFlags.FerryConnections))
            {

                var ferryPen = new Pen(palette.FerryLines, 50) { DashPattern = new[] { 10f, 10f } };

                foreach (var ferryConnection in _mapper.FerryConnections)
                {
                    var connections = _mapper.LookupFerryConnection(ferryConnection.FerryPortId);

                    foreach (var conn in connections)
                    {
                        if (conn.Connections.Count == 0) // no extra nodes -> straight line
                        {
                            if (_mapper.RouteFerryPorts.ContainsKey(_mapper.FerryPortbyId[conn.StartPortToken]) ||
                                _mapper.RouteFerryPorts.ContainsKey(_mapper.FerryPortbyId[conn.EndPortToken]))
                            {
                                ferryPen = new Pen(Brushes.Red, 50) { DashPattern = new[] { 10f, 10f } };
                            }
                            else
                            {
                                ferryPen = new Pen(palette.FerryLines, 50) { DashPattern = new[] { 10f, 10f } };
                            }
                            g.DrawLine(ferryPen, conn.StartPortLocation, conn.EndPortLocation);
                            continue;
                        }

                        var startYaw = Math.Atan2(conn.Connections[0].Z - conn.StartPortLocation.Y, // get angle of the start port to the first node
                            conn.Connections[0].X - conn.StartPortLocation.X);
                        var bezierNodes = RenderHelper.GetBezierControlNodes(conn.StartPortLocation.X,
                            conn.StartPortLocation.Y, startYaw, conn.Connections[0].X, conn.Connections[0].Z,
                            conn.Connections[0].Rotation);

                        var bezierPoints = new List<PointF>
                        {
                            new PointF(conn.StartPortLocation.X, conn.StartPortLocation.Y), // start
                            new PointF(conn.StartPortLocation.X + bezierNodes.Item1.X, conn.StartPortLocation.Y + bezierNodes.Item1.Y), // control1
                            new PointF(conn.Connections[0].X - bezierNodes.Item2.X, conn.Connections[0].Z - bezierNodes.Item2.Y), // control2
                            new PointF(conn.Connections[0].X, conn.Connections[0].Z)
                        };

                        for (var i = 0; i < conn.Connections.Count - 1; i++) // loop all extra nodes
                        {
                            var ferryPoint = conn.Connections[i];
                            var nextFerryPoint = conn.Connections[i + 1];

                            bezierNodes = RenderHelper.GetBezierControlNodes(ferryPoint.X, ferryPoint.Z, ferryPoint.Rotation,
                                nextFerryPoint.X, nextFerryPoint.Z, nextFerryPoint.Rotation);

                            bezierPoints.Add(new PointF(ferryPoint.X + bezierNodes.Item1.X, ferryPoint.Z + bezierNodes.Item1.Y)); // control1
                            bezierPoints.Add(new PointF(nextFerryPoint.X - bezierNodes.Item2.X, nextFerryPoint.Z - bezierNodes.Item2.Y)); // control2
                            bezierPoints.Add(new PointF(nextFerryPoint.X, nextFerryPoint.Z)); // end
                        }

                        var lastFerryPoint = conn.Connections[conn.Connections.Count - 1];
                        var endYaw = Math.Atan2(conn.EndPortLocation.Y - lastFerryPoint.Z, // get angle of the last node to the end port
                            conn.EndPortLocation.X - lastFerryPoint.X);

                        bezierNodes = RenderHelper.GetBezierControlNodes(lastFerryPoint.X,
                            lastFerryPoint.Z, lastFerryPoint.Rotation, conn.EndPortLocation.X, conn.EndPortLocation.Y,
                            endYaw);

                        bezierPoints.Add(new PointF(lastFerryPoint.X + bezierNodes.Item1.X, lastFerryPoint.Z + bezierNodes.Item1.Y)); // control1
                        bezierPoints.Add(new PointF(conn.EndPortLocation.X - bezierNodes.Item2.X, conn.EndPortLocation.Y - bezierNodes.Item2.Y)); // control2
                        bezierPoints.Add(new PointF(conn.EndPortLocation.X, conn.EndPortLocation.Y)); // end

                        var color = palette.FerryLines;
                        try
                        {
                            if (_mapper.RouteFerryPorts.ContainsKey(_mapper.FerryPortbyId[conn.StartPortToken]) ||
                                 _mapper.RouteFerryPorts.ContainsKey(_mapper.FerryPortbyId[conn.EndPortToken]))
                            {
                                color = Brushes.Red;
                            }
                        }
                        catch { continue; }
                            
                        ferryPen = new Pen(color, 50) { DashPattern = new[] { 10f, 10f } };

                        g.DrawBeziers(ferryPen, bezierPoints.ToArray());
                    }
                }
                ferryPen.Dispose();
            }
            var ferryTime = DateTime.Now.Ticks - ferryStartTime;

            var mapAreaStartTime = DateTime.Now.Ticks;
            if (renderFlags.IsActive(RenderFlags.MapAreas))
            {
                var drawingQueue = new List<TsPrefabPolyLook>();
                foreach (var mapArea in _mapper.MapAreas)
                {
                    if (!activeDlcGuards.Contains(mapArea.DlcGuard) ||
                        mapArea.IsSecret && !renderFlags.IsActive(RenderFlags.SecretRoads) ||
                        mapArea.X < startPoint.X - itemDrawMargin || mapArea.X > endPoint.X + itemDrawMargin ||
                        mapArea.Z < startPoint.Y - itemDrawMargin || mapArea.Z > endPoint.Y + itemDrawMargin)
                    {
                        continue;
                    }

                    var points = new List<PointF>();

                    foreach (var mapAreaNode in mapArea.NodeUids)
                    {
                        var node = _mapper.GetNodeByUid(mapAreaNode);
                        if (node == null) continue;
                        points.Add(new PointF(node.X, node.Z));
                    }

                    Brush fillColor = palette.PrefabRoad;
                    var zIndex = mapArea.DrawOver ? 10 : 0;
                    if ((mapArea.ColorIndex & 0x03) == 3)
                    {
                        fillColor = palette.PrefabGreen;
                        zIndex = mapArea.DrawOver ? 13 : 3;
                    }
                    else if ((mapArea.ColorIndex & 0x02) == 2)
                    {
                        fillColor = palette.PrefabDark;
                        zIndex = mapArea.DrawOver ? 12 : 2;
                    }
                    else if ((mapArea.ColorIndex & 0x01) == 1)
                    {
                        fillColor = palette.PrefabLight;
                        zIndex = mapArea.DrawOver ? 11 : 1;
                    }

                    drawingQueue.Add(new TsPrefabPolyLook(points)
                    {
                        Color = fillColor,
                        ZIndex = zIndex
                    });
                }

                foreach (var mapArea in drawingQueue.OrderBy(p => p.ZIndex))
                {
                    mapArea.Draw(g);
                }
            }
            var mapAreaTime = DateTime.Now.Ticks - mapAreaStartTime;

            var prefabStartTime = DateTime.Now.Ticks;
            var prefabs = _mapper.Prefabs.Where(item =>
                    item.X >= startPoint.X - itemDrawMargin && item.X <= endPoint.X + itemDrawMargin && item.Z >= startPoint.Y - itemDrawMargin &&
                    item.Z <= endPoint.Y + itemDrawMargin && activeDlcGuards.Contains(item.DlcGuard))
                .ToList();

            if (renderFlags.IsActive(RenderFlags.Prefabs))
            {
                List<TsPrefabLook> drawingQueue = new List<TsPrefabLook>();

                foreach (var prefabItem in _mapper.Prefabs)
                {
                    if (!activeDlcGuards.Contains(prefabItem.DlcGuard) ||
                        prefabItem.IsSecret && !renderFlags.IsActive(RenderFlags.SecretRoads) ||
                        prefabItem.X < startPoint.X - itemDrawMargin || prefabItem.X > endPoint.X + itemDrawMargin ||
                        prefabItem.Z < startPoint.Y - itemDrawMargin || prefabItem.Z > endPoint.Y + itemDrawMargin)
                    {
                        continue;
                    }
                    
                    var originNode = _mapper.GetNodeByUid(prefabItem.Nodes[0]);
                    if (prefabItem.Prefab.PrefabNodes == null) continue;

                    if (!prefabItem.HasLooks())
                    {
                        var mapPointOrigin = prefabItem.Prefab.PrefabNodes[prefabItem.Origin];

                        var rot = (float)(originNode.Rotation - Math.PI -
                                           Math.Atan2(mapPointOrigin.RotZ, mapPointOrigin.RotX) + Math.PI / 2);

                        var prefabStartX = originNode.X - mapPointOrigin.X;
                        var prefabStartZ = originNode.Z - mapPointOrigin.Z;

                        List<int> pointsDrawn = new List<int>();

                        bool navPrefContains = _mapper.RoutePrefabs.Contains(prefabItem);

                        List<TsPrefabCurve> CurvesAdded = new List<TsPrefabCurve>();
                        // This will recursively traverse the prefab curves and add them to the lanes
                        // Basically it will spit out a list of:
                        // StartCurve -> Curve1 -> Curve2 -> ... -> EndCurve
                        List<List<TsPrefabCurve>> TraverseCurveTillEnd(TsPrefabCurve curve, List<TsPrefabCurve> curves, HashSet<int> visited = null)
                        {
                            if (visited == null)
                            {
                                visited = new HashSet<int>();
                            }

                            List<List<TsPrefabCurve>> lanes = new List<List<TsPrefabCurve>>();

                            // Add the current curve's id to the visited set
                            visited.Add(curve.id);

                            if(curve.nextLines.Count == 0)
                            {
                                lanes.Add(new List<TsPrefabCurve> { curve });
                                CurvesAdded.Add(curve);
                            }
                            else
                            {
                                for(int i = 0; i < curve.nextLines.Count; i++)
                                {
                                    var nextCurve = curves[curve.nextLines[i]];

                                    // If the next curve has already been visited, skip it
                                    if (visited.Contains(nextCurve.id))
                                    {
                                        continue;
                                    }

                                    List<List<TsPrefabCurve>> nextLanes = TraverseCurveTillEnd(nextCurve, curves, new HashSet<int>(visited));
                                    foreach (var lane in nextLanes)
                                    {
                                        lane.Insert(0, curve);
                                        lanes.Add(lane);
                                        CurvesAdded.Add(curve);
                                    }
                                }
                            }

                            return lanes;
                        }

                        // List<List<TsPrefabCurve>> TraverseCurveTillStart(TsPrefabCurve curve, List<TsPrefabCurve> curves, HashSet<int> visited = null)
                        // {
                        //     if (visited == null)
                        //     {
                        //         visited = new HashSet<int>();
                        //     }
                        // 
                        //     List<List<TsPrefabCurve>> lanes = new List<List<TsPrefabCurve>>();
                        // 
                        //     // Add the current curve's id to the visited set
                        //     visited.Add(curve.id);
                        // 
                        //     if(curve.prevLines.Count == 0)
                        //     {
                        //         lanes.Add(new List<TsPrefabCurve> { curve });
                        //         CurvesAdded.Add(curve);
                        //     }
                        //     else
                        //     {
                        //         for(int i = 0; i < curve.prevLines.Count; i++)
                        //         {
                        //             var prevCurve = curves[curve.prevLines[i]];
                        // 
                        //             // If the previous curve has already been visited, skip it
                        //             if (visited.Contains(prevCurve.id))
                        //             {
                        //                 continue;
                        //             }
                        // 
                        //             List<List<TsPrefabCurve>> prevLanes = TraverseCurveTillStart(prevCurve, curves, new HashSet<int>(visited));
                        //             foreach (var lane in prevLanes)
                        //             {
                        //                 lane.Add(curve);
                        //                 lanes.Add(lane);
                        //                 CurvesAdded.Add(curve);
                        //             }
                        //         }
                        //     }
                        // 
                        //     return lanes;
                        // }

                        List<List<TsPrefabCurve>> Lanes = new List<List<TsPrefabCurve>>();
                        // ^ This array is in the following format:
                        // List <
                        //  StartCurve -> Curve1 -> Curve2 -> ... -> EndCurve
                        // >

                        for (int i = 0; i < prefabItem.Prefab.PrefabCurves.Count; i++)
                        {
                            var curve = prefabItem.Prefab.PrefabCurves[i];

                            if(curve.prevLines.Count == 0 && curve.nextLines.Count > 0)
                            {
                                List<List<TsPrefabCurve>> lanes = TraverseCurveTillEnd(curve, prefabItem.Prefab.PrefabCurves);
                                Lanes.AddRange(lanes);
                            }
                            else if(curve.prevLines.Count > 0 && curve.nextLines.Count == 0)
                            {
                                // This wasn't needed after all. Will keep it here just in case.
                                //List<List<TsPrefabCurve>> lanes = TraverseCurveTillStart(curve, prefabItem.Prefab.PrefabCurves);
                                //Lanes.AddRange(lanes);
                            }
                            else if (curve.prevLines.Count == 0 && curve.nextLines.Count == 0)
                            {
                                // This means that the prefab only has a start and an end point.
                                Lanes.Add(new List<TsPrefabCurve> { curve });
                            }
                        }

                        Lanes = Lanes
                            .GroupBy(lane => String.Join(",", lane.Select(curve => curve.id)))
                            .Select(group => group.First())
                            .ToList();


                        if(Lanes.Count == 0) continue;

                        // Debug rendering for lane routes.
                        // var currentLaneToRender = DateTime.Now.Second;
                        // while (currentLaneToRender > Lanes.Count)
                        // {
                        //     currentLaneToRender -= Lanes.Count;
                        // }
                        // currentLaneToRender = -1; // Render all lanes at once instead of changing it each second.
                        // 
                        // for (int i = 0; i < Lanes.Count; i++)
                        // {
                        //     var color = Brushes.Red;
                        //     if (i % 2 == 0)
                        //     {
                        //         color = Brushes.Blue;
                        //     }
                        // 
                        //     TsPrefabLook prefabLook = new TsPrefabRoadLook()
                        //     {
                        //         Color = color,
                        //         Width = 1f,
                        //         ZIndex = 1000
                        //     };
                        // 
                        // 
                        //     if(i != currentLaneToRender && currentLaneToRender != -1) continue;
                        //     
                        //     for (int j = 0; j < Lanes[i].Count; j++)
                        //     {
                        //         var curveStartPoint = RenderHelper.RotatePoint(prefabStartX + Lanes[i][j].start_X, prefabStartZ + Lanes[i][j].start_Z, rot, originNode.X, originNode.Z);
                        //         var curveEndPoint = RenderHelper.RotatePoint(prefabStartX + Lanes[i][j].end_X, prefabStartZ + Lanes[i][j].end_Z, rot, originNode.X, originNode.Z);
                        //         prefabLook.AddPoint(curveStartPoint.X, curveStartPoint.Y);
                        //         prefabLook.AddPoint(curveEndPoint.X, curveEndPoint.Y);
                        //     }
                        // 
                        //     drawingQueue.Add(prefabLook);
                        // }
                        // 
                        // int resolution = 5; // How many points each bezier has

                        // TODO: Add bezier drawing logic here!
                        for(int i = 0; i < Lanes.Count; i++)
                        {
                            List<PointF> anchor = new List<PointF>();

                            foreach(TsPrefabCurve curve in Lanes[i])
                            {
                                if(anchor.Count == 0)
                                {
                                    var curveStartPoint = RenderHelper.RotatePoint(prefabStartX + curve.start_X, prefabStartZ + curve.start_Z, rot, originNode.X, originNode.Z);
                                    var curveEndPoint = RenderHelper.RotatePoint(prefabStartX + curve.end_X, prefabStartZ + curve.end_Z, rot, originNode.X, originNode.Z);
                                    anchor.Add(curveStartPoint);
                                    anchor.Add(curveEndPoint);
                                } else 
                                {
                                    var curveEndPoint = RenderHelper.RotatePoint(prefabStartX + curve.end_X, prefabStartZ + curve.end_Z, rot, originNode.X, originNode.Z);
                                    anchor.Add(curveEndPoint);
                                }
                            }

                            List<PointF> point = new List<PointF>();

                            void GetPathPoints()
                            {
                                PointF[] temp_1 = new PointF[anchor.Count];
                                for (int j = 0; j < temp_1.Length; j++)
                                {//Get the anchor point coordinates
                                    temp_1[j] = anchor[j];
                                }
                                point = new List<PointF>();//The linked list set of points on the final Bezier curve
                                float pointNumber = 10;//The number of points on the Bezier curve
                                PointF[] temp_2;
                                PointF[] temp_3;
                                for (int pointIndex = 0; pointIndex <= (int)pointNumber; pointIndex++)
                                {
                                    temp_3 = temp_1;
                                    for (int j = temp_3.Length - 1; j > 0; j--)
                                    {
                                        temp_2 = new PointF[j];
                                        for (int k = 0; k < j; k++)
                                        {
                                            temp_2[k] =  new PointF(temp_3[k].X + (temp_3[k + 1].X - temp_3[k].X) * pointIndex / pointNumber, temp_3[k].Y + (temp_3[k + 1].Y - temp_3[k].Y) * pointIndex / pointNumber);
                                        }
                                        temp_3 = temp_2;
                                    }
                                    PointF find = temp_3[0];
                                    point.Add(find);
                                }
                            }

                            GetPathPoints();

                            var color = Brushes.Red;
                            if (i % 2 == 0) color = Brushes.Orange;

                            TsPrefabLook prefabLook = new TsPrefabRoadLook()
                            {
                                Color = color,
                                Width = 1f,
                                ZIndex = 1000
                            };

                            foreach (var p in point)
                            {
                                prefabLook.AddPoint(p.X, p.Y);
                            }

                            drawingQueue.Add(prefabLook);
                        }


                    }

                    prefabItem.GetLooks().ForEach(x => drawingQueue.Add(x));

                    foreach (var prefabLook in drawingQueue.OrderBy(p => p.ZIndex))
                    {
                        prefabLook.Draw(g);
                    }
                }

            }
            var prefabTime = DateTime.Now.Ticks - prefabStartTime;

            var roadStartTime = DateTime.Now.Ticks;
            if (renderFlags.IsActive(RenderFlags.Roads))
            {
                var roads = _mapper.Roads.Where(item =>
                        item.X >= startPoint.X - itemDrawMargin && item.X <= endPoint.X + itemDrawMargin && item.Z >= startPoint.Y - itemDrawMargin &&
                        item.Z <= endPoint.Y + itemDrawMargin && activeDlcGuards.Contains(item.DlcGuard))
                    .ToList();

                foreach (var road in _mapper.Roads)
                {
                    if (!activeDlcGuards.Contains(road.DlcGuard) ||
                        road.IsSecret && !renderFlags.IsActive(RenderFlags.SecretRoads) ||
                        road.X < startPoint.X - itemDrawMargin || road.X > endPoint.X + itemDrawMargin ||
                        road.Z < startPoint.Y - itemDrawMargin || road.Z > endPoint.Y + itemDrawMargin)
                    {
                        continue;
                    }

                    var startNode = road.GetStartNode();
                    var endNode = road.GetEndNode();

                    if (!road.HasPoints())
                    {
                        var newPoints = new List<PointF>();

                        var sx = startNode.X;
                        var sz = startNode.Z;
                        var ex = endNode.X;
                        var ez = endNode.Z;

                        var radius = Math.Sqrt(Math.Pow(sx - ex, 2) + Math.Pow(sz - ez, 2));

                        var tanSx = Math.Cos(-(Math.PI * 0.5f - startNode.Rotation)) * radius;
                        var tanEx = Math.Cos(-(Math.PI * 0.5f - endNode.Rotation)) * radius;
                        var tanSz = Math.Sin(-(Math.PI * 0.5f - startNode.Rotation)) * radius;
                        var tanEz = Math.Sin(-(Math.PI * 0.5f - endNode.Rotation)) * radius;

                        for (var i = 0; i < 8; i++)
                        {
                            var s = i / (float)(8 - 1);
                            var x = (float)TsRoadLook.Hermite(s, sx, ex, tanSx, tanEx);
                            var z = (float)TsRoadLook.Hermite(s, sz, ez, tanSz, tanEz);
                            newPoints.Add(new PointF(x, z));
                        }
                        road.AddPoints(newPoints);
                    }

                    var roadWidth = road.RoadLook.GetWidth();
                    Pen roadPen;
                    if (road.IsSecret)
                    {
                        if (zoomIndex < 3)
                        {
                            roadPen = new Pen(palette.Road, roadWidth) { DashPattern = new[] { 1f, 1f } };
                        }
                        else // zoomed out with DashPattern causes OutOfMemory Exception
                        {
                            roadPen = new Pen(palette.Road, roadWidth);
                        }
                    }
                    else
                    {
                        roadPen = new Pen(palette.Road, roadWidth);
                    }

                    var color = palette.Road;
                    if (_mapper.RouteRoads.Contains(road)) color = Brushes.Red;

                    g.DrawCurve(new Pen(color, roadWidth), road.GetPoints().ToArray());
                    roadPen.Dispose();
                }
            }
            var roadTime = DateTime.Now.Ticks - roadStartTime;

            var mapOverlayStartTime = DateTime.Now.Ticks;
            if (renderFlags.IsActive(RenderFlags.MapOverlays))
            {
                foreach (var mapOverlay in _mapper.OverlayManager.GetOverlays())
                {
                    if (!activeDlcGuards.Contains(mapOverlay.DlcGuard) ||
                        mapOverlay.IsSecret && !renderFlags.IsActive(RenderFlags.SecretRoads) ||
                        mapOverlay.Position.X < startPoint.X - itemDrawMargin ||
                        mapOverlay.Position.X > endPoint.X + itemDrawMargin ||
                        mapOverlay.Position.Y < startPoint.Y - itemDrawMargin ||
                        mapOverlay.Position.Y > endPoint.Y + itemDrawMargin)
                    {
                        continue;
                    }

                    var b = mapOverlay.GetBitmap();

                    if (b == null || !renderFlags.IsActive(RenderFlags.BusStopOverlay) && mapOverlay.OverlayType == OverlayType.BusStop) continue;
                    g.DrawImage(b, mapOverlay.Position.X - (b.Width / 2f), mapOverlay.Position.Y - (b.Height / 2f),
                        b.Width, b.Height);

                }
            }
            var mapOverlayTime = DateTime.Now.Ticks - mapOverlayStartTime;

            var cityStartTime = DateTime.Now.Ticks;
            if (renderFlags.IsActive(RenderFlags.CityNames)) // TODO: Fix position and scaling
            {
                var cityFont = new Font("Arial", 100 + zoomCaps[zoomIndex] / 100, FontStyle.Bold);

                foreach (var city in _mapper.Cities)
                {
                    var name = _mapper.Localization.GetLocaleValue(city.City.LocalizationToken) ?? city.City.Name;
                    var node = _mapper.GetNodeByUid(city.NodeUid);
                    var coords = (node == null) ? new PointF(city.X, city.Z) : new PointF(node.X, node.Z);
                    if (city.City.XOffsets.Count > zoomIndex && city.City.YOffsets.Count > zoomIndex)
                    {
                        coords.X += city.City.XOffsets[zoomIndex] / (scale * zoomCaps[zoomIndex]);
                        coords.Y += city.City.YOffsets[zoomIndex] / (scale * zoomCaps[zoomIndex]);
                    }

                    var textSize = g.MeasureString(name, cityFont);
                    g.DrawString(name, cityFont, _cityShadowColor, coords.X + 2, coords.Y + 2);
                    g.DrawString(name, cityFont, palette.CityName, coords.X, coords.Y);
                }
                cityFont.Dispose();
            }
            var cityTime = DateTime.Now.Ticks - cityStartTime;

            g.ResetTransform();
            var elapsedTime = DateTime.Now.Ticks - startTime;
            if (renderFlags.IsActive(RenderFlags.TextOverlay))
            {
                g.DrawString(
                    $"DrawTime: {elapsedTime / TimeSpan.TicksPerMillisecond} ms, x: {startPoint.X}, y: {startPoint.Y}, scale: {scale}",
                    _defaultFont, Brushes.WhiteSmoke, 5, 5);

                g.FillRectangle(new SolidBrush(Color.FromArgb(100, 0, 0, 0)), 5, 20, 150, 150);
                g.DrawString($"Road: {roadTime / TimeSpan.TicksPerMillisecond}ms", _defaultFont, Brushes.White, 10, 40);
                g.DrawString($"Prefab: {prefabTime / TimeSpan.TicksPerMillisecond}ms", _defaultFont, Brushes.White, 10, 55);
                g.DrawString($"Ferry: {ferryTime / TimeSpan.TicksPerMillisecond}ms", _defaultFont, Brushes.White, 10, 70);
                g.DrawString($"MapOverlay: {mapOverlayTime / TimeSpan.TicksPerMillisecond}ms", _defaultFont, Brushes.White, 10, 85);
                g.DrawString($"MapArea: {mapAreaTime / TimeSpan.TicksPerMillisecond}ms", _defaultFont, Brushes.White, 10, 115);
                g.DrawString($"City: {cityTime / TimeSpan.TicksPerMillisecond}ms", _defaultFont, Brushes.White, 10, 130);
            }

            if(!exported)
            {
                Logger.Instance.Info("Start export");
                Exporter.Export(_mapper);
                Logger.Instance.Info("End export");
                exported = true;
            }

        }
    }
}