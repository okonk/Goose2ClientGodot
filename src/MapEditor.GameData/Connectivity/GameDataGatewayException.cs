using System;

namespace MapEditor.GameData.Connectivity;

public enum GatewayFailureKind
{
    Authentication,
    Permission,
    NotFound,
    Schema,
    RateLimited,
    Temporary,
    Transport
}

public sealed class GameDataGatewayException : Exception
{
    public GatewayFailureKind Kind { get; }
    public string SpreadsheetId { get; }
    public bool RequestWasRejected { get; }

    public GameDataGatewayException(
        GatewayFailureKind kind,
        string spreadsheetId,
        bool requestWasRejected,
        string message)
        : this(kind, spreadsheetId, requestWasRejected, message, innerException: null)
    {
    }

    public GameDataGatewayException(
        GatewayFailureKind kind,
        string spreadsheetId,
        bool requestWasRejected,
        string message,
        Exception? innerException)
        : base(message, innerException)
    {
        Kind = kind;
        SpreadsheetId = spreadsheetId ?? throw new ArgumentNullException(nameof(spreadsheetId));
        RequestWasRejected = requestWasRejected;
    }
}
