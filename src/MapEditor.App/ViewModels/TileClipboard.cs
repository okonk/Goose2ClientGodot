using MapEditor.Core;

namespace MapEditor.App.ViewModels;

internal sealed class TileClipboard
{
    private TileClipboard(int width, int height, MapTileLayer[]?[] layers)
    {
        Width = width;
        Height = height;
        Layers = layers;
    }

    public int Width { get; }

    public int Height { get; }

    public MapTileLayer[]?[] Layers { get; }

    public static TileClipboard Capture(MapDocument document, byte selectedLayers, MapTileRectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || rect.X < 0 || rect.Y < 0 ||
            rect.X + rect.Width > document.Width || rect.Y + rect.Height > document.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(rect));
        }

        var layers = new MapTileLayer[MapDocument.LayerCount][];
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            if ((selectedLayers & (1 << layer)) == 0)
            {
                continue;
            }

            var data = new MapTileLayer[rect.Width * rect.Height];
            for (int row = 0; row < rect.Height; row++)
            {
                for (int col = 0; col < rect.Width; col++)
                {
                    data[row * rect.Width + col] = document[rect.X + col, rect.Y + row].GetLayer(layer);
                }
            }

            layers[layer] = data;
        }

        return new TileClipboard(rect.Width, rect.Height, layers);
    }
}
