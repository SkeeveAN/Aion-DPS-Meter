using AionDPS.Aion2.Protocol;
using AionDPS.Combat.Sources;
using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>Sessions: cut at every zone change, events before a change stay with the old stay, a pass through a portal hall leaves no entry,
/// a combat id change joins the player across stays.</summary>
public static class SelfCheckSessions
{
    private const int Verteron = 1010;
    private const int Krao = 600001;

    private const int Boss = 100;

    /// <summary>A boss with 600,000 hit points and three players, one 1,000 hit each per second in turn: 600 hits, a kill at 200 s.</summary>
    private static SessionInfo BossStay(DateTime t0, Func<int, int, long> amount, bool honestHp = true, int playersHit = 3)
    {
        var recorder = new SessionRecorder(_ => SessionKind.Instance);
        recorder.Enter(new ZoneChange(t0, Krao));
        var hits = new List<DamageEvent>();
        for (int i = 0; i < 600; i++)
        {
            int player = 1 + i % playersHit;
            hits.Add(new DamageEvent(t0.AddMilliseconds(i * 333), player, Boss, amount(i, player), false, "Strike"));
        }

        // The other two players were there and did something else.
        hits.Add(new DamageEvent(t0.AddSeconds(1), 2, 101, 500, false, "Strike"));
        hits.Add(new DamageEvent(t0.AddSeconds(1), 3, 101, 500, false, "Strike"));
        recorder.Feed(CombatBatch.DamageOnly(hits.OrderBy(h => h.Timestamp).ToList()));
        SessionInfo session = recorder.Sessions[0];
        foreach (int id in new[] { 1, 2, 3 })
        {
            session.Classes[id] = "Gladiator";
            session.Names[id] = $"P{id}";
        }

        session.Names[Boss] = "Test Boss";
        long hp = 600_000;
        var samples = new List<Aion2.HpSample> { new(t0.AddMilliseconds(-100), hp) };
        foreach (DamageEvent h in hits.Where(h => h.TargetObjectId == Boss).OrderBy(h => h.Timestamp))
        {
            hp = Math.Max(0, hp - (honestHp ? h.Amount : 1000));
            samples.Add(new Aion2.HpSample(h.Timestamp, hp));
        }

        session.Hp[Boss] = new TargetHp(600_000, samples, Array.Empty<DateTime>());
        return session;
    }

