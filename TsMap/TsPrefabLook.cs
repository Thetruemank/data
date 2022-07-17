using System.Collections.Generic;
using System.Drawing;

namespace TsMap
{
    public abstract class TsPrefabLook
    {
        public int ZIndex { get; set; }
        public Brush Color { get; set; }
        public readonly List<PointF> Points;
        public readonly TsItem.TsPrefabItem PrefabItem;

        protected TsPrefabLook(List<PointF> points, TsItem.TsPrefabItem prefabItem)
        {
            Points = points;
            PrefabItem = prefabItem;
        }

        protected TsPrefabLook() : this(new List<PointF>(), null) { }

        public void AddPoint(PointF p)
        {
            Points.Add(p);
        }

        public void AddPoint(float x, float y)
        {
            AddPoint(new PointF(x, y));
        }

        public abstract void Draw(Graphics g);
    }

    public class TsPrefabRoadLook : TsPrefabLook
    {
        public float Width { private get; set; }

        public TsPrefabRoadLook()
        {
            ZIndex = 1;
        }

        public override void Draw(Graphics g)
        {
            g.DrawLines(new Pen(Color, Width), Points.ToArray());
        }
    }

    public class TsPrefabPolyLook : TsPrefabLook
    {
        public TsPrefabPolyLook(List<PointF> points, TsItem.TsPrefabItem prefabItem) : base(points, prefabItem) { }

        public override void Draw(Graphics g)
        {
            g.FillPolygon(Color, Points.ToArray());
        }
    }
}
