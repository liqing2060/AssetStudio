﻿using System;
using System.IO;

namespace AssetStudio
{
    public class EndianBinaryWriter : BinaryWriter
    {
        public EndianType endian;

        public EndianBinaryWriter(Stream stream, EndianType endian = EndianType.BigEndian) : base(stream)
        {
            this.endian = endian;
        }

        public long Position
        {
            get => BaseStream.Position;
            set => BaseStream.Position = value;
        }

        public bool NeedReverse(EndianType endian)
        {
            return BitConverter.IsLittleEndian != (endian == EndianType.LittleEndian);
        }

        public bool NeedReverse()
        {
            return NeedReverse(endian);
        }

        public override void Write(short value)
        {
            var buff = BitConverter.GetBytes(value);
            if (NeedReverse())
            {
                Array.Reverse(buff);
            }
            Write(buff);
        }

        public override void Write(int value)
        {
            var buff = BitConverter.GetBytes(value);
            if (NeedReverse())
            {
                Array.Reverse(buff);
            }
            Write(buff);
        }

        public void Write(int value, EndianType endian)
        {
            var buff = BitConverter.GetBytes(value);
            if (NeedReverse(endian))
            {
                Array.Reverse(buff);
            }
            Write(buff);
        }

        public override void Write(long value)
        {
            var buff = BitConverter.GetBytes(value);
            if (NeedReverse())
            {
                Array.Reverse(buff);
            }
            Write(buff);
        }

        public override void Write(ushort value)
        {
            var buff = BitConverter.GetBytes(value);
            if (NeedReverse())
            {
                Array.Reverse(buff);
            }
            Write(buff);
        }

        public override void Write(uint value)
        {
            var buff = BitConverter.GetBytes(value);
            if (NeedReverse())
            {
                Array.Reverse(buff);
            }
            Write(buff);
        }

        public override void Write(ulong value)
        {
            var buff = BitConverter.GetBytes(value);
            if (NeedReverse())
            {
                Array.Reverse(buff);
            }
            Write(buff);
        }

        public override void Write(float value)
        {
            var buff = BitConverter.GetBytes(value);
            if (NeedReverse())
            {
                Array.Reverse(buff);
            }
            Write(buff);
        }

        public override void Write(double value)
        {
            var buff = BitConverter.GetBytes(value);
            if (NeedReverse())
            {
                Array.Reverse(buff);
            }
            Write(buff);
        }
    }
}
