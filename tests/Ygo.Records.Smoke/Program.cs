using Microsoft.Data.Sqlite;
using Ygo.CardLib;
using Ygo.Recognition;
using Ygo.Records;

if (args.Length >= 2 && args[0] == "verify")
{
    using var vs = new RecordStore(args[1]);
    var snaps = vs.ListSnapshots();
    var turns = string.Join(",", snaps.GroupBy(s => s.TurnOrder).Select(g => $"{(g.Key.Length == 0 ? "空" : g.Key)}:{g.Count()}"));
    Console.WriteLine($"verify snapshots={snaps.Count} cards={snaps.Sum(s => s.Cards.Count)} aliases={vs.ListAliases().Count} turns={turns} decks={vs.ListDecks().Count} linked={snaps.Count(s => s.DeckId != null)} cleaned={snaps.Count(s => s.ImagePath.Length == 0)} sessions={vs.ListSessions().Count} emptySessions={vs.ListSessions().Count(s => vs.ListSnapshots(s.Id).Count == 0)}");
    return 0;
}

if (args.Length >= 3 && args[0] == "import-deck")
{
    var root2 = DataRoot.Resolve() ?? Directory.GetCurrentDirectory();
    var db2 = CardDatabase.Load(Path.Combine(root2, "data", "cards.json"));
    using var st = new RecordStore(args[1]);
    var parsed = DeckTextParser.Parse(File.ReadAllText(args[2]));
    var name = args.Length > 3 ? args[3] : parsed.Name;
    var rows = new List<DeckCardRow>();
    int total = 0, matched = 0;
    foreach (var (sec, entries) in new[] { ("Main", parsed.Main), ("Extra", parsed.Extra), ("Side", parsed.Side) })
        foreach (var e in entries)
        {
            total += e.Count;
            int? cid = db2.TryExact(e.Name, out var c) ? c.Cid : null;
            if (cid != null) matched += e.Count;
            rows.Add(new DeckCardRow(sec, cid, e.Name, e.Count));
        }
    var id = st.AddDeck(name, rows);
    Console.WriteLine($"import-deck id={id} name={name} total={total} matched={matched}");
    return matched == total ? 0 : 1;
}

