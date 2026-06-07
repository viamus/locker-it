namespace Lockerit.App;

public sealed record AppSettings
{
    public string LanguageCode { get; init; } = "en";
    public string? VaultDatabasePath { get; init; }
    public bool HideToTrayOnClose { get; init; } = true;
}