    private static bool RunPlausibility(DateTime t0)
    {
        // A fair fight: the damage counted is what the boss lost.
        SessionAnalysis fair = SessionAnalyzer.Analyze(BossStay(t0, (_, _) => 1000));
        bool ok = fair.Findings.Any(f => f.Key == "Sessions.Check.HpOk" && f.Level == FindingLevel.Ok) && fair.Overall == FindingLevel.Ok;

        // Counted twice: every hit 2,000 while the boss lost 1,000 per hit.
        SessionAnalysis doubled = SessionAnalyzer.Analyze(BossStay(t0, (_, _) => 2000, honestHp: false));
        bool bad = doubled.Overall == FindingLevel.Bad;

        // One player owns it all (the others' hits were given to him): the hit points still match, the attribution is odd.
        SessionAnalysis owner = SessionAnalyzer.Analyze(BossStay(t0, (_, _) => 1000, playersHit: 1));
        bool oneOwner = owner.Findings.Any(f => f.Key == "Sessions.Check.OnePlayer" && f.Level == FindingLevel.Warn);

        // No hit points at all: it says so, and nothing is called wrong.
        SessionInfo bare = BossStay(t0, (_, _) => 1000);
        bare.Hp.Clear();
        bare.CachedAnalysis = null;
        SessionAnalysis unknown = SessionAnalyzer.Analyze(bare);
        bool noHp = unknown.Findings.Any(f => f.Key == "Sessions.Check.NoHp") && unknown.Overall == FindingLevel.Info;

        // Hits repeated at the same instant.
        var twins = new List<DamageEvent>();
        for (int i = 0; i < 300; i++)
        {
            DamageEvent e = new(t0.AddSeconds(i), 1, Boss, 100 + i, false, "Strike");
            twins.Add(e);
            if (i % 5 == 0)
            {
                twins.Add(e);
            }
        }

        bool duplicates = SessionAnalyzer.DuplicateRatio(twins) > SessionAnalyzer.DuplicateShare && SessionAnalyzer.DuplicateRatio(twins.Distinct().ToList()) == 0;

        // The rage: the boss's damage jumps at 4:30 and stays up (not before four minutes: earlier jumps are phases or the kill burst).
        DamageEvent[] Swings(int seconds, Func<int, long> amountAt) => Enumerable.Range(0, seconds).Select(i => new DamageEvent(t0.AddSeconds(i), Boss, 1, amountAt(i), false)).ToArray();
        EnrageInfo? rage = BossEnrage.Detect(Swings(330, i => i < 270 ? 1000 : 3000), t0, t0.AddSeconds(329));
        bool raged = rage is not null && Math.Abs(rage.At.TotalSeconds - 270) <= 10 && rage.Factor is > 2.5 and < 3.5;
        // A surge of 20 s that ends again is a mechanic.
        bool surge = BossEnrage.Detect(Swings(330, i => i is >= 250 and < 270 ? 3000 : 1000), t0, t0.AddSeconds(329)) is null;
        bool calm = BossEnrage.Detect(Swings(330, _ => 1000), t0, t0.AddSeconds(329)) is null;
        bool shortFight = BossEnrage.Detect(Swings(40, i => i < 20 ? 1000 : 3000), t0, t0.AddSeconds(39)) is null;
        // A jump in the last seconds of a 3:50 fight - the kill burst - is no rage.
        bool earlyEnd = BossEnrage.Detect(Swings(230, i => i < 200 ? 1000 : 4000), t0, t0.AddSeconds(229)) is null;

        Console.WriteLine($"  -> plausibility: damage = HP lost is in order: {ok}");
        Console.WriteLine($"  -> counted twice (2x the HP lost) is flagged wrong: {bad}");
        Console.WriteLine($"  -> one player with all of the boss's damage while others were there is flagged: {oneOwner}");
        Console.WriteLine($"  -> without hit points it says so and does not accuse: {noHp}");
        Console.WriteLine($"  -> hits repeated at the same instant are found: {duplicates}");
        Console.WriteLine($"  -> rage: found at about 4:30, about 3x: {raged}; a surge that ends is not one: {surge}; steady damage is not one: {calm}; a short fight has none: {shortFight}; a burst in the last seconds of a 3:50 fight is not one: {earlyEnd}");
        return ok && bad && oneOwner && noHp && duplicates && raged && surge && calm && shortFight && earlyEnd;
    }

