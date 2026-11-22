using FamilyLists.Core.Bills;
using static FamilyLists.Storage.Sqlite.SqlHelpers;

namespace FamilyLists.Storage.Sqlite;

public sealed class SqliteBillRepository(VaultDatabase db) : IBillRepository
{
    public Task<Bill?> GetBillAsync(string id, CancellationToken cancellationToken = default) =>
        db.UseAsync(c => c.Query("SELECT json FROM bills WHERE id = $id", r => FromJson<Bill>(r.GetString(0)), ("$id", id)).FirstOrDefault(), cancellationToken);

    public Task<IReadOnlyList<Bill>> BillsOfMonthAsync(string monthId, CancellationToken cancellationToken = default) =>
        db.UseAsync<IReadOnlyList<Bill>>(
            c => c.Query(
                "SELECT json FROM bills WHERE month_id = $m AND is_deleted = 0 AND family_id = (SELECT family_id FROM months WHERE id = $m) ORDER BY created_at DESC",
                r => FromJson<Bill>(r.GetString(0)),
                ("$m", monthId)),
            cancellationToken);

    public Task<IReadOnlyList<Bill>> BillsOfListAsync(string listId, CancellationToken cancellationToken = default) =>
        db.UseAsync<IReadOnlyList<Bill>>(
            c => c.Query(
                "SELECT json FROM bills WHERE list_id = $l AND is_deleted = 0 AND family_id = (SELECT family_id FROM lists WHERE id = $l) ORDER BY created_at",
                r => FromJson<Bill>(r.GetString(0)),
                ("$l", listId)),
            cancellationToken);

    public Task SaveBillAsync(Bill b, CancellationToken cancellationToken = default) =>
        db.UseAsync(c => c.Run(
            """
            INSERT INTO bills (id, family_id, list_id, month_id, created_at, version, is_deleted, json)
            VALUES ($id, $f, $l, $m, $at, $v, $del, $json)
            ON CONFLICT (id) DO UPDATE SET version = $v, is_deleted = $del, json = $json
            """,
            null,
            ("$id", b.Id), ("$f", b.FamilyId), ("$l", b.ListId), ("$m", b.MonthId), ("$at", Utc(b.CreatedAt)),
            ("$v", b.Version.ToString()), ("$del", Bool(b.IsDeleted)), ("$json", ToJson(b))), cancellationToken);

    public Task<BillLine?> GetLineAsync(string id, CancellationToken cancellationToken = default) =>
        db.UseAsync(c => c.Query("SELECT json FROM bill_lines WHERE id = $id", r => FromJson<BillLine>(r.GetString(0)), ("$id", id)).FirstOrDefault(), cancellationToken);

    public Task<IReadOnlyList<BillLine>> LinesAsync(string billId, CancellationToken cancellationToken = default) =>
        db.UseAsync<IReadOnlyList<BillLine>>(
            c => c.Query(
                "SELECT json FROM bill_lines WHERE bill_id = $b AND is_deleted = 0 AND family_id = (SELECT family_id FROM bills WHERE id = $b) ORDER BY position",
                r => FromJson<BillLine>(r.GetString(0)),
                ("$b", billId)),
            cancellationToken);

    public Task SaveLineAsync(BillLine l, CancellationToken cancellationToken = default) =>
        db.UseAsync(c => c.Run(
            """
            INSERT INTO bill_lines (id, family_id, bill_id, position, version, is_deleted, json) VALUES ($id, $f, $b, $pos, $v, $del, $json)
            ON CONFLICT (id) DO UPDATE SET position = $pos, version = $v, is_deleted = $del, json = $json
            """,
            null,
            ("$id", l.Id), ("$f", l.FamilyId), ("$b", l.BillId), ("$pos", l.Position), ("$v", l.Version.ToString()), ("$del", Bool(l.IsDeleted)), ("$json", ToJson(l))), cancellationToken);
}
