namespace MapEditor.App.Settings;

public sealed record AppSettings(string? AssetDirectory, AppTheme Theme = AppTheme.Dark);
