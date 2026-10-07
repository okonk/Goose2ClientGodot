using Godot;

namespace Goose2Client;

public static class ServerConfig
{
    private const string Path = "user://login.cfg";

    public static (string Host, int Port) Load()
    {
        var host = ServerSettings.DefaultHost;
        var port = ServerSettings.DefaultPort;

        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            host = (string)cfg.GetValue("server", "host", host);
            port = (int)cfg.GetValue("server", "port", port);
        }

        // Env wins over the saved config so the headless-test override keeps working.
        if (OS.GetEnvironment("GOOSE_HOST") is { Length: > 0 } h)
            host = h;
        if (int.TryParse(OS.GetEnvironment("GOOSE_PORT"), out int p))
            port = p;

        return (host, port);
    }

    public static void Save(string host, int port)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("server", "host", host);
        cfg.SetValue("server", "port", port);
        cfg.Save(Path);
    }
}
