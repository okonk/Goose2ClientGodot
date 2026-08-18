namespace Goose2Client
{
    /// <summary>
    /// The build identifier stamped by build.sh, or "dev" when running from the editor.
    /// </summary>
    public static class BuildInfo
    {
        private const string BuildIdPath = "res://build_id.txt";

        private static string? cached;

        /// <summary>Build id for display. Read once, then cached for the process lifetime.</summary>
        public static string Id => cached ??= Normalize(ReadBuildIdFile());

        /// <summary>
        /// Pure fallback logic, split out so it is testable — the test project has no Godot
        /// engine and cannot call FileAccess.
        /// </summary>
        public static string Normalize(string? raw)
            => string.IsNullOrWhiteSpace(raw) ? "dev" : raw.Trim();

        private static string? ReadBuildIdFile()
        {
            if (!Godot.FileAccess.FileExists(BuildIdPath)) return null;

            using var f = Godot.FileAccess.Open(BuildIdPath, Godot.FileAccess.ModeFlags.Read);
            return f?.GetAsText();
        }
    }
}
