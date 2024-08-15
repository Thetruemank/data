using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection.Emit;
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

                        var LaneObjects = prefabItem.Prefab.Lanes;
                        List<List<TsPrefabCurve>> Lanes = new List<List<TsPrefabCurve>>();

                        foreach(TsPrefabLane lane in LaneObjects)
                        {
                            Lanes.Add(lane.curves);
                        }

                        // Catmull-Rom spline implementation
                        for (int i = 0; i < Lanes.Count; i++)
                        {
                            var lane = Lanes[i];

                            TsPrefabLook prefabLook = new TsPrefabRoadLook()
                            {
                                Color = Brushes.Red, // Choose your color
                                Width = 1f,
                                ZIndex = 1000
                            };

                            for (int j = 0; j < lane.Count; j++)
                            {

                                if (lane.Count < 4)
                                {
                                    for (int k = 0; k < lane.Count; k++)
                                    {
                                        var curveStartPoint = RenderHelper.RotatePoint(prefabStartX + lane[k].start_X, prefabStartZ + lane[k].start_Z, rot, originNode.X, originNode.Z);
                                        var curveEndPoint = RenderHelper.RotatePoint(prefabStartX + lane[k].end_X, prefabStartZ + lane[k].end_Z, rot, originNode.X, originNode.Z);
                                        prefabLook.AddPoint(curveStartPoint.X, curveStartPoint.Y);
                                        prefabLook.AddPoint(curveEndPoint.X, curveEndPoint.Y);
                                    }
                                    continue;
                                }

                                if (j == 0){
                                    var curveStartPoint = RenderHelper.RotatePoint(prefabStartX + lane[j].start_X, prefabStartZ + lane[j].start_Z, rot, originNode.X, originNode.Z);
                                    prefabLook.AddPoint(curveStartPoint.X, curveStartPoint.Y);
                                    continue;
                                }

                                if (j == lane.Count - 1){
                                    var curveEndPoint = RenderHelper.RotatePoint(prefabStartX + lane[j].end_X, prefabStartZ + lane[j].end_Z, rot, originNode.X, originNode.Z);
                                    prefabLook.AddPoint(curveEndPoint.X, curveEndPoint.Y);
                                    continue;
                                }

                                var p0 = j == 0 ? lane[j] : lane[j - 1];
                                var p1 = lane[j];
                                var p2 = j == lane.Count - 1 ? lane[j] : lane[j + 1];
                                var p3 = j == lane.Count - 2 ? lane[j] : lane[j + 2];

                                for (float t = 0; t <= 1; t += 0.05f) // Adjust the step for more/less detail
                                {
                                    var x = 0.5f * ((2 * p1.start_X) +
                                                    (-p0.start_X + p2.start_X) * t +
                                                    (2 * p0.start_X - 5 * p1.start_X + 4 * p2.start_X - p3.start_X) * t * t +
                                                    (-p0.start_X + 3 * p1.start_X - 3 * p2.start_X + p3.start_X) * t * t * t);

                                    var z = 0.5f * ((2 * p1.start_Z) +
                                                    (-p0.start_Z + p2.start_Z) * t +
                                                    (2 * p0.start_Z - 5 * p1.start_Z + 4 * p2.start_Z - p3.start_Z) * t * t +
                                                    (-p0.start_Z + 3 * p1.start_Z - 3 * p2.start_Z + p3.start_Z) * t * t * t);

                                    var rotatedPoint = RenderHelper.RotatePoint(prefabStartX + x, prefabStartZ + z, rot, originNode.X, originNode.Z);
                                    prefabLook.AddPoint(rotatedPoint.X, rotatedPoint.Y);
                                }
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