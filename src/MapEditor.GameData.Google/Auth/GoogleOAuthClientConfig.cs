using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Apis.Auth.OAuth2;

namespace MapEditor.GameData.Google.Auth;

public sealed class GoogleOAuthClientConfigurationException : Exception
{
    public GoogleOAuthClientConfigurationException(string message)
        : base(message)
    {
    }

    public GoogleOAuthClientConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class GoogleOAuthClientConfig
{
    public const string EnvironmentVariableName = "GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT";
    public const string BesideExecutableFileName = "google-oauth-client.json";

    private readonly string? _clientJsonPath;
    private readonly string _executableDirectory;

    public GoogleOAuthClientConfig()
        : this(Environment.GetEnvironmentVariable(EnvironmentVariableName), AppContext.BaseDirectory)
    {
    }

    public GoogleOAuthClientConfig(string? clientJsonPath, string executableDirectory)
    {
        _clientJsonPath = clientJsonPath;
        _executableDirectory = executableDirectory;
    }

    public string ResolveClientJsonPath()
    {
        return !string.IsNullOrWhiteSpace(_clientJsonPath) && Path.IsPathRooted(_clientJsonPath)
            ? _clientJsonPath
            : Path.Combine(_executableDirectory, BesideExecutableFileName);
    }

    public ClientSecrets LoadClientSecrets()
    {
        var path = ResolveClientJsonPath();
        if (!File.Exists(path))
        {
            throw new GoogleOAuthClientConfigurationException($"Google OAuth client JSON was not found at {path}.");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GoogleOAuthClientConfigurationException($"Google OAuth client JSON at {path} could not be read.", ex);
        }

        ValidateDesktopClient(bytes, path);

        try
        {
            // GoogleClientSecrets only maps the legacy "installed"/"web" sections, so the
            // modern "client" section is normalized to "installed" before loading.
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var loadBytes = bytes;
            if (root.TryGetProperty("client", out var client) && !root.TryGetProperty("installed", out _))
            {
                var converted = new JsonObject { ["installed"] = JsonNode.Parse(client.GetRawText()) };
                loadBytes = Encoding.UTF8.GetBytes(converted.ToJsonString());
            }

            using var stream = new MemoryStream(loadBytes);
            return GoogleClientSecrets.FromStream(stream).Secrets;
        }
        catch (Exception ex)
        {
            throw new GoogleOAuthClientConfigurationException($"Google OAuth client JSON at {path} is not a valid desktop client file.", ex);
        }
    }

    private static void ValidateDesktopClient(byte[] bytes, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new GoogleOAuthClientConfigurationException($"Google OAuth client JSON at {path} is not a desktop client file.");
            }

            if (root.TryGetProperty("client", out var client) && client.ValueKind == JsonValueKind.Object)
            {
                RequireDesktopSection(client, path);
            }
            else if (root.TryGetProperty("installed", out var installed) && installed.ValueKind == JsonValueKind.Object)
            {
                RequireDesktopSection(installed, path);
            }
            else if (root.TryGetProperty("web", out _))
            {
                throw new GoogleOAuthClientConfigurationException($"Google OAuth client JSON at {path} is a web client, not a desktop client.");
            }
            else
            {
                throw new GoogleOAuthClientConfigurationException($"Google OAuth client JSON at {path} is not a desktop client file.");
            }
        }
        catch (JsonException)
        {
            throw new GoogleOAuthClientConfigurationException($"Google OAuth client JSON at {path} is not valid JSON.");
        }
    }

    private static void RequireDesktopSection(JsonElement section, string path)
    {
        if (!section.TryGetProperty("client_id", out var clientId) || !IsNonEmptyString(clientId)
            || !section.TryGetProperty("client_secret", out var clientSecret) || !IsNonEmptyString(clientSecret)
            || !section.TryGetProperty("auth_uri", out _)
            || !section.TryGetProperty("token_uri", out _))
        {
            throw new GoogleOAuthClientConfigurationException($"Google OAuth client JSON at {path} is not a desktop client file.");
        }
    }

    private static bool IsNonEmptyString(JsonElement element)
        => element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString());
}
