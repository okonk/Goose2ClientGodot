namespace MapEditor.Rendering;

public readonly record struct SpriteReference(int Sheet, int Graphic)
{
    public bool IsEmpty => Graphic == 0;
}

public readonly record struct SpriteSourceRect(int X, int Y, int Width, int Height);
