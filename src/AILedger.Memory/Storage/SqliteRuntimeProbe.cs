using Microsoft.Data.Sqlite;

namespace AILedger.Memory.Storage;

public static class SqliteRuntimeProbe
{
    public static void RequireFts5(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_compileoption_used('ENABLE_FTS5')";
        var enabled = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (enabled != 1)
        {
            throw new NotSupportedException(
                "The packaged SQLite runtime does not provide FTS5; rebuild requires a compatible runtime.");
        }
    }
}
