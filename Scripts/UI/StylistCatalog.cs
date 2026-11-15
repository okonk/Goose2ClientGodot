using System.Collections.Generic;

namespace Goose2Client.UI
{
    public static class StylistCatalog
    {
        public const int BaldHairId = 0;
        public static IReadOnlyList<int> HairIds { get; } = new[] { BaldHairId, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46 };
        public static IReadOnlyList<int> FaceIds { get; } = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
        public const int MaleBodyId = 1;
        public const int FemaleBodyId = 11;
        public static IReadOnlyList<int> BodyIds { get; } = new[] { MaleBodyId, FemaleBodyId };
    }
}
