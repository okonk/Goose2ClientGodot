using Godot;

namespace Goose2Client;

/// <summary>
/// Thin wrapper over Godot ConfigFile persisted at user://login.cfg.
/// Faithful port of Unity PlayerPrefs.GetString/SetString("CharacterName"/"CharacterPassword").
/// Plaintext on disk is intentional to match the original Unity behavior; hardening is out of scope.
/// </summary>
public static class LoginCredentialStore
{
    private const string Path = "user://login.cfg";

    /// <summary>
    /// Returns ("", "") when the file does not exist or keys are missing.
    /// </summary>
    public static (string Name, string Password) Load()
    {
        var cfg = new ConfigFile();
        var err = cfg.Load(Path);
        if (err != Error.Ok)
            return ("", "");

        var name = (string)cfg.GetValue("credentials", "name", "");
        var password = (string)cfg.GetValue("credentials", "password", "");
        return (name, password);
    }

    public static void Save(string name, string password)
    {
        var cfg = new ConfigFile();
        cfg.SetValue("credentials", "name", name);
        cfg.SetValue("credentials", "password", password);
        cfg.Save(Path);
    }
}
