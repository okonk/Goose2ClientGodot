using System;
using MapEditor.Core;

namespace MapEditor.Rendering;

public sealed class MapRenderer
{
    public const int MaximumTileReadsPerRender = 800_000;

    private readonly SpriteAssetCache _assets;

    public MapRenderer(SpriteAssetCache assets)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    public void Render(MapRenderRequest request, IMapDrawSink sink)
    {
        MapDocument document = request.Document ?? throw new ArgumentNullException(nameof(request));
        MapRenderOptions options = request.Options ?? throw new ArgumentNullException(nameof(request));
        ArgumentNullException.ThrowIfNull(sink);
        ObjectDisposedException.ThrowIf(_assets.IsDisposed, _assets);

        ViewportTransform viewport = request.Viewport;
        ViewportTileRanges ranges = ViewportCulling.Compute(
            viewport,
            document.Width,
            document.Height,
            _assets.MaxFrameWidth,
            _assets.MaxFrameHeight);
        MapLayerVisibility visibility = options.VisibleLayers;

        long visibleLayerCount = 0;
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            if (visibility.IsVisible(layer))
            {
                visibleLayerCount++;
            }
        }

        long requestedReads = (long)ranges.SpriteCandidates.Width * ranges.SpriteCandidates.Height * visibleLayerCount;
        if (options.ShowBlocked)
        {
            requestedReads += (long)ranges.VisibleCells.Width * ranges.VisibleCells.Height;
        }

        if (requestedReads > MaximumTileReadsPerRender)
        {
            throw new MapRenderWorkLimitException(
                requestedReads,
                MaximumTileReadsPerRender,
                $"Render work limit exceeded: {requestedReads} tile reads requested, maximum {MaximumTileReadsPerRender}. Zoom in, hide layers or the blocked overlay, or inspect anomalously large sprite frame metadata.");
        }

        RenderRect visibleWorld = viewport.VisibleWorldRect;
        SpriteManifest? manifest = _assets.Manifest;

        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            if (!visibility.IsVisible(layer))
            {
                continue;
            }

