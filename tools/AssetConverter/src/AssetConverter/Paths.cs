namespace Goose2.AssetConverter;

/// <summary>Absolute locations of the original game data. Defaults suit the cloud
/// workspace; override per-machine with environment variables.</summary>
public static class Paths
{
    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

    public static string IllutiaData => Env("ILLUTIA_DATA", "/home/agent/workspace/Illutia/data");
    public static string IllutiaMaps => Env("ILLUTIA_MAPS", "/home/agent/workspace/Illutia/maps");
    public static string UnitySpritesheets =>
        Env("UNITY_SPRITESHEETS", "/home/agent/workspace/Goose2Client/Assets/Spritesheets");

    public static string AsperetaData => Env("ASPERETA_DATA",
        "/home/hayden/code/gooseclient/AsperetaClient/bin/Release/net8/data");
    public static string AsperetaMaps => Env("ASPERETA_MAPS",
        "/home/hayden/code/gooseclient/AsperetaClient/bin/Release/net8/maps");

    public static string CompiledEnc => Path.Combine(IllutiaData, "compiled.enc");
    public static string AsperetaCompiledEnc => Path.Combine(AsperetaData, "compiled.enc");

    public static string Adf(int fileNumber) => Path.Combine(IllutiaData, $"{fileNumber}.adf");
    public static string UnityPng(int fileNumber) =>
        Path.Combine(UnitySpritesheets, $"{fileNumber}.png");
}
