using System.Collections.Generic;

namespace Goose2Client
{
    // Port of the server's SpellEffect.ShapeTiles; tests/Goose2Client.Tests/Fixtures/shape-tiles.json is the shared contract.
    public static class SpellShapes
    {
        public static bool HasPreview(SpellShape shape) => shape is not (SpellShape.Target or SpellShape.Random);

        public static List<(int X, int Y)> Tiles(SpellShape shape, int size, int ox, int oy, Direction facing)
        {
            var tiles = new List<(int X, int Y)>();

            switch (shape)
            {
                case SpellShape.Target:
                    tiles.Add((ox, oy));
                    break;

                case SpellShape.LineFront:
                {
                    int x = ox;
                    int y = oy;
                    for (int i = 1; i <= size; i++)
                    {
                        (x, y) = facing switch
                        {
                            Direction.Up => (x, y - 1),
                            Direction.Right => (x + 1, y),
                            Direction.Down => (x, y + 1),
                            Direction.Left => (x - 1, y),
                            _ => (x, y),
                        };
                        tiles.Add((x, y));
                    }
                    break;
                }

                case SpellShape.Area:
                    for (int y = oy - size; y <= oy + size; y++)
                        for (int x = ox - size; x <= ox + size; x++)
                            tiles.Add((x, y));
                    break;

                case SpellShape.Cross:
                    for (int y = oy - size; y <= oy + size; y++)
                        for (int x = ox - size; x <= ox + size; x++)
                            if (x == ox - (y - oy) || x == ox + (y - oy))
                                tiles.Add((x, y));
                    break;

                case SpellShape.Plus:
                    for (int y = oy - size; y <= oy + size; y++)
                        for (int x = ox - size; x <= ox + size; x++)
                            if (x == ox || y == oy)
                                tiles.Add((x, y));
                    break;

                case SpellShape.TriangleFront:
                {
                    int x = ox;
                    int y = oy;
                    for (int i = 1; i <= size; i++)
                    {
                        (x, y) = facing switch
                        {
                            Direction.Up => (ox - i, y - 1),
                            Direction.Right => (x + 1, oy - i),
                            Direction.Down => (ox - i, y + 1),
                            Direction.Left => (x - 1, oy - i),
                            _ => (x, y),
                        };

                        for (int j = 0; j < i * 2 - 1; j++)
                        {
                            (x, y) = facing switch
                            {
                                Direction.Up => (x + 1, y),
                                Direction.Right => (x, y + 1),
                                Direction.Down => (x + 1, y),
                                Direction.Left => (x, y + 1),
                                _ => (x, y),
                            };
                            tiles.Add((x, y));
                        }
                    }
                    break;
                }
            }

            return tiles;
        }
    }
}
