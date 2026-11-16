using Microsoft.Data.Sqlite;
using Ygo.CardLib;
using Ygo.Recognition;

namespace Ygo.Records;

public sealed record SessionRow(long Id, string StartedAt, string Note);

public sealed record CardRow(int Slot, int? CardCid, string RawText, double OcrScore, double MatchSim, int? CorrectedCid, string? CorrectedAt);

public sealed record SnapshotRow(long Id, long SessionId, string CapturedAt, string ImagePath, string CapturePath, int ElapsedMs, string TurnOrder, IReadOnlyList<CardRow> Cards, long? DeckId = null);

public sealed record AliasRow(string RawText, int CardCid, string CreatedAt, int Hits);

public sealed record DeckRow(long Id, string Name, string ImportedAt, int TotalCards, long? CoverCid);

public sealed record DeckCardRow(string Section, int? Cid, string RawName, int Count);

// P13 聚合标注：属性（动点/手坑/废件/解牌/自定义）与组合类型（两卡动等），均按卡组独立。
public sealed record DeckTagRow(long Id, string Name, IReadOnlyList<int> Cids);

public sealed record DeckComboEntryRow(long Id, string Name, IReadOnlyList<int> Cids, IReadOnlyList<int> OptCids);

public sealed record DeckComboTypeRow(long Id, string Name, IReadOnlyList<DeckComboEntryRow> Entries);

