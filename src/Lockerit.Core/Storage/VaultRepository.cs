using System.Text.Json;
using System.Security.Cryptography;
using Lockerit.Core.Models;
using Lockerit.Core.Security;
using Microsoft.Data.Sqlite;

namespace Lockerit.Core.Storage;

public sealed class VaultRepository
{
    private readonly string _databasePath;
    private readonly VaultKey _key;
    private readonly AesGcmVaultCipher _cipher;

    public VaultRepository(string databasePath, VaultKey key, AesGcmVaultCipher cipher)
    {
        _databasePath = databasePath;
        _key = key;
        _cipher = cipher;
    }

    public void Initialize()
    {
        SQLitePCL.Batteries_V2.Init();
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        using var connection = OpenConnection();
        connection.Open();

        Execute(connection, "PRAGMA foreign_keys = ON;");
        Execute(connection, "PRAGMA secure_delete = ON;");
        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS VaultItems (
                Id TEXT NOT NULL PRIMARY KEY,
                Kind TEXT NOT NULL,
                Payload TEXT NOT NULL
            );
            """);
        Execute(
            connection,
            """
            CREATE INDEX IF NOT EXISTS IX_VaultItems_Kind
            ON VaultItems (Kind);
            """);
    }

    public IReadOnlyList<PasswordSecret> GetPasswords()
    {
        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM VaultItems WHERE Kind = $kind;";
        command.Parameters.AddWithValue("$kind", VaultItemKinds.Password);

        using var reader = command.ExecuteReader();
        var secrets = new List<PasswordSecret>();

        while (reader.Read())
        {
            var payload = reader.GetString(0);
            try
            {
                secrets.Add(_cipher.DecryptJson<PasswordSecret>(payload, _key, VaultItemKinds.Password));
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or InvalidOperationException)
            {
                throw new InvalidOperationException("A Lockerit password entry could not be decrypted.", ex);
            }
        }

        return secrets
            .OrderBy(secret => secret.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(secret => secret.UserName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<VaultFileAttachment> GetFileAttachments()
    {
        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM VaultItems WHERE Kind = $kind;";
        command.Parameters.AddWithValue("$kind", VaultItemKinds.FileAttachment);

        using var reader = command.ExecuteReader();
        var files = new List<VaultFileAttachment>();

        while (reader.Read())
        {
            var payload = reader.GetString(0);
            try
            {
                files.Add(_cipher.DecryptJson<VaultFileAttachment>(payload, _key, VaultItemKinds.FileAttachment));
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or InvalidOperationException)
            {
                throw new InvalidOperationException("A Lockerit file attachment could not be decrypted.", ex);
            }
        }

        return files
            .OrderBy(file => file.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(file => file.UpdatedAt)
            .ToArray();
    }

    public PasswordSecret? GetPassword(Guid id)
    {
        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM VaultItems WHERE Id = $id AND Kind = $kind;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$kind", VaultItemKinds.Password);

        var payload = command.ExecuteScalar() as string;
        if (payload is null)
        {
            return null;
        }

        try
        {
            return _cipher.DecryptJson<PasswordSecret>(payload, _key, VaultItemKinds.Password);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("A Lockerit password entry could not be decrypted.", ex);
        }
    }

    public VaultFileAttachment? GetFileAttachment(Guid id)
    {
        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM VaultItems WHERE Id = $id AND Kind = $kind;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$kind", VaultItemKinds.FileAttachment);

        var payload = command.ExecuteScalar() as string;
        if (payload is null)
        {
            return null;
        }

        try
        {
            return _cipher.DecryptJson<VaultFileAttachment>(payload, _key, VaultItemKinds.FileAttachment);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("A Lockerit file attachment could not be decrypted.", ex);
        }
    }

    public void UpsertPassword(PasswordSecret secret)
    {
        var payload = _cipher.EncryptJson(secret, _key, VaultItemKinds.Password);

        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VaultItems (Id, Kind, Payload)
            VALUES ($id, $kind, $payload)
            ON CONFLICT(Id) DO UPDATE SET
                Kind = excluded.Kind,
                Payload = excluded.Payload;
            """;
        command.Parameters.AddWithValue("$id", secret.Id.ToString("D"));
        command.Parameters.AddWithValue("$kind", VaultItemKinds.Password);
        command.Parameters.AddWithValue("$payload", payload);
        command.ExecuteNonQuery();
    }

    public void UpsertFileAttachment(VaultFileAttachment file)
    {
        var payload = _cipher.EncryptJson(file, _key, VaultItemKinds.FileAttachment);

        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VaultItems (Id, Kind, Payload)
            VALUES ($id, $kind, $payload)
            ON CONFLICT(Id) DO UPDATE SET
                Kind = excluded.Kind,
                Payload = excluded.Payload;
            """;
        command.Parameters.AddWithValue("$id", file.Id.ToString("D"));
        command.Parameters.AddWithValue("$kind", VaultItemKinds.FileAttachment);
        command.Parameters.AddWithValue("$payload", payload);
        command.ExecuteNonQuery();
    }

    public void DeletePassword(Guid id)
    {
        DeleteItem(id, VaultItemKinds.Password);
    }

    public void DeleteFileAttachment(Guid id)
    {
        DeleteItem(id, VaultItemKinds.FileAttachment);
    }

    private void DeleteItem(Guid id, string kind)
    {
        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM VaultItems WHERE Id = $id AND Kind = $kind;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$kind", kind);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        };

        return new SqliteConnection(builder.ToString());
    }

    private static void Execute(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
