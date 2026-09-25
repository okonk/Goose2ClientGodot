namespace Goose2Client.Character
{
    public static class BodyClassification
    {
        public static bool IsLayered(int bodyId) => bodyId < 100 || (bodyId >= 10000 && bodyId <= 10099);
    }
}