    /// <summary>The frames of the real recordings (Talisra, 2026-10-07 16:30): going into combat, then the rage stat 300 s later.</summary>
    private static bool RunRageSignal(DateTime t0)
    {
        byte[] Engage(byte[] id) => new byte[] { 0x21, 0x8D }.Concat(id).Concat(new byte[] { 0x01, 0x01, 0x2C, 0x1A, 0xCA, 0x16, 0xA1, 0x01, 0x00, 0x00 }).ToArray();
        byte[] Leave(byte[] id) => new byte[] { 0x21, 0x8D }.Concat(id).Concat(new byte[] { 0x00, 0x00 }).ToArray();
        byte[] Stat(byte[] id, uint value) => new byte[] { 0x4B, 0x36 }.Concat(id).Concat(new byte[] { 0x01, 0xCD, 0x00 }).Concat(BitConverter.GetBytes(value)).ToArray();
        byte[] boss = { 0xE4, 0xBA, 0x01 };
        byte[] other = { 0xF7, 0xBB, 0x02 };

        var decoder = new Aion2.Protocol.Aion2FrameDecoder(Aion2.Protocol.Aion2Protocol.Load(), new Aion2.Aion2EntityDirectory());
        decoder.Decode(Engage(boss), t0);
        decoder.Decode(Stat(boss, 5000), t0.AddSeconds(212)); // the same stat in a short fight or idle: not the rage
        decoder.Decode(Stat(other, 10000), t0.AddSeconds(300)); // an entity that never went into combat
        bool early = decoder.DrainEnrages().Count == 0;

        decoder.Decode(Stat(boss, 10000), t0.AddSeconds(300.04));
        decoder.Decode(Stat(boss, 6000), t0.AddSeconds(314)); // later values of the same fight add nothing
        IReadOnlyList<EnrageMark> marks = decoder.DrainEnrages();
        bool found = marks.Count == 1 && marks[0].EntityId == 23908 && marks[0].EngagedAt == t0 && Math.Abs((marks[0].Timestamp - t0).TotalSeconds - 300.04) < 0.01;

        // Leaving combat resets the clock: a rage stat after coming out and back in is measured from the new start.
        decoder.Decode(Leave(boss), t0.AddSeconds(320));
        decoder.Decode(Stat(boss, 10000), t0.AddSeconds(330));
        bool reset = decoder.DrainEnrages().Count == 0;

        // The session keeps it, the check reports it as the server's: at 5:00, with the damage factor read around it.
        SessionInfo stay = BossStay(t0, (_, _) => 1000);
        stay.Enrages.Add(new EnrageMark(t0.AddSeconds(100), Boss, t0.AddSeconds(-200)));
        BossAttempt? attempt = SessionAnalyzer.Analyze(stay).Attempts.FirstOrDefault();
        bool analysed = attempt?.Enrage is { Exact: true } rage && rage.At == TimeSpan.FromSeconds(300);

        Console.WriteLine($"  -> rage signal: not before 4:50 and not on an entity out of combat: {early}; found at 5:00 after going into combat: {found}; leaving combat resets: {reset}; the session check reports it as exact: {analysed}");
        return early && found && reset && analysed;
    }

