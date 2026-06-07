namespace Lockerit.Core.Models;

public sealed record PasswordSecret
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = "General";
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static PasswordSecret Create(
        string title,
        string category,
        string userName,
        string password,
        string url,
        string notes)
    {
        var now = DateTimeOffset.UtcNow;

        return new PasswordSecret
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            Category = NormalizeCategory(category),
            UserName = userName.Trim(),
            Password = password,
            Url = url.Trim(),
            Notes = notes.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public PasswordSecret Update(
        string title,
        string category,
        string userName,
        string password,
        string url,
        string notes)
    {
        return this with
        {
            Title = title.Trim(),
            Category = NormalizeCategory(category),
            UserName = userName.Trim(),
            Password = password,
            Url = url.Trim(),
            Notes = notes.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string NormalizeCategory(string category)
    {
        return string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
    }
}
