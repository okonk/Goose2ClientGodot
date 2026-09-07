using System;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace MapEditor.GameData.Google.Sheets;

public static class GoogleSheetsServiceFactory
{
    public static SheetsService Create(UserCredential credential, string? baseUri = null)
    {
        var initializer = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential ?? throw new ArgumentNullException(nameof(credential)),
            ApplicationName = "Goose2MapEditor",
            // The coordinator owns retries, so a mutation must never be retried invisibly by the client.
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None
        };
        if (baseUri is not null)
        {
            initializer.BaseUri = baseUri;
        }
        return new SheetsService(initializer);
    }
}