    /// <summary>The comparison of players: the skills sorted by how far they are from the baseline, a local player from a session, and an online fight read from the site's answers.</summary>
    private static bool RunCompare(DateTime t0)
    {
        var rec = new SessionRecorder(_ => SessionKind.Instance)
        {
            Identify = id => id switch { 1 => ("Aahz", "Sorcerer"), 2 => ("Bob", "Sorcerer"), _ => (null, null) },
            IdentifyPlayer = id => id == 1 ? new PlayerMeta(1, 1303, 150_000) : id == 2 ? new PlayerMeta(2, 1303, 120_000) : null,
        };
        rec.Enter(new ZoneChange(t0, Krao));
        var events = new List<DamageEvent>();
        for (int i = 0; i < 10; i++)
        {
            events.Add(new DamageEvent(t0.AddSeconds(i), 1, 100, 1000, false, "Firestorm", i % 2 == 0, SkillId: 15040047));
            events.Add(new DamageEvent(t0.AddSeconds(i), 2, 100, 600, false, "Firestorm", false, SkillId: 15040047));
            if (i % 3 == 0)
            {
                events.Add(new DamageEvent(t0.AddSeconds(i), 1, 100, 5000, false, "Meteor", true, SkillId: 15040051));
            }
        }

        rec.Feed(CombatBatch.DamageOnly(events));
        rec.Enter(new ZoneChange(t0.AddMinutes(5), Verteron));
        SessionInfo stay = rec.Sessions[0];
        var extras = new FightExtras(new[] { new SkillUse(t0.AddSeconds(1), 1, 15040047), new SkillUse(t0.AddSeconds(2), 1, 15040047), new SkillUse(t0.AddSeconds(2), 2, 15040047) },
            Array.Empty<BuffEvent>(), Array.Empty<StaggerEvent>(), new Dictionary<int, string>());
        CompareEntry first = CompareModel.FromSession(stay, null, 1, extras, "a");
        CompareEntry second = CompareModel.FromSession(stay, null, 2, extras, "b");
        List<CompareRow> table = CompareModel.Table(new[] { first, second }, 0);
        bool local = first.TotalDamage == 10_000 + 20_000 && first.CombatPower == 150_000 && first.ClassName == "Sorcerer" && second.TotalDamage == 6_000
            && first.Skills.Single(s => s.Skill == "Firestorm").Uses == 2 && second.Skills.Single(s => s.Skill == "Firestorm").Uses == 1 && first.Marks!.Any(m => m.IsUse);
        // Meteor only the baseline has (gap -20000/9), Firestorm both (gap -4000/9): the skill that makes up most of the gap is first, the baseline's own gap is 0
        bool sorted = table.Count == 2 && table[0].Skill == "Meteor" && table[0].Gaps[1] < table[1].Gaps[1] && table[0].Gaps[0] == 0 && table[0].PerEntry[1] is null;

        string fightJson = "{\"encounter\":{\"id\":17717,\"bossName\":\"Gelcos\",\"durationSeconds\":117.15},\"roster\":[{\"participantId\":196188,\"playerName\":\"Ponce\",\"className\":\"Gladiator\",\"totalDamage\":2092842,\"combatPower\":117663}]}";
        string partJson = "{\"damageSkills\":[{\"skillName\":\"Overhead Slam\",\"hits\":189,\"critHits\":189,\"totalDamage\":1110642,\"minHit\":3724,\"maxHit\":16133}]}";
        CompareModel.OnlineFight fight = CompareModel.ParseFight(fightJson);
        CompareEntry online = CompareModel.ParseParticipant(partJson, fight, fight.Players[0]);
        bool read = fight.EncounterId == 17717 && fight.Players.Count == 1 && online.Online && online.Marks is null && online.CombatPower == 117_663
            && online.Skills[0].Total == 1_110_642 && online.Skills[0].Uses == -1 && Math.Abs(online.Dps - 1_110_642 / 117.15) < 0.01;
        bool urls = CompareModel.EncounterIdOf("https://aiondps.com/encounters/17717") == 17717 && CompareModel.EncounterIdOf("17717") == 17717
            && CompareModel.EncounterIdOf("https://aiondps.com/de/encounters/17717?x=1") == 17717 && CompareModel.EncounterIdOf("https://evil.example/encounters/5") is null
            && CompareModel.EncounterIdOf("hello") is null;
        Console.WriteLine($"  -> a player's skills, damage, combat power and cast notices come from a session: {local}");
        Console.WriteLine($"  -> the table puts the skills that make up the gap to the baseline first: {sorted}");
        Console.WriteLine($"  -> an online fight and player are read from the site's answers, and only our own addresses are taken: {read && urls}");
        return local && sorted && read && urls;
    }

    private static bool RunFileRoundTrip(DateTime t0)
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aiondps-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var live = new SessionRecorder(_ => SessionKind.Instance)
            {
                Identify = id => id == 1 ? ("Aahz", "Gladiator") : (null, null),
                IdentifyPlayer = id => id == 1 ? new PlayerMeta(24_900_123, 1303, 169_620) : null,
            };
            live.Enter(new ZoneChange(t0, Krao));
            live.Feed(CombatBatch.DamageOnly(new[] { new DamageEvent(t0.AddSeconds(5), 1, 100, 900, false, "Cleave", true) }));
            live.Enter(new ZoneChange(t0.AddMinutes(30), Verteron));
            SessionInfo stay = live.Sessions[0];
            string path = System.IO.Path.Combine(dir, "stay" + SessionFile.Extension);
            SessionFile.SaveStay(path, stay);

            SessionFile.LoadedSession file = SessionFile.Load(path);
            var other = new SessionRecorder(_ => SessionKind.Map);
            other.Enter(new ZoneChange(t0.AddHours(5), Verteron));
            SessionInfo imported = other.Import(file.Stay!.ZoneId, file.Stay.Kind, file.Stay.Start, file.Stay.End);
            imported.Data.IngestEvents(file.Events);
            foreach ((int id, string cls) in file.Stay.Classes)
            {
                imported.Classes[id] = cls;
            }

