using System;
using Google;
using MapEditor.GameData.Connectivity;

namespace MapEditor.GameData.Google.Sheets;

internal static class GoogleFailureMapper
{
    public static GameDataGatewayException Map(Exception exception, string spreadsheetId)
    {
        if (exception is GoogleApiException apiException)
        {
            var status = (int)apiException.HttpStatusCode;
            if (status == 0)
            {
                return Create(GatewayFailureKind.Transport, spreadsheetId, false,
                    "Google Sheets request failed without a response.", apiException);
            }
            switch (status)
            {
                case 401:
                    return Create(GatewayFailureKind.Authentication, spreadsheetId, false,
                        "Google Sheets request was rejected: not authenticated (401).", apiException);
                case 403:
                    return Create(GatewayFailureKind.Permission, spreadsheetId, false,
                        "Google Sheets request was rejected: insufficient permission (403).", apiException);
                case 404:
                    return Create(GatewayFailureKind.NotFound, spreadsheetId, false,
                        "Google Sheets resource was not found (404).", apiException);
                case 429:
                    // 429 is the only explicit rejection the coordinator may treat as safely unapplied.
                    return Create(GatewayFailureKind.RateLimited, spreadsheetId, true,
                        "Google Sheets request was rate limited (429).", apiException);
            }
            if (status is >= 500 and <= 599)
            {
                return Create(GatewayFailureKind.Temporary, spreadsheetId, false,
                    $"Google Sheets request failed with server error ({status}).", apiException);
            }
            return Create(GatewayFailureKind.Temporary, spreadsheetId, false,
                $"Google Sheets request failed with status {status}.", apiException);
        }
        return Create(GatewayFailureKind.Transport, spreadsheetId, false,
            $"Google Sheets request failed: {exception.Message}", exception);
    }

    private static GameDataGatewayException Create(
        GatewayFailureKind kind,
        string spreadsheetId,
        bool requestWasRejected,
        string message,
        Exception innerException)
        => new(kind, spreadsheetId, requestWasRejected, message, innerException);
}
