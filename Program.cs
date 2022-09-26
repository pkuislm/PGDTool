using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
                PGDSprite sprite = new(args[1]);
                sprite.ToPNG(args[2]);
            }
            else if (args[0] == "p")
            {
                PGDSprite sprite = new(args[1]);
                sprite.FromPNG(args[2]);
                sprite.Pack(args[1]+"N");
            }
            else
            {
                Console.Write("Unrecognized mode");
            }
        }
    }

    public class PGDSprite
    {
        public uint sig;
        public uint offsetX;
        public uint offsetY;
        public uint imageX;
        public uint imageY;
        public uint spriteWidth;
        public uint spriteHeight;
        public ushort compressionMethod;
        public ushort unk;
        public uint unpacked_size;
        public uint packed_size;
        PGDImage image;
        
        public PGDSprite(string path)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var br = new BinaryReader(fs);

            br.BaseStream.Position = 0;
            sig = br.ReadUInt32();
            if(sig != 0x204547)
            {
                throw (new PGDExpection("Unknown Signature"));
            }

            offsetX = br.ReadUInt32();
            offsetY = br.ReadUInt32();
            imageX = br.ReadUInt32();
            imageY = br.ReadUInt32();
            spriteWidth = br.ReadUInt32();
            spriteHeight = br.ReadUInt32();
            compressionMethod = br.ReadUInt16();
            unk = br.ReadUInt16();
            unpacked_size = br.ReadUInt32();
            packed_size = br.ReadUInt32();

            image = new(br, unpacked_size);

            br.Close();
            fs.Close();
        }

        public void ToPNG(string output)
        {
            image.ToPNG(output);
        }

        public void FromPNG(string input)
        {
            image.FromPNG(input);
        }

        public void Pack(string output)
        {
            var fs = new FileStream(output, FileMode.Create, FileAccess.ReadWrite);
            var bw = new BinaryWriter(fs);

            unpacked_size = (uint)image.imageData.Length;

            bw.Seek(40, SeekOrigin.Begin);
            packed_size = image.Write(bw);

            bw.Seek(0, SeekOrigin.Begin);
            bw.Write(sig);
            bw.Write(offsetX);
            bw.Write(offsetY);
            bw.Write(imageX);
            bw.Write(imageY);
            bw.Write(spriteWidth);
            bw.Write(spriteHeight);
            bw.Write(compressionMethod);
            bw.Write(unk);
            bw.Write(unpacked_size);
            bw.Write(packed_size);

            bw.Close();
            fs.Close();
        }
    }

    public class PGDImage
    {
        public ushort unk;
        public ushort bpp;
        public ushort width;
        public ushort height;
        public byte[] imageData;

        public PGDImage(BinaryReader br, uint unpacked_size)
        {
            imageData = new byte[unpacked_size];
            Decompress(br);
            unk = Utility.ToUInt16(imageData, 0);
            bpp = Utility.ToUInt16(imageData, 2);
            width = Utility.ToUInt16(imageData, 4);
            height = Utility.ToUInt16(imageData, 6);
        }

        public uint Write(BinaryWriter bw)
        {
            Utility.Pack((ushort)7, imageData, 0);
            Utility.Pack(bpp, imageData, 2);
            Utility.Pack(width, imageData, 4);
            Utility.Pack(height, imageData, 6);
            return (uint)Compress(bw, imageData);
        }

        public void ToPNG(string output)
        {
            switch(bpp)
            {
                case 24:
                {
                    var image = Image.LoadPixelData<Bgr24>(GetRawPixels(), width, height);
                    image.SaveAsPng(output);
                    break;
                }
                case 32:
                {
                    var image = Image.LoadPixelData<Bgra32>(GetRawPixels(), width, height);
                    image.SaveAsPng(output);
                    break;
                }
                default:
                    throw new PGDExpection("Unknown Image Format");
            }
        }

        public void FromPNG(string input)
        {
            var fs = new FileStream(input, FileMode.Open, FileAccess.Read);
            var image = Image.Load(fs);

            if(image.Width != width || image.Height != height)
            {
                throw new PGDExpection("Image size unmatch");
            }
            if (image.PixelType.BitsPerPixel != bpp)
            {
                if(image.PixelType.BitsPerPixel < bpp)
                {
                    throw new PGDExpection("Image pixel depth unmatch");
                }
                else
                {
                    Console.WriteLine("Warning: Current png's depth is greater than original pgd. Alpha channel will be discarded.");
                }
            }

            switch(bpp)
            {
                case 24:
                {
                    imageData = Encode24(image);
                    break;
                }
                case 32:
                {
                    imageData = Encode32(image);
                    break;
                }
            }
        }

        byte[] Encode24(Image image)
        {
            var pixels = image.CloneAs<Bgr24>();

            var raw = new byte[height * width * 3];

            uint idx = 0;
            for (var y = 0; y < height; ++y)
            {
                for (var x = 0; x < width; ++x)
                {
                    raw[idx + 0] = pixels[x, y].B;
                    raw[idx + 1] = pixels[x, y].G;
                    raw[idx + 2] = pixels[x, y].R;
                    idx += 3;
                }
            }
            return PackRawPixels(raw, 3);
        }

        byte[] Encode32(Image image)
        {
            var pixels = image.CloneAs<Bgra32>();

            var raw = new byte[height * width * 4];

            int idx = 0;
            for (var y = 0; y < height; ++y)
            {
                for (var x = 0; x < width; ++x)
                {
                    raw[idx + 0] = pixels[x, y].B;
                    raw[idx + 1] = pixels[x, y].G;
                    raw[idx + 2] = pixels[x, y].R;
                    raw[idx + 3] = pixels[x, y].A;
                    idx += 4;
                }
            }
            return PackRawPixels(raw, 4);
        }

        byte[] GetRawPixels()
        {
            int pixel_size = bpp / 8;
            int src = 8;
            int stride = width * pixel_size;
            var output = new byte[height * stride];
            int ctl = src;

            src += height;

            int dst = 0;
            for (int row = 0; row < height; ++row)
            {
                byte c = imageData[ctl++];
                if (0 != (c & 1))
                {
                    Buffer.BlockCopy(imageData, src, output, dst, pixel_size);
                    int prev = dst;//作为参考的前一个像素点的位置，此处是位于当前行的起始位置
                    int count = stride - pixel_size;//去掉第一个像素

                    src += pixel_size;
                    dst += pixel_size;
                    while (count-- > 0)
                    {
                        output[dst++] = (byte)(output[prev++] - imageData[src++]);
                    }
                }
                else if (0 != (c & 2))
                {
                    int prev = dst - stride;//作为参考的前一个像素点的位置，次数是位于上一行的起始位置
                    int count = stride;//一整行
                    while (count-- > 0)
                    {
                        output[dst++] = (byte)(output[prev++] - imageData[src++]);
                    }
                }
                else
                {
                    Buffer.BlockCopy(imageData, src, output, dst, pixel_size);
                    dst += pixel_size;
                    src += pixel_size;
                    int prev = dst - stride;//上一行，从第二个像素开始
                    int count = stride - pixel_size;//去除第一个像素
                    while (count-- > 0)
                    {
                        output[dst] = (byte)((output[prev++] + output[dst - pixel_size]) / 2 - imageData[src++]);
                        ++dst;
                    }
                }
            }
            return output;
        }

        byte[] PackRawPixels(byte[] input, int pixel_size)
        {
            byte[] ctl = new byte[height];
            ctl[0] = 1;
            for (var i = 1; i < ctl.Length; ++i)
            {
                ctl[i] = GetRowCompressMode(input, pixel_size, i);
            }

            byte[] output = new byte[input.Length + ctl.Length + 8];

            Buffer.BlockCopy(ctl, 0, output, 8, ctl.Length);

            int stride = width * pixel_size;

            int dst = ctl.Length + 8;
            int src = 0;
            for(var i = 0; i < ctl.Length; ++i)
            {
                switch(ctl[i])
                {
                    case 1://行内差：压缩值由前一个像素与当前像素做差得到
                    {
                        Buffer.BlockCopy(input, src, output, dst, pixel_size);
                        int prev = src;//作为参考的前一个像素点的位置，此处是位于当前行的起始位置
                        int count = stride - pixel_size;//去掉第一个像素

                        src += pixel_size;
                        dst += pixel_size;
                        while (count-- > 0)
                        {
                            output[dst++] = (byte)(input[prev++] - input[src++]);
                        }
                        break;
                    }
                    case 2://行间差：压缩值由上一行同位置的像素与当前像素做差得到
                    {
                        int prev = src - stride;//作为参考的前一个像素点的位置，此处是位于上一行的起始位置
                        int count = stride;//一整行
                        while (count-- > 0)
                        {
                            output[dst++] = (byte)(input[prev++] - input[src++]);
                        }
                        break;
                    }
                    case 4://混合：压缩值由 (当前像素前一个像素 + 上一行与当前像素同位置的像素) / 2 - 当前像素 得到
                    {
                        Buffer.BlockCopy(input, src, output, dst, pixel_size);
                        dst += pixel_size;
                        src += pixel_size;
                        int prev = src - stride;//上一行，从第二个像素开始
                        int count = stride - pixel_size;//去除第一个像素
                        while (count-- > 0)
                        {
                            //TODO：修复计算方式
                            output[dst] = (byte)((input[prev++] + input[dst - pixel_size]) / 2 - input[src++]);
                            dst++;
                        }
                        break;
                    }
                }
            }
            return output;
        }

        byte GetRowCompressMode(byte[] rows, int pixel_size, int col)
        {
            int stride = width * pixel_size;
            int type1 = 0;
            int type2 = 0;
            int type3 = 0;
            //对3种模式进行计算，并取最小值
            for (var i = 0; i < stride - pixel_size; ++i)
            {
                type1 += (byte)(rows[stride * col + i] - rows[stride * col + i + pixel_size]);
                type3 += (byte)((rows[stride * (col - 1) + i + pixel_size] + rows[stride * col + i]) / 2 - rows[stride * col + i + pixel_size]);
            }
            for (var i = 0; i < stride; ++i)
            {
                type2 += (byte)(rows[stride * (col - 1) + i] - rows[stride * col + i]);
            }
            if (type2 <= type3 && type2 <= type1) return 2;
            //if (type3 <= type1 && type3 <= type2) return 4;
            return 1;
        }

        void Decompress(BinaryReader br)
        {
            int dst = 0;
            int ctl = 2;
            while (dst < imageData.Length)
            {
                ctl >>= 1;
                if (ctl == 1)
                    ctl = br.ReadByte() | 0x100;
                int count;
                if ((ctl & 1) != 0)
                {
                    int offset = br.ReadUInt16();
                    count = offset & 7;
                    if ((offset & 8) == 0)
                    {
                        count = count << 8 | br.ReadByte();
                    }
                    count += 4;
                    offset >>= 4;
                    Utility.CopyOverlapped(imageData, dst - offset, dst, count);
                }
                else
                {
                    count = br.ReadByte();
                    br.Read(imageData, dst, count);
                }
                dst += count;
            }
        }

        long Compress(BinaryWriter bw, byte[] input)
        {
            int length = 0;
            long start = bw.BaseStream.Position;
            while(length < input.Length)
            {
                bw.Write((byte)0);
                for(var i = 0; i < 8; ++i)
                {
                    if(input.Length - length >= 0xFF)
                    {
                        bw.Write((byte)0xFF);
                        bw.Write(input, length, 0xFF);
                        length += 0xFF;
                    }
                    else
                    {
                        bw.Write((byte)input.Length - length);
                        bw.Write(input, length, input.Length - length);
                        length += 0xFF;
                        break;
                    }

                }
            }
            return bw.BaseStream.Position - start;
        }
    }

    public class PGDExpection : ApplicationException
    {
        public PGDExpection(string mes): base(mes)
        {
        }
    }
}