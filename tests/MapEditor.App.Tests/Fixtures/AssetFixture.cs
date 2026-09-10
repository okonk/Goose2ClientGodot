using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using MapEditor.Core.Terrain;

namespace MapEditor.App.Tests.Fixtures;

public sealed class AssetFixture : IDisposable
{
    public AssetFixture()
    {
        Root = Directory.CreateTempSubdirectory("map-editor-assets-").FullName;
        AssetDirectory = Path.Combine(Root, "assets");
        Directory.CreateDirectory(Path.Combine(AssetDirectory, "sheets"));
    }

    public string Root { get; }

    public string AssetDirectory { get; }

    public void WriteManifest(string json)
        => File.WriteAllText(Path.Combine(AssetDirectory, "manifest.json"), json);

    public void WriteSheet(int sheetId, int width, int height)
        => File.WriteAllBytes(Path.Combine(AssetDirectory, "sheets", $"{sheetId}.png"), PngSheet.Create(width, height));

    public void WriteCorruptSheet(int sheetId)
        => File.WriteAllText(Path.Combine(AssetDirectory, "sheets", $"{sheetId}.png"), "this is not a png, just text bytes");

    public void WriteTerrainCatalog(TerrainCatalog catalog)
        => File.WriteAllText(Path.Combine(AssetDirectory, "terrain-brushes.json"), TerrainCatalogJson.Serialize(catalog));

    public void Dispose()
        => Directory.Delete(Root, recursive: true);

    public static class PngSheet
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static byte[] Create(int width, int height)
        {
            using MemoryStream output = new();
            output.Write(Signature);
            WriteChunk(output, "IHDR", Ihdr(width, height));

            using MemoryStream raw = new();
            byte[] row = new byte[1 + width * 4];
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0);
                raw.Write(row, 1, row.Length - 1);
            }

            using MemoryStream idat = new();
            using (DeflateStream deflate = new(idat, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(raw.ToArray());
            }

            WriteChunk(output, "IDAT", idat.ToArray());
            WriteChunk(output, "IEND", Array.Empty<byte>());
            return output.ToArray();
        }

        private static byte[] Ihdr(int width, int height)
        {
            byte[] data = new byte[13];
            WriteBigEndian(data, 0, width);
            WriteBigEndian(data, 4, height);
            data[8] = 8;
            data[9] = 6;
            return data;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            byte[] length = new byte[4];
            WriteBigEndian(length, 0, data.Length);
            stream.Write(length);
            byte[] name = Encoding.ASCII.GetBytes(type);
            stream.Write(name);
            stream.Write(data);
            byte[] crcInput = new byte[name.Length + data.Length];
            name.CopyTo(crcInput, 0);
            data.CopyTo(crcInput, name.Length);
            byte[] crc = new byte[4];
            WriteBigEndian(crc, 0, (int)Crc32(crcInput));
            stream.Write(crc);
        }

        private static void WriteBigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
                }
            }

            return crc ^ 0xFFFFFFFF;
        }
    }
}
