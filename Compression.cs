using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PGDTool
{
    public class Compression
    {
        public static byte[] LzssDecode(byte[] input, int outputSize)
        {
            var output = new byte[outputSize];

            var dst = 0;
            var idx = 0;
            var ctl = 2;
            while (dst < output.Length)
            {
                ctl >>= 1;
                if (ctl == 1)
                    ctl = input[idx++] | 0x100;
                int count;
                if ((ctl & 1) != 0)
                {
                    int offset = Utility.ToUInt16(input, idx);
                    idx += 2;
                    count = offset & 7;
                    if ((offset & 8) == 0)
                    {
                        count = (count << 8) | input[idx++];
                    }
                    count += 4;
                    offset >>= 4;
                    Utility.CopyOverlapped(output, dst - offset, dst, count);
                }
                else
                {
                    count = input[idx++];
                    Buffer.BlockCopy(input, idx, output, dst, count);
                    idx += count;
                }
                dst += count;
            }

            return output;
        }

        private struct Match
        {
            public int Distance;
            public int Length;

            public Match(int distance, int length)
            {
                Distance = distance;
                Length = length;
            }
        }

        private sealed class LzssSearchTree
        {
            public const int WindowSize = 0xFFF;
            public const int RingSize = 0x1000;         // 12-bit distance
            public const int RingMask = RingSize - 1;
            public const int Nil = RingSize;

            public const int MinMatch = 4;
            public const int MaxMatch = 0x3FF + 4;

            private readonly byte[] _src;
            private readonly int _srcSize;

            private readonly int[] _lson = new int[RingSize + 1];
            private readonly int[] _rson = new int[RingSize + 257];
            private readonly int[] _dad = new int[RingSize + 1];

            private readonly int[] _nodePos = new int[RingSize];

            public LzssSearchTree(byte[] src, int srcSize)
            {
                _src = src;
                _srcSize = srcSize;

                for (int i = 0; i < _dad.Length; i++)
                    _dad[i] = Nil;

                for (int i = 0; i < _lson.Length; i++)
                    _lson[i] = Nil;

                for (int i = 0; i < _rson.Length; i++)
                    _rson[i] = Nil;

                for (int i = 0; i < _nodePos.Length; i++)
                    _nodePos[i] = -1;
            }

            public void InsertOnly(int pos, int remaining)
            {
                if (remaining < MinMatch)
                    return;

                int slot = pos & RingMask;

                DeleteNode(slot);

                _nodePos[slot] = pos;

                // 限制的最大匹配大小。过小会影响压缩率
                int maxLen = Math.Min(256, remaining);

                int cmp = 1;
                int p = RingSize + 1 + _src[pos];

                _lson[slot] = Nil;
                _rson[slot] = Nil;

                while (true)
                {
                    if (cmp >= 0)
                    {
                        if (_rson[p] != Nil)
                        {
                            p = _rson[p];
                        }
                        else
                        {
                            _rson[p] = slot;
                            _dad[slot] = p;
                            return;
                        }
                    }
                    else
                    {
                        if (_lson[p] != Nil)
                        {
                            p = _lson[p];
                        }
                        else
                        {
                            _lson[p] = slot;
                            _dad[slot] = p;
                            return;
                        }
                    }

                    int oldPos = _nodePos[p];

                    int i = 1;
                    for (; i < maxLen; i++)
                    {
                        cmp = _src[pos + i] - _src[oldPos + i];
                        if (cmp != 0)
                            break;
                    }

                    if (i >= maxLen)
                        break;
                }

                ReplaceNode(p, slot);
            }

            public Match InsertAndFind(int pos, int remaining)
            {
                if (remaining < MinMatch)
                    return default;

                int slot = pos & RingMask;

                DeleteNode(slot);

                _nodePos[slot] = pos;

                int maxLen = Math.Min(MaxMatch, remaining);

                int bestLen = 0;
                int bestDist = 0;

                int cmp = 1;
                int p = RingSize + 1 + _src[pos];

                _lson[slot] = Nil;
                _rson[slot] = Nil;

                while (true)
                {
                    if (cmp >= 0)
                    {
                        if (_rson[p] != Nil)
                        {
                            p = _rson[p];
                        }
                        else
                        {
                            _rson[p] = slot;
                            _dad[slot] = p;
                            return new Match(bestDist, bestLen);
                        }
                    }
                    else
                    {
                        if (_lson[p] != Nil)
                        {
                            p = _lson[p];
                        }
                        else
                        {
                            _lson[p] = slot;
                            _dad[slot] = p;
                            return new Match(bestDist, bestLen);
                        }
                    }

                    int oldPos = _nodePos[p];
                    int distance = pos - oldPos;

                    int i = 1;

                    for (; i < maxLen; i++)
                    {
                        int a = _src[pos + i];
                        int b = _src[oldPos + i];

                        cmp = a - b;

                        if (cmp != 0)
                            break;
                    }

                    if (distance > 0 && distance <= WindowSize)
                    {
                        int allowedLen = Math.Min(i, distance - 1);

                        if (allowedLen > bestLen && allowedLen >= MinMatch)
                        {
                            bestLen = allowedLen;
                            bestDist = distance;

                            if (bestLen == maxLen)
                                break;
                        }
                    }

                    if (i >= maxLen)
                        break;
                }

                ReplaceNode(p, slot);
                return new Match(bestDist, bestLen);
            }

            private void DeleteNode(int p)
            {
                if (_dad[p] == Nil)
                    return;

                int q;

                if (_rson[p] == Nil)
                {
                    q = _lson[p];
                }
                else if (_lson[p] == Nil)
                {
                    q = _rson[p];
                }
                else
                {
                    q = _lson[p];

                    if (_rson[q] != Nil)
                    {
                        do
                        {
                            q = _rson[q];
                        }
                        while (_rson[q] != Nil);

                        _rson[_dad[q]] = _lson[q];
                        _dad[_lson[q]] = _dad[q];

                        _lson[q] = _lson[p];
                        _dad[_lson[p]] = q;
                    }

                    _rson[q] = _rson[p];
                    _dad[_rson[p]] = q;
                }

                _dad[q] = _dad[p];

                if (_rson[_dad[p]] == p)
                    _rson[_dad[p]] = q;
                else
                    _lson[_dad[p]] = q;

                _dad[p] = Nil;
            }

            private void ReplaceNode(int oldNode, int newNode)
            {
                _dad[newNode] = _dad[oldNode];
                _lson[newNode] = _lson[oldNode];
                _rson[newNode] = _rson[oldNode];

                _dad[_lson[oldNode]] = newNode;
                _dad[_rson[oldNode]] = newNode;

                if (_rson[_dad[oldNode]] == oldNode)
                    _rson[_dad[oldNode]] = newNode;
                else
                    _lson[_dad[oldNode]] = newNode;

                _dad[oldNode] = Nil;
            }
        }


        public static byte[] LzssEncode(byte[] input)
        {
            var rawBlocks = (input.Length + 254) / 255;
            var capacity = Math.Max(16, input.Length + rawBlocks + (rawBlocks + 7) / 8 + 16);
            var dst = new byte[capacity];

            var size = LzssEncode(dst, input, input.Length);
            Array.Resize(ref dst, size);
            return dst;
        }

        private static int LzssEncode(byte[] dst, byte[] src, int srcSize)
        {
            if (dst == null)
                throw new ArgumentNullException(nameof(dst));
            if (src == null)
                throw new ArgumentNullException(nameof(src));
            if ((uint)srcSize > (uint)src.Length)
                throw new ArgumentOutOfRangeException(nameof(srcSize));

            var dstPos = 0;
            var srcPos = 0;

            var rawSize = 0;
            var rawStart = 0;

            var headerBit = 0;
            var headerPos = dstPos++;
            dst[headerPos] = 0;

            var tree = new LzssSearchTree(src, srcSize);

            void Ensure(int count)
            {
                if (dstPos > dst.Length - count)
                    throw new ArgumentException("output buffer is too small", nameof(dst));
            }

            void UpdateHeader()
            {
                headerBit++;

                if (headerBit == 8)
                {
                    Ensure(1);
                    headerPos = dstPos++;
                    dst[headerPos] = 0;
                    headerBit = 0;
                }
            }

            void FlushRaw()
            {
                Ensure(1 + rawSize);

                dst[dstPos++] = (byte)rawSize;
                Buffer.BlockCopy(src, rawStart, dst, dstPos, rawSize);

                dstPos += rawSize;
                rawSize = 0;
            }

            while (srcPos != srcSize)
            {
                var remaining = srcSize - srcPos;

                var match = tree.InsertAndFind(srcPos, remaining);

                if (match.Length >= LzssSearchTree.MinMatch)
                {
                    if (rawSize != 0)
                    {
                        FlushRaw();
                        UpdateHeader();
                    }

                    dst[headerPos] |= (byte)(1 << headerBit);

                    var count = match.Length - LzssSearchTree.MinMatch;

                    if (count <= 0x07)
                    {
                        // 2-byte mode:
                        // distance: 12 bits
                        // flag bit 3 = 1
                        // count: low 3 bits
                        var word = (match.Distance << 4) | 0x0008 | count;

                        Ensure(2);
                        dst[dstPos++] = (byte)word;
                        dst[dstPos++] = (byte)(word >> 8);
                    }
                    else
                    {
                        // 3-byte mode:
                        // 24 bits = distance:12 | count:12
                        var pack = (match.Distance << 12) | count;

                        Ensure(3);
                        dst[dstPos++] = (byte)(pack >> 8);
                        dst[dstPos++] = (byte)(pack >> 16);
                        dst[dstPos++] = (byte)pack;
                    }

                    var oldSrcPos = srcPos;
                    srcPos += match.Length;

                    // 当前 srcPos 已经在 InsertAndFind 里插入过了。
                    // 匹配跨过的中间位置也要插入树，否则后续窗口不完整。
                    for (var p = oldSrcPos + 1; p < srcPos; p++)
                        tree.InsertOnly(p, srcSize - p);

                    UpdateHeader();
                }
                else
                {
                    if (rawSize == 0)
                    {
                        rawStart = srcPos;
                        rawSize = 1;
                    }
                    else
                    {
                        rawSize++;
                    }

                    srcPos++;

                    if (rawSize == 255)
                    {
                        FlushRaw();
                        UpdateHeader();
                    }
                }
            }

            if (rawSize != 0)
                FlushRaw();

            return dstPos;
        }
    }
}
