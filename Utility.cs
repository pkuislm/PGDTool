using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGDTool
{
    public static class Utility
    {
        public static ushort ToUInt16<TArray>(TArray value, int index) where TArray : IList<byte>
        {
            return (ushort)(value[index] | value[index + 1] << 8);
        }

        public static short ToInt16<TArray>(TArray value, int index) where TArray : IList<byte>
        {
            return (short)(value[index] | value[index + 1] << 8);
        }

        public static uint ToUInt32<TArray>(TArray value, int index) where TArray : IList<byte>
        {
            return (uint)(value[index] | value[index + 1] << 8 | value[index + 2] << 16 | value[index + 3] << 24);
        }

        public static int ToInt32<TArray>(TArray value, int index) where TArray : IList<byte>
        {
            return (int)ToUInt32(value, index);
        }

        public static ulong ToUInt64<TArray>(TArray value, int index) where TArray : IList<byte>
        {
            return (ulong)ToUInt32(value, index) | ((ulong)ToUInt32(value, index + 4) << 32);
        }

        public static long ToInt64<TArray>(TArray value, int index) where TArray : IList<byte>
        {
            return (long)ToUInt64(value, index);
        }

        public static void Pack(ushort value, byte[] buf, int index)
        {
            buf[index] = (byte)(value);
            buf[index + 1] = (byte)(value >> 8);
        }

        public static void Pack(uint value, byte[] buf, int index)
        {
            buf[index] = (byte)(value);
            buf[index + 1] = (byte)(value >> 8);
            buf[index + 2] = (byte)(value >> 16);
            buf[index + 3] = (byte)(value >> 24);
        }

        public static void Pack(ulong value, byte[] buf, int index)
        {
            Pack((uint)value, buf, index);
            Pack((uint)(value >> 32), buf, index + 4);
        }

        public static void Pack(short value, byte[] buf, int index)
        {
            Pack((ushort)value, buf, index);
        }

        public static void Pack(int value, byte[] buf, int index)
        {
            Pack((uint)value, buf, index);
        }

        public static void Pack(long value, byte[] buf, int index)
        {
            Pack((ulong)value, buf, index);
        }
        public static void CopyOverlapped(byte[] data, int src, int dst, int count)
        {
            if (dst > src)
            {
                while (count > 0)
                {
                    int preceding = Math.Min(dst - src, count);
                    Buffer.BlockCopy(data, src, data, dst, preceding);
                    dst += preceding;
                    count -= preceding;
                }
            }
            else
            {
                Buffer.BlockCopy(data, src, data, dst, count);
            }
        }
    }
}