var root = args.Length > 0 ? args[0] : DataRoot.Resolve() ?? Directory.GetCurrentDirectory();
var db = CardDatabase.Load(Path.Combine(root, "data", "cards.json"));
var cfg = HandConfig.Load(Path.Combine(root, "data", "config.json"));
var codec = new LabelCodec(cfg);
var tmp = Path.Combine(Path.GetTempPath(), "ygo-records-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmp);
try
{
    using var store = new RecordStore(Path.Combine(tmp, "ygo.db"));
    Check(File.Exists(Path.Combine(tmp, "ygo.db")), "db created");
    var s1 = store.CreateSession("巳剑_20261115_100000");
    var s2 = store.CreateSession("巳剑_20261115_100001");
    Check(s2 != s1, "each call creates a new session");
    Check(store.ListSessions().Count == 2, "sessions listed");
    Check(store.ListSessions().First(x => x.Id == s1).Note == "巳剑_20261115_100000", "session name stored");

    db.TryExact("青眼白龙", out var blue);
    db.TryExact("灰流丽", out var ash);
    var slots = new SlotResult?[]
    {
        new(blue, "青眼白龙", 0.99, 1.0), new(ash, "灰流丽", 0.98, 1.0), null,
        new SlotResult(null, "之應导的帘", 0.7, 0), new(blue, "青眼白龙", 0.9, 1.0),
    };
    var img = Path.Combine(tmp, "shot.png");
    File.WriteAllBytes(img, new byte[] { 1 });
    var id = store.AddSnapshot(s1, img, "wgc", 1234, "", slots);
    var list = store.ListSnapshots();
    Check(list.Count == 1 && list[0].Cards.Count == 5, "snapshot + 5 cards");
    Check(list[0].Cards[2].CardCid == null && list[0].Cards[3].RawText == "之應导的帘", "null slot + raw stored");
    Check(list[0].ElapsedMs == 1234 && list[0].CapturePath == "wgc", "elapsed + capture path stored");
    Check(store.ListSnapshots(s1).Count == 1 && store.ListSnapshots(s2).Count == 0, "session filter");

    db.TryExact("黑之魔导的帘布", out var curtain);
    store.Correct(id, 4, curtain.Cid, "之應导的帘");
    var after = store.GetSnapshot(id)!;
    Check(after.Cards[3].CorrectedCid == curtain.Cid && after.Cards[3].CorrectedAt != null, "correction stored");
    Check(store.TryAlias("之應导的帘", out var aliasCid) && aliasCid == curtain.Cid, "alias hit after correction");
    Check(store.ListAliases().Single().Hits == 1, "alias hits bumped");
    store.UpsertAlias("之應导的帘", blue.Cid);
    Check(store.TryAlias("之應导的帘", out var c2) && c2 == blue.Cid, "alias overwrite");
    store.DeleteAlias("之應导的帘");
    Check(!store.TryAlias("之應导的帘", out _), "alias delete");

    store.SetTurnOrder(id, "先手");
    Check(store.GetSnapshot(id)!.TurnOrder == "先手", "turn order stored");

    var reSlots = new SlotResult?[]
    {
        new(blue, "青眼白龙", 0.99, 1.0), new(ash, "灰流丽", 0.98, 1.0), new(ash, "灰流丽", 0.97, 1.0),
        new(curtain, "黑之魔导的帘布", 0.95, 1.0), new(blue, "青眼白龙", 0.9, 1.0),
    };
    store.UpdateRecognition(id, reSlots, 4321);
    var re = store.GetSnapshot(id)!;
    Check(re.ElapsedMs == 4321 && re.Cards[3].CardCid == curtain.Cid && re.Cards[2].CardCid == ash.Cid, "re-recognize refreshes ocr fields");
    Check(re.Cards[3].CorrectedCid == curtain.Cid, "re-recognize keeps human correction");

    var csv = Path.Combine(tmp, "out.csv");
    store.ExportCsv(csv, db, store.ListSnapshots());
    var lines = File.ReadAllLines(csv);
    Check(lines.Length == 6, "csv header + 5 rows");
    Check(lines[0].StartsWith("SnapshotId,SessionId,CapturedAt,TurnOrder,Deck,Slot"), "csv header");
    Check(File.ReadAllBytes(csv)[0] == 0xEF, "csv utf8 bom");
    Check(lines.Any(l => l.Contains("黑之魔导的帘布")), "csv corrected name");

    var impDir = Path.Combine(tmp, "imp");
    Directory.CreateDirectory(impDir);
    File.WriteAllBytes(Path.Combine(impDir, "f_000001__青眼白龙+灰流丽+？+次元的裂缝+被封印的艾克佐迪亚.png"), new byte[] { 1 });
    File.WriteAllBytes(Path.Combine(impDir, "SKIP_f_000002.png"), new byte[] { 1 });
    var n = store.ImportLabeledDir(impDir, db, codec, "导入验收集");
    Check(n == 1, "import skips SKIP_ and unlabeled");
    var imported = store.ListSnapshots().First(x => x.CapturePath == "import");
    Check(imported.Cards[0].CorrectedCid == blue.Cid && imported.Cards[2].CorrectedCid == null, "import labels as corrected");

    store.DeleteSnapshots(new[] { id }, false);
    Check(store.GetSnapshot(id) == null && File.Exists(img), "delete keeps image by default");
    store.DeleteSnapshots(new[] { imported.Id }, true);
    Check(!File.Exists(imported.ImagePath), "delete with images removes png");
    Check(store.ListSnapshots().Count == 0, "all snapshots deleted");

    // M3-9: deck clipboard parse + deck store + snapshot classification
    var fixDir = Path.Combine(root, "tests", "fixtures");
    var gallery = DeckTextParser.Parse(File.ReadAllText(Path.Combine(fixDir, "deck-gallery.txt")));
    var listView = DeckTextParser.Parse(File.ReadAllText(Path.Combine(fixDir, "deck-list.txt")));
    Check(gallery.Name == "巳剑" && listView.Name == "巳剑", "deck name parsed");
    Check(gallery.Main.Sum(e => e.Count) == 41 && gallery.Extra.Sum(e => e.Count) == 15 && gallery.Side.Sum(e => e.Count) == 0, "gallery section totals 41/15/0");
    Check(gallery.Main.Count + gallery.Extra.Count + gallery.Side.Count == 37, "37 unique entries");
    Check(SameEntries(gallery.Main, listView.Main) && SameEntries(gallery.Extra, listView.Extra) && SameEntries(gallery.Side, listView.Side), "gallery == list view");
    var allEntries = gallery.Main.Concat(gallery.Extra).Concat(gallery.Side).ToList();
    Check(allEntries.All(e => db.TryExact(e.Name, out _)), "all deck names match cards.json");
    var deckId = store.AddDeck("巳剑", ToRows(gallery));
    Check(store.ListDecks().Single().TotalCards == 56, "deck listed with 56 cards");
    Check(store.GetDeckCards(deckId).Count == 37, "deck cards stored");
    var coverCid = store.ListDecks().Single().CoverCid;
    Check(coverCid != null && store.GetDeckCards(deckId).Any(c => c.Section == "Main" && c.Cid == coverCid), "deck cover picked from main deck at creation");
    var withDeck = store.AddSnapshot(s1, img, "pw", 100, "先手", slots, deckId);
    var noDeck = store.AddSnapshot(s1, img, "pw", 100, "后手", slots);
    Check(store.GetSnapshot(withDeck)!.DeckId == deckId && store.GetSnapshot(noDeck)!.DeckId == null, "snapshot deck link stored");
    Check(store.ListSnapshots(null, deckId).Single().Id == withDeck, "deck filter");
    Check(store.ListSnapshots(null, 0).Single().Id == noDeck, "unclassified filter");
    store.SetDeck(noDeck, deckId);
    Check(store.ListSnapshots(null, deckId).Count == 2, "SetDeck reclassifies");
    var s3 = store.CreateSession("巳剑_20261116_090000");
    var third = store.AddSnapshot(s3, img, "pw", 100, "先手", slots, deckId);
    Check(store.ListSessions(deckId).Count == 2, "ListSessions(deck) only sessions holding that deck");
    Check(HandStats.Compute(store.ListSnapshots(s1, deckId)).Total == 2, "single-session scope");
    Check(HandStats.Compute(store.ListSnapshots(null, deckId)).Total == 3, "merged-all-sessions scope");
    // 重开稳定性检查放在已有快照之后：开库会 prune 空会话，提前重开会删掉 s1 导致后续 FK 失败
    using (var coverReopen = new RecordStore(Path.Combine(tmp, "ygo.db")))
        Check(coverReopen.ListDecks().Single().CoverCid == coverCid, "deck cover stable across reopen (no re-random)");
    store.DeleteSnapshots(new[] { third }, false);
    store.DeleteDeck(deckId);
    Check(store.ListDecks().Count == 0 && store.GetSnapshot(withDeck)!.DeckId == null, "delete deck unlinks snapshots");
    store.DeleteSnapshots(new[] { withDeck, noDeck }, false);

    // alias participates in recognition: known 超框 miss on own sample becomes a hit once confirmed
    var ownDir = Path.Combine(root, "samples", "own");
    var missFile = Directory.Exists(ownDir) ? Directory.GetFiles(ownDir, "*005302*").FirstOrDefault() : null;
    if (missFile == null)
    {
        Console.WriteLine("SKIP: alias-recognition block (samples/own not provided; see README)");
    }
    else
    {
        using var bmp = new System.Drawing.Bitmap(missFile);
        using (var plain = new HandRecognizer(db, cfg))
        {
            var weak = plain.Recognize(bmp).Slots[4]!;
            Check(weak.Card == null || weak.MatchSim < 0.9, "known 超框 slot weak without alias");
            Check(weak.Candidates.Count > 0, "weak slot exposes candidates");
            Check(weak.Candidates.Any(c => c.Card.Cid == curtain.Cid), "true card listed among candidates");
        }
        store.UpsertAlias("建学的帘养", curtain.Cid);
        using (var withAlias = new HandRecognizer(db, cfg, store))
        {
            var fixedSlot = withAlias.Recognize(bmp).Slots[4]!;
            Check(fixedSlot.Card?.Cid == curtain.Cid && fixedSlot.MatchSim == 1, "alias turns miss into confirmed hit");
        }
        Check(store.ListAliases().Single(x => x.CardCid == curtain.Cid).Hits >= 1, "alias usage counted");
    }

    // M3-11: theoretical opening rate + per deck-day observed rate
    Check(Math.Abs(HandStats.Theoretical(40, 1, 5) - 12.5) < 1e-9, "theo 1 copy in 40 x5 = 12.5%");
    Check(HandStats.Theoretical(40, 0, 5) == 0 && HandStats.Theoretical(40, 40, 5) == 100, "theo zero / full deck");
    Check(Math.Abs(HandStats.Theoretical(40, 3, 5) - 33.755) < 0.01, "theo 3 copies in 40 x5 ~33.76%");
    Check(HandStats.Theoretical(5, 1, 5) == 100, "theo guaranteed when main <= hand");
    Check(HandStats.Mark(50, 40) == "↑" && HandStats.Mark(30, 40) == "↓" && HandStats.Mark(40.5, 40) == "-", "mark up/down/within-1pp");
    var sessionSnaps = new List<SnapshotRow>
    {
        MkSnap(1, "先手", new int?[] { 1, 2 }),
        MkSnap(2, "先手", new int?[] { 1, 1, 3 }),
        MkSnap(3, "后手", new int?[] { 2, 4 }),
    };
    var ds = HandStats.Compute(sessionSnaps);
    Check(ds.Total == 3 && ds.First == 2 && ds.Second == 1, "session buckets 3/2/1");
    Check(ds.Hits(1, "") == 2 && ds.Hits(1, "先手") == 2 && ds.Hits(1, "后手") == 0, "per-bucket distinct-card hits");
    Check(Math.Abs(ds.Rate(1, "")!.Value - 66.667) < 0.01 && ds.Rate(1, "后手") == 0, "observed rate total 2/3, second 0/1");
    Check(HandStats.Compute(new List<SnapshotRow>()).Rate(1, "") == null, "no samples -> null rate");

    // M3-11 fix: legacy GUI import stored Chinese Section; normalize on open
    var legacyDb = Path.Combine(tmp, "legacy.db");
    using (var legacy = new RecordStore(legacyDb))
        legacy.AddDeck("旧", new List<DeckCardRow> { new("主卡组", blue.Cid, "青眼白龙", 3), new("额外", ash.Cid, "灰流丽", 1), new("侧卡组", null, "x", 1) });
    using (var reopened = new RecordStore(legacyDb))
    {
        var secs = reopened.GetDeckCards(reopened.ListDecks().Single().Id).Select(c => c.Section).ToList();
        Check(secs.SequenceEqual(new[] { "Main", "Extra", "Side" }), "legacy Chinese Section normalized to Main/Extra/Side");
    }
    // P11：旧库 Deck 无 CoverCid 列 → 开库补抽一次（候选仅主卡组）
    var coverOldDb = Path.Combine(tmp, "cover-old.db");
    using (var oc = new SqliteConnection($"Data Source={coverOldDb};Pooling=False"))
    {
        oc.Open();
        using var ocCmd = oc.CreateCommand();
        ocCmd.CommandText = """
            CREATE TABLE Deck(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, ImportedAt TEXT NOT NULL);
            CREATE TABLE DeckCard(Id INTEGER PRIMARY KEY AUTOINCREMENT, DeckId INTEGER NOT NULL, Section TEXT NOT NULL, Cid INTEGER, RawName TEXT NOT NULL, Count INTEGER NOT NULL);
            INSERT INTO Deck(Name, ImportedAt) VALUES('旧封面', '2026-11-16 10:00:00');
            INSERT INTO DeckCard(DeckId, Section, Cid, RawName, Count) VALUES(1, 'Main', $c, '青眼白龙', 3);
            """;
        ocCmd.Parameters.AddWithValue("$c", blue.Cid);
        ocCmd.ExecuteNonQuery();
    }
    using (var coverMig = new RecordStore(coverOldDb))
        Check(coverMig.ListDecks().Single().CoverCid == blue.Cid, "legacy deck without CoverCid gets one on open");
    // P12：存储设置 roundtrip/默认值 + 目录搬迁/占用统计
    var setRoot = Path.Combine(tmp, "setroot");
    Directory.CreateDirectory(setRoot);
    var st = new AppSettings { CapturesDir = Path.Combine(tmp, "cap-a"), CardDataDir = Path.Combine(tmp, "card-a") };
    st.Save(setRoot);
    var st2 = AppSettings.Load(setRoot);
    Check(st2.CapturesDir == st.CapturesDir && st2.CardDataDir == st.CardDataDir, "settings roundtrip");
    var noRoot = Path.Combine(tmp, "no-such-root");
    Check(AppSettings.Load(noRoot).Captures(noRoot) == Path.Combine(AppSettings.DefaultBase(), "captures"), "settings default captures under Documents");
        Check(AppSettings.Load(noRoot).CardData(noRoot) == Path.Combine(AppSettings.DefaultBase(), "carddata"), "settings default carddata under Documents");
    var moveFrom = Path.Combine(tmp, "move-from");
    Directory.CreateDirectory(Path.Combine(moveFrom, "sub"));
    File.WriteAllText(Path.Combine(moveFrom, "a.png"), "x");
    File.WriteAllText(Path.Combine(moveFrom, "sub", "b.webp"), "y");
    var moveTo = Path.Combine(tmp, "move-to");
    CardDataUpdater.MoveDirectory(moveFrom, moveTo);
    Check(!Directory.Exists(moveFrom) && File.Exists(Path.Combine(moveTo, "a.png")) && File.Exists(Path.Combine(moveTo, "sub", "b.webp")), "MoveDirectory relocates tree");
    Check(CardDataUpdater.DirectoryBytes(moveTo) == 2, "DirectoryBytes sums tree");
    // P13：属性/组合 CRUD + 聚合统计（0~5 分布 / 合先后 / 条目全齐 / 类型任一 / 期望）
    var aggDb = Path.Combine(tmp, "agg.db");
    using (var agg = new RecordStore(aggDb))
    {
        var dId = agg.AddDeck("聚合", new List<DeckCardRow> { new("Main", blue.Cid, "青眼白龙", 3), new("Main", ash.Cid, "灰流丽", 3) });
        Check(agg.ListTags(dId).Select(t => t.Name).SequenceEqual(new[] { "动点", "手坑", "废件" }), "new deck seeds default types");
        Check(agg.ListComboTypes(dId).Single().Name == "两卡动", "new deck seeds default combo type");
        var feiId = agg.ListTags(dId).First(t => t.Name == "废件").Id;
        agg.DeleteTag(feiId);
        Check(agg.ListTags(dId).Count == 2, "default type deletable");
        var tag = agg.ListTags(dId).First(t => t.Name == "手坑").Id;
        agg.ToggleTagCard(tag, ash.Cid);
        Check(agg.ListTags(dId).First(t => t.Id == tag).Cids.SequenceEqual(new[] { ash.Cid }), "tag toggle adds card");
        agg.ToggleTagCard(tag, ash.Cid);
        Check(agg.ListTags(dId).First(t => t.Id == tag).Cids.Count == 0, "tag toggle removes card");
        agg.ToggleTagCard(tag, ash.Cid);
        agg.ToggleTagCard(tag, blue.Cid);
        db.TryByCid(blue.Cid, out var blueCard);
        db.TryByCid(ash.Cid, out var ashCard);
        var aggS1 = agg.CreateSession("s1");
        agg.AddSnapshot(aggS1, "i1", "pw", 1, "先手", new List<SlotResult?> { new(ashCard, "", 1, 1), new(blueCard, "", 1, 1), null, null, null }, dId);
        agg.AddSnapshot(aggS1, "i2", "pw", 1, "先手", new List<SlotResult?> { new(ashCard, "", 1, 1), null, null, null, null }, dId);
        agg.AddSnapshot(aggS1, "i3", "pw", 1, "后手", new List<SlotResult?> { new(blueCard, "", 1, 1), null, null, null, null }, dId);
        var snaps = agg.ListSnapshots(null, dId);
        var ts = DeckAgg.TagStats(snaps, new[] { ash.Cid, blue.Cid });
        Check(ts.Total.Hands == 3 && ts.Total.Hits == 3, "tag hits all hands");
        Check(ts.Total.Dist[2] == 1 && ts.Total.Dist[1] == 2 && ts.Total.Dist[0] == 0, "tag 0~5 distribution");
        Check(ts.First.Dist[2] == 1 && ts.Second.Dist[1] == 1, "tag split buckets");
        var ct = agg.ListComboTypes(dId).First(t => t.Name == "两卡动").Id;
        agg.AddComboEntry(ct, "蓝+灰", new[] { blue.Cid, ash.Cid });
        var entries = agg.ListComboTypes(dId).First(t => t.Id == ct).Entries;
        Check(entries.Single().Cids.Count == 2, "combo entry stored");
        var cs = DeckAgg.ComboStats(snaps, entries.Select(e => new DeckAgg.EntrySpec(e.Cids, e.OptCids)).ToList());
        Check(cs.Total.Hits == 1 && cs.First.Hits == 1 && cs.Second.Hits == 0, "combo entry all-present / type any");
        var es = DeckAgg.EntryStats(snaps, new DeckAgg.EntrySpec(new[] { blue.Cid, ash.Cid }, Array.Empty<int>()));
        Check(es.Total.Hits == 1 && es.Total.Dist[1] == 1, "entry stats");
        Check(DeckAgg.Expected(6, 6, 5) == 5, "expected copies");
        Check(ts.Total.Mean != null && Math.Abs(ts.Total.Mean.Value - 4.0 / 3) < 1e-9, "observed mean copies per hand");
        // P14：锚卡+任选集合条目（灰流丽必需 + 任选{青眼白龙, 次元的裂缝?}）用 blue 作锚、ash 作可选反例
        var thirdCard = db.Cards.First(c => c.Cid != blue.Cid && c.Cid != ash.Cid);
        var optEntry = agg.AddComboEntry(ct, "锚+任选", new[] { ash.Cid }, new[] { blue.Cid, thirdCard.Cid });
        var optSpec = new DeckAgg.EntrySpec(new[] { ash.Cid }, new[] { blue.Cid, thirdCard.Cid });
        var os = DeckAgg.EntryStats(snaps, optSpec);
        Check(os.Total.Hits == 1 && os.First.Hits == 1 && os.Second.Hits == 0, "anchor required AND any-of opt present");
        var copies = new Dictionary<int, int> { [blue.Cid] = 3, [ash.Cid] = 3, [thirdCard.Cid] = 1 };
        var mt = 41;
        var et = DeckAgg.EntryTheory(mt, copies, new DeckAgg.EntrySpec(new[] { blue.Cid, ash.Cid }, Array.Empty<int>()), 5);
        Check(et != null && et > 0 && et <= 100, "entry theory computed");
        var ot = DeckAgg.EntryTheory(mt, copies, optSpec, 5);
        Check(ot != null && ot > et, "anchor+any-of theory >= all-present theory");
        var cth = DeckAgg.ComboTheory(mt, copies, new List<DeckAgg.EntrySpec> { new(new[] { blue.Cid, ash.Cid }, Array.Empty<int>()), optSpec }, 5);
        Check(cth != null && cth >= ot && cth < 100, "type theory inclusion-exclusion >= single entry and percent-scaled");
        agg.DeleteComboEntry(optEntry);
        agg.DeleteDeck(dId);
        Check(agg.ListTags(dId).Count == 0 && agg.ListComboTypes(dId).Count == 0, "deck delete cascades tags/combos");
    }
    // 会话重构：旧按天会话自动按卡组各合并为一个会话（卡组名+最早截图时间戳）
    var migDb = Path.Combine(tmp, "mig.db");
    long migA, migB;
    using (var pre = new RecordStore(migDb))
    {
        migA = pre.AddDeck("甲", new List<DeckCardRow> { new("Main", blue.Cid, "青眼白龙", 3) });
        migB = pre.AddDeck("乙", new List<DeckCardRow> { new("Main", ash.Cid, "灰流丽", 3) });
    }
    using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={migDb};Pooling=False"))
    {
        raw.Open();
        using var ins = raw.CreateCommand();
        ins.CommandText = "INSERT INTO DuelSession(StartedAt, Note) VALUES('2026-11-15','2026-11-15 对局'); SELECT last_insert_rowid();";
        var oldSid = (long)ins.ExecuteScalar()!;
        ins.CommandText = $"""
            INSERT INTO HandSnapshot(SessionId, CapturedAt, ImagePath, CapturePath, ElapsedMs, TurnOrder, DeckId) VALUES
            ({oldSid},'2026-11-15 10:00:00','','',0,'先手',{migA}),
            ({oldSid},'2026-11-15 11:00:00','','',0,'后手',{migA}),
            ({oldSid},'2026-11-15 12:00:00','','',0,'先手',{migB}),
            ({oldSid},'2026-11-15 13:00:00','','',0,'',NULL);
            """;
        ins.ExecuteNonQuery();
    }
    using (var post = new RecordStore(migDb))
    {
        var sessions = post.ListSessions();
        Check(sessions.Count == 3, "legacy day session split per deck (2 decks + unclassified)");
        Check(sessions.All(s => s.StartedAt.Length > 10), "migrated sessions carry full timestamp");
        var sa = post.ListSessions(migA).Single();
        Check(sa.Note == "甲_20261115_100000", "merged session named deck+earliest capture");
        Check(post.ListSnapshots(sa.Id, migA).Count == 2, "deck A snapshots merged into one session");
        Check(post.ListSessions(migB).Single().Note == "乙_20261115_120000", "deck B merged separately");
        Check(post.ListSessions(0).Single().Note == "未分类", "unclassified merged into one shared session");
    }
    using (var again = new RecordStore(migDb))
    {
        Check(again.ListSessions().Count == 3, "migration idempotent on reopen");
        var shared = again.ListSessions(0).Single();
        Check(again.GetOrCreateSession(RecordStore.UnclassifiedSession) == shared.Id, "GetOrCreateSession reuses shared unclassified");
        Check(again.GetOrCreateSession(RecordStore.UnclassifiedSession) == shared.Id, "GetOrCreateSession stable across calls");
        Check(again.ListSnapshots(shared.Id).Count == 1, "merge keeps unclassified snapshots");
        again.CreateSession("空会话_20261116_120000");
        Check(again.ListSessions().Count == 4, "empty session exists before prune");
        Check(again.PruneEmptySessions() == 1 && again.ListSessions().Count == 3, "prune drops only empty sessions");
        Check(Directory.GetDirectories(tmp, "backup-session-scheme-*").Length == 1, "pre-migration backup written once");
    }
    // 多个历史「未分类_时间戳」会话合并为一个，快照一张不丢
    var mig2Db = Path.Combine(tmp, "mig2.db");
    using (var pre2 = new RecordStore(mig2Db))
    {
        Check(pre2.ListSessions().Count == 0, "fresh db has no sessions");
        pre2.CreateSession("未分类_20261114_100000");
        pre2.CreateSession("未分类_20261115_110000");
    }
    using (var raw2 = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={mig2Db};Pooling=False"))
    {
        raw2.Open();
        using var ins2 = raw2.CreateCommand();
        ins2.CommandText = """
            INSERT INTO HandSnapshot(SessionId, CapturedAt, ImagePath, CapturePath, ElapsedMs, TurnOrder, DeckId) VALUES
            (1,'2026-11-14 10:00:00','','',0,'先手',NULL),
            (2,'2026-11-15 11:00:00','','',0,'后手',NULL);
            """;
        ins2.ExecuteNonQuery();
    }
    using (var post2 = new RecordStore(mig2Db))
    {
        var u = post2.ListSessions(0).Single();
        Check(u.Note == "未分类" && post2.ListSessions().Count == 1, "two legacy unclassified sessions collapse to one");
        Check(post2.ListSnapshots(u.Id).Count == 2, "both snapshots survive the collapse");
        Check(u.StartedAt == "2026-11-14 10:00:00", "shared session keeps earliest capture time");
    }

    // M3-15: 截图缓存清理标记——只标记 captures 目录下的记录，外部样本路径不动
    var capDir = Path.Combine(tmp, "captures");
    Directory.CreateDirectory(capDir);
    var capImg = Path.Combine(capDir, "shot.png");
    File.WriteAllBytes(capImg, new byte[] { 1 });
    var sampleDir = Path.Combine(tmp, "samples");
    Directory.CreateDirectory(sampleDir);
    var sampleImg = Path.Combine(sampleDir, "keep.png");
    File.WriteAllBytes(sampleImg, new byte[] { 1 });
    var capSnap = store.AddSnapshot(s1, capImg, "wgc", 10, "先手", slots);
    var keepSnap = store.AddSnapshot(s1, sampleImg, "import", 0, "", slots);
    Check(store.MarkImagesCleanedUnder(capDir) == 1, "clean marks only captures-dir snapshots");
    Check(store.GetSnapshot(capSnap)!.ImagePath == "", "cleaned snapshot ImagePath emptied");
    Check(store.GetSnapshot(keepSnap)!.ImagePath == sampleImg && File.Exists(sampleImg), "outside sample path+file untouched");
    Check(store.MarkImagesCleanedUnder(capDir) == 0, "clean mark idempotent");
    store.DeleteSnapshots(new[] { capSnap, keepSnap }, false);
    Console.WriteLine("RECORDS SMOKE PASS");
    return 0;
}
finally
{
    Directory.Delete(tmp, true);
}

static void Check(bool ok, string what)
{
    if (!ok)
    {
        Console.WriteLine("FAIL: " + what);
        Environment.Exit(1);
    }
    Console.WriteLine("ok: " + what);
}

static bool SameEntries(List<DeckEntry> a, List<DeckEntry> b) => a.Count == b.Count && a.SequenceEqual(b);

static SnapshotRow MkSnap(long id, string turn, int?[] cids) =>
    new(id, 1, "2026-11-15 10:00:00", "", "", 0, turn,
        cids.Select((c, i) => new CardRow(i + 1, c, "", 0, 0, null, null)).ToList());

List<DeckCardRow> ToRows(DeckParseResult p)
{
    var r = new List<DeckCardRow>();
    foreach (var (sec, entries) in new[] { ("Main", p.Main), ("Extra", p.Extra), ("Side", p.Side) })
        foreach (var e in entries)
            r.Add(new DeckCardRow(sec, db.TryExact(e.Name, out var c) ? c.Cid : null, e.Name, e.Count));
    return r;
}