using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace PGDTool
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if(args.Length < 3)
            {
                Console.WriteLine("Usage: \nPGD.exe [d] [input] [output]\nPGD.exe [p] [inputPGD] [inputPNG] [output]");
                return;
            }
            if (args[0] == "d")
            {
                PGDImage image = new(args[1]);
                image.ToPNG(args[2]);
            }
            else if (args[0] == "p")
            {
                PGDImage image = new(args[1]);
                image.FromPNG(args[2], args[3]);
            }
            else
            {
                Console.Write("Unrecognized mode");
            }
        }
    }

    public struct PGDInfo
    {
        //"GE"
        public ushort sig;
        public ushort dataOffsetSize;//sizeof(sig .. unk) == 32

        public int posX;
        public int posY;
        public int imageWidth;
        public int imageHeight;
        public int widthOffset;
        public int heightOffset;

        public ushort compressionMethod;//LZE(xt) = 1(32bpp), LZY(uv) = 2(24bpp), LZP(ng) = 3(24/32bpp)
        public ushort unk;

        public PGDInfo(BinaryReader br)
        {
            sig = br.ReadUInt16();
            dataOffsetSize = br.ReadUInt16();
            posX = br.ReadInt32();
            posY = br.ReadInt32();
            imageWidth = br.ReadInt32();
            imageHeight = br.ReadInt32();
            widthOffset = br.ReadInt32();
            heightOffset = br.ReadInt32();
            compressionMethod = br.ReadUInt16();
            unk = br.ReadUInt16();
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(sig);
            bw.Write(dataOffsetSize);
            bw.Write(posX);
            bw.Write(posY);
            bw.Write(imageWidth);
            bw.Write(imageHeight);
            bw.Write(widthOffset);
            bw.Write(heightOffset);
            bw.Write(compressionMethod);
            bw.Write(unk);
        }
    }

    public struct PGDLzInfo
    {
        public int unpackedSize;
        public int packedSize;

        public PGDLzInfo(BinaryReader br)
        {
            unpackedSize = br.ReadInt32();
            packedSize = br.ReadInt32();
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(unpackedSize);
            bw.Write(packedSize);
        }
    }

    public class PGDImage
    {
        private string _origImgPath;
        public PGDInfo imageHeader;
        public PGDLzInfo compressInfo;

        public PGDImage(string path)
        {
            _origImgPath = path;
            var br = new BinaryReader(new FileStream(_origImgPath, FileMode.Open, FileAccess.Read));

            imageHeader = new PGDInfo(br);
            if (imageHeader.sig != 0x4547 || imageHeader.dataOffsetSize != 32)
            {
                throw new PGDExpection("Invalid EG image.");
            }
            compressInfo = new PGDLzInfo(br);

            br.Close();
        }

        public void ToPNG(string output)
        {
            var br = new BinaryReader(new FileStream(_origImgPath, FileMode.Open, FileAccess.Read));
            br.BaseStream.Position = 32 + 8;

            var compressed = br.ReadBytes(compressInfo.packedSize);

            switch (imageHeader.compressionMethod)
            {
                case 1: //LZE(xt)
                {
                    var data = new PGDLZEData();
                    var image = Image.LoadPixelData<Bgra32>(data.GetPixels(this, compressed).Item2, imageHeader.imageWidth, imageHeader.imageHeight);
                    image.SaveAsPng(output);
                    break;
                }
                case 2: //LZY(uv)
                {
                    var data = new PGDLZYData();
                    var image = Image.LoadPixelData<Bgr24>(data.GetPixels(this, compressed).Item2, imageHeader.imageWidth, imageHeader.imageHeight);
                    image.SaveAsPng(output);
                    break;
                }
                case 3: //LZP(ng)
                {
                    var data = new PGDLZPData();
                    var (bpp, pixels) = data.GetPixels(this, compressed);
                    if (bpp == 24)
                    {
                        var image = Image.LoadPixelData<Bgr24>(pixels, imageHeader.imageWidth, imageHeader.imageHeight);
                        image.SaveAsPng(output);
                    }
                    else
                    {
                        var image = Image.LoadPixelData<Bgra32>(pixels, imageHeader.imageWidth, imageHeader.imageHeight);
                        image.SaveAsPng(output);
                    }
                    break;
                }
            }

        }

        public void FromPNG(string input, string output)
        {
            var image = Image.Load(input);
            if (imageHeader.imageWidth != image.Width || imageHeader.imageHeight != image.Height)
            {
                throw new PGDExpection($"Incorrect image size({image.Width}x{image.Height} != {imageHeader.imageWidth}x{imageHeader.imageHeight}).");
            }

            var fs = new FileStream(output, FileMode.Create, FileAccess.ReadWrite);
            var bw = new BinaryWriter(fs);
            imageHeader.Write(bw);

            var bpp = 24;
            var pixels = Array.Empty<byte>();
            var extra = 0;
            switch (imageHeader.compressionMethod)
            {
                case 1: //LZE(xt)
                {
                    var data = new PGDLZEData();
                    (bpp, pixels) = data.SetPixels(image);
                    break;
                }
                case 2: //LZY(uv)
                {
                    var data = new PGDLZYData();
                    (bpp, pixels) = data.SetPixels(image);
                    extra = (image.Width * image.Height) >> 1;//uv channel
                    break;
                }
                case 3: //LZP(ng)
                {
                    var data = new PGDLZPData();
                    (bpp, pixels) = data.SetPixels(image);
                    extra = 8 + image.Height;//lzp_header + filter_ctl bytes
                    break;
                }
            }

            var lzHeader = new PGDLzInfo
            {
                unpackedSize = image.Width * image.Height * (bpp >> 3) + extra,
                packedSize = pixels.Length
            };
            lzHeader.Write(bw);
            bw.Write(pixels);

            bw.Close();
            fs.Close();
        }
    }

    public interface PGDLZData
    {
        //bpp, pixelData
        public (int, byte[]) GetPixels(PGDImage info, byte[] data);
        public (int, byte[]) SetPixels(Image image);
    }

    public class PGDLZEData : PGDLZData
    {
        static byte[] LzeRestore(byte[] input)
        {
            var result = new byte[input.Length];
            var pixelSize = input.Length >> 2;

            var idx = 0;
            for (var i = 0; i < pixelSize; i++)
            {
                result[idx + 3] = input[i + pixelSize * 0];
                result[idx + 2] = input[i + pixelSize * 1];
                result[idx + 1] = input[i + pixelSize * 2];
                result[idx + 0] = input[i + pixelSize * 3];
                idx += 4;
            }

            return result;
        }

        //Convert BGRABGRA.... to AAA...RRR...GGG...BBB
        static byte[] LzeMerge(byte[] input)
        {
            var result = new byte[input.Length];
            var pixelSize = input.Length >> 2;

            var idx = 0;
            for (var i = 0; i < pixelSize; i++)
            {
                result[i + pixelSize * 0] = input[idx + 3];
                result[i + pixelSize * 1] = input[idx + 2];
                result[i + pixelSize * 2] = input[idx + 1];
                result[i + pixelSize * 3] = input[idx + 0];
                idx += 4;
            }

            return result;
        }

        public (int, byte[]) GetPixels(PGDImage info, byte[] data)
        {
            return (32, LzeRestore(Compression.LzssDecode(data, info.compressInfo.unpackedSize)));
        }

        public (int, byte[]) SetPixels(Image image)
        {
            var pixels = new byte[image.Height * image.Width * 4];
            image.CloneAs<Bgra32>().CopyPixelDataTo(pixels);
            return (32, Compression.LzssEncode(LzeMerge(pixels)));
        }
    }

    //24bpp
    //To use this compress mode, image.height % 8 and image.width % 8 must be 0
    public class PGDLZYData : PGDLZData
    {
        public static byte[] Bgr24ToYuv411(
            byte[] srcBgr24,
            int width,
            int height)
        {

            int pixelCount = checked(width * height);
            int uvSize = pixelCount >> 2;
            int dstSize = uvSize * 2 + pixelCount;

            var dst = new byte[dstSize];

            int uBase = 0;
            int vBase = uvSize;
            int yBase = uvSize * 2;

            int block = 0;

            for (int y = 0; y < height; y += 2)
            {
                int srcRow0 = y * width * 3;
                int srcRow1 = srcRow0 + width * 3;

                int yRow0 = yBase + y * width;
                int yRow1 = yRow0 + width;

                for (int x = 0; x < width; x += 2)
                {
                    int p0 = srcRow0 + x * 3;
                    int p1 = p0 + 3;
                    int p2 = srcRow1 + x * 3;
                    int p3 = p2 + 3;

                    int y1 = RGBToY(srcBgr24[p0 + 2], srcBgr24[p0 + 1], srcBgr24[p0 + 0]);
                    int y2 = RGBToY(srcBgr24[p1 + 2], srcBgr24[p1 + 1], srcBgr24[p1 + 0]);
                    int y3 = RGBToY(srcBgr24[p2 + 2], srcBgr24[p2 + 1], srcBgr24[p2 + 0]);
                    int y4 = RGBToY(srcBgr24[p3 + 2], srcBgr24[p3 + 1], srcBgr24[p3 + 0]);

                    dst[yRow0 + x + 0] = (byte)y1;
                    dst[yRow0 + x + 1] = (byte)y2;
                    dst[yRow1 + x + 0] = (byte)y3;
                    dst[yRow1 + x + 1] = (byte)y4;

                    int r =
                        srcBgr24[p0 + 2] +
                        srcBgr24[p1 + 2] +
                        srcBgr24[p2 + 2] +
                        srcBgr24[p3 + 2];

                    int g =
                        srcBgr24[p0 + 1] +
                        srcBgr24[p1 + 1] +
                        srcBgr24[p2 + 1] +
                        srcBgr24[p3 + 1];

                    int b =
                        srcBgr24[p0 + 0] +
                        srcBgr24[p1 + 0] +
                        srcBgr24[p2 + 0] +
                        srcBgr24[p3 + 0];

                    r >>= 2;
                    g >>= 2;
                    b >>= 2;

                    int u = -(173 * r) - (339 * g) + (512 * b);
                    int v = (512 * r) - (429 * g) - (83 * b);


                    dst[uBase + block] = (byte)(u >> 10);
                    dst[vBase + block] = (byte)(v >> 10);

                    block++;
                }
            }

            return dst;
        }

        public static byte[] Yuv411ToBgr24(
        byte[] srcYuv411,
        int width,
        int height)
        {
            int pixelCount = checked(width * height);
            int uvSize = pixelCount >> 2;
            int dstSize = pixelCount * 3;

            var dst = new byte[dstSize];

            int uBase = 0;
            int vBase = uvSize;
            int yBase = uvSize * 2;

            int block = 0;

            for (int y = 0; y < height; y += 2)
            {
                int dstRow0 = y * width * 3;
                int dstRow1 = dstRow0 + width * 3;

                int yRow0 = yBase + y * width;
                int yRow1 = yRow0 + width;

                for (int x = 0; x < width; x += 2)
                {
                    int u = ToSigned(srcYuv411[uBase + block]);
                    int v = ToSigned(srcYuv411[vBase + block]);

                    YuvToRGB(dst, dstRow0 + x * 3, srcYuv411[yRow0 + x + 0], u, v);
                    YuvToRGB(dst, dstRow0 + (x + 1) * 3, srcYuv411[yRow0 + x + 1], u, v);
                    YuvToRGB(dst, dstRow1 + x * 3, srcYuv411[yRow1 + x + 0], u, v);
                    YuvToRGB(dst, dstRow1 + (x + 1) * 3, srcYuv411[yRow1 + x + 1], u, v);

                    block++;
                }
            }

            return dst;
        }

        private static int RGBToY(int r, int g, int b)
        {
            var y = ((306 * r) + (601 * g) + (116 * b)) >> 10;
            return Math.Clamp(y, 0, 255);
        }

        private static void YuvToRGB(
            Span<byte> dstBgr24,
            int offset,
            int y,
            int u,
            int v)
        {
            //   Y = ( 306R + 601G + 116B) >> 10
            //   U = (-173R - 339G + 512B) >> 10
            //   V = ( 512R - 429G -  83B) >> 10
            int r = (1025 * y + 1436 * v) >> 10;
            int g = (1025 * y - 351 * u - 731 * v) >> 10;
            int b = (1025 * y + 1816 * u + 1 * v) >> 10;

            dstBgr24[offset + 0] = (byte)Math.Clamp(b, 0, 255);
            dstBgr24[offset + 1] = (byte)Math.Clamp(g, 0, 255);
            dstBgr24[offset + 2] = (byte)Math.Clamp(r, 0, 255);
        }

        private static int ToSigned(byte value)
        {
            return value < 128 ? value : value - 256;
        }

        public (int, byte[]) GetPixels(PGDImage info, byte[] data)
        {
            var decompressed = Compression.LzssDecode(data, info.compressInfo.unpackedSize);
            return (24, Yuv411ToBgr24(decompressed, info.imageHeader.imageWidth, info.imageHeader.imageHeight));
        }
    
        public (int, byte[]) SetPixels(Image image)
        {
            var pixels = new byte[image.Height * image.Width * 3];
            image.CloneAs<Bgr24>().CopyPixelDataTo(pixels);
            return (8, Compression.LzssEncode(Bgr24ToYuv411(pixels, image.Width, image.Height)));
        }
    }

    //24/32bpp
    public class PGDLZPData : PGDLZData
    {
        private const byte PNG_FILTER_SUB = 1 << 0;
        private const byte PNG_FILTER_UP = 1 << 1;
        private const byte PNG_FILTER_AVG = 1 << 2;
        private const byte PNG_FILTER_ALL = PNG_FILTER_SUB | PNG_FILTER_UP | PNG_FILTER_AVG;

        private const byte PNG_FILTER_END = PNG_FILTER_AVG;

        private struct PGDLZPHeader
        {
            public short pngFilter;
            public short bpp;
            public short width;
            public short height;
        }

        byte[] PNGToRGB(PGDLZPHeader header, byte[] imageData)
        {
            var src = 8; //Skip header
            var pixelSize = header.bpp / 8;
            var stride = header.width * pixelSize;
            var output = new byte[header.height * stride];
            var ctl = src;

            src += header.height;

            var dst = 0;
            for (var row = 0; row < header.height; ++row)
            {
                var c = imageData[ctl++];
                if (0 != (c & PNG_FILTER_SUB))
                {
                    Buffer.BlockCopy(imageData, src, output, dst, pixelSize);
                    var prev = dst; //作为参考的前一个像素点的位置，此处是位于当前行的起始位置
                    var count = stride - pixelSize; //去掉第一个像素

                    src += pixelSize;
                    dst += pixelSize;
                    while (count-- > 0)
                    {
                        output[dst++] = (byte)(output[prev++] - imageData[src++]);
                    }
                }
                else if (0 != (c & PNG_FILTER_UP))
                {
                    var prev = dst - stride; //作为参考的前一个像素点的位置，次数是位于上一行的起始位置
                    var count = stride; //一整行
                    while (count-- > 0)
                    {
                        output[dst++] = (byte)(output[prev++] - imageData[src++]);
                    }
                }
                else //PNG_FILTER_AVG
                {
                    Buffer.BlockCopy(imageData, src, output, dst, pixelSize);
                    dst += pixelSize;
                    src += pixelSize;
                    var lpUp = dst - stride;
                    var lpSub = dst - pixelSize;
                    var count = stride - pixelSize;
                    while (count-- > 0)
                    {
                        output[dst++] = (byte)(((output[lpUp++] + output[lpSub++]) >> 1) - imageData[src++]);
                    }
                }
            }

            return output;
        }

        //类似PNG，给每一行应用不同的Filter并打分，取最小得分，此时理论上lz压缩效率最高
        public static class LineScore
        {
            private static readonly int[] AbsTable = BuildAbsTable();

            private static int[] BuildAbsTable()
            {
                int[] table = new int[256];

                for (int i = 0; i < 256; i++)
                {
                    int s = i < 128 ? i : i - 256;
                    table[i] = Math.Abs(s);
                }

                return table;
            }

            public static int Score(ReadOnlySpan<byte> line)
            {
                int score = 0;

                for (int i = 0; i < line.Length; i++)
                {
                    score += AbsTable[line[i]];
                }

                Span<int> last = stackalloc int[4096];

                for (int i = 0; i < last.Length; i++)
                {
                    last[i] = -1;
                }

                var repeatBonus = 0;

                for (var i = 0; i + 4 <= line.Length; i++)
                {
                    var key = ReadU32(line, i);
                    var h = Hash4(key) & 4095;

                    var prev = last[h];
                    last[h] = i;

                    if (prev < 0)
                    {
                        continue;
                    }

                    var distance = i - prev;

                    if (distance <= 0 || distance > 0xFFF)
                    {
                        continue;
                    }

                    var maxLen = Math.Min(64, line.Length - i);
                    var len = CountMatch(line, prev, i, maxLen);

                    if (len >= 4)
                    {
                        repeatBonus += len * 12;
                        i += len - 1;
                    }
                }

                return score - repeatBonus;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint ReadU32(ReadOnlySpan<byte> s, int i)
            {
                return
                    s[i] |
                    ((uint)s[i + 1] << 8) |
                    ((uint)s[i + 2] << 16) |
                    ((uint)s[i + 3] << 24);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int Hash4(uint x)
            {
                x ^= x >> 16;
                x *= 0x7FEB352D;
                x ^= x >> 15;
                return unchecked((int)x);
            }

            private static int CountMatch(ReadOnlySpan<byte> s, int a, int b, int maxLen)
            {
                int n = 0;

                while (n < maxLen && s[a + n] == s[b + n])
                    n++;

                return n;
            }
        }

        private static bool TryFilterLine(
            ReadOnlySpan<byte> srcLine,
            ReadOnlySpan<byte> prevLine,
            Span<byte> dstLine,
            int bytesPerPixel,
            int line,
            int filter)
        {
            switch (filter)
            {
                case PNG_FILTER_SUB:
                {
                    srcLine.Slice(0, bytesPerPixel).CopyTo(dstLine);

                    for (var i = bytesPerPixel; i < srcLine.Length; i++)
                    {
                        dstLine[i] = (byte)(srcLine[i - bytesPerPixel] - srcLine[i]);
                    }
                    return true;
                }

                case PNG_FILTER_UP:
                {
                    if (line == 0)
                        return false;

                    for (var i = 0; i < srcLine.Length; i++)
                    {
                        dstLine[i] = (byte)(prevLine[i] - srcLine[i]);
                    }
                    return true;
                }

                case PNG_FILTER_AVG:
                {
                    if (line == 0)
                        return false;

                    srcLine.Slice(0, bytesPerPixel).CopyTo(dstLine);

                    for (var i = bytesPerPixel; i < srcLine.Length; i++)
                    {
                        var predictor = (srcLine[i - bytesPerPixel] + prevLine[i]) >> 1;
                        dstLine[i] = (byte)(predictor - srcLine[i]);
                    }
                    return true;
                }

                default:
                    return false;
            }
        }
        private static byte[] RGBToPNG(PGDLZPHeader header, byte[] input)
        {

            var lineSize = header.width * (header.bpp >> 3);
            var imageSize = lineSize * header.height;
            var encodedSize = header.height + imageSize;

            var dstArr = new byte[encodedSize];
            var src = input.AsSpan();
            var dst = dstArr.AsSpan();

            var lheader = dst.Slice(0, header.height);
            var body = dst.Slice(header.height, imageSize);

            var candidateArray = ArrayPool<byte>.Shared.Rent(lineSize);
            var bestArray = ArrayPool<byte>.Shared.Rent(lineSize);

            try
            {
                var candidate = candidateArray.AsSpan(0, lineSize);
                var bestLine = bestArray.AsSpan(0, lineSize);

                for (var line = 0; line < header.height; line++)
                {
                    var lineOffset = line * lineSize;

                    var srcLine = src.Slice(lineOffset, lineSize);
                    var prevLine =
                        line == 0 ? Span<byte>.Empty : src.Slice(lineOffset - lineSize, lineSize);

                    var bestScore = int.MaxValue;
                    var bestFilter = PNG_FILTER_SUB;

                    for(byte i = 1; i <= PNG_FILTER_END; i <<= 1)
                    {
                        if ((header.pngFilter & i) == 0)
                            continue;

                        if (!TryFilterLine(srcLine, prevLine, candidate, (header.bpp >> 3), line, i))
                            continue;

                        var score = LineScore.Score(candidate);

                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestFilter = i;
                            candidate.CopyTo(bestLine);
                        }
                    }

                    lheader[line] = bestFilter;
                    bestLine.CopyTo(body.Slice(lineOffset, lineSize));
                }

                return dstArr;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(candidateArray);
                ArrayPool<byte>.Shared.Return(bestArray);
            }
        }

        public (int, byte[]) GetPixels(PGDImage info, byte[] data)
        {
            var decompressed = Compression.LzssDecode(data, info.compressInfo.unpackedSize);
            var header = new PGDLZPHeader
            {
                pngFilter = Utility.ToInt16(decompressed, 0),
                bpp = Utility.ToInt16(decompressed, 2),
                width = Utility.ToInt16(decompressed, 4),
                height = Utility.ToInt16(decompressed, 6)
            };

            return (header.bpp, PNGToRGB(header, decompressed));
        }

        public (int, byte[]) SetPixels(Image image)
        {
            var header = new PGDLZPHeader
            {
                pngFilter = PNG_FILTER_ALL, //By default, allowing to use every filter
                bpp = Convert.ToInt16(image.PixelType.BitsPerPixel),
                width = Convert.ToInt16(image.Width),
                height = Convert.ToInt16(image.Height)
            };

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write(header.pngFilter);
            bw.Write(header.bpp);
            bw.Write(header.width);
            bw.Write(header.height);


            switch (header.bpp)
            {
                case 24:
                {
                    var imageData = new byte[header.width * header.height * (header.bpp >> 3)];
                    image.CloneAs<Bgr24>().CopyPixelDataTo(imageData);
                    bw.Write(RGBToPNG(header, imageData));
                    break;
                }
                case 32:
                {
                    var imageData = new byte[header.width * header.height * (header.bpp >> 3)];
                    image.CloneAs<Bgra32>().CopyPixelDataTo(imageData);
                    bw.Write(RGBToPNG(header, imageData));
                    break;
                }
            }

            return (header.bpp, Compression.LzssEncode(ms.ToArray()));
        }
    }

    public class PGDExpection : ApplicationException
    {
        public PGDExpection(string mes): base(mes)
        {
        }
    }
}