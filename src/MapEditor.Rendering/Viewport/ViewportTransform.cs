using System;

namespace MapEditor.Rendering;

public readonly record struct ViewportTransform
{
    public RenderSize ViewportSize { get; }

    public RenderPoint WorldOrigin { get; }

    public MapZoom Zoom { get; }

    public double Scale => MapZoomLevels.GetScale(Zoom);

    public RenderRect VisibleWorldRect => new(
        WorldOrigin.X,
        WorldOrigin.Y,
        ViewportSize.Width / Scale,
        ViewportSize.Height / Scale);

    public ViewportTransform(RenderSize viewportSize, RenderPoint worldOrigin, MapZoom zoom)
    {
        Geometry.CheckSize(viewportSize, nameof(viewportSize));
        Geometry.CheckPoint(worldOrigin, nameof(worldOrigin));
        MapZoomLevels.GetScale(zoom);
        ViewportSize = viewportSize;
        WorldOrigin = worldOrigin;
        Zoom = zoom;
    }

    public RenderPoint WorldToScreen(RenderPoint worldPoint)
    {
        Geometry.CheckPoint(worldPoint, nameof(worldPoint));
        return new((worldPoint.X - WorldOrigin.X) * Scale, (worldPoint.Y - WorldOrigin.Y) * Scale);
    }

    public RenderRect WorldToScreen(RenderRect worldRect)
    {
        Geometry.CheckRect(worldRect, nameof(worldRect));
        RenderPoint origin = WorldToScreen(new RenderPoint(worldRect.X, worldRect.Y));
        return new(origin.X, origin.Y, worldRect.Width * Scale, worldRect.Height * Scale);
    }

    public RenderPoint ScreenToWorld(RenderPoint screenPoint)
    {
        Geometry.CheckPoint(screenPoint, nameof(screenPoint));
        return new(screenPoint.X / Scale + WorldOrigin.X, screenPoint.Y / Scale + WorldOrigin.Y);
    }

    public MapTileCoordinate ScreenToTile(RenderPoint screenPoint)
    {
        RenderPoint world = ScreenToWorld(screenPoint);
        return new(
            (int)Math.Floor(world.X / ViewportCulling.TileSize),
            (int)Math.Floor(world.Y / ViewportCulling.TileSize));
    }

    public ViewportTransform WithWorldOrigin(RenderPoint worldOrigin)
    {
        Geometry.CheckPoint(worldOrigin, nameof(worldOrigin));
        return new ViewportTransform(ViewportSize, worldOrigin, Zoom);
    }

    public ViewportTransform PanByScreenDelta(RenderPoint screenDelta)
    {
        Geometry.CheckPoint(screenDelta, nameof(screenDelta));
        return new ViewportTransform(
            ViewportSize,
            new RenderPoint(
                WorldOrigin.X - screenDelta.X / Scale,
                WorldOrigin.Y - screenDelta.Y / Scale),
            Zoom);
    }

    public ViewportTransform ZoomAt(MapZoom nextZoom, RenderPoint screenAnchor)
    {
        double nextScale = MapZoomLevels.GetScale(nextZoom);
        Geometry.CheckPoint(screenAnchor, nameof(screenAnchor));
        RenderPoint worldUnderAnchor = ScreenToWorld(screenAnchor);
        return new ViewportTransform(
            ViewportSize,
            new RenderPoint(
                worldUnderAnchor.X - screenAnchor.X / nextScale,
                worldUnderAnchor.Y - screenAnchor.Y / nextScale),
            nextZoom);
    }
}
