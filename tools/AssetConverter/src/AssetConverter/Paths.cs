namespace Goose2.AssetConverter;

/// <summary>Absolute locations of the original game data and the Unity client's
/// known-good generated output (used as test oracles). Change here if the repos move.</summary>
public static class Paths
{
    public const string IllutiaData = "/home/agent/workspace/Illutia/data";
    public const string IllutiaMaps = "/home/agent/workspace/Illutia/maps";
    public const string UnitySpritesheets =
        "/home/agent/workspace/Goose2Client/Assets/Spritesheets";

    public static string CompiledEnc => Path.Combine(IllutiaData, "compiled.enc");

    public static string Adf(int fileNumber) => Path.Combine(IllutiaData, $"{fileNumber}.adf");
    public static string UnityPng(int fileNumber) =>
        Path.Combine(UnitySpritesheets, $"{fileNumber}.png");
}
