using System;
using System.IO;
using System.Text.Json;

namespace MapEditor.App.Settings;

public sealed class AppSettingsStore
{
    private const string TempPrefix = "settings-";
    private const string TempSuffix = ".tmp";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ISettingsFileOperations _operations;
    private readonly string _path;

    public AppSettingsStore(string path)
        : this(path, new SystemFileOperations())
    {
    }

    internal AppSettingsStore(string path, ISettingsFileOperations operations)
    {
        _path = path;
        _operations = operations;
    }

    public AppSettings Load()
    {
        if (!_operations.FileExists(_path))
        {
            return new AppSettings(null);
        }

        string text;
        try
        {
            text = _operations.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is not AppSettingsException)
        {
            throw new AppSettingsException(_path, "could not be read.", ex);
        }

        SettingsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SettingsFile>(text, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new AppSettingsException(_path, "malformed JSON.", ex);
        }

        var assetDirectory = file?.AssetDirectory;
        if (assetDirectory is not null && !Path.IsPathRooted(assetDirectory))
        {
            throw new AppSettingsException(_path, $"assetDirectory must be an absolute path, got '{assetDirectory}'.");
        }

        return new AppSettings(assetDirectory, ParseTheme(file?.Theme), file?.SpreadsheetUrl);
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(
            new SettingsFile
            {
                AssetDirectory = settings.AssetDirectory,
                Theme = settings.Theme.ToString(),
                SpreadsheetUrl = settings.SpreadsheetUrl
            },
            SerializerOptions);
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        var temp = Path.Combine(directory, TempPrefix + Guid.NewGuid().ToString("N") + TempSuffix);
        var failed = false;

        try
        {
            _operations.CreateDirectory(directory);
            _operations.WriteAllText(temp, json);
            _operations.Move(temp, _path);
        }
        catch (Exception ex)
        {
            failed = true;
            throw new AppSettingsException(_path, "could not be saved.", ex);
        }
        finally
        {
            try
            {
                if (_operations.FileExists(temp))
                {
                    _operations.DeleteFile(temp);
                }
            }
            catch when (failed)
            {
            }
        }
    }

    /// <summary>Loads the current settings, falling back to defaults when the file is unreadable.</summary>
    public AppSettings LoadOrDefault()
    {
        try
        {
            return Load();
        }
        catch (AppSettingsException)
        {
            return new AppSettings(null);
        }
    }

    /// <summary>Rewrites one field without discarding the others already on disk.</summary>
    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        Save(mutate(LoadOrDefault()));
    }

    // An unrecognised or missing theme falls back to the default rather than failing the load.
    private static AppTheme ParseTheme(string? value)
        => Enum.TryParse(value, ignoreCase: true, out AppTheme theme) ? theme : AppTheme.Dark;

    private sealed class SettingsFile
    {
        public string? AssetDirectory { get; set; }

        public string? Theme { get; set; }

        public string? SpreadsheetUrl { get; set; }
    }

    private sealed class SystemFileOperations : ISettingsFileOperations
    {
        public bool FileExists(string path) => File.Exists(path);

        public string ReadAllText(string path) => File.ReadAllText(path);

        public void CreateDirectory(string directory) => Directory.CreateDirectory(directory);

        public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

        public void Move(string source, string destination) => File.Move(source, destination, overwrite: true);

        public void DeleteFile(string path) => File.Delete(path);
    }
}
