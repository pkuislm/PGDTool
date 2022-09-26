# PGDTool
用于解/封 SoftPal引擎的PGD（文件头为“GE”）的Sprite文件

用法：

PGD.exe [d] [input] [output]

PGD.exe [p] [inputPGD] [inputPNG] [output]

限制：目前只测试过PGDImage中unk字段值为7的图片，且该工具并未实现LZ压缩，只是单纯写入图片数据



PGD/GE格式说明：

PGD文件主要分为两层：

第一层是Sprite的信息，包括该Sprite在游戏画面中的相对位置（大概）、Sprite中每个图片的长和宽、压缩方式、大小以及该Sprite的图像数据。

第二层是这个Sprite的图像数据，其采用了一层LZ压缩以及一层为了提高压缩方式而对像素点进行的处理（这个处理方式有点像PNG的那种）

具体实现可以查看代码。
