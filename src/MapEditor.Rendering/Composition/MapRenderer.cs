using System;
using System.Collections.Generic;
using System.Linq;
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

        RenderRect visibleWorld = viewport.VisibleWorldRect;

        IReadOnlyList<NpcAppearanceGroup>? npcGroups = options.PreviewMode ? options.NpcPreviews : null;

        if (npcGroups is not null)
        {
            foreach (NpcAppearanceGroup group in npcGroups)
            {
                if (IsEntityCandidate(group, document, visibleWorld))
                {
                    requestedReads += group.Parts.Count;
                }
            }
        }

        if (requestedReads > MaximumTileReadsPerRender)
        {
            throw new MapRenderWorkLimitException(
                requestedReads,
                MaximumTileReadsPerRender,
                $"Render work limit exceeded: {requestedReads} tile reads requested, maximum {MaximumTileReadsPerRender}. Zoom in, hide layers or the blocked overlay, or inspect anomalously large sprite frame metadata.");
        }

        for (int layer = 0; layer <= 1; layer++)
        {
            if (visibility.IsVisible(layer))
            {
                EmitMapLayer(layer, document, ranges, viewport, sink);
            }
        }

        RenderEntityStage(document, ranges, visibility, npcGroups, viewport, sink);

        for (int layer = 3; layer < MapDocument.LayerCount; layer++)
        {
            if (visibility.IsVisible(layer))
            {
                EmitMapLayer(layer, document, ranges, viewport, sink);
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

        if (options.PreviewMode && npcGroups is not null)
        {
            DrawMarkers(options.SpawnMarkers, GameDataMarkerKind.Spawn, document, viewport, sink,
                new HashSet<int>(npcGroups.Select(group => group.OccurrenceIndex)));
        }
        else
        {
            DrawMarkers(options.SpawnMarkers, GameDataMarkerKind.Spawn, document, viewport, sink);
        }
        DrawMarkers(options.WarpMarkers, GameDataMarkerKind.Warp, document, viewport, sink);

        if (npcGroups is not null)
        {
            DrawNpcSpawnAnchors(npcGroups, options.SpawnMarkers, document, viewport, sink);
        }

        if (options.SelectedTile is { } selected)
        {
            DrawTarget(document, viewport, selected, CellOverlayKind.Selected, MapRenderPalette.SelectedStroke, sink);
        }

        if (options.HoveredTile is { } hovered)
        {
            DrawTarget(document, viewport, hovered, CellOverlayKind.Hovered, MapRenderPalette.HoveredStroke, sink);
        }

        if (options.SelectionRectangle is { } selection)
        {
            DrawRectangleOutline(document, viewport, selection, MapRenderPalette.SelectionStroke, sink);
        }

        if (options.PasteGhost is { } ghost)
        {
            DrawRectangleFill(document, viewport, ghost, MapRenderPalette.PasteGhostFill, CellOverlayKind.PasteGhost, sink);
            DrawRectangleOutline(document, viewport, ghost, MapRenderPalette.PasteGhostStroke, sink);
        }

        if (options.BlockPreview is { } preview)
        {
            DrawRectangleFill(document, viewport, preview.Rectangle,
                preview.Blocked ? MapRenderPalette.BlockPreviewFill : MapRenderPalette.UnblockPreviewFill,
                CellOverlayKind.BlockPreview, sink);
        }
    }

    private void EmitMapLayer(int layer, MapDocument document, ViewportTileRanges ranges, ViewportTransform viewport, IMapDrawSink sink)
    {
        RenderRect visibleWorld = viewport.VisibleWorldRect;
        SpriteManifest? manifest = _assets.Manifest;
        for (int y = ranges.SpriteCandidates.MinY; y <= ranges.SpriteCandidates.MaxY; y++)
        {
            for (int x = ranges.SpriteCandidates.MinX; x <= ranges.SpriteCandidates.MaxX; x++)
            {
                MapTileLayer tileLayer = document[x, y].GetLayer(layer);
                if (tileLayer.Graphic == 0)
                {
                    continue;
                }

                EmitMapTile(layer, x, y, tileLayer, manifest, viewport, visibleWorld, sink);
            }
        }
    }

    private void EmitMapTile(
        int layer,
        int x,
        int y,
        MapTileLayer tileLayer,
        SpriteManifest? manifest,
        ViewportTransform viewport,
        RenderRect visibleWorld,
        IMapDrawSink sink)
    {
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
            return;
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

    private void RenderEntityStage(
        MapDocument document,
        ViewportTileRanges ranges,
        MapLayerVisibility visibility,
        IReadOnlyList<NpcAppearanceGroup>? npcGroups,
        ViewportTransform viewport,
        IMapDrawSink sink)
    {
        RenderRect visibleWorld = viewport.VisibleWorldRect;
        SpriteManifest? manifest = _assets.Manifest;
        List<EntityCandidate> candidates = new();
        int ordinal = 0;

        if (visibility.IsVisible(2))
        {
            for (int y = ranges.SpriteCandidates.MinY; y <= ranges.SpriteCandidates.MaxY; y++)
            {
                for (int x = ranges.SpriteCandidates.MinX; x <= ranges.SpriteCandidates.MaxX; x++)
                {
                    MapTileLayer tileLayer = document[x, y].GetLayer(2);
                    if (tileLayer.Graphic == 0)
                    {
                        continue;
                    }

                    candidates.Add(new EntityCandidate(
                        (y + 1) * ViewportCulling.TileSize,
                        x * ViewportCulling.TileSize + 16,
                        EntityKind.MapObject,
                        ordinal++,
                        new MapTileCoordinate(x, y),
                        tileLayer,
                        null));
                }
            }
        }

        if (npcGroups is not null)
        {
            foreach (NpcAppearanceGroup group in npcGroups)
            {
                if (!IsEntityCandidate(group, document, visibleWorld))
                {
                    continue;
                }

                candidates.Add(new EntityCandidate(
                    group.SortAnchorY,
                    group.SortAnchorX,
                    EntityKind.Npc,
                    group.OccurrenceIndex,
                    new MapTileCoordinate(group.TileX, group.TileY),
                    null,
                    group));
            }
        }

        candidates.Sort(Comparer<EntityCandidate>.Create((a, b) =>
            a.AnchorY != b.AnchorY ? a.AnchorY.CompareTo(b.AnchorY)
            : a.AnchorX != b.AnchorX ? a.AnchorX.CompareTo(b.AnchorX)
            : a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind)
            : a.Ordinal.CompareTo(b.Ordinal)));

        foreach (EntityCandidate candidate in candidates)
        {
            if (candidate.Group is not null)
            {
                EmitNpcGroup(candidate.Group, viewport, sink);
            }
            else
            {
                EmitMapTile(2, candidate.Tile.X, candidate.Tile.Y, candidate.TileLayer.GetValueOrDefault(), manifest, viewport, visibleWorld, sink);
            }
        }
    }

    private static bool IsEntityCandidate(NpcAppearanceGroup group, MapDocument document, RenderRect visibleWorld)
    {
        if (group.TileX < 0 || group.TileX >= document.Width || group.TileY < 0 || group.TileY >= document.Height)
        {
            return false;
        }

        if (Intersects(CellRect(group.TileX, group.TileY), visibleWorld))
        {
            return true;
        }

        foreach (NpcPartDrawOperation part in group.Parts)
        {
            if (part.IsReady && Intersects(PartRect(part), visibleWorld))
            {
                return true;
            }
        }

        return false;
    }

    private void EmitNpcGroup(NpcAppearanceGroup group, ViewportTransform viewport, IMapDrawSink sink)
    {
        RenderRect visibleWorld = viewport.VisibleWorldRect;
        RenderRect cell = CellRect(group.TileX, group.TileY);
        bool cellIntersects = Intersects(cell, visibleWorld);
        RenderRect cellScreen = viewport.WorldToScreen(cell);

        foreach (NpcPartDrawOperation part in group.Parts)
        {
            if (part.IsReady)
            {
                RenderRect destination = PartRect(part);
                if (!Intersects(destination, visibleWorld))
                {
                    continue;
                }

                SpriteResolution resolution = _assets.Resolve(part.Reference);
                if (resolution.Status == SpriteResolutionStatus.Ready)
                {
                    sink.DrawNpcImage(new NpcImageDrawOperation(
                        group.OccurrenceIndex,
                        part.Slot,
                        part.Reference,
                        resolution.Image!,
                        part.SourceRect,
                        viewport.WorldToScreen(destination),
                        part.Tint,
                        SpriteSampling.NearestNeighbor));
                }
                else
                {
                    sink.DrawNpcPartPlaceholder(new NpcPartPlaceholderDrawOperation(
                        group.OccurrenceIndex,
                        part.Slot,
                        resolution.Status,
                        cellScreen,
                        MapRenderPalette.PlaceholderFill,
                        MapRenderPalette.PlaceholderStroke,
                        resolution.Diagnostic ?? string.Empty));
                }
            }
            else if (cellIntersects)
            {
                sink.DrawNpcPartPlaceholder(new NpcPartPlaceholderDrawOperation(
                    group.OccurrenceIndex,
                    part.Slot,
                    part.Status,
                    cellScreen,
                    MapRenderPalette.PlaceholderFill,
                    MapRenderPalette.PlaceholderStroke,
                    part.Diagnostic ?? string.Empty));
            }
        }
    }

    private static void DrawNpcSpawnAnchors(
        IReadOnlyList<NpcAppearanceGroup> groups,
        IReadOnlyList<GameDataMarkerInput>? spawnMarkers,
        MapDocument document,
        ViewportTransform viewport,
        IMapDrawSink sink)
    {
        RenderRect visible = viewport.VisibleWorldRect;
        foreach (NpcAppearanceGroup group in groups)
        {
            if (group.TileX < 0 || group.TileX >= document.Width || group.TileY < 0 || group.TileY >= document.Height)
            {
                continue;
            }

            RenderRect cell = CellRect(group.TileX, group.TileY);
            if (!Intersects(cell, visible))
            {
                continue;
            }

            bool selected = IsSpawnSelected(spawnMarkers, group.OccurrenceIndex);

            sink.DrawNpcSpawnAnchor(new NpcSpawnAnchorDrawOperation(
                group.OccurrenceIndex,
                new MapTileCoordinate(group.TileX, group.TileY),
                viewport.WorldToScreen(cell),
                selected,
                MapRenderPalette.NpcAnchorFill,
                selected ? MapRenderPalette.NpcAnchorSelectedStroke : MapRenderPalette.NpcAnchorStroke,
                group.EquipmentDiagnostic));
        }
    }

    private static bool IsSpawnSelected(IReadOnlyList<GameDataMarkerInput>? spawnMarkers, int occurrenceIndex)
    {
        if (spawnMarkers is null)
        {
            return false;
        }

        foreach (GameDataMarkerInput marker in spawnMarkers)
        {
            if (marker.OccurrenceIndex == occurrenceIndex)
            {
                return marker.Selected;
            }
        }

        return false;
    }

    private enum EntityKind
    {
        MapObject,
        Npc
    }

    private readonly struct EntityCandidate
    {
        public EntityCandidate(int anchorY, int anchorX, EntityKind kind, int ordinal, MapTileCoordinate tile, MapTileLayer? tileLayer, NpcAppearanceGroup? group)
        {
            AnchorY = anchorY;
            AnchorX = anchorX;
            Kind = kind;
            Ordinal = ordinal;
            Tile = tile;
            TileLayer = tileLayer;
            Group = group;
        }

        public int AnchorY { get; }

        public int AnchorX { get; }

        public EntityKind Kind { get; }

        public int Ordinal { get; }

        public MapTileCoordinate Tile { get; }

        public MapTileLayer? TileLayer { get; }

        public NpcAppearanceGroup? Group { get; }
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

    private static void DrawMarkers(
        IReadOnlyList<GameDataMarkerInput>? markers,
        GameDataMarkerKind kind,
        MapDocument document,
        ViewportTransform viewport,
        IMapDrawSink sink,
        HashSet<int>? suppressedSpawnOccurrences = null)
    {
        if (markers is null)
        {
            return;
        }

        RenderRect visible = viewport.VisibleWorldRect;
        foreach (GameDataMarkerInput marker in markers)
        {
            if (kind == GameDataMarkerKind.Spawn && suppressedSpawnOccurrences?.Contains(marker.OccurrenceIndex) == true)
            {
                continue;
            }

            MapTileCoordinate tile = marker.Tile;
            if (tile.X < 0 || tile.X >= document.Width || tile.Y < 0 || tile.Y >= document.Height)
            {
                continue;
            }

            RenderRect cell = CellRect(tile.X, tile.Y);
            if (!Intersects(cell, visible))
            {
                continue;
            }

            sink.DrawGameDataMarker(new GameDataMarkerDrawOperation(
                kind,
                marker.OccurrenceIndex,
                tile,
                viewport.WorldToScreen(cell),
                marker.Selected,
                marker.Diagnostic));
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

    private static void DrawRectangleOutline(
        MapDocument document,
        ViewportTransform viewport,
        MapTileRectangle rectangle,
        RenderColor color,
        IMapDrawSink sink)
    {
        if (rectangle.ClipTo(document.Width, document.Height) is not { } rect)
        {
            return;
        }

        RenderRect visible = viewport.VisibleWorldRect;
        double left = Math.Max(visible.X, 0.0);
        double top = Math.Max(visible.Y, 0.0);
        double right = Math.Min(visible.X + visible.Width, document.Width * (double)ViewportCulling.TileSize);
        double bottom = Math.Min(visible.Y + visible.Height, document.Height * (double)ViewportCulling.TileSize);

        double x0 = rect.X * (double)ViewportCulling.TileSize;
        double y0 = rect.Y * (double)ViewportCulling.TileSize;
        double x1 = (rect.X + rect.Width) * (double)ViewportCulling.TileSize;
        double y1 = (rect.Y + rect.Height) * (double)ViewportCulling.TileSize;

        if (x0 >= left && x0 <= right)
        {
            sink.DrawGridLine(new GridLineDrawOperation(
                viewport.WorldToScreen(new RenderPoint(x0, y0)),
                viewport.WorldToScreen(new RenderPoint(x0, y1)),
                color));
        }

        if (x1 >= left && x1 <= right)
        {
            sink.DrawGridLine(new GridLineDrawOperation(
                viewport.WorldToScreen(new RenderPoint(x1, y0)),
                viewport.WorldToScreen(new RenderPoint(x1, y1)),
                color));
        }

        if (y0 >= top && y0 <= bottom)
        {
            sink.DrawGridLine(new GridLineDrawOperation(
                viewport.WorldToScreen(new RenderPoint(x0, y0)),
                viewport.WorldToScreen(new RenderPoint(x1, y0)),
                color));
        }

        if (y1 >= top && y1 <= bottom)
        {
            sink.DrawGridLine(new GridLineDrawOperation(
                viewport.WorldToScreen(new RenderPoint(x0, y1)),
                viewport.WorldToScreen(new RenderPoint(x1, y1)),
                color));
        }
    }

    private static void DrawRectangleFill(
        MapDocument document,
        ViewportTransform viewport,
        MapTileRectangle rectangle,
        RenderColor fill,
        CellOverlayKind kind,
        IMapDrawSink sink)
    {
        if (rectangle.ClipTo(document.Width, document.Height) is not { } rect)
        {
            return;
        }

        RenderRect visible = viewport.VisibleWorldRect;
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                RenderRect cell = CellRect(x, y);
                if (!Intersects(cell, visible))
                {
                    continue;
                }

                sink.DrawCellOverlay(new CellOverlayDrawOperation(
                    kind,
                    new MapTileCoordinate(x, y),
                    viewport.WorldToScreen(cell),
                    fill,
                    MapRenderPalette.Transparent));
            }
        }
    }

    private static RenderRect PartRect(NpcPartDrawOperation part)
        => new(part.Destination.X, part.Destination.Y, part.Destination.Width, part.Destination.Height);

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
        public static readonly RenderColor SelectionStroke = new(0xFF, 0xFF, 0xFF, 0xFF);
        public static readonly RenderColor PasteGhostFill = new(0xFF, 0xFF, 0xFF, 0x60);
        public static readonly RenderColor PasteGhostStroke = new(0xFF, 0xBF, 0xBF, 0xBF);
        public static readonly RenderColor BlockPreviewFill = new(0xFF, 0x00, 0x00, 0x60);
        public static readonly RenderColor UnblockPreviewFill = new(0x00, 0xFF, 0x00, 0x60);
        public static readonly RenderColor NpcAnchorFill = new(0x00, 0xC8, 0xFF, 0x40);
        public static readonly RenderColor NpcAnchorStroke = new(0x00, 0xC8, 0xFF, 0xFF);
        public static readonly RenderColor NpcAnchorSelectedStroke = new(0xFF, 0xFF, 0x00, 0xFF);
    }
}
