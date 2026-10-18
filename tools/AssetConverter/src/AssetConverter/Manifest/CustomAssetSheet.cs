using Goose2.AssetConverter.Png;

namespace Goose2.AssetConverter.Manifest;

public sealed class CustomAssetSheet
{
    public const int ExclamationGraphic = 1;
    public const int QuestionGraphic = 2;
    public const int ExperienceGraphic = 3;

    public const int GrandfatherClockGraphic = 1;

    public const int KainPortraitGraphic = 1;
    public const int ShiversPortraitGraphic = 2;
    public const int SpykePortraitGraphic = 3;
    public const int DoomPortraitGraphic = 4;
    public const int RexPortraitGraphic = 5;
    public const int PiePortraitGraphic = 6;
    public const int CrocPortraitGraphic = 7;

    public static readonly CustomAssetSheet Icons = new(30000, 32, 32, isTileSheet: false,
    [
        (ExclamationGraphic, "exclamation.png"),
        (QuestionGraphic, "question.png"),
        (ExperienceGraphic, "experience.png"),
    ]);

    public static readonly CustomAssetSheet Tiles = new(30001, 32, 64, isTileSheet: true,
    [
        (GrandfatherClockGraphic, "grandfather-clock.png"),
    ]);

    public static readonly CustomAssetSheet Portraits = new(30002, 32, 40, isTileSheet: true,
    [
        (KainPortraitGraphic, "portrait-kain.png"),
        (ShiversPortraitGraphic, "portrait-shivers.png"),
        (SpykePortraitGraphic, "portrait-spyke.png"),
        (DoomPortraitGraphic, "portrait-doom.png"),
        (RexPortraitGraphic, "portrait-rex.png"),
        (PiePortraitGraphic, "portrait-pie.png"),
        (CrocPortraitGraphic, "portrait-croc.png"),
    ]);

    public static readonly IReadOnlyList<CustomAssetSheet> All = [Icons, Tiles, Portraits];

    private readonly (int Graphic, string FileName)[] _graphics;

    private CustomAssetSheet(int sheetNumber, int cellWidth, int cellHeight, bool isTileSheet,
        (int Graphic, string FileName)[] graphics)
    {
        SheetNumber = sheetNumber;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        IsTileSheet = isTileSheet;
        _graphics = graphics;
    }

    public int SheetNumber { get; }
    public int CellWidth { get; }
    public int CellHeight { get; }
    public bool IsTileSheet { get; }

    public static IEnumerable<int> TileSheetNumbers => All.Where(s => s.IsTileSheet).Select(s => s.SheetNumber);

    public static void WriteAll(string sourceDir, string outputDir)
    {
        foreach (CustomAssetSheet sheet in All)
        {
            sheet.Write(sourceDir, outputDir);
        }
    }

    public Dictionary<string, int[]> CreateFrames() =>
        _graphics.ToDictionary(g => g.Graphic.ToString(), g => new[] { (g.Graphic - 1) * CellWidth, 0, CellWidth, CellHeight });

    public void Write(string sourceDir, string outputDir)
    {
        int sheetWidth = _graphics.Length * CellWidth;
        byte[] sheet = new byte[sheetWidth * CellHeight * 4];
        int cellRowLength = CellWidth * 4;
        int sheetRowLength = sheetWidth * 4;
        foreach ((int graphic, string fileName) in _graphics)
        {
            byte[] cell = LoadCell(sourceDir, fileName);
            int offset = (graphic - 1) * cellRowLength;
            for (int y = 0; y < CellHeight; y++)
            {
                Buffer.BlockCopy(cell, y * cellRowLength, sheet, y * sheetRowLength + offset, cellRowLength);
            }
        }

        // The source icons hide a magenta key colour under alpha 0; zero it so filtering can't bleed it.
        for (int i = 0; i < sheet.Length; i += 4)
        {
            if (sheet[i + 3] == 0)
            {
                sheet[i] = 0;
                sheet[i + 1] = 0;
                sheet[i + 2] = 0;
            }
        }

        PngWriter.Write(sheet, sheetWidth, CellHeight, Path.Combine(outputDir, $"{SheetNumber}.png"));
    }

    private byte[] LoadCell(string sourceDir, string fileName)
    {
        string path = Path.Combine(sourceDir, fileName);
        byte[] image = SkiaImageCodec.DecodeRgba(File.ReadAllBytes(path), out int width, out int height);
        if (width == CellWidth && height == CellHeight)
        {
            return image;
        }

        throw new InvalidDataException($"{path} must be {CellWidth}x{CellHeight} pixels.");
    }
}
