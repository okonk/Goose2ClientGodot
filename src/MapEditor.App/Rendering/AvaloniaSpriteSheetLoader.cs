using System;
using System.IO;
using Avalonia.Media.Imaging;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class AvaloniaSpriteSheetLoader : ISpriteSheetLoader
{
    public SpriteSheetLoadResult Load(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Bitmap bitmap = new(stream);
            return SpriteSheetLoadResult.Success(new AvaloniaSpriteSheetImage(bitmap));
        }
        catch (FileNotFoundException)
        {
            return SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, $"Sheet file not found: {path}.");
        }
        catch (ArgumentException ex)
        {
            return SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.InvalidData, $"Sheet file is not a decodable PNG: {path}. {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.Unreadable, $"Sheet file could not be read: {path}. {ex.Message}");
        }
        catch (IOException ex)
        {
            return SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.Unreadable, $"Sheet file could not be read: {path}. {ex.Message}");
        }
    }
}
