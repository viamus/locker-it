namespace Lockerit.Core.Storage;

public sealed record LockeritPaths(string RootDirectory, string DatabasePath, string KeyFilePath)
{
    public const string DefaultDatabaseFileName = "lockerit.db";

    public static LockeritPaths ForCurrentUser()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lockerit");

        return ForRoot(root);
    }

    public static LockeritPaths ForRoot(string rootDirectory)
    {
        return new LockeritPaths(
            rootDirectory,
            Path.Combine(rootDirectory, DefaultDatabaseFileName),
            Path.Combine(rootDirectory, "keyring.bin"));
    }

    public static LockeritPaths ForDatabaseFile(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return ForCurrentUser();
        }

        var fullDatabasePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(databasePath.Trim()));
        var rootDirectory = Path.GetDirectoryName(fullDatabasePath);

        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("The database path must include a directory.", nameof(databasePath));
        }

        var databaseName = Path.GetFileNameWithoutExtension(fullDatabasePath);
        var keyFileName = string.IsNullOrWhiteSpace(databaseName)
            ? "keyring.bin"
            : $"{databaseName}.keyring.bin";

        return new LockeritPaths(
            rootDirectory,
            fullDatabasePath,
            Path.Combine(rootDirectory, keyFileName));
    }
}
