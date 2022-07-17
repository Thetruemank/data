using System.Collections.Generic;
using System.Drawing;

namespace TsMap
{
    public class TsFerryPoint
    {
        public float X;
        public float Z;
        public double Rotation;

        public TsFerryPoint(float x, float z)
        {
            X = x;
            Z = z;
        }
        public void SetRotation(double rot)
        {
            Rotation = rot;
        }
    }

    public class TsFerryConnection
    {
        public ulong StartPortToken { get; set; }
        public PointF StartPortLocation { get; private set; }
        public ulong EndPortToken { get; set; }
        public PointF EndPortLocation { get; private set; }
        public List<TsFerryPoint> Connections = new List<TsFerryPoint>();

        public int Price;
        public int Time;
        public int Distance;

<<<<<<< HEAD
        public void AddConnectionPosition(int index, float x, float z)
=======
        public void AddConnectionPosition(float x, float z)
>>>>>>> d5c7e3d5c3abf745f5d0863649bf2f871f18fd36
        {
            if (Connections.Count > index) return;
            Connections.Add(new TsFerryPoint(x / 256, z / 256));
        }
        public void AddRotation(int index, double rot)
        {
            if (Connections.Count <= index) return;
            Connections[index].SetRotation(rot);
        }

        public void SetPortLocation(ulong ferryPortId, float x, float z)
        {
            if (ferryPortId == StartPortToken)
            {
                StartPortLocation = new PointF(x, z);
            }
            else if (ferryPortId == EndPortToken)
            {
                EndPortLocation = new PointF(x, z);
            }
        }
    }
}
