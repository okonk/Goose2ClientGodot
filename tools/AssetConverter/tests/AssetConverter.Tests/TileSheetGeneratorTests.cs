using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Tiles;
using Xunit;

namespace AssetConverter.Tests;

public class TileSheetGeneratorTests : IDisposable
{
    private readonly string _dir;

    public TileSheetGeneratorTests()
    {
        _dir = Directory.CreateTempSubdirectory("tilegen").FullName;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Generate_AppliesStaticNonPartNonIconRulePlusAsperetaRange()
    {
        WriteAdf(10, AdfType.Graphic, frameCount: 2, animCount: 0);
        WriteAdf(11, AdfType.Graphic, frameCount: 2, animCount: 2);
        WriteAdf(12, AdfType.Graphic, frameCount: 1, animCount: 0);
        WriteAdf(13, AdfType.Graphic, frameCount: 1, animCount: 0);
        WriteAdf(14, AdfType.Sound, frameCount: 0, animCount: 0);

        WriteEnc((type: 1, id: 1, files: new[] { 12, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));

        string manifest = Path.Combine(_dir, "manifest.json");
        File.WriteAllText(manifest, """{ "tileSize": 32, "sheets": { "10": { "1": [0, 0, 4, 4] }, "20000": { "1": [0, 0, 4, 4] }, "20001": { "1": [0, 0, 4, 4] } } }""");

        string icons = Path.Combine(_dir, "sheets.json");
        File.WriteAllText(icons, """{ "atlasWidth": 2048, "iconSheets": [13] }""");

        IReadOnlyList<int> tiles = TileSheetGenerator.Generate(_dir, manifest, icons);

        Assert.Equal(new[] { 10, 20000, 20001 }, tiles);
    }

    [Fact]
    public void Write_EmitsJsonArrayNextToManifest()
    {
        TileSheetGenerator.Write(_dir, new[] { 1, 5 });

        Assert.Equal("[1,5]", File.ReadAllText(Path.Combine(_dir, TileSheetGenerator.OutputFileName)).Trim());
    }

    private void WriteAdf(int fileNumber, AdfType type, int frameCount, int animCount)
    {
        const int offset = 7;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)type);
        w.Write((byte)0);
        w.Write(0);
        w.Write((byte)offset);
        int firstFrame = fileNumber * 100;
        w.Write(firstFrame + offset);
        w.Write(frameCount + offset);
        w.Write(firstFrame + 1000 + offset);
        w.Write(animCount + offset);
        for (int i = 0; i < frameCount; i++)
        {
            w.Write(i + offset);
            w.Write(i + offset);
            w.Write(4 + offset);
            w.Write(4 + offset);
        }

        for (int a = 0; a < animCount; a++)
        {
            w.Write((byte)((1 + offset) & 0xFF));
            w.Write(0 + offset);
        }

        w.Write(0 + offset);
        w.Write(new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(_dir, $"{fileNumber}.adf"), ms.ToArray());
    }

    private void WriteEnc(params (int type, int id, int[] files)[] entries)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach ((int type, int id, int[] files) in entries)
        {
            w.Write((short)(type + 1));
            w.Write(id);
            for (int i = 0; i < 44; i++)
            {
                w.Write(0);
            }

            foreach (int fileNumber in files)
            {
                w.Write(fileNumber);
            }
        }

        File.WriteAllBytes(Path.Combine(_dir, "compiled.enc"), ms.ToArray());
    }
}