            bool same = file.Stay.ZoneId == Krao && file.Stay.Kind == SessionKind.Instance && file.Stay.Start == t0 && file.Stay.End == t0.AddMinutes(30)
                && file.Events.Count == 1 && file.Events[0].IsCritical && file.Names.GetValueOrDefault(1) == "Aahz" && file.Stay.Classes.GetValueOrDefault(1) == "Gladiator";
            bool placed = other.Sessions.Count == 2 && other.Sessions[0] == imported && other.Current is { ZoneId: Verteron } && imported.IsSaved && imported.FromFile;
            bool outOfAll = other.AllEvents().Count == 0;
            bool playerKept = file.Players is { } back0 && back0.GetValueOrDefault(1) == new PlayerMeta(24_900_123, 1303, 169_620) && back0.Count == 1;
            Console.WriteLine($"  -> character id, server and combat power of the players come back with the file: {playerKept}");
            Console.WriteLine($"  -> a saved stay reads back whole (zone, times, hits, names, classes): {same}");
            Console.WriteLine($"  -> a loaded stay goes in before the open one, is kept, and stays out of \"all\": {placed && outOfAll}");

            // with the extras (rotation, effects, stagger) or without, as the user chose
            var extras = new FightExtras(new[] { new SkillUse(t0.AddSeconds(5), 1, 11800007) }, Array.Empty<BuffEvent>(), Array.Empty<StaggerEvent>(), new Dictionary<int, string> { [1] = "Aahz" });
            string withPath = System.IO.Path.Combine(dir, "with" + SessionFile.Extension), withoutPath = System.IO.Path.Combine(dir, "without" + SessionFile.Extension);
            SessionFile.SaveStay(withPath, stay, extras);
            SessionFile.SaveStay(withoutPath, stay, null);
            SessionFile.LoadedSession withFile = SessionFile.Load(withPath), withoutFile = SessionFile.Load(withoutPath);
            bool extrasKept = withFile.Stay!.Extras is { } back && back.Uses.Count == 1 && back.Uses[0].SkillId == 11800007 && back.Names[1] == "Aahz" && withoutFile.Stay!.Extras is null
                && new System.IO.FileInfo(withPath).Length > new System.IO.FileInfo(withoutPath).Length;
            Console.WriteLine($"  -> saved with the extras they come back with the file; without them the file has none and is smaller: {extrasKept}");

