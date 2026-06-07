namespace Lockerit.Core.Models;

public sealed record VaultFileAttachment
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Category { get; init; } = "General";
    public string Notes { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long Size { get; init; }
    public byte[] Content { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static VaultFileAttachment Create(
        string fileName,
        string category,
        string notes,
        string contentType,
        byte[] content)
    {
        if (content.Length == 0)
        {
            throw new ArgumentException("The file content is empty.", nameof(content));
        }

        var now = DateTimeOffset.UtcNow;
        var normalizedFileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            throw new ArgumentException("The file name is required.", nameof(fileName));
        }

        return new VaultFileAttachment
        {
            Id = Guid.NewGuid(),
            FileName = normalizedFileName,
            Category = NormalizeCategory(category),
            Notes = notes.Trim(),
            ContentType = contentType.Trim(),
            Size = content.LongLength,
            Content = (byte[])content.Clone(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public VaultFileAttachment ToSummary()
    {
        return this with
        {
            Notes = string.Empty,
            Content = []
        };
    }

    private static string NormalizeCategory(string category)
    {
        return string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
    }
}