            for (int y = ranges.SpriteCandidates.MinY; y <= ranges.SpriteCandidates.MaxY; y++)
            {
                for (int x = ranges.SpriteCandidates.MinX; x <= ranges.SpriteCandidates.MaxX; x++)
                {
                    MapTileLayer tileLayer = document[x, y].GetLayer(layer);
                    if (tileLayer.Graphic == 0)
                    {
                        continue;
                    }

                    SpriteReference reference = new(tileLayer.Sheet, tileLayer.Graphic);
                    SpriteSourceRect sourceRect = default;
                    bool knownFrame = manifest is not null && manifest.TryGetSourceRect(reference, out sourceRect);
                    RenderRect cell = CellRect(x, y);
                    bool cellIntersects = Intersects(cell, visibleWorld);
                    bool spriteIntersects = false;
                    RenderRect spriteDestination = default;

                    if (knownFrame)
                    {
                        spriteDestination = new(
                            x * ViewportCulling.TileSize + 16 - sourceRect.Width / 2.0,
                            (y + 1) * ViewportCulling.TileSize - sourceRect.Height,
                            sourceRect.Width,
                            sourceRect.Height);
                        spriteIntersects = Intersects(spriteDestination, visibleWorld);
                    }

                    if (!spriteIntersects && !cellIntersects)
                    {
                        continue;
                    }

                    SpriteResolution resolution = _assets.Resolve(reference);

                    if (resolution.Status == SpriteResolutionStatus.Ready && spriteIntersects)
                    {
                        sink.DrawSprite(new SpriteDrawOperation(
                            layer,
                            new MapTileCoordinate(x, y),
                            reference,
                            resolution.Image!,
                            resolution.SourceRect,
                            viewport.WorldToScreen(spriteDestination),
                            SpriteSampling.NearestNeighbor));
                    }
                    else if (resolution.Status != SpriteResolutionStatus.Ready && cellIntersects)
                    {
                        sink.DrawPlaceholder(new PlaceholderDrawOperation(
                            layer,
                            new MapTileCoordinate(x, y),
                            reference,
                            resolution.Status,
                            viewport.WorldToScreen(cell),
                            MapRenderPalette.PlaceholderFill,
                            MapRenderPalette.PlaceholderStroke,
                            resolution.Diagnostic ?? string.Empty));
                    }
                }
            }
        }

        if (options.ShowBlocked)
        {
            for (int y = ranges.VisibleCells.MinY; y <= ranges.VisibleCells.MaxY; y++)
            {
                for (int x = ranges.VisibleCells.MinX; x <= ranges.VisibleCells.MaxX; x++)
                {
                    if (!document[x, y].IsBlocked)
                    {
                        continue;
                    }

                    sink.DrawCellOverlay(new CellOverlayDrawOperation(
                        CellOverlayKind.Blocked,
                        new MapTileCoordinate(x, y),
                        viewport.WorldToScreen(CellRect(x, y)),
                        MapRenderPalette.BlockedFill,
                        MapRenderPalette.Transparent));
                }
            }
        }

        if (options.ShowGrid)
        {
            DrawGrid(document, viewport, ranges, sink);
        }

        if (options.SelectedTile is { } selected)
        {
            DrawTarget(document, viewport, selected, CellOverlayKind.Selected, MapRenderPalette.SelectedStroke, sink);
        }

        if (options.HoveredTile is { } hovered)
        {
            DrawTarget(document, viewport, hovered, CellOverlayKind.Hovered, MapRenderPalette.HoveredStroke, sink);
        }
    }

    private static void DrawGrid(MapDocument document, ViewportTransform viewport, ViewportTileRanges ranges, IMapDrawSink sink)
    {
        if (ranges.VisibleCells.IsEmpty)
        {
            return;
        }

        RenderRect visible = viewport.VisibleWorldRect;
        double left = Math.Max(visible.X, 0.0);
        double top = Math.Max(visible.Y, 0.0);
        double right = Math.Min(visible.X + visible.Width, document.Width * (double)ViewportCulling.TileSize);
        double bottom = Math.Min(visible.Y + visible.Height, document.Height * (double)ViewportCulling.TileSize);

        if (left >= right || top >= bottom)
        {
            return;
        }

        for (int boundary = ranges.VisibleCells.MinX; boundary <= ranges.VisibleCells.MaxX + 1; boundary++)
        {
            double worldX = boundary * ViewportCulling.TileSize;
            if (worldX < left || worldX > right)
            {
                continue;
            }

            sink.DrawGridLine(new GridLineDrawOperation(
                viewport.WorldToScreen(new RenderPoint(worldX, top)),
                viewport.WorldToScreen(new RenderPoint(worldX, bottom)),
                MapRenderPalette.Grid));
        }

        for (int boundary = ranges.VisibleCells.MinY; boundary <= ranges.VisibleCells.MaxY + 1; boundary++)
        {
            double worldY = boundary * ViewportCulling.TileSize;
            if (worldY < top || worldY > bottom)
            {
                continue;
            }

            sink.DrawGridLine(new GridLineDrawOperation(
                viewport.WorldToScreen(new RenderPoint(left, worldY)),
                viewport.WorldToScreen(new RenderPoint(right, worldY)),
                MapRenderPalette.Grid));
        }
    }

    private static void DrawTarget(
        MapDocument document,
        ViewportTransform viewport,
        MapTileCoordinate tile,
        CellOverlayKind kind,
        RenderColor stroke,
        IMapDrawSink sink)
    {
        if (tile.X < 0 || tile.X >= document.Width || tile.Y < 0 || tile.Y >= document.Height)
        {
            return;
        }

        RenderRect cell = CellRect(tile.X, tile.Y);
        if (!Intersects(cell, viewport.VisibleWorldRect))
        {
            return;
        }

        sink.DrawCellOverlay(new CellOverlayDrawOperation(kind, tile, viewport.WorldToScreen(cell), MapRenderPalette.Transparent, stroke));
    }

    private static RenderRect CellRect(int x, int y)
        => new(x * ViewportCulling.TileSize, y * ViewportCulling.TileSize, ViewportCulling.TileSize, ViewportCulling.TileSize);

    private static bool Intersects(RenderRect a, RenderRect b)
        => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    internal static class MapRenderPalette
    {
        public static readonly RenderColor Transparent = new(0, 0, 0, 0);
        public static readonly RenderColor PlaceholderFill = new(0xFF, 0x00, 0xFF, 0xCC);
        public static readonly RenderColor PlaceholderStroke = new(0xFF, 0xFF, 0x00, 0xFF);
        public static readonly RenderColor BlockedFill = new(0xFF, 0x00, 0x00, 0x60);
        public static readonly RenderColor Grid = new(0xFF, 0xFF, 0xFF, 0x30);
        public static readonly RenderColor SelectedStroke = new(0x00, 0xFF, 0xFF, 0xFF);
        public static readonly RenderColor HoveredStroke = new(0xFF, 0x00, 0xFF, 0xFF);
    }
}
