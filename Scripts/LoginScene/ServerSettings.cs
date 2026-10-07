namespace Goose2Client;

public static class ServerSettings
{
    public const string DefaultHost = "game.illutia.net";
    public const int DefaultPort = 2006;

    public static string NormalizeHost(string? raw)
    {
        var host = (raw ?? "").Trim().ToLowerInvariant();
        foreach (var scheme in new[] { "https://", "http://" })
            if (host.StartsWith(scheme))
            {
                host = host[scheme.Length..];
                break;
            }
        return host.TrimEnd('/');
    }

    public static bool TryValidate(string? hostRaw, string? portRaw, out string host, out int port, out string? error)
    {
        host = NormalizeHost(hostRaw);
        port = DefaultPort;

        if (host.Length == 0)
        {
            error = "Enter a server address.";
            return false;
        }
        if (host.Contains(' ') || host.Contains(':'))
        {
            error = "Enter only the address; put the port in the Port field.";
            return false;
        }

        var trimmed = (portRaw ?? "").Trim();
        if (trimmed.Length == 0)
        {
            error = null;
            return true;
        }
        if (!int.TryParse(trimmed, out port) || port < 1 || port > 65535)
        {
            port = DefaultPort;
            error = "Port must be a number from 1 to 65535.";
            return false;
        }

        error = null;
        return true;
    }

    public static string FormatLabel(string host, int port)
        => port == DefaultPort ? host : $"{host}:{port}";
}