public sealed record DeckItem(long Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class RecordStore : IDisposable, IAliasLookup
{
    private readonly SqliteConnection _db;

    public RecordStore(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _db = new SqliteConnection($"Data Source={path};Pooling=False");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        Exec("""
            CREATE TABLE IF NOT EXISTS DuelSession(
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              StartedAt TEXT NOT NULL,
              Note TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS HandSnapshot(
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              SessionId INTEGER NOT NULL REFERENCES DuelSession(Id),
              CapturedAt TEXT NOT NULL,
              ImagePath TEXT NOT NULL,
              CapturePath TEXT NOT NULL DEFAULT '',
              ElapsedMs INTEGER NOT NULL DEFAULT 0,
              TurnOrder TEXT NOT NULL DEFAULT '',
              DeckId INTEGER REFERENCES Deck(Id));
            CREATE TABLE IF NOT EXISTS SnapshotCard(
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              SnapshotId INTEGER NOT NULL REFERENCES HandSnapshot(Id),
              Slot INTEGER NOT NULL,
              CardCid INTEGER,
              RawText TEXT NOT NULL DEFAULT '',
              OcrScore REAL NOT NULL DEFAULT 0,
              MatchSim REAL NOT NULL DEFAULT 0,
              CorrectedCid INTEGER,
              CorrectedAt TEXT);
            CREATE TABLE IF NOT EXISTS OcrAlias(
              RawText TEXT PRIMARY KEY,
              CardCid INTEGER NOT NULL,
              CreatedAt TEXT NOT NULL,
              Hits INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS Deck(
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              Name TEXT NOT NULL,
              ImportedAt TEXT NOT NULL,
              CoverCid INTEGER);
            CREATE TABLE IF NOT EXISTS DeckCard(
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              DeckId INTEGER NOT NULL REFERENCES Deck(Id),
              Section TEXT NOT NULL,
              Cid INTEGER,
              RawName TEXT NOT NULL,
              Count INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS DeckTag(
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              DeckId INTEGER NOT NULL REFERENCES Deck(Id),
              Name TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS DeckTagCard(
              TagId INTEGER NOT NULL REFERENCES DeckTag(Id),
              Cid INTEGER NOT NULL,
              PRIMARY KEY(TagId, Cid));
            CREATE TABLE IF NOT EXISTS DeckComboType(
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              DeckId INTEGER NOT NULL REFERENCES Deck(Id),
              Name TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS DeckComboEntry(
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              TypeId INTEGER NOT NULL REFERENCES DeckComboType(Id),
              Name TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS DeckComboEntryCard(
              EntryId INTEGER NOT NULL REFERENCES DeckComboEntry(Id),
              Cid INTEGER NOT NULL,
              Role TEXT NOT NULL DEFAULT 'req',
              PRIMARY KEY(EntryId, Cid));
            """);
        using (var pragma = Cmd("PRAGMA table_info(HandSnapshot)"))
        using (var r = pragma.ExecuteReader())
        {
            var hasDeck = false;
            while (r.Read()) if (r.GetString(1) == "DeckId") hasDeck = true;
            if (!hasDeck) Exec("ALTER TABLE HandSnapshot ADD COLUMN DeckId INTEGER REFERENCES Deck(Id)");
        }
        using (var pragma = Cmd("PRAGMA table_info(Deck)"))
        using (var r = pragma.ExecuteReader())
        {
            var hasCover = false;
            while (r.Read()) if (r.GetString(1) == "CoverCid") hasCover = true;
            if (!hasCover) Exec("ALTER TABLE Deck ADD COLUMN CoverCid INTEGER");
        }
        using (var pragma = Cmd("PRAGMA table_info(DeckComboEntryCard)"))
        using (var r = pragma.ExecuteReader())
        {
            var hasRole = false;
            while (r.Read()) if (r.GetString(1) == "Role") hasRole = true;
            if (!hasRole) Exec("ALTER TABLE DeckComboEntryCard ADD COLUMN Role TEXT NOT NULL DEFAULT 'req'");
        }
        // 早期 GUI 导入误把中文显示名当 Section 存库，统一回 canonical Main/Extra/Side。
        Exec("UPDATE DeckCard SET Section='Main' WHERE Section='主卡组'; UPDATE DeckCard SET Section='Extra' WHERE Section='额外'; UPDATE DeckCard SET Section='Side' WHERE Section='侧卡组';");
        MigrateDaySessions();
        MigrateSessionScheme(path);
        MigrateDeckCover();
    }

    // 封面=建卡组时随机抽的一张主卡组卡，存库后不再变;旧库无 CoverCid 的补抽一次。
    private void MigrateDeckCover()
    {
        using var cmd = Cmd("SELECT Id FROM Deck WHERE CoverCid IS NULL");
        var ids = new List<long>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) ids.Add(r.GetInt64(0));
        foreach (var id in ids) AssignCover(id);
    }

    private void AssignCover(long deckId)
    {
        using var cmd = Cmd("""
            UPDATE Deck SET CoverCid=(
              SELECT Cid FROM DeckCard WHERE DeckId=$d AND Section='Main' AND Cid IS NOT NULL
              ORDER BY RANDOM() LIMIT 1)
            WHERE Id=$d
            """, ("$d", deckId));
        cmd.ExecuteNonQuery();
    }

    // 旧版会话按天建（StartedAt 只存日期）;现改为「一次运行×一个卡组」一会话。
    // 旧数据按卡组各合并为一个会话，名取卡组名+该组最早截图时间戳。
    private void MigrateDaySessions()
    {
        var old = new List<long>();
        using (var q = Cmd("SELECT Id FROM DuelSession WHERE length(StartedAt)=10"))
        using (var r = q.ExecuteReader()) while (r.Read()) old.Add(r.GetInt64(0));
        if (old.Count == 0) return;
        var ids = string.Join(",", old);
        var groups = new List<(long? DeckId, string First)>();
        using (var q = Cmd($"SELECT DeckId, MIN(CapturedAt) FROM HandSnapshot WHERE SessionId IN ({ids}) GROUP BY DeckId"))
        using (var r = q.ExecuteReader())
            while (r.Read()) groups.Add((r.IsDBNull(0) ? null : r.GetInt64(0), r.GetString(1)));
        var decks = ListDecks().ToDictionary(d => d.Id, d => d.Name);
        using var tx = _db.BeginTransaction();
        foreach (var (deckId, first) in groups)
        {
            var deckName = deckId != null ? decks.GetValueOrDefault(deckId.Value, "未分类") : "未分类";
            var name = deckName + "_" + first.Replace("-", "").Replace(" ", "_").Replace(":", "");
            long sid;
            using (var ins = Cmd("INSERT INTO DuelSession(StartedAt, Note) VALUES($t, $n); SELECT last_insert_rowid();",
                       ("$t", first), ("$n", name)))
                sid = (long)ins.ExecuteScalar()!;
            var ps = new List<(string, object?)> { ("$s", sid) };
            var sql = $"UPDATE HandSnapshot SET SessionId=$s WHERE SessionId IN ({ids})";
            if (deckId == null) sql += " AND DeckId IS NULL";
            else { sql += " AND DeckId=$d"; ps.Add(("$d", deckId)); }
            using var up = Cmd(sql, ps.ToArray());
            up.ExecuteNonQuery();
        }
        using (var del = Cmd($"DELETE FROM DuelSession WHERE Id IN ({ids})")) del.ExecuteNonQuery();
        tx.Commit();
    }

    public static string DefaultDbPath(string root) => Path.Combine(root, "records", "ygo.db");

    public const string UnclassifiedSession = "未分类";

    // 未分类固定共用一个会话；历史遗留的「未分类_时间戳」合并进来，空会话一律不保留。
    // 只挪快照归属 / 删空会话行，不动任何记录内容;改动前先备份，备份失败则本次不改。
    private void MigrateSessionScheme(string path)
    {
        var legacy = new List<long>();
        using (var q = Cmd("SELECT Id FROM DuelSession WHERE Note LIKE $p AND Note<>$n",
                   ("$p", UnclassifiedSession + "%"), ("$n", UnclassifiedSession)))
        using (var r = q.ExecuteReader()) while (r.Read()) legacy.Add(r.GetInt64(0));
        int empty;
        using (var q = Cmd("SELECT COUNT(*) FROM DuelSession d WHERE NOT EXISTS(SELECT 1 FROM HandSnapshot h WHERE h.SessionId=d.Id)"))
            empty = Convert.ToInt32(q.ExecuteScalar());
        if (legacy.Count == 0 && empty == 0) return;
        if (!BackupBeforeMigrate(path)) return;
        if (legacy.Count > 0)
        {
            var ids = string.Join(",", legacy);
            int held;
            using (var q = Cmd($"SELECT COUNT(*) FROM HandSnapshot WHERE SessionId IN ({ids})"))
                held = Convert.ToInt32(q.ExecuteScalar());
            if (held > 0)
            {
                long target;
                using (var q = Cmd("SELECT Id FROM DuelSession WHERE Note=$n ORDER BY Id LIMIT 1", ("$n", UnclassifiedSession)))
                {
                    var hit = q.ExecuteScalar();
                    target = hit == null ? CreateSession(UnclassifiedSession) : (long)hit;
                }
                string first;
                using (var q = Cmd($"SELECT MIN(CapturedAt) FROM HandSnapshot WHERE SessionId IN ({ids})"))
                    first = (string)q.ExecuteScalar()!;
                using var tx = _db.BeginTransaction();
                using (var up = Cmd($"UPDATE HandSnapshot SET SessionId=$t WHERE SessionId IN ({ids})", ("$t", target)))
                    up.ExecuteNonQuery();
                using (var ren = Cmd("UPDATE DuelSession SET Note=$n, StartedAt=$a WHERE Id=$t",
                           ("$n", UnclassifiedSession), ("$a", first), ("$t", target)))
                    ren.ExecuteNonQuery();
                using (var del = Cmd($"DELETE FROM DuelSession WHERE Id IN ({ids}) AND Id<>$t", ("$t", target)))
                    del.ExecuteNonQuery();
                tx.Commit();
            }
        }
        PruneEmptySessions();
    }

    private bool BackupBeforeMigrate(string path)
    {
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,
                "backup-session-scheme-" + DateTime.Now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, "ygo.db");
            if (File.Exists(dest)) return true;
            using var v = Cmd("VACUUM INTO $p", ("$p", dest));
            v.ExecuteNonQuery();
            return File.Exists(dest);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand Cmd(string sql, params (string Name, object? Value)[] ps)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    public long CreateSession(string name)
    {
        using var ins = Cmd("INSERT INTO DuelSession(StartedAt, Note) VALUES($t, $n); SELECT last_insert_rowid();",
            ("$t", Now()), ("$n", name));
        return (long)ins.ExecuteScalar()!;
    }

    // 同名会话复用（未分类走这条）；调用方在真正要入库时才调，避免空会话。
    public long GetOrCreateSession(string name)
    {
        using var find = Cmd("SELECT Id FROM DuelSession WHERE Note=$n ORDER BY Id LIMIT 1", ("$n", name));
        var hit = find.ExecuteScalar();
        return hit == null ? CreateSession(name) : (long)hit;
    }

    public int PruneEmptySessions()
    {
        using var del = Cmd("DELETE FROM DuelSession WHERE NOT EXISTS(SELECT 1 FROM HandSnapshot h WHERE h.SessionId=DuelSession.Id)");
        return del.ExecuteNonQuery();
    }

    // deckId: null = all sessions, 0 = sessions holding unclassified snapshots, >0 = sessions holding that deck's snapshots.
    public IReadOnlyList<SessionRow> ListSessions(long? deckId = null)
    {
        var rows = new List<SessionRow>();
        var ps = new List<(string, object?)>();
        var sql = "SELECT Id, StartedAt, Note FROM DuelSession";
        if (deckId != null)
        {
            sql = "SELECT DISTINCT s.Id, s.StartedAt, s.Note FROM DuelSession s JOIN HandSnapshot h ON h.SessionId=s.Id";
            if (deckId == 0) sql += " WHERE h.DeckId IS NULL";
            else { sql += " WHERE h.DeckId=$d"; ps.Add(("$d", deckId)); }
            sql += " ORDER BY s.Id DESC";
        }
        else sql += " ORDER BY Id DESC";
        using var cmd = Cmd(sql, ps.ToArray());
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(new SessionRow(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return rows;
    }

    public long AddSnapshot(long sessionId, string imagePath, string capturePath, int elapsedMs, string turnOrder, IReadOnlyList<SlotResult?> slots, long? deckId = null)
    {
        using var tx = _db.BeginTransaction();
        long id;
        using (var ins = Cmd("""
            INSERT INTO HandSnapshot(SessionId, CapturedAt, ImagePath, CapturePath, ElapsedMs, TurnOrder, DeckId)
            VALUES($s, $t, $p, $c, $e, $o, $d); SELECT last_insert_rowid();
            """, ("$s", sessionId), ("$t", Now()), ("$p", imagePath), ("$c", capturePath), ("$e", elapsedMs), ("$o", turnOrder), ("$d", deckId)))
            id = (long)ins.ExecuteScalar()!;
        for (var i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            using var card = Cmd("""
                INSERT INTO SnapshotCard(SnapshotId, Slot, CardCid, RawText, OcrScore, MatchSim)
                VALUES($id, $slot, $cid, $raw, $score, $sim)
                """, ("$id", id), ("$slot", i + 1), ("$cid", s?.Card?.Cid), ("$raw", s?.RawText ?? ""),
                ("$score", s?.OcrScore ?? 0), ("$sim", s?.MatchSim ?? 0));
            card.ExecuteNonQuery();
        }
        tx.Commit();
        return id;
    }

    // deckId: null = no filter, 0 = unclassified only, >0 = that deck.
    public IReadOnlyList<SnapshotRow> ListSnapshots(long? sessionId = null, long? deckId = null)
    {
        var where = new List<string>();
        var ps = new List<(string, object?)>();
        if (sessionId != null) { where.Add("SessionId=$s"); ps.Add(("$s", sessionId)); }
        if (deckId != null)
        {
            if (deckId == 0) where.Add("DeckId IS NULL");
            else { where.Add("DeckId=$d"); ps.Add(("$d", deckId)); }
        }
        var sql = "SELECT Id, SessionId, CapturedAt, ImagePath, CapturePath, ElapsedMs, TurnOrder, DeckId FROM HandSnapshot"
                  + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") + " ORDER BY Id DESC";
        var snaps = new List<SnapshotRow>();
        using (var cmd = Cmd(sql, ps.ToArray()))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                snaps.Add(new SnapshotRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetString(6), Array.Empty<CardRow>(),
                    r.IsDBNull(7) ? null : r.GetInt64(7)));
        }
        return snaps.Select(s => s with { Cards = ListCards(s.Id) }).ToList();
    }

    public SnapshotRow? GetSnapshot(long id) => ListSnapshots().FirstOrDefault(s => s.Id == id);

    private IReadOnlyList<CardRow> ListCards(long snapshotId)
    {
        var rows = new List<CardRow>();
        using var cmd = Cmd("SELECT Slot, CardCid, RawText, OcrScore, MatchSim, CorrectedCid, CorrectedAt FROM SnapshotCard WHERE SnapshotId=$i ORDER BY Slot", ("$i", snapshotId));
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new CardRow(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4),
                r.IsDBNull(5) ? null : r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return rows;
    }

    public void Correct(long snapshotId, int slot, int cid, string aliasRaw)
    {
        using (var cmd = Cmd("UPDATE SnapshotCard SET CorrectedCid=$c, CorrectedAt=$t WHERE SnapshotId=$i AND Slot=$s",
                   ("$c", cid), ("$t", Now()), ("$i", snapshotId), ("$s", slot)))
            cmd.ExecuteNonQuery();
        if (!string.IsNullOrWhiteSpace(aliasRaw)) UpsertAlias(aliasRaw, cid);
    }

    public void SetTurnOrder(long snapshotId, string turnOrder)
    {
        using var cmd = Cmd("UPDATE HandSnapshot SET TurnOrder=$o WHERE Id=$i", ("$o", turnOrder), ("$i", snapshotId));
        cmd.ExecuteNonQuery();
    }

    public void SetDeck(long snapshotId, long? deckId)
    {
        using var cmd = Cmd("UPDATE HandSnapshot SET DeckId=$d WHERE Id=$i", ("$d", deckId), ("$i", snapshotId));
        cmd.ExecuteNonQuery();
    }

    public long AddDeck(string name, IReadOnlyList<DeckCardRow> cards)
    {
        using var tx = _db.BeginTransaction();
        long id;
        using (var ins = Cmd("INSERT INTO Deck(Name, ImportedAt) VALUES($n, $t); SELECT last_insert_rowid();", ("$n", name), ("$t", Now())))
            id = (long)ins.ExecuteScalar()!;
        foreach (var c in cards)
        {
            using var cmd = Cmd("""
                INSERT INTO DeckCard(DeckId, Section, Cid, RawName, Count) VALUES($d, $s, $c, $r, $n)
                """, ("$d", id), ("$s", c.Section), ("$c", c.Cid), ("$r", c.RawName), ("$n", c.Count));
            cmd.ExecuteNonQuery();
        }
        AssignCover(id);
        // P15：新建卡组自带示例类型/组合，帮助理解两行用途;均可删除。
        foreach (var tagName in new[] { "动点", "手坑", "废件" })
        {
            using var t = Cmd("INSERT INTO DeckTag(DeckId, Name) VALUES($d, $n)", ("$d", id), ("$n", tagName));
            t.ExecuteNonQuery();
        }
        using (var ct = Cmd("INSERT INTO DeckComboType(DeckId, Name) VALUES($d, '两卡动')", ("$d", id)))
            ct.ExecuteNonQuery();
        tx.Commit();
        return id;
    }

    public IReadOnlyList<DeckRow> ListDecks()
    {
        var rows = new List<DeckRow>();
        using var cmd = Cmd("""
            SELECT d.Id, d.Name, d.ImportedAt, COALESCE(SUM(c.Count), 0), d.CoverCid
            FROM Deck d LEFT JOIN DeckCard c ON c.DeckId = d.Id
            GROUP BY d.Id ORDER BY d.Id DESC
            """);
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(new DeckRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt64(4)));
        return rows;
    }

    public IReadOnlyList<DeckCardRow> GetDeckCards(long deckId)
    {
        var rows = new List<DeckCardRow>();
        using var cmd = Cmd("SELECT Section, Cid, RawName, Count FROM DeckCard WHERE DeckId=$d ORDER BY Id", ("$d", deckId));
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(new DeckCardRow(r.GetString(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetString(2), r.GetInt32(3)));
        return rows;
    }

    public void DeleteDeck(long deckId)
    {
        using var tx = _db.BeginTransaction();
        using (var unlink = Cmd("UPDATE HandSnapshot SET DeckId=NULL WHERE DeckId=$d", ("$d", deckId))) unlink.ExecuteNonQuery();
        using (var cards = Cmd("DELETE FROM DeckCard WHERE DeckId=$d", ("$d", deckId))) cards.ExecuteNonQuery();
        using (var tc = Cmd("DELETE FROM DeckTagCard WHERE TagId IN (SELECT Id FROM DeckTag WHERE DeckId=$d)", ("$d", deckId))) tc.ExecuteNonQuery();
        using (var tg = Cmd("DELETE FROM DeckTag WHERE DeckId=$d", ("$d", deckId))) tg.ExecuteNonQuery();
        using (var ec = Cmd("DELETE FROM DeckComboEntryCard WHERE EntryId IN (SELECT e.Id FROM DeckComboEntry e JOIN DeckComboType t ON t.Id=e.TypeId WHERE t.DeckId=$d)", ("$d", deckId))) ec.ExecuteNonQuery();
        using (var en = Cmd("DELETE FROM DeckComboEntry WHERE TypeId IN (SELECT Id FROM DeckComboType WHERE DeckId=$d)", ("$d", deckId))) en.ExecuteNonQuery();
        using (var ct = Cmd("DELETE FROM DeckComboType WHERE DeckId=$d", ("$d", deckId))) ct.ExecuteNonQuery();
        using (var deck = Cmd("DELETE FROM Deck WHERE Id=$d", ("$d", deckId))) deck.ExecuteNonQuery();
        tx.Commit();
    }

    // ===== P13 聚合标注 / 组合 =====

    public IReadOnlyList<DeckTagRow> ListTags(long deckId)
    {
        var tags = new List<DeckTagRow>();
        using (var cmd = Cmd("SELECT Id, Name FROM DeckTag WHERE DeckId=$d ORDER BY Id", ("$d", deckId)))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) tags.Add(new DeckTagRow(r.GetInt64(0), r.GetString(1), new List<int>()));
        foreach (var t in tags)
        {
            var cids = (List<int>)t.Cids;
            using var cmd = Cmd("SELECT Cid FROM DeckTagCard WHERE TagId=$t ORDER BY Cid", ("$t", t.Id));
            using var r = cmd.ExecuteReader();
            while (r.Read()) cids.Add(r.GetInt32(0));
        }
        return tags;
    }

    public long AddTag(long deckId, string name)
    {
        using var cmd = Cmd("INSERT INTO DeckTag(DeckId, Name) VALUES($d, $n); SELECT last_insert_rowid();", ("$d", deckId), ("$n", name));
        return (long)cmd.ExecuteScalar()!;
    }

    public void DeleteTag(long tagId)
    {
        using var tx = _db.BeginTransaction();
        using (var c = Cmd("DELETE FROM DeckTagCard WHERE TagId=$t", ("$t", tagId))) c.ExecuteNonQuery();
        using (var t = Cmd("DELETE FROM DeckTag WHERE Id=$t", ("$t", tagId))) t.ExecuteNonQuery();
        tx.Commit();
    }

    // 编辑态连点卡牌：有则移除、无则加入。
    public bool ToggleTagCard(long tagId, int cid)
    {
        using var q = Cmd("SELECT 1 FROM DeckTagCard WHERE TagId=$t AND Cid=$c", ("$t", tagId), ("$c", cid));
        var exists = q.ExecuteScalar() != null;
        using var cmd = exists
            ? Cmd("DELETE FROM DeckTagCard WHERE TagId=$t AND Cid=$c", ("$t", tagId), ("$c", cid))
            : Cmd("INSERT INTO DeckTagCard(TagId, Cid) VALUES($t, $c)", ("$t", tagId), ("$c", cid));
        cmd.ExecuteNonQuery();
        return !exists;
    }

    public IReadOnlyList<DeckComboTypeRow> ListComboTypes(long deckId)
    {
        var types = new List<DeckComboTypeRow>();
        using (var cmd = Cmd("SELECT Id, Name FROM DeckComboType WHERE DeckId=$d ORDER BY Id", ("$d", deckId)))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) types.Add(new DeckComboTypeRow(r.GetInt64(0), r.GetString(1), new List<DeckComboEntryRow>()));
        foreach (var t in types)
        {
            var entries = (List<DeckComboEntryRow>)t.Entries;
            using var cmd = Cmd("SELECT Id, Name FROM DeckComboEntry WHERE TypeId=$t ORDER BY Id", ("$t", t.Id));
            using var r = cmd.ExecuteReader();
            while (r.Read()) entries.Add(new DeckComboEntryRow(r.GetInt64(0), r.GetString(1), new List<int>(), new List<int>()));
            foreach (var e in entries)
            {
                var cids = (List<int>)e.Cids;
                var opts = (List<int>)e.OptCids;
                using var cc = Cmd("SELECT Cid, Role FROM DeckComboEntryCard WHERE EntryId=$e ORDER BY Cid", ("$e", e.Id));
                using var rr = cc.ExecuteReader();
                while (rr.Read())
                {
                    if (rr.GetString(1) == "opt") opts.Add(rr.GetInt32(0));
                    else cids.Add(rr.GetInt32(0));
                }
            }
        }
        return types;
    }

    public long AddComboType(long deckId, string name)
    {
        using var cmd = Cmd("INSERT INTO DeckComboType(DeckId, Name) VALUES($d, $n); SELECT last_insert_rowid();", ("$d", deckId), ("$n", name));
        return (long)cmd.ExecuteScalar()!;
    }

    public void DeleteComboType(long typeId)
    {
        using var tx = _db.BeginTransaction();
        using (var ec = Cmd("DELETE FROM DeckComboEntryCard WHERE EntryId IN (SELECT Id FROM DeckComboEntry WHERE TypeId=$t)", ("$t", typeId))) ec.ExecuteNonQuery();
        using (var en = Cmd("DELETE FROM DeckComboEntry WHERE TypeId=$t", ("$t", typeId))) en.ExecuteNonQuery();
        using (var t = Cmd("DELETE FROM DeckComboType WHERE Id=$t", ("$t", typeId))) t.ExecuteNonQuery();
        tx.Commit();
    }

    // req=组内每卡各≥1 全齐;opt=集合中任意≥1（P14「一卡+任选」）;opt 空=纯全齐条目。
    public long AddComboEntry(long typeId, string name, IReadOnlyList<int> reqCids, IReadOnlyList<int>? optCids = null)
    {
        using var tx = _db.BeginTransaction();
        long id;
        using (var ins = Cmd("INSERT INTO DeckComboEntry(TypeId, Name) VALUES($t, $n); SELECT last_insert_rowid();", ("$t", typeId), ("$n", name)))
            id = (long)ins.ExecuteScalar()!;
        foreach (var cid in reqCids.Distinct())
        {
            using var cmd = Cmd("INSERT INTO DeckComboEntryCard(EntryId, Cid, Role) VALUES($e, $c, 'req')", ("$e", id), ("$c", cid));
            cmd.ExecuteNonQuery();
        }
        foreach (var cid in (optCids ?? Array.Empty<int>()).Distinct().Where(c => !reqCids.Contains(c)))
        {
            using var cmd = Cmd("INSERT INTO DeckComboEntryCard(EntryId, Cid, Role) VALUES($e, $c, 'opt')", ("$e", id), ("$c", cid));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return id;
    }

    public void DeleteComboEntry(long entryId)
    {
        using var tx = _db.BeginTransaction();
        using (var c = Cmd("DELETE FROM DeckComboEntryCard WHERE EntryId=$e", ("$e", entryId))) c.ExecuteNonQuery();
        using (var e = Cmd("DELETE FROM DeckComboEntry WHERE Id=$e", ("$e", entryId))) e.ExecuteNonQuery();
        tx.Commit();
    }

    public void UpdateRecognition(long snapshotId, IReadOnlyList<SlotResult?> slots, int elapsedMs)
    {
        using var tx = _db.BeginTransaction();
        using (var cmd = Cmd("UPDATE HandSnapshot SET ElapsedMs=$e WHERE Id=$i", ("$e", elapsedMs), ("$i", snapshotId)))
            cmd.ExecuteNonQuery();
        for (var i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            using var card = Cmd("""
                UPDATE SnapshotCard SET CardCid=$cid, RawText=$raw, OcrScore=$score, MatchSim=$sim
                WHERE SnapshotId=$id AND Slot=$slot
                """, ("$cid", s?.Card?.Cid), ("$raw", s?.RawText ?? ""), ("$score", s?.OcrScore ?? 0),
                ("$sim", s?.MatchSim ?? 0), ("$id", snapshotId), ("$slot", i + 1));
            card.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void UpsertAlias(string raw, int cid)
    {
        var key = CardDatabase.Normalize(raw);
        if (key.Length == 0) return;
        using var cmd = Cmd("""
            INSERT INTO OcrAlias(RawText, CardCid, CreatedAt) VALUES($r, $c, $t)
            ON CONFLICT(RawText) DO UPDATE SET CardCid=excluded.CardCid, CreatedAt=excluded.CreatedAt
            """, ("$r", key), ("$c", cid), ("$t", Now()));
        cmd.ExecuteNonQuery();
    }

    public bool TryAlias(string raw, out int cid)
    {
        if (!TryPeek(raw, out cid)) return false;
        using var bump = Cmd("UPDATE OcrAlias SET Hits=Hits+1 WHERE RawText=$r", ("$r", CardDatabase.Normalize(raw)));
        bump.ExecuteNonQuery();
        return true;
    }

    public bool TryPeek(string raw, out int cid)
    {
        var key = CardDatabase.Normalize(raw);
        cid = 0;
        if (key.Length == 0) return false;
        using var find = Cmd("SELECT CardCid FROM OcrAlias WHERE RawText=$r", ("$r", key));
        var hit = find.ExecuteScalar();
        if (hit == null) return false;
        cid = (int)(long)hit;
        return true;
    }

    bool IAliasLookup.TryLookup(string raw, out int cid) => TryAlias(raw, out cid);

    public IReadOnlyList<AliasRow> ListAliases()
    {
        var rows = new List<AliasRow>();
        using var cmd = Cmd("SELECT RawText, CardCid, CreatedAt, Hits FROM OcrAlias ORDER BY Hits DESC, RawText");
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(new AliasRow(r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetInt32(3)));
        return rows;
    }

    public void DeleteAlias(string raw)
    {
        using var cmd = Cmd("DELETE FROM OcrAlias WHERE RawText=$r", ("$r", CardDatabase.Normalize(raw)));
        cmd.ExecuteNonQuery();
    }

    public void DeleteSnapshots(IEnumerable<long> ids, bool deleteImages)
    {
        var paths = new List<string>();
        using var tx = _db.BeginTransaction();
        foreach (var id in ids)
        {
            using (var find = Cmd("SELECT ImagePath FROM HandSnapshot WHERE Id=$i", ("$i", id)))
            using (var r = find.ExecuteReader())
                while (r.Read()) paths.Add(r.GetString(0));
            using (var cards = Cmd("DELETE FROM SnapshotCard WHERE SnapshotId=$i", ("$i", id))) cards.ExecuteNonQuery();
            using (var snap = Cmd("DELETE FROM HandSnapshot WHERE Id=$i", ("$i", id))) snap.ExecuteNonQuery();
        }
        tx.Commit();
        if (!deleteImages) return;
        foreach (var p in paths)
            try { if (File.Exists(p)) File.Delete(p); } catch (IOException) { }
    }

    // ImagePath='' 表示证据截图已被清理;文件删除由调用方负责，这里只标记证据图位于 dir 下的记录。
    public int MarkImagesCleanedUnder(string dir)
    {
        var prefix = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var ids = new List<long>();
        using (var q = Cmd("SELECT Id, ImagePath FROM HandSnapshot WHERE ImagePath<>''"))
        using (var r = q.ExecuteReader())
            while (r.Read())
                if (Path.GetFullPath(r.GetString(1)).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    ids.Add(r.GetInt64(0));
        if (ids.Count == 0) return 0;
        using var up = Cmd($"UPDATE HandSnapshot SET ImagePath='' WHERE Id IN ({string.Join(",", ids)})");
        up.ExecuteNonQuery();
        return ids.Count;
    }

    // BOM so Excel opens the Chinese columns correctly.
    public void ExportCsv(string path, CardDatabase db, IReadOnlyList<SnapshotRow> rows)
    {
        string Name(int? cid) => cid != null && db.TryByCid(cid.Value, out var c) ? c.DisplayName : "";
        var decks = ListDecks().ToDictionary(d => d.Id, d => d.Name);
        static string Esc(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
        using var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
        w.WriteLine("SnapshotId,SessionId,CapturedAt,TurnOrder,Deck,Slot,CardCid,CardName,CorrectedCid,CorrectedName,RawText,OcrScore,MatchSim,CorrectedAt,ElapsedMs,CapturePath,ImagePath");
        foreach (var s in rows)
            foreach (var c in s.Cards)
                w.WriteLine(string.Join(",",
                    s.Id, s.SessionId, Esc(s.CapturedAt), Esc(s.TurnOrder), Esc(s.DeckId != null ? decks.GetValueOrDefault(s.DeckId.Value, "") : ""), c.Slot,
                    c.CardCid?.ToString() ?? "", Esc(Name(c.CardCid)),
                    c.CorrectedCid?.ToString() ?? "", Esc(Name(c.CorrectedCid)),
                    Esc(c.RawText), c.OcrScore.ToString("0.00"), c.MatchSim.ToString("0.00"),
                    Esc(c.CorrectedAt ?? ""), s.ElapsedMs, Esc(s.CapturePath), Esc(s.ImagePath)));
    }

    public int ImportLabeledDir(string dir, CardDatabase db, LabelCodec codec, string sessionNote)
    {
        var files = Directory.EnumerateFiles(dir)
            .Where(p => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        long session = 0;
        var count = 0;
        foreach (var path in files)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (codec.IsSkipped(stem)) continue;
            if (!codec.TryParse(stem, out _, out var names)) continue;
            if (session == 0) session = CreateSession(sessionNote + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            using var tx = _db.BeginTransaction();
            long id;
            using (var ins = Cmd("""
                INSERT INTO HandSnapshot(SessionId, CapturedAt, ImagePath, CapturePath, ElapsedMs, TurnOrder)
                VALUES($s, $t, $p, 'import', 0, ''); SELECT last_insert_rowid();
                """, ("$s", session), ("$t", Now()), ("$p", Path.GetFullPath(path))))
                id = (long)ins.ExecuteScalar()!;
            for (var i = 0; i < names.Length; i++)
            {
                int? cid = null;
                if (names[i] != null && (db.TryExact(names[i]!, out var card) || db.TryExactKey(names[i]!, out card)))
                    cid = card.Cid;
                using var cardCmd = Cmd("""
                    INSERT INTO SnapshotCard(SnapshotId, Slot, CardCid, RawText, OcrScore, MatchSim, CorrectedCid, CorrectedAt)
                    VALUES($id, $slot, NULL, '', 0, 0, $cid, $t)
                    """, ("$id", id), ("$slot", i + 1), ("$cid", cid), ("$t", Now()));
                cardCmd.ExecuteNonQuery();
            }
            tx.Commit();
            count++;
        }
        return count;
    }

    public void Dispose() => _db.Dispose();
}