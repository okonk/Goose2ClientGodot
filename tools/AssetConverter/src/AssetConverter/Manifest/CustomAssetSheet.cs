using Goose2.AssetConverter.Png;

namespace Goose2.AssetConverter.Manifest;

public static class CustomAssetSheet
{
    public const int SheetNumber = 30000;
    public const int ExclamationGraphic = 1;
    public const int QuestionGraphic = 2;
    public const int ExperienceGraphic = 3;

    private const int IconSize = 32;

    private static readonly (int Graphic, string FileName)[] Icons =
    [
        (ExclamationGraphic, "exclamation.png"),
        (QuestionGraphic, "question.png"),
        (ExperienceGraphic, "experience.png"),
    ];

    public static Dictionary<string, int[]> CreateFrames() =>
        Icons.ToDictionary(icon => icon.Graphic.ToString(), icon => new[] { (icon.Graphic - 1) * IconSize, 0, IconSize, IconSize });

    public static void Write(string sourceDir, string outputDir)
    {
        int sheetWidth = Icons.Length * IconSize;
        byte[] sheet = new byte[sheetWidth * IconSize * 4];
        int iconRowLength = IconSize * 4;
        int sheetRowLength = sheetWidth * 4;
        foreach ((int graphic, string fileName) in Icons)
        {
            byte[] icon = LoadIcon(sourceDir, fileName);
            int offset = (graphic - 1) * iconRowLength;
            for (int y = 0; y < IconSize; y++)
            {
                Buffer.BlockCopy(icon, y * iconRowLength, sheet, y * sheetRowLength + offset, iconRowLength);
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

        PngWriter.Write(sheet, sheetWidth, IconSize, Path.Combine(outputDir, $"{SheetNumber}.png"));
    }

    private static byte[] LoadIcon(string sourceDir, string fileName)
    {
        string path = Path.Combine(sourceDir, fileName);
        byte[] image = SkiaImageCodec.DecodeRgba(File.ReadAllBytes(path), out int width, out int height);
        if (width == IconSize && height == IconSize)
        {
            return image;
        }

        throw new InvalidDataException($"{path} must be 32x32 pixels.");
    }
}
