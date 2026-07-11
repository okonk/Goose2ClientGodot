namespace Goose2Client.Character;

/// <summary>Pure static helper that assembles Title + Name + Surname, matching Unity.</summary>
public static class NameFormatting
{
    public static string FullName(string? title, string name, string? surname)
    {
        return $"{title} {name} {surname}".Trim();
    }
}
