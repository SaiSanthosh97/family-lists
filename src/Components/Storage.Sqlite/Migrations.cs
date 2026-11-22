using Microsoft.Data.Sqlite;

namespace FamilyLists.Storage.Sqlite;

/// <summary>
/// Schema versions, applied in order and tracked in <c>PRAGMA user_version</c>. Never edit a released step:
/// add a new one. Records are stored as one JSON document each (the same JSON that sync sends) plus the columns
/// needed for lookups; the whole file is encrypted, so the JSON is never in plain text on disk.
/// </summary>
internal static class Migrations
{
    private static readonly string[] Steps =
    [
        // 1: groups, keys, lists, bills, sync bookkeeping
        """
        CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);

        CREATE TABLE families (
            id TEXT PRIMARY KEY, version TEXT NOT NULL, is_deleted INTEGER NOT NULL, json TEXT NOT NULL);

        CREATE TABLE family_keys (
            family_id TEXT NOT NULL, epoch INTEGER NOT NULL, wrapped_key BLOB NOT NULL,
            PRIMARY KEY (family_id, epoch));

        CREATE TABLE lists (
            id TEXT PRIMARY KEY, family_id TEXT NOT NULL, version TEXT NOT NULL, is_deleted INTEGER NOT NULL, json TEXT NOT NULL);
        CREATE INDEX ix_lists_family ON lists (family_id, is_deleted);

        CREATE TABLE months (
            id TEXT PRIMARY KEY, family_id TEXT NOT NULL, list_id TEXT NOT NULL, period TEXT NOT NULL,
            version TEXT NOT NULL, is_deleted INTEGER NOT NULL, json TEXT NOT NULL);
        CREATE INDEX ix_months_list ON months (list_id, is_deleted, period);

        CREATE TABLE items (
            id TEXT PRIMARY KEY, family_id TEXT NOT NULL, list_id TEXT NOT NULL, month_id TEXT,
            position INTEGER NOT NULL, created_at TEXT NOT NULL,
            version TEXT NOT NULL, is_deleted INTEGER NOT NULL, json TEXT NOT NULL);
        CREATE INDEX ix_items_list ON items (list_id, month_id, is_deleted);

        CREATE TABLE bills (
            id TEXT PRIMARY KEY, family_id TEXT NOT NULL, list_id TEXT NOT NULL, month_id TEXT NOT NULL, created_at TEXT NOT NULL,
            version TEXT NOT NULL, is_deleted INTEGER NOT NULL, json TEXT NOT NULL);
        CREATE INDEX ix_bills_month ON bills (month_id, is_deleted);
        CREATE INDEX ix_bills_list ON bills (list_id, is_deleted);

        CREATE TABLE bill_lines (
            id TEXT PRIMARY KEY, family_id TEXT NOT NULL, bill_id TEXT NOT NULL, position INTEGER NOT NULL,
            version TEXT NOT NULL, is_deleted INTEGER NOT NULL, json TEXT NOT NULL);
        CREATE INDEX ix_bill_lines_bill ON bill_lines (bill_id, is_deleted, position);

        CREATE TABLE file_keys (
            file_version_id TEXT PRIMARY KEY, family_id TEXT NOT NULL, wrapped_key BLOB NOT NULL, stored_at TEXT NOT NULL);

        CREATE TABLE change_log (
            record_type TEXT NOT NULL, record_id TEXT NOT NULL, family_id TEXT NOT NULL, version TEXT NOT NULL, json TEXT NOT NULL,
            PRIMARY KEY (record_type, record_id));
        CREATE INDEX ix_change_log_family ON change_log (family_id);

        CREATE TABLE outbox (
            id INTEGER PRIMARY KEY AUTOINCREMENT, family_id TEXT NOT NULL, kind TEXT NOT NULL, payload TEXT NOT NULL, created_at TEXT NOT NULL);
        CREATE INDEX ix_outbox_family ON outbox (family_id, id);
        """,
    ];

    public static void Apply(SqliteConnection connection)
    {
        var version = Scalar(connection, "PRAGMA user_version;");
        for (var i = (int)version; i < Steps.Length; i++)
        {
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Steps[i] + $"\nPRAGMA user_version = {i + 1};";
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public static int Latest => Steps.Length;

    /// <summary>Every table holding a group's own data (deleted when this phone leaves the group).</summary>
    public static readonly string[] GroupTables = ["bill_lines", "bills", "items", "months", "lists", "file_keys", "family_keys"];

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, System.Globalization.CultureInfo.InvariantCulture);
    }
}
