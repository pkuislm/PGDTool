# PGDTool
用于解/封 SoftPal引擎的PGD（文件头为“GE”）的文件

用法：

PGD.exe [d] [input] [output]

PGD.exe [p] [inputPGD] [inputPNG] [output]



PGD/GE格式说明：

PGD采用了LZSS变体作为压缩算法，但其对于像素的预处理有如下几种方式：

LZE：压缩前将原本交错存放的通道`[BGRA][BGRA]...`铺平成为`[AAAA...][BBBB...][GGGG...][RRRR...]`。仅支持32BPP

LZY：压缩前将色彩从RGB转为YUV411。仅支持24BPP，且图片宽和高必须能被8整除

LZP：采用了类似PNG的思想，压缩前为每一行选择最优的过滤器。可以选择24或32BPP

具体实现可以查看代码。
