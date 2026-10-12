using Goose2.AssetConverter.Png;

namespace Goose2.AssetConverter.Manifest;

public static class CustomAssetSheet
{
    public const int SheetNumber = 30000;
    public const int ExclamationGraphic = 1;
    public const int QuestionGraphic = 2;

    public static Dictionary<string, int[]> CreateFrames() => new()
    {
        [ExclamationGraphic.ToString()] = new[] { 0, 0, 32, 32 },
        [QuestionGraphic.ToString()] = new[] { 32, 0, 32, 32 },
    };

    public static void Write(string sourceDir, string outputDir)
    {
        byte[] exclamation = LoadIcon(sourceDir, "exclamation.png");
        byte[] question = LoadIcon(sourceDir, "question.png");
        byte[] sheet = new byte[64 * 32 * 4];
        int iconRowLength = 32 * 4;
        int sheetRowLength = 64 * 4;
        for (int y = 0; y < 32; y++)
        {
            Buffer.BlockCopy(exclamation, y * iconRowLength, sheet, y * sheetRowLength, iconRowLength);
            Buffer.BlockCopy(question, y * iconRowLength, sheet, y * sheetRowLength + iconRowLength, iconRowLength);
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

        PngWriter.Write(sheet, 64, 32, Path.Combine(outputDir, $"{SheetNumber}.png"));
    }

    private static byte[] LoadIcon(string sourceDir, string fileName)
    {
        string path = Path.Combine(sourceDir, fileName);
        byte[] image = SkiaImageCodec.DecodeRgba(File.ReadAllBytes(path), out int width, out int height);
        if (width == 32 && height == 32)
        {
            return image;
        }

        throw new InvalidDataException($"{path} must be 32x32 pixels.");
    }
}
