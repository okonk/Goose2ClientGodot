namespace MapEditor.Rendering;

public enum GraphicCategory
{
    Body,
    Hair,
    Eyes,
    Chest,
    Helm,
    Legs,
    Feet,
    Hand,
    Tiles,
    Spells
}

public readonly record struct GraphicCategoryMapping(GraphicCategory Category, int? Id);
