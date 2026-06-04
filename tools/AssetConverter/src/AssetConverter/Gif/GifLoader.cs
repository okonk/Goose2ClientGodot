using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Goose2.AssetConverter.Gif
{
    public class GifLoader
    {
        class GifHeader
        {
            public byte[] Signature;
            public byte[] Version;
        }

        class LogicalScreenDescriptor
        {
            public ushort Width;
            public ushort Height;
            public byte PackedFieldValue;
            public byte BackgroundColorIndex;
            public byte PixelAspectRatio;
            public bool GlobalColorTableFlag { get { return (PackedFieldValue >> 7) == 1; } }
            public byte ColorResolution { get { return (byte)(((PackedFieldValue & 0x70) >> 4) + 1); } }
            public byte SortFlag { get { return (byte)((PackedFieldValue & 0x8) >> 3); } }
            public int SizeOfGlobalColorTable { get { return (int)(2 << (PackedFieldValue & 0x7)); } }
        }

        class ColorTable
        {
            public byte[] Colors;
        }

        class GraphicControlExtension
        {
            public byte PackedFieldValue;
            public ushort DelayTime;
            public byte TransparentColorIndex;
            public byte DisposalMethod { get { return (byte)((PackedFieldValue & 0x1C) >> 2); } }
            public byte UserInputFlag { get { return (byte)((PackedFieldValue & 0x2) >> 1); } }
            public bool TransparentColorFlag { get { return (PackedFieldValue & 0x1) == 1; } }
        }

        class ImageDescriptor
        {
            public ushort ImageLeft;
            public ushort ImageTop;
            public ushort ImageWidth;
            public ushort ImageHeight;
            public byte PackedFieldValue;

            public bool LocalColorTable { get { return (PackedFieldValue >> 7) == 1; } }
            public bool Interlace { get { return ((PackedFieldValue & 0x40) >> 6) == 1; } }
            public bool Sort { get { return ((PackedFieldValue & 0x20) >> 5) == 1; } }
            public int SizeOfLocalColorTable { get { return (int)(2 << (PackedFieldValue & 0x7)); } }
        }

        const byte StartOfImage = 0x2C;
        const byte StartOfExtensionBlock = 0x21;
        const byte EndOfFile = 0x3B;

        const byte GraphicControlLabel = 0xF9;
        const byte PlainTextLabel = 0x01;
        const byte ApplicationLabel = 0xFF;
        const byte CommentLabel = 0xFE;

        const ushort ClearCode = ushort.MaxValue - 1;
        const ushort EndOfInformationCode = ushort.MaxValue;

        private static Dictionary<ushort, ushort[]> codeTable;

        private static Dictionary<ushort, ushort[]> InitializeCodeTable(ColorTable colorTable, byte lzwMinimumCodeSize, out ushort nextFreeCode)
        {
            var codes = new Dictionary<ushort, ushort[]>();

            ushort colorTableSize = (ushort)(colorTable.Colors.Length / 3);
            for (ushort i = 0; i < colorTableSize; i++)
            {
                codes[i] = new[] { i };
            }

            ushort minCodeSize = (ushort)(1 << lzwMinimumCodeSize);
            ushort index = Math.Max(minCodeSize, colorTableSize);
            codes[index] = new[] { ClearCode };
            codes[(ushort)(index + 1)] = new[] { EndOfInformationCode };

            nextFreeCode = (ushort)(index + 2);

            return codes;
        }

        private static void WriteToOutput(byte[] output, LogicalScreenDescriptor logicalScreenDescriptor, ImageDescriptor imageDescriptor, GraphicControlExtension graphicControlExtension, ColorTable colorTable, ushort[] colorIndexes, ref int outputIndex, ref int interlacePass)
        {
            foreach (var colorIndex in colorIndexes)
            {
                byte r = colorTable.Colors[colorIndex * 3 + 0];
                byte g = colorTable.Colors[colorIndex * 3 + 1];
                byte b = colorTable.Colors[colorIndex * 3 + 2];
                //byte a = (byte)(graphicControlExtension != null && graphicControlExtension.TransparentColorFlag && graphicControlExtension.TransparentColorIndex == colorIndex ? 0 : 255);
                byte a = (byte)(r <= 1 && g == 0 && b == 0 ? 0 : 255); // hack for illutia data transparency

                output[outputIndex + 0] = r;
                output[outputIndex + 1] = g;
                output[outputIndex + 2] = b;
                output[outputIndex + 3] = a;


                // TODO: figure out wrapping

                if (imageDescriptor.Interlace && (outputIndex + 4) % (imageDescriptor.ImageWidth * 4) == 0)
                {
                    int offsetLines = 0;
                    switch (interlacePass)
                    {
                        case 0:
                            offsetLines = 7;
                            break;
                        case 1:
                            offsetLines = 7;
                            break;
                        case 2:
                            offsetLines = 3;
                            break;
                        case 3:
                            offsetLines = 1;
                            break;
                    }

                    int offsetBytes = offsetLines * imageDescriptor.ImageWidth * 4 + 4;
                    if (outputIndex + offsetBytes >= output.Length)
                    {
                        // new pass
                        interlacePass++;
                        switch (interlacePass)
                        {
                            case 1:
                                offsetLines = 4;
                                break;
                            case 2:
                                offsetLines = 2;
                                break;
                            case 3:
                                offsetLines = 1;
                                break;
                        }

                        outputIndex = offsetLines * imageDescriptor.ImageWidth * 4;
                    }
                    else
                    {
                        outputIndex += offsetBytes;
                    }
                }
                else
                {
                    outputIndex += 4;
                }
            }
        }

        public static byte[] Load(byte[] input, out int width, out int height)
        {
            byte[] output;

            using (var reader = new BinaryReader(new MemoryStream(input)))
            {
                var header = new GifHeader();
                header.Signature = reader.ReadBytes(3);
                header.Version = reader.ReadBytes(3);

                // TODO: Validate actually is a GIF89a or GIF87a

                var logicalScreenDescriptor = new LogicalScreenDescriptor();
                logicalScreenDescriptor.Width = reader.ReadUInt16();
                logicalScreenDescriptor.Height = reader.ReadUInt16();
                logicalScreenDescriptor.PackedFieldValue = reader.ReadByte();
                logicalScreenDescriptor.BackgroundColorIndex = reader.ReadByte();
                logicalScreenDescriptor.PixelAspectRatio = reader.ReadByte();

                if (logicalScreenDescriptor.PixelAspectRatio != 0)
                    throw new Exception("Unsupported PixelAspectRatio " + logicalScreenDescriptor.PixelAspectRatio);

                ColorTable globalColorTable = null;
                if (logicalScreenDescriptor.GlobalColorTableFlag)
                {
                    globalColorTable = new ColorTable();
                    globalColorTable.Colors = reader.ReadBytes(logicalScreenDescriptor.SizeOfGlobalColorTable * 3);
                }

                // TODO: Possibly prefill background colour

                output = new byte[logicalScreenDescriptor.Width * logicalScreenDescriptor.Height * 4];

                GraphicControlExtension graphicControlExtension = null;

                byte startByte = reader.ReadByte();
                while (startByte != EndOfFile)
                {
                    switch (startByte)
                    {
                        case StartOfExtensionBlock:
                            byte label = reader.ReadByte();

                            switch (label)
                            {
                                case GraphicControlLabel:
                                    byte extensionBlockSize = reader.ReadByte();
                                    // We only care about the GraphicControlExtension
                                    graphicControlExtension = new GraphicControlExtension();
                                    graphicControlExtension.PackedFieldValue = reader.ReadByte();
                                    graphicControlExtension.DelayTime = reader.ReadUInt16();
                                    graphicControlExtension.TransparentColorIndex = reader.ReadByte();

                                    byte terminator = reader.ReadByte();
                                    if (terminator != 0) throw new Exception("Unexpected byte, expected terminator but got " + terminator);
                                    break;
                                case CommentLabel:
                                    // Ignore comments, they don't contain a length marker so read until a 0 byte
                                    while (reader.ReadByte() != 0) ;
                                    break;
                                case PlainTextLabel:
                                case ApplicationLabel:
                                default:
                                    // Ignore all other labels
                                    reader.ReadBytes(reader.ReadByte());

                                    var dataBlockLength = reader.ReadByte();
                                    while (dataBlockLength != 0)
                                    {
                                        reader.ReadBytes(dataBlockLength);

                                        dataBlockLength = reader.ReadByte();
                                    }
                                    break;
                            }

                            break;
                        case StartOfImage:
                            ImageDescriptor imageDescriptor = new ImageDescriptor();
                            imageDescriptor.ImageLeft = reader.ReadUInt16();
                            imageDescriptor.ImageTop = reader.ReadUInt16();
                            imageDescriptor.ImageWidth = reader.ReadUInt16();
                            imageDescriptor.ImageHeight = reader.ReadUInt16();
                            imageDescriptor.PackedFieldValue = reader.ReadByte();

                            ColorTable localColorTable = globalColorTable;
                            if (imageDescriptor.LocalColorTable)
                            {
                                localColorTable = new ColorTable();
                                localColorTable.Colors = reader.ReadBytes(imageDescriptor.SizeOfLocalColorTable * 3);
                            }

                            var indexStream = new List<ushort>();

                            byte lzwMinimumCodeSize = reader.ReadByte();
                            byte imageBlockSize = reader.ReadByte();

                            int currentCodeSize = lzwMinimumCodeSize + 1;
                            var bitReader = new BitReader();
                            ushort nextFreeCode;
                            codeTable = InitializeCodeTable(localColorTable, lzwMinimumCodeSize, out nextFreeCode);
                            ushort[] previousCodeValue = null;
                            int outputIndex = (imageDescriptor.ImageLeft + (imageDescriptor.ImageTop * logicalScreenDescriptor.Width) * 4);
                            int interlacePass = 0;

                            while (imageBlockSize > 0)
                            {
                                bitReader.AddBytes(reader.ReadBytes(imageBlockSize));

                                while (bitReader.HasNextValue(currentCodeSize))
                                {
                                    ushort nextCode = bitReader.NextValue(currentCodeSize);
                                    ushort[] codeValue;
                                    if (codeTable.TryGetValue(nextCode, out codeValue))
                                    {
                                        if (codeValue[0] == ClearCode)
                                        {
                                            codeTable = InitializeCodeTable(localColorTable, lzwMinimumCodeSize, out nextFreeCode);
                                            currentCodeSize = lzwMinimumCodeSize + 1;
                                            previousCodeValue = null;
                                        }
                                        else if (codeValue[0] == EndOfInformationCode)
                                        {
                                            break;
                                        }
                                        else
                                        {
                                            indexStream.AddRange(codeValue);
                                            WriteToOutput(output, logicalScreenDescriptor, imageDescriptor, graphicControlExtension, localColorTable, codeValue, ref outputIndex, ref interlacePass);

                                            if (previousCodeValue != null)
                                            {
                                                ushort[] newValue = new ushort[previousCodeValue.Length + 1];
                                                Array.Copy(previousCodeValue, 0, newValue, 0, previousCodeValue.Length);
                                                newValue[previousCodeValue.Length] = codeValue[0];
                                                codeTable[nextFreeCode] = newValue;
                                                nextFreeCode++;
                                            }

                                            previousCodeValue = codeValue;
                                        }
                                    }
                                    else
                                    {
                                        indexStream.AddRange(previousCodeValue);
                                        indexStream.Add(previousCodeValue[0]);
                                        WriteToOutput(output, logicalScreenDescriptor, imageDescriptor, graphicControlExtension, localColorTable, previousCodeValue, ref outputIndex, ref interlacePass);
                                        WriteToOutput(output, logicalScreenDescriptor, imageDescriptor, graphicControlExtension, localColorTable, new[] { previousCodeValue[0] }, ref outputIndex, ref interlacePass);

                                        ushort[] newValue = new ushort[previousCodeValue.Length + 1];
                                        Array.Copy(previousCodeValue, 0, newValue, 0, previousCodeValue.Length);
                                        newValue[previousCodeValue.Length] = previousCodeValue[0];
                                        codeTable[nextFreeCode] = newValue;
                                        nextFreeCode++;

                                        previousCodeValue = newValue;
                                    }

                                    if (currentCodeSize < 12 && (1 << currentCodeSize) == nextFreeCode)
                                        currentCodeSize++;
                                }

                                imageBlockSize = reader.ReadByte();
                            }

                            break;
                        default:
                            throw new Exception("Unknown starting byte: " + startByte);
                    }

                    startByte = reader.ReadByte();
                }

                width = logicalScreenDescriptor.Width;
                height = logicalScreenDescriptor.Height;
            }

            return output;
        }

        class BitReader
        {
            const int BufferSize = 4096;

            private int currentIndex = 0;
            private int currentBit = 0;
            private int maxIndex = 0;
            private byte[] buffer = new byte[BufferSize];

            public void AddBytes(byte[] bytes)
            {
                if (bytes.Length > buffer.Length)
                    throw new Exception("Unexpected length of input bytes");

                if (bytes.Length + maxIndex - currentIndex > buffer.Length)
                    throw new Exception("Too many bytes for buffer");

                if (bytes.Length + maxIndex > buffer.Length)
                {
                    var newBuffer = new byte[BufferSize];
                    Array.Copy(buffer, currentIndex, newBuffer, 0, maxIndex - currentIndex);
                    buffer = newBuffer;
                    maxIndex -= currentIndex;
                    currentIndex = 0;
                }

                Array.Copy(bytes, 0, buffer, maxIndex, bytes.Length);
                maxIndex += bytes.Length;
            }

            public ushort NextValue(int numberOfBits)
            {
                // This can probably be done more efficiently.. but oh well

                ushort returnValue = 0;

                for (int i = 0; i < numberOfBits; i++)
                {
                    byte currentValue = buffer[currentIndex];
                    returnValue = (ushort)(returnValue | (((currentValue & (1 << currentBit)) >> currentBit) << i));

                    currentBit = (currentBit + 1) % 8;
                    if (currentBit == 0) currentIndex++;
                }

                return returnValue;
            }

            public bool HasNextValue(int numberOfBits)
            {
                int remainingBits = ((maxIndex - currentIndex) * 8) - currentBit;
                return remainingBits >= numberOfBits;
            }
        }
    }
}
