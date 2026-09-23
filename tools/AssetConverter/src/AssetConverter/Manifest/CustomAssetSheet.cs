using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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
        using var exclamation = LoadIcon(sourceDir, "exclamation.png");
        using var question = LoadIcon(sourceDir, "question.png");
        using var sheet = new Image<Rgba32>(64, 32);
        sheet.Mutate(context =>
        {
            context.DrawImage(exclamation, new Point(0, 0), 1f);
            context.DrawImage(question, new Point(32, 0), 1f);
        });

        Directory.CreateDirectory(outputDir);
        sheet.SaveAsPng(Path.Combine(outputDir, $"{SheetNumber}.png"));
    }

    private static Image<Rgba32> LoadIcon(string sourceDir, string fileName)
    {
        string path = Path.Combine(sourceDir, fileName);
        var image = Image.Load<Rgba32>(path);
        if (image.Width == 32 && image.Height == 32)
        {
            return image;
        }

        image.Dispose();
        throw new InvalidDataException($"{path} must be 32x32 pixels.");
    }
}
