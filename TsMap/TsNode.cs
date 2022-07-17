using System;
using TsMap.Helpers;

namespace TsMap
{
    public class TsNode
    {
        public ulong Uid { get; }

        public float X { get; }
        public float Z { get; }
        public float Rotation { get; }

<<<<<<< HEAD
        public TsItem.TsItem ForwardItem { get; set; }
        public ulong ForwardItemUID { get; private set; }
        public TsItem.TsItem BackwardItem { get; set; }
        public ulong BackwardItemUID { get; private set; }


        public TsNode(TsSector sector, int fileOffset)
        {
            Uid = MemoryHelper.ReadUInt64(sector.Stream, fileOffset);
=======
        public TsItem ForwardItem { get; set; }
        public ulong ForwardItemUID { get; private set; }
        public TsItem BackwardItem { get; set; }
        public ulong BackwardItemUID { get; private set; }

        public TsNode(TsSector sector, int fileOffset)
        {
            Uid = BitConverter.ToUInt64(sector.Stream, fileOffset);
            
            ForwardItemUID = BitConverter.ToUInt64(sector.Stream, fileOffset + 0x2C);
            BackwardItemUID = BitConverter.ToUInt64(sector.Stream, fileOffset + 0x24);
            ForwardItem = null;
            BackwardItem = null;
            
            X = BitConverter.ToInt32(sector.Stream, fileOffset += 0x08) / 256f;
            Z = BitConverter.ToInt32(sector.Stream, fileOffset += 0x08) / 256f;
>>>>>>> d5c7e3d5c3abf745f5d0863649bf2f871f18fd36

            ForwardItemUID = BitConverter.ToUInt64(sector.Stream, fileOffset + 0x2C);
            BackwardItemUID = BitConverter.ToUInt64(sector.Stream, fileOffset + 0x24);
            ForwardItem = null;
            BackwardItem = null;

            X = MemoryHelper.ReadInt32(sector.Stream, fileOffset += 0x08) / 256f;
            Z = MemoryHelper.ReadInt32(sector.Stream, fileOffset += 0x08) / 256f;

            var rX = MemoryHelper.ReadSingle(sector.Stream, fileOffset += 0x04);
            var rZ = MemoryHelper.ReadSingle(sector.Stream, fileOffset + 0x08);

            var rot = Math.PI - Math.Atan2(rZ, rX);
            Rotation = (float) (rot % Math.PI * 2);
        }
    }
}
