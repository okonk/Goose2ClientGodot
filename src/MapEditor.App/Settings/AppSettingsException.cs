using System;

namespace MapEditor.App.Settings;

public sealed class AppSettingsException : Exception
{
    public string Path { get; }

    public AppSettingsException(string path, string message, Exception? innerException = null)
        : base($"Settings file '{path}': {message}", innerException)
    {
        Path = path;
    }
}
