namespace Goose2Client
{
    /// <summary>
    /// Reads text files that live under res://.
    ///
    /// In an exported build res:// lives inside the .pck, so ProjectSettings.GlobalizePath
    /// returns a path next to the executable that does not exist on disk and System.IO
    /// throws. Only Godot.FileAccess can read through the pck, which is why every res://
    /// text read goes through here rather than File.ReadAllText.
    /// </summary>
    public static class ResourceText
    {
        /// <summary>Whole file as text. Throws if the resource is missing or unreadable.</summary>
        public static string ReadAll(string resPath)
        {
            using var f = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
            if (f == null)
                throw new System.IO.IOException(
                    $"could not open '{resPath}': {Godot.FileAccess.GetOpenError()}");

            return f.GetAsText();
        }
    }
}