            // what the first version of the file lost: the skill id, the tick and estimate flags, the avoids, the cast notice's own bytes, the meta block
            var rich = new SessionRecorder(_ => SessionKind.Instance)
            {
                Identify = id => id == 1 ? ("Aahz", "Gladiator") : (null, null),
                CountersSource = () => new CaptureCounters(1000, 3, 1, 0, 0),
                BuildSource = () => new BuildSnapshot(t0, t0.AddMinutes(-1), 2, Array.Empty<Aion2.Aion2PresetInfo>(), new[] { new Aion2.Aion2EquippedItem(1, 100001, 5) }, Array.Empty<Aion2.Aion2DaevanionBoard>(), null, null),
            };
            rich.Enter(new ZoneChange(t0, Krao));
            rich.Feed(new CombatBatch(
                new[]
                {
                    new DamageEvent(t0.AddSeconds(5), 1, 100, 900, false, "Cleave", false, IsTick: false, SkillId: 15040047, Estimated: true),
                    new DamageEvent(t0.AddSeconds(6), 1, 100, 40, false, "Burn", false, IsTick: true, SkillId: 15040048),
                },
                new[] { new AionDPS.Combat.Sources.AvoidEvent(t0.AddSeconds(7), 100, 1, AionDPS.Combat.Sources.AvoidKind.Dodge) },
                Array.Empty<AionDPS.Combat.Sources.KillEvent>()));
            rich.Enter(new ZoneChange(t0.AddMinutes(10), Verteron));
            var richExtras = new FightExtras(new[] { new SkillUse(t0.AddSeconds(5), 1, 15040047, Target: 100, Seq: 11, Aim: 3) }, Array.Empty<BuffEvent>(), Array.Empty<StaggerEvent>(), new Dictionary<int, string> { [1] = "Aahz" });
            string richPath = System.IO.Path.Combine(dir, "rich" + SessionFile.Extension);
            SessionFile.SaveStay(richPath, rich.Sessions[0], richExtras);
            SessionFile.LoadedSession richFile = SessionFile.Load(richPath);
            bool fieldsKept = richFile.Events.Count == 2
                && richFile.Events[0].SkillId == 15040047 && richFile.Events[0].Estimated && !richFile.Events[0].IsTick
                && richFile.Events[1].SkillId == 15040048 && richFile.Events[1].IsTick && !richFile.Events[1].Estimated
                && richFile.Avoids.Count == 1 && richFile.Avoids[0].TargetObjectId == 1
                && richFile.Stay!.Extras!.Uses[0] is { Target: 100, Seq: 11, Aim: 3 };
            string json;
            using (var input = System.IO.File.OpenRead(richPath))
            using (var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress))
            using (var reader = new System.IO.StreamReader(gzip))
            {
                json = reader.ReadToEnd();
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            System.Text.Json.JsonElement meta = doc.RootElement.GetProperty("Meta");
            bool metaKept = meta.GetProperty("Client").GetString() is { Length: > 0 }
                && meta.GetProperty("Streams").GetProperty("skillUses").GetInt32() == 1
                && meta.GetProperty("Streams").GetProperty("avoids").GetInt32() == 1
                && meta.GetProperty("Capture").GetProperty("Dropped").GetInt64() == 0
                && meta.GetProperty("BuildStart").GetProperty("Preset").GetInt32() == 2
                && meta.GetProperty("BuildEnd").GetProperty("Gear")[0].GetProperty("ItemId").GetInt32() == 100001
                && meta.GetProperty("Notes").GetArrayLength() > 0;
            Console.WriteLine($"  -> skill id, tick and estimate flags, avoids and the cast notice's own bytes come back with the file: {fieldsKept}");
            Console.WriteLine($"  -> the file says which streams it holds, the capture's losses and the setup at start and end: {metaKept}");

            // one player under two combat ids: the log of the fight follows the merge
            var state = new Aion2.Aion2CombatState();
            state.NoteSkillUse(t0, 77, 11010340, 100, 5, 3);
            state.MergeIds(77, 88);
            FightExtras slice = state.Slice(t0.AddSeconds(-1), t0.AddSeconds(1), id => "Aahz");
            bool merged = slice.Uses.Count == 1 && slice.Uses[0].Actor == 88 && slice.Uses[0].Seq == 5;
            Console.WriteLine($"  -> after the game changes a player's combat id the skill uses are under the new id as well: {merged}");
            return same && playerKept && placed && outOfAll && extrasKept && fieldsKept && metaKept && merged;
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { }
        }
    }

    public static bool Run()
    {
        Console.WriteLine("[selftest] Sessions:");
        var t0 = new DateTime(2026, 10, 13, 11, 0, 0);
        var recorder = new SessionRecorder(zone => Aion2Zones.IsInstance(zone) ? SessionKind.Instance : SessionKind.Map);
        DamageEvent Hit(int seconds, int source = 1) => new(t0.AddSeconds(seconds), source, 100, 500, IsHeal: false, Skill: "Cleave");

        // Verteron for ten minutes, a batch that straddles the move into the Krao Cave, then the way back.
        recorder.Feed(new CombatBatch(new[] { Hit(0) }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(), new[] { new ZoneChange(t0, Verteron) }));
        recorder.Feed(CombatBatch.DamageOnly(new[] { Hit(60), Hit(120) }));
        recorder.Feed(new CombatBatch(
            new[] { Hit(590), Hit(601), Hit(610) }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(),
            new[] { new ZoneChange(t0.AddSeconds(600), Krao) }));
        recorder.Feed(new CombatBatch(
            new[] { Hit(1200) }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(),
            new[] { new ZoneChange(t0.AddSeconds(1190), Verteron) }));

        // Back in Verteron after the cave: the earlier Verteron stay is picked up again (the list keeps the current one last).
        var sessions = recorder.Sessions;
        bool two = sessions.Count == 2;
        bool kinds = two && sessions[0].Kind == SessionKind.Instance && sessions[1].Kind == SessionKind.Map && sessions[1].ZoneId == Verteron;
        bool split = two && sessions[0].Data.Events.Count == 2 && sessions[1].Data.Events.Count == 5;
        bool ends = two && sessions[0].End == t0.AddSeconds(1190) && sessions[1].IsOpen && sessions[1].Start == t0 && sessions[1].Away == TimeSpan.FromSeconds(590);
        Console.WriteLine($"  -> map, instance, back to the map picks the map stay up again: {two && kinds}");
        Console.WriteLine($"  -> events before a zone change stay with the old stay, the way back adds to the first (2 / 5): {split}");
        Console.WriteLine($"  -> the instance gets its end time, the map stay is open again and counts the time away: {ends}");

        // The first visit of an instance after another one is a stay of its own; two instance runs with a map between are two stays.
        var runs = new SessionRecorder(zone => Aion2Zones.IsInstance(zone) ? SessionKind.Instance : SessionKind.Map);
        runs.Feed(new CombatBatch(new[] { Hit(0) }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(), new[] { new ZoneChange(t0, Verteron) }));
        runs.Feed(new CombatBatch(new[] { Hit(700) }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(), new[] { new ZoneChange(t0.AddSeconds(600), Krao) }));
        runs.Feed(new CombatBatch(new[] { Hit(1300) }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(), new[] { new ZoneChange(t0.AddSeconds(1200), Verteron) }));
        runs.Feed(new CombatBatch(new[] { Hit(1900) }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(), new[] { new ZoneChange(t0.AddSeconds(1800), Krao) }));
        bool twoRuns = runs.Sessions.Count == 3 && runs.Sessions[0].Kind == SessionKind.Instance && runs.Sessions[1].ZoneId == Verteron && runs.Sessions[2].Kind == SessionKind.Instance && runs.Sessions[2].IsOpen;
        Console.WriteLine($"  -> a second run is a stay of its own, the map stay stays one: {twoRuns}");

        // A map in between (not an instance) keeps the stays apart.
        var detour = new SessionRecorder(zone => Aion2Zones.IsInstance(zone) ? SessionKind.Instance : SessionKind.Map);
        detour.Feed(new CombatBatch(new[] { Hit(0) }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(), new[] { new ZoneChange(t0, Verteron) }));
        detour.Feed(new CombatBatch(new[] { Hit(700) }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(), new[] { new ZoneChange(t0.AddSeconds(600), 20) }));
        detour.Feed(new CombatBatch(new[] { Hit(1300) }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(), new[] { new ZoneChange(t0.AddSeconds(1200), Verteron) }));
        bool apart = detour.Sessions.Count == 3;
        Console.WriteLine($"  -> another map in between is no instance run: the stays stay apart: {apart}");

        // A pass through a hall: no hit and under two minutes - no entry. The same stay with a hit is kept.
        var hall = new SessionRecorder(_ => SessionKind.Map);
        hall.Enter(new ZoneChange(t0, 902));
        hall.Enter(new ZoneChange(t0.AddSeconds(40), Verteron));
        bool hallGone = hall.Sessions.Count == 1 && hall.Sessions[0].ZoneId == Verteron;
        Console.WriteLine($"  -> an empty stay of 40 s leaves no entry: {hallGone}");

        // The same zone twice in a row (a reconnect) is no new stay.
        var again = new SessionRecorder(_ => SessionKind.Map);
        again.Enter(new ZoneChange(t0, Verteron));
        again.Enter(new ZoneChange(t0.AddSeconds(5), Verteron));
        bool oneStay = again.Sessions.Count == 1;
        Console.WriteLine($"  -> the same zone again does not start a new stay: {oneStay}");

        // An instance entered straight from the run before (the decoder marks it a restart) is a stay of its own; Krao is an instance, Verteron is not.
        var rerun = new SessionRecorder(zone => Aion2Zones.IsInstance(zone) ? SessionKind.Instance : SessionKind.Map);
        rerun.Enter(new ZoneChange(t0, 600022));
        rerun.Feed(CombatBatch.DamageOnly(new[] { Hit(60) }));
        rerun.Enter(new ZoneChange(t0.AddMinutes(20), 600022, Restart: true));
        rerun.Feed(CombatBatch.DamageOnly(new[] { Hit(1300) }));
        bool restarted = rerun.Sessions.Count == 2 && rerun.Sessions[0].End == t0.AddMinutes(20) && rerun.Sessions[0].Data.Events.Count == 1 && rerun.Sessions[1].IsOpen && rerun.Sessions[1].Data.Events.Count == 1;
        Console.WriteLine($"  -> the same instance again straight from the run before is a new stay: {restarted}");

        // Hits before any zone frame (the meter started mid-map) go to an unknown-map stay.
        var late = new SessionRecorder(_ => SessionKind.Map);
        late.Feed(CombatBatch.DamageOnly(new[] { Hit(0) }));
        bool unknown = late.Sessions.Count == 1 && late.Sessions[0].ZoneId == 0;
        Console.WriteLine($"  -> hits before the first zone frame open an unknown-map stay: {unknown}");

        // A map change gives the player a new combat id: the "all" view sees one player.
        recorder.MergeIds(1, 2);
        bool merged = recorder.AllEvents().All(e => e.SourceObjectId == 2);
        Console.WriteLine($"  -> a combat id change joins the player in every stay: {merged}");

        // Names are kept with the session, and a placeholder is replaced once the real name is known.
        string name = "Gladiator #7";
        var named = new SessionRecorder(_ => SessionKind.Map) { Identify = id => (id == 1 ? name : null, null) };
        named.Feed(CombatBatch.DamageOnly(new[] { Hit(0) }));
        name = "Aahz";
        named.RefreshNames();
        bool renamed = named.Sessions[0].Names.GetValueOrDefault(1) == "Aahz";
        Console.WriteLine($"  -> a placeholder name is replaced by the real one: {renamed}");

        // Memory cap: the oldest finished stay goes first.
        var small = new SessionRecorder(_ => SessionKind.Map) { MaxEvents = 5 };
        small.Enter(new ZoneChange(t0, 1));
        small.Feed(CombatBatch.DamageOnly(Enumerable.Range(0, 4).Select(i => Hit(i)).ToList()));
        small.Enter(new ZoneChange(t0.AddMinutes(5), 2));
        small.Feed(CombatBatch.DamageOnly(Enumerable.Range(400, 4).Select(i => Hit(i)).ToList()));
        bool capped = small.Sessions.Count == 1 && small.Sessions[0].ZoneId == 2;
        Console.WriteLine($"  -> over the event cap the oldest finished stay is dropped: {capped}");

        // Save one stay to a file and read it back: events, shields, names, classes, zone and times survive; the file is not part of "all".
        bool roundTrip = RunFileRoundTrip(t0);
        bool compare = RunCompare(t0);
        bool plausibility = RunPlausibility(t0);
        bool rageSignal = RunRageSignal(t0);
        bool instance = Aion2Zones.IsInstance(Krao) && !Aion2Zones.IsInstance(Verteron);
        Console.WriteLine($"  -> 600001 is an instance, 1010 is not: {instance}");

        return restarted && two && kinds && twoRuns && apart && split && ends && hallGone && oneStay && unknown && merged && renamed && capped && roundTrip && compare && plausibility && rageSignal && instance;
    }
}
