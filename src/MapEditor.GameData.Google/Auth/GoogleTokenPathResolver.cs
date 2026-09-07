using System;
using System.IO;

namespace MapEditor.GameData.Google.Auth;

public static class GoogleTokenPathResolver
{
    public static string Resolve()
    {
        if (OperatingSystem.IsWindows())
        {
            return ResolveWindows(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        }

        if (OperatingSystem.IsMacOS())
        {
            return ResolveMacOs(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        }

        return ResolveLinux(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public static string ResolveWindows(string appDataDirectory)
    {
        return Path.Combine(appDataDirectory, "Goose2MapEditor", "google-tokens");
    }

    public static string ResolveMacOs(string applicationSupportDirectory)
    {
        return Path.Combine(applicationSupportDirectory, "Goose2MapEditor", "google-tokens");
    }

    public static string ResolveLinux(string? xdgConfigHome, string homeDirectory)
    {
        var baseDirectory = !string.IsNullOrEmpty(xdgConfigHome) && Path.IsPathRooted(xdgConfigHome)
            ? xdgConfigHome
            : Path.Combine(homeDirectory, ".config");

        return Path.Combine(baseDirectory, "goose2-map-editor", "google-tokens");
    }
}
