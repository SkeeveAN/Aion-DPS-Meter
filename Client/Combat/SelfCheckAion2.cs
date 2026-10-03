using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat.Sources;
using AionDPS.Data;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// Self-checks for the combat-source seam and the Aion 2 machinery that can be verified without
/// a running game: the Chat.log source must behave exactly like the parser it wraps, TCP
/// reassembly must survive reordering and retransmission, and the data-driven decoder must turn a
/// synthetic protocol description into the right events. Run from SelfCheck.Run().
/// </summary>
public static class SelfCheckAion2
{
    public static bool Run()
    {
        bool ok = true;
        ok &= RunCombatSourceSeamScenario();
        ok &= RunTcpReassemblerScenario();
        ok &= RunAion2ProtocolScenario();
        ok &= RunAion2RealCaptureScenario();
        ok &= RunAion2BundleScenario();
        ok &= RunAion2NoDamageFrameScenario();
        ok &= RunAion2SummonOwnerScenario();
        ok &= RunAion2NamedSummonScenario();
        ok &= RunAion2GuildScenario();
        ok &= RunAion2ShieldIsNoSummonScenario();
        ok &= RunAion2TwoSorcerersScenario();
        ok &= RunAion2DotTickScenario();
        ok &= RunAion2HitPointsScenario();
        ok &= RunHpCheckScenario();
        ok &= RunAion2SoloLocalNameScenario();
        ok &= RunAion2NamesScenario();
        ok &= RunAion2MidStreamScenario();
        ok &= RunAion2CharacterScenario();
        ok &= RunAion2LoginListsScenario();
        ok &= RunAion2CharacterStoreScenario();
        ok &= RunAion2SeenProfileScenario();
        ok &= RunRestartSnapshotScenario();
        ok &= RunAion2BossSpawnScenario();
        ok &= RunClassCatalogScenario();
        ok &= RunSettingsMigrationScenario();
        ok &= RunHotkeyBindingScenario();
        return ok;
    }

    private static bool RunCombatSourceSeamScenario()
    {
        Console.WriteLine("[selftest] Combat-source seam (FakeCombatSource):");
        var fake = new FakeCombatSource();
        int you = fake.Entities.LocalPlayerId;
        fake.Enqueue(new DamageEvent(DateTime.UtcNow, you, fake.IdOf("Dummy"), 10, IsHeal: false));
        bool fakePausedDiscards = fake.Poll(true).IsEmpty && fake.Poll(false).IsEmpty;
        fake.Enqueue(new DamageEvent(DateTime.UtcNow, you, fake.IdOf("Dummy"), 10, IsHeal: false));
        bool fakeDelivers = fake.Poll(false).Damage.Count == 1 && fake.Entities.IsLocalPlayer(you);
        Console.WriteLine($"  -> FakeCombatSource honours pause and delivers queued batches: {fakePausedDiscards && fakeDelivers}");
        return fakePausedDiscards && fakeDelivers;
    }

    private static readonly FrameLayout TestLayout = new(LengthOffset: 0, LengthSize: 2, LittleEndian: true, LengthIncludesHeader: true, HeaderSize: 4, OpcodeOffset: 2, OpcodeSize: 2, MaxFrameLength: 4096);

    private static byte[] Frame(ushort opcode, params byte[] body)
    {
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), opcode);
        body.CopyTo(frame, 4);
        return frame;
    }

    private static TcpSegment Segment(uint seq, byte[] bytes) =>
        new(new DateTime(2026, 9, 22, 20, 0, 0, DateTimeKind.Utc), "10.0.0.1:7777", "192.168.0.2:50000", seq, bytes, FromServer: true);

    /// <summary>
    /// Real frames from the 2026-09-30 EU capture (game server port 13328), each annotated with what
    /// the in-game combat log showed for that hit. Goes through the shipped opcodes.json, the
    /// varint framing and the real decoder - the regression test for the calibration.
    /// </summary>
    private static bool RunAion2RealCaptureScenario()
    {
        Console.WriteLine("[selftest] Aion 2 real capture (varint framing + damage frame, shipped opcodes.json):");
        // (frame body incl. opcode, expected amount, expected crit, expected heal, expected skill)
        (string Hex, long Amount, bool Crit, bool Heal, string Skill)[] frames =
        {
            ("04388484030600be08fe26a8004f02000002433baf4101000000ba4ffd020100", 381, false, false, "Keen Strike"),
            ("04388484032600be08fe26a8005703000002433baf4101000000ba4fcc0401020100", 588, true, false, "Keen Strike"),
            ("04388484032600be080e4ea8004b02000002837dbe4101000000ba4fb00301010100", 432, false, false, "Rupture Strike"),
            ("0438be080400be0857fcb2004a020792ea4501000000ba4f410100", 65, false, true, "Blood Absorption"),
        };

        var wire = new List<byte>();
        foreach (var f in frames)
        {
            byte[] body = Convert.FromHexString(f.Hex);
            // Record = LEB128(length) + body, where length = body.Length + 4 (total = length + prefix - 4).
            int length = body.Length + 4;
            while (length >= 0x80)
            {
                wire.Add((byte)(length & 0x7f | 0x80));
                length >>= 7;
            }

            wire.Add((byte)length);
            wire.AddRange(body);
        }

        Aion2Protocol protocol = Aion2Protocol.Load();
        using var source = new Aion2PacketCombatSource(protocol);
        source.Ingest(Segment(1000, wire.ToArray()));
        CombatBatch batch = source.Poll(false);

        bool calibrated = protocol.IsCalibrated && protocol.ServerPorts.Contains(13328);
        bool count = batch.Damage.Count == frames.Length;
        bool all = count;
        for (int i = 0; count && i < frames.Length; i++)
        {
            DamageEvent ev = batch.Damage[i];
            bool match = ev.Amount == frames[i].Amount && ev.IsCritical == frames[i].Crit && ev.IsHeal == frames[i].Heal && ev.Skill == frames[i].Skill;
            Console.WriteLine($"  -> frame {i}: {ev.Skill} {ev.Amount}{(ev.IsCritical ? " crit" : "")}{(ev.IsHeal ? " heal" : "")} (expected {frames[i].Skill} {frames[i].Amount}): {match}");
            all &= match;
        }

        bool classNamed = count && source.Entities.NameFor(batch.Damage[0].SourceObjectId) is string name && name.StartsWith("Player #", StringComparison.Ordinal)
            && source.Entities is Aion2EntityDirectory dir && dir.ClassOf(batch.Damage[0].SourceObjectId) == "Gladiator";
        Console.WriteLine($"  -> calibrated, port 13328 known: {calibrated}; {frames.Length} frames decoded: {count}");
        Console.WriteLine($"  -> an unnamed player is shown as \"Player #id\" (the class is the icon's job) and its class is Gladiator: {classNamed}");
        return calibrated && all && classNamed;
    }

    /// <summary>A real bundle frame (opcode 0xFFFF, LZ4 block) from the same capture: twenty inner
    /// frames of which ten use the damage opcode and four are real hits (the other six are the
    /// no-damage companion frames, see <see cref="RunAion2NoDamageFrameScenario"/>). Before bundles
    /// were unpacked those hits were silently missing - roughly 40 % of all damage frames in a real
    /// session.</summary>
    private static bool RunAion2BundleScenario()
    {
        Console.WriteLine("[selftest] Aion 2 bundle frame (LZ4) from a real capture:");
        const string bundleHex = "ffff08020000ff13200438b81c0400ec1b5e7d14011a02c3f8006c01000000c24e87080100200438f81b1d000150c40701001e1d0015003a0015c43a000c1b002700c51b004f200438ea53000212cd53001fea530007071b000c53001fe153000212b353001fe1530007071b00095300f100332a38f81b011335ade5cc0ae02e0001006065a020f4a0019e00f0090c5e7d1401004f576fc60aa7724600c021440d0e92f81b012e0090160538f81b09ec1b354a0120c00b2a0080332a38ea1b0113391f000f4d001415ea4d0010ea4d0010394d00118d77004f332a38e19a001c15e14d0010e14d00019a0011854d00b00e0036857120f4a0010000";
        long[] expected = { 1031, 964, 973, 947 };

        // LZ4 sanity: a literal-only block round-trips, a bad back-reference is rejected.
        bool lz4Literal = Aion2Lz4.TryDecompress(new byte[] { 0x30, 1, 2, 3 }, 3, out byte[] lit) && lit.SequenceEqual(new byte[] { 1, 2, 3 });
        bool lz4Rejects = !Aion2Lz4.TryDecompress(new byte[] { 0x11, 9, 0x05, 0x00 }, 10, out _);

        byte[] body = Convert.FromHexString(bundleHex);
        var wire = new List<byte>();
        int length = body.Length + 4;
        while (length >= 0x80)
        {
            wire.Add((byte)(length & 0x7f | 0x80));
            length >>= 7;
        }

        wire.Add((byte)length);
        wire.AddRange(body);

        Aion2Protocol protocol = Aion2Protocol.Load();
        using var source = new Aion2PacketCombatSource(protocol);
        source.Ingest(Segment(9000, wire.ToArray()));
        CombatBatch batch = source.Poll(false);

        bool bundleKnown = protocol.BundleOpcode == 0xFFFF;
        bool amounts = batch.Damage.Select(e => e.Amount).SequenceEqual(expected);
        bool oneActor = batch.Damage.Select(e => e.SourceObjectId).Distinct().Count() == 1;
        Console.WriteLine($"  -> LZ4 literal block decodes / bad back-reference is rejected: {lz4Literal && lz4Rejects}");
        Console.WriteLine($"  -> shipped opcodes.json names the bundle opcode: {bundleKnown}");
        Console.WriteLine($"  -> all {expected.Length} damage frames inside the bundle are decoded with their real amounts: {amounts}");
        Console.WriteLine($"  -> they belong to one actor: {oneActor}");
        return lz4Literal && lz4Rejects && bundleKnown && amounts && oneActor;
    }

    /// <summary>
    /// Damage-opcode frames whose first flag byte lacks bit 0x04 carry no damage block - only a 1-4
    /// "amount" that is really a counter. Real frames from a Krao Cave capture (2026-10-02, Ultimate
    /// Berk, a Spiritmaster): each real hit is followed by one or two such frames, and counting them
    /// doubled the hit counts (Combustion 68 instead of the 35 the in-game combat analyzer showed,
    /// Elemental Fusion 8 instead of 4) and diluted every crit rate.
    /// </summary>
    private static bool RunAion2NoDamageFrameScenario()
    {
        Console.WriteLine("[selftest] Aion 2 damage-opcode frames without a damage block (real Krao Cave frames):");
        (string Hex, long? Amount, bool Crit, string Skill)[] frames =
        {
            ("0438E1AD010600C522E0B7F80011020000028BD3276101000000A85ADA3E0100", 8026, false, "Elemental Fusion"),
            ("0438C5220000C522E2B7F8000F0253D4276101000000A85A0100", null, false, "Elemental Fusion"),
            ("0438C5220000C522E0B7F800110295D3276102000000A85A0200", null, false, "Elemental Fusion"),
            ("0438A5C7012600C522102DF90036030000014B9A556101000000A85A8D1701A7020100", 2957, true, "Dimensional Control"),
            ("0438E1AD010000C522102DF90014024C9A556101000000A85A0100", null, false, "Dimensional Control"),
            ("0438A9A6010400C522E046F6009D038BAF336001000000A85AA50F0100", 1957, true, "Jointstrike: Curse"),
            ("0438A9A6010000C522E046F6009D028DAF336001000000A85A0100", null, false, "Jointstrike: Curse"),
            ("0438A9A6010000C52240C0F40072020D199B5F01000000A85A0100", null, false, "Combustion"),
            ("043885AE010400D92440C0F40009020B199B5F010000009656E7040100", 615, false, "Combustion"),
        };

        var wire = new List<byte>();
        foreach (var f in frames)
        {
            byte[] body = Convert.FromHexString(f.Hex);
            int length = body.Length + 4;
            while (length >= 0x80)
            {
                wire.Add((byte)(length & 0x7f | 0x80));
                length >>= 7;
            }

            wire.Add((byte)length);
            wire.AddRange(body);
        }

        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        source.Ingest(Segment(5000, wire.ToArray()));
        CombatBatch batch = source.Poll(false);

        var hits = frames.Where(f => f.Amount is not null).ToList();
        bool onlyHits = batch.Damage.Count == hits.Count;
        bool all = onlyHits;
        for (int i = 0; onlyHits && i < hits.Count; i++)
        {
            DamageEvent ev = batch.Damage[i];
            bool match = ev.Amount == hits[i].Amount && ev.IsCritical == hits[i].Crit && ev.Skill == hits[i].Skill && !ev.IsHeal;
            Console.WriteLine($"  -> hit {i}: {ev.Skill} {ev.Amount}{(ev.IsCritical ? " crit" : "")} (expected {hits[i].Skill} {hits[i].Amount}): {match}");
            all &= match;
        }

        Console.WriteLine($"  -> {frames.Length} frames, only the {hits.Count} with a damage block become hits ({batch.Damage.Count}): {onlyHits}");
        return all;
    }

    /// <summary>
    /// Summoned spirits get a new entity id at every summon; their spawn frame names the summoner.
    /// Real frames from a Canyon Urugugu capture (2026-10-02, Divine Auldor) with three
    /// Spiritmasters in the party: a Water Spirit of Destinyy (id 10894) and a Fire Spirit of the
    /// local player (id 3279). Their hits must land on their summoners - the in-game combat analyzer
    /// lists them under the player, and with this the local player's 320 hits / 48 % crit / per-skill
    /// totals on Auldor match it exactly. A monster names itself as owner, which also clears a
    /// summon whose id the server hands out again.
    /// </summary>
    private static bool RunAion2SummonOwnerScenario()
    {
        Console.WriteLine("[selftest] Aion 2 summoned spirits credited to their summoner (real Canyon Urugugu frames):");
        const string waterSpiritSpawn = "4136A5AE011F1000C18E2C0000020028A04500788245008031440E86B1437AFC01DF28DF2819070000190700000000000000000000000000005892010064000000F04902000100000000000000A08601000000000090D00300010111010F329A09FFFFFFFFFFFFFFFF8075D52ABB0300008E5509022B5A9B45CADA7B4590C525440702068E2A000002CD00C4040000D0003D0100001E000000E31D030000";
        const string fireSpiritSpawn = "4136B7DD011F1000B28E2C00400200E8E44500F06B4500806C443AAF7D4366B40198639863630B0000630B000000000000000000000000000060B2010064000000F04902000100000000000000A08601000000000000E20400010101110144AA9809FFFFFFFFFFFFFFFF8075D52ABB030000CF190E02FB71E7451296784571BE6044070206CF0C000002CD005A000000D000300100002D000000DD1D030000";
        const string waterSpiritHit = "0438EC91010600A5AE01BB86010002020000024FA09800010000009E5A690100";
        const string waterSpiritSpawnHeal = "0438A5AE010400A5AE01343F030101025CB04465010000009E5AB0DB060100";
        const string fireSpiritCrit = "0438EC91012600B7DD01AE8601000503000002D99A980001000000C0528B0501410100";
        // The same spawn frame with the owner field naming the entity itself: an ordinary monster.
        string reusedAsMonster = waterSpiritSpawn.Replace("8075D52ABB0300008E55", "8075D52ABB030000A5AE01", StringComparison.Ordinal);

        static byte[] Wire(params string[] hexes)
        {
            var wire = new List<byte>();
            foreach (string hex in hexes)
            {
                byte[] body = Convert.FromHexString(hex);
                int length = body.Length + 4;
                while (length >= 0x80)
                {
                    wire.Add((byte)(length & 0x7f | 0x80));
                    length >>= 7;
                }

                wire.Add((byte)length);
                wire.AddRange(body);
            }

            return wire.ToArray();
        }

        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        var dir = (Aion2EntityDirectory)source.Entities;
        byte[] first = Wire(waterSpiritSpawn, fireSpiritSpawn, waterSpiritHit, waterSpiritSpawnHeal, fireSpiritCrit);
        source.Ingest(Segment(7000, first));
        CombatBatch batch = source.Poll(false);

        bool owners = dir.SummonOwnerOf(22309) == 10894 && dir.SummonOwnerOf(28343) == 3279;
        bool waterToDestinyy = batch.Damage.Count == 2 && batch.Damage[0].SourceObjectId == 10894 && batch.Damage[0].Amount == 105 && !batch.Damage[0].IsHeal;
        bool spawnHealDropped = batch.Damage.Count == 2 && !batch.Damage.Any(d => d.IsHeal);
        bool fireToLocal = batch.Damage.Count == 2 && batch.Damage[1].SourceObjectId == 3279 && batch.Damage[1].Amount == 651 && batch.Damage[1].IsCritical;

        source.Ingest(Segment(7000 + (uint)first.Length, Wire(reusedAsMonster, waterSpiritHit)));
        CombatBatch after = source.Poll(false);
        bool reuseCleared = dir.SummonOwnerOf(22309) is null && after.Damage.Count == 1 && after.Damage[0].SourceObjectId == 22309;

        Console.WriteLine($"  -> spawn frames name the summoners (Destinyy 10894, local 3279): {owners}");
        Console.WriteLine($"  -> the Water Spirit's 105 is credited to Destinyy: {waterToDestinyy}");
        Console.WriteLine($"  -> the spirit's own spawn heal is no heal of its summoner's: {spawnHealDropped}");
        Console.WriteLine($"  -> the Fire Spirit's 651 crit is credited to the local player: {fireToLocal}");
        Console.WriteLine($"  -> the id respawning as a monster is no longer anybody's summon: {reuseCleared}");
        return owners && waterToDestinyy && spawnHealDropped && fireToLocal && reuseCleared;
    }

    /// <summary>
    /// Damage-over-time ticks (opcode 0x0538), real frames from the Krao Cave capture (2026-10-02,
    /// Ultimate Berk). Without them the local player's Jointstrike: Curse read 9,367 (its direct
    /// hits only) against the in-game analyzer's 21,547; its 23 ticks add exactly the missing
    /// 12,180. Only ticks with a damage amount and a skill id, dealt to someone else, count.
    /// </summary>
    private static bool RunAion2DotTickScenario()
    {
        Console.WriteLine("[selftest] Aion 2 damage-over-time ticks (real Krao Cave frames):");
        (string Hex, long? Amount, int Actor, int Target, string? Skill)[] frames =
        {
            ("0538D490020AC522108BAF3360A804E046F600", 552, 4421, 34900, "Jointstrike: Curse"),
            ("0538D490020AD924248BAF3360C901E046F600", 201, 4697, 34900, "Jointstrike: Curse"),
            ("0538C5220AD49002F402E71327076AE0771B00", 106, 34900, 4421, null),
            // A player's tick on itself (flags 0x0B: damage, heal and skill fields) - not damage dealt.
            ("0538833A0B833A91015FB2FC0BFF03AA01DDAF1E00", null, 0, 0, null),
            // Heal-only (0x09) and no-amount (0x08) ticks.
            ("0538D92409833A240BED006CBA02407D1401", null, 0, 0, null),
            // A Chanter's Recuperation on a party member: the same 0x0a shape as a damage tick, but
            // a heal - once counted as damage, it made the whole party read as enemies.
            ("0538D9240A833A490BED006C4D407D1401", null, 0, 0, null),
            // The same Recuperation as a direct hit on a member the meter has not seen cast yet.
            ("0438D9240400833A407D140105020BED006C010000008256B4020100", null, 0, 0, null),
            ("0538C52208C522EC0195D32761E2B7F800", null, 0, 0, null),
        };

        var wire = new List<byte>();
        foreach (var f in frames)
        {
            byte[] body = Convert.FromHexString(f.Hex);
            int length = body.Length + 4;
            while (length >= 0x80)
            {
                wire.Add((byte)(length & 0x7f | 0x80));
                length >>= 7;
            }

            wire.Add((byte)length);
            wire.AddRange(body);
        }

        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        source.Ingest(Segment(6000, wire.ToArray()));
        CombatBatch batch = source.Poll(false);

        var ticks = frames.Where(f => f.Amount is not null).ToList();
        bool recuperationIsHeal = batch.Damage.Count(e => e.IsHeal && !e.IsTick && e.Skill == "Recuperation" && e.Amount == 308) == 1;
        batch = batch with { Damage = batch.Damage.Where(e => !e.IsHeal).ToList() };
        bool onlyTicks = batch.Damage.Count == ticks.Count;
        bool all = onlyTicks;
        for (int i = 0; onlyTicks && i < ticks.Count; i++)
        {
            DamageEvent ev = batch.Damage[i];
            bool match = ev.Amount == ticks[i].Amount && ev.SourceObjectId == ticks[i].Actor && ev.TargetObjectId == ticks[i].Target && !ev.IsHeal && ev.IsTick
                && (ticks[i].Skill is null || ev.Skill == ticks[i].Skill);
            Console.WriteLine($"  -> tick {i}: {ev.SourceObjectId} -> {ev.TargetObjectId} {ev.Skill} {ev.Amount} (expected {ticks[i].Actor} -> {ticks[i].Target} {ticks[i].Amount}): {match}");
            all &= match;
        }

        // Jointstrike: Curse on Ultimate Berk as the in-game analyzer lists it: 6 hits, 21,547 damage
        // (here: two casts and three ticks) - the ticks raise the total, not the hit count.
        DateTime at = new(2026, 10, 2, 11, 52, 4);
        var curse = new List<DamageEvent>
        {
            new(at, 4421, 34900, 1739, false, "Jointstrike: Curse", IsCritical: true),
            new(at.AddSeconds(10), 4421, 34900, 1957, false, "Jointstrike: Curse"),
            new(at.AddSeconds(1), 4421, 34900, 552, false, "Jointstrike: Curse", IsTick: true),
            new(at.AddSeconds(2), 4421, 34900, 552, false, "Jointstrike: Curse", IsTick: true),
            new(at.AddSeconds(11), 4421, 34900, 528, false, "Jointstrike: Curse", IsTick: true),
        };
        SkillUsage usage = SkillBreakdown.For(curse).Single();
        bool ticksNotHits = usage.Hits == 2 && usage.CritHits == 1 && usage.Total == 1739 + 1957 + 552 + 552 + 528 && usage.Min == 1739 && usage.Max == 1957;

        Console.WriteLine($"  -> {frames.Length} tick frames, only the {ticks.Count} damage ticks dealt to another entity count ({batch.Damage.Count}): {onlyTicks}");
        Console.WriteLine($"  -> in the skill breakdown ticks add to the total but not to hits/crits/min/max: {ticksNotHits}");
        Console.WriteLine($"  -> a Recuperation hit on a member not yet seen casting is a heal, not damage: {recuperationIsHeal}");

        // Recuperation's own ticks, rebuilt from the values logged on the Krao Cave capture: announced 334 (0x09), then 83 per tick (0x0b) while
        // the amount field counts down what is left - each tick is a heal of 83.
        string[] hot =
        {
            "0538862D09833A240BED006CCE02407D1401",
            "0538862D0B833A240BED006CFB0153407D1401",
            "0538862D0B833A240BED006CA80153407D1401",
        };
        var hotWire = new List<byte>();
        foreach (string hex in hot)
        {
            byte[] body = Convert.FromHexString(hex);
            hotWire.Add((byte)(body.Length + 4));
            hotWire.AddRange(body);
        }

        using var hotSource = new Aion2PacketCombatSource(Aion2Protocol.Load());
        hotSource.Ingest(Segment(8000, hotWire.ToArray()));
        var hotEvents = hotSource.Poll(false).Damage;
        bool hotTicks = hotEvents.Count == 2 && hotEvents.All(e => e.IsHeal && e.IsTick && e.Amount == 83 && e.Skill == "Recuperation");
        Console.WriteLine($"  -> Recuperation's ticks are heals of 83 each, the announcement is no heal: {hotTicks}");
        return all && ticksNotHits && recuperationIsHeal && hotTicks;
    }

    /// <summary>
    /// The hit-point frame (0x008d), real frames from the solo Krao Cave run with a wipe (2026-10-02):
    /// Ultimate Berk worn down to 98,804, back to 123,000 under the same entity id for the retry,
    /// then hit again. Also a player's frame that mixes 4-byte stats with the 8-byte hit points, and
    /// a stats-only frame that carries no hit points at all.
    /// </summary>
    private static bool RunAion2HitPointsScenario()
    {
        Console.WriteLine("[selftest] Aion 2 hit points and boss reset (real Krao Cave frames):");
        DateTime t = new(2026, 10, 2, 12, 48, 45, DateTimeKind.Local);
        (double Seconds, string Hex)[] frames =
        {
            (0.0, "008DCE8D0102010008DD010000000000"),   // Berk 122,120
            (34.2, "008DCE8D01020100F481010000000000"),  // Berk 98,804 - the failed attempt ends
            (34.8, "008DCE8D0102010078E0010000000000"),  // Berk 123,000 - reset for the retry
            (56.8, "008DCE8D0102010093DD010000000000"),  // Berk 122,259
            (57.0, "008DCF19030201900A000003D89E01000100BD24000000000000"), // player 3279: 9,405 among 4-byte stats
            (57.1, "008DCF19030508CD0700000A60B201000BF04902000CA08601000D00E2040001075B1B000000000000"), // no current HP
        };

        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        var dir = (Aion2EntityDirectory)source.Entities;
        uint seq = 4000;
        foreach (var (seconds, hex) in frames)
        {
            byte[] body = Convert.FromHexString(hex);
            byte[] record = new byte[body.Length + 1];
            record[0] = (byte)(body.Length + 4);
            body.CopyTo(record, 1);
            source.Ingest(new TcpSegment(t.AddSeconds(seconds), "10.0.0.1:7777", "192.168.0.2:50000", seq, record, FromServer: true));
            seq += (uint)record.Length;
        }

        const int Berk = 18126;
        var berk = dir.HitPoints.SamplesAround(Berk, t, t.AddMinutes(1));
        bool readings = berk.Select(s => s.Hp).SequenceEqual(new long[] { 122_120, 98_804, 123_000, 122_259 });
        bool maximum = dir.HitPoints.HighestSeen(Berk) == 123_000;
        bool oneReset = dir.HitPoints.ResetsOf(Berk) is [var reset] && reset == t.AddSeconds(34.8);
        var player = dir.HitPoints.SamplesAround(3279, t, t.AddMinutes(1));
        bool playerHp = player.Count == 1 && player[0].Hp == 9_405;

        Console.WriteLine($"  -> Berk's readings 122,120 / 98,804 / 123,000 / 122,259: {readings}");
        Console.WriteLine($"  -> highest seen = its full health, 123,000: {maximum}");
        Console.WriteLine($"  -> exactly one reset, when it came back to full for the retry: {oneReset}");
        Console.WriteLine($"  -> a player's hit points behind 4-byte stats (9,405), a stats-only frame adds nothing: {playerHp}");
        return readings && maximum && oneReset && playerHp;
    }

    /// <summary>
    /// The guard against wrong totals, on Ultimate Berk's real hit points from the solo Krao Cave
    /// retry (2026-10-02): 122,259 -> 103,698 -> 0. Hits that add up to what it lost match; the same
    /// hits counted twice, or a missing hit, are called out; and both attempts summed together (the
    /// old merge) are flagged as more damage than the boss has hit points.
    /// </summary>
    private static bool RunHpCheckScenario()
    {
        Console.WriteLine("[selftest] HP check (counted damage against hit points lost, real Berk readings):");
        const int Berk = 18126, You = 11707;
        DateTime t = new(2026, 10, 2, 12, 49, 42, 518, DateTimeKind.Local);
        var readings = new List<(DateTime, long)> { (t, 122_259), (t.AddSeconds(2.2), 103_698), (t.AddSeconds(15), 0) };
        var hits = new List<DamageEvent>
        {
            new(t, You, Berk, 741, false),                  // already in the first reading
            new(t.AddSeconds(1), You, Berk, 12_487, false),
            new(t.AddSeconds(2.2), You, Berk, 6_074, false), // same packet as the second reading
            new(t.AddSeconds(15), You, Berk, 108_000, false), // the killing blow, overkill included
        };

        HpCheckResult? match = HpCheck.Evaluate(readings, hits, 123_000);
        HpCheckResult? doubled = HpCheck.Evaluate(readings, hits.Append(hits[1]).ToList(), 123_000);
        HpCheckResult? missing = HpCheck.Evaluate(readings, hits.Where((_, i) => i != 1).ToList(), 123_000);
        var merged = hits.Prepend(new DamageEvent(t.AddSeconds(-50), You, Berk, 24_196, false)).ToList();
        HpCheckResult? overFull = HpCheck.Evaluate(readings, merged, 123_000);

        bool matchOk = match is { Verdict: HpCheckVerdict.Match, Lost: 18_561, Counted: 18_561, Killed: true, OverFullHealth: false };
        bool doubledOk = doubled is { Verdict: HpCheckVerdict.Excess };
        bool missingOk = missing is { Verdict: HpCheckVerdict.Missing };
        bool overFullOk = overFull is { OverFullHealth: true };
        bool noReadings = HpCheck.Evaluate(new List<(DateTime, long)>(), hits, 0) is null;

        // A shield phase (Transcendent Bakarma, 2026-10-02 23:09): no reading for 15 s, 976 HP
        // lost while 252,502 damage was shown - the game counts it, the check sets it aside.
        var phaseReadings = new List<(DateTime, long)> { (t, 1_000_000), (t.AddSeconds(1), 990_000), (t.AddSeconds(16), 989_024), (t.AddSeconds(17), 979_024) };
        var phaseHits = new List<DamageEvent>
        {
            new(t, You, Berk, 500, false),
            new(t.AddSeconds(0.5), You, Berk, 10_000, false),
            new(t.AddSeconds(8), You, Berk, 252_502, false),
            new(t.AddSeconds(17), You, Berk, 10_000, false),
        };
        bool phaseOk = HpCheck.Evaluate(phaseReadings, phaseHits, 1_000_000) is { Verdict: HpCheckVerdict.Match, Shielded: 252_502, OverFullHealth: false };

        Console.WriteLine($"  -> 18,561 HP lost between two readings, 18,561 counted: match, kill seen: {matchOk}");
        Console.WriteLine($"  -> a hit counted twice reads as too much: {doubledOk}; a missed hit as too little: {missingOk}");
        Console.WriteLine($"  -> the failed attempt summed in: more damage than the boss's 123,000 HP: {overFullOk}");
        Console.WriteLine($"  -> no hit-point readings, no verdict: {noReadings}");
        Console.WriteLine($"  -> damage during a shield phase (hit points frozen) set aside: {phaseOk}");
        return matchOk && doubledOk && missingOk && overFullOk && noReadings && phaseOk;
    }

    /// <summary>Solo, the local player is only ever inferred (no party roster, no frame naming it):
    /// its row must carry the name from Settings rather than "Player #id".</summary>
    private static bool RunAion2SoloLocalNameScenario()
    {
        Console.WriteLine("[selftest] Aion 2 solo: the local player shows the configured name:");
        var dir = new Aion2EntityDirectory();
        dir.SetConfiguredLocalName("Boulenbouche");
        dir.NoteClass(6326, "Elementalist");
        bool named = dir.InferLocalPlayer() == 6326 && dir.NameFor(6326) == "Boulenbouche" && dir.IsLocalPlayer(6326);
        Console.WriteLine($"  -> the only unnamed caster, 6326, is shown as Boulenbouche: {named}");

        // In the open world strangers around are unnamed casters too, and one hitting more than you
        // made the guess fail. Detailed stats go to the local player alone: real frames from the solo
        // Krao Cave capture (entity 11707), after a stranger (9999) out-casts it ten to one.
        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        var crowd = (Aion2EntityDirectory)source.Entities;
        crowd.SetConfiguredLocalName("Boulenbouche");
        crowd.NoteClass(11707, "Elementalist");
        for (int i = 0; i < 10; i++)
        {
            crowd.NoteClass(9999, "Ranger");
        }

        bool strangerWasGuessed = crowd.InferLocalPlayer() == 9999;
        var wire = new List<byte>();
        foreach (string hex in new[] { "008DBB5B010103508B0100", "008DBB5B010101500B0000", "008DBB5B01010612DF0400",
                     "008DBB5B010103508B0100", "008DBB5B010101500B0000", "008DBB5B01010612DF0400" })
        {
            byte[] body = Convert.FromHexString(hex);
            wire.Add((byte)(body.Length + 4));
            wire.AddRange(body);
        }

        source.Ingest(Segment(9500, wire.ToArray()));
        bool statsDecide = crowd.InferLocalPlayer() == 11707 && crowd.NameFor(11707) == "Boulenbouche" && crowd.NameFor(9999) == "Player #9999";
        Console.WriteLine($"  -> in a crowd the detailed-stats frames pick the local player over a busier stranger: {strangerWasGuessed && statsDecide}");
        return named && strangerWasGuessed && statsDecide;
    }

    /// <summary>
    /// A Cleric's Divine Aura (Canyon Urugugu capture, 2026-10-02): its spawn frame names itself as
    /// owner but carries the Cleric's name, "Psefon", right after the type bytes. Its hits must be
    /// Psefon's once Psefon's id is known - they used to make an extra "Player #id" row.
    /// </summary>
    private static bool RunAion2NamedSummonScenario()
    {
        Console.WriteLine("[selftest] Aion 2 summon announced by its owner's name (real Divine Aura frames):");
        const string auraSpawn = "4136C3CD011F000106507365666F6EC0902C00400200B8D3450090624500005F44D235EC41FF1401C620C620620800006208000000000000000000000000000010D0010064000000F04902000100000000000000A08601000000000090D00300010101110181969800FFFFFFFFFFFFFFFF8075D52ABB030000C3CD01010200B8D3450090624500005F44070206FE10000002CD00A0000000D000360100001E00000000";
        const string auraHit = "0438EC91010600C3CD0150B00501020200000193D3386601000000BC50C2070100";
        var wire = new List<byte>();
        foreach (string hex in new[] { auraSpawn, auraHit })
        {
            byte[] body = Convert.FromHexString(hex);
            int length = body.Length + 4;
            while (length >= 0x80)
            {
                wire.Add((byte)(length & 0x7f | 0x80));
                length >>= 7;
            }

            wire.Add((byte)length);
            wire.AddRange(body);
        }

        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        var dir = (Aion2EntityDirectory)source.Entities;
        dir.Register(4350, "Psefon");
        source.Ingest(Segment(9900, wire.ToArray()));
        var hits = source.Poll(false).Damage;
        bool credited = hits.Count == 1 && hits[0].SourceObjectId == 4350 && hits[0].Amount == 962 && dir.SummonOwnerOf(26307) == 4350;
        Console.WriteLine($"  -> the Divine Aura's 962 is Psefon's: {credited}");
        return credited;
    }

    /// <summary>
    /// A Sorcerer's summon, and a monster that only looks like one (Draupnir capture, 2026-10-02,
    /// party with one Sorcerer, MaRio = 15422). Phantasmal Lakshmi (39081) strikes MaRio's Steel
    /// Barrier: a tick frame naming the monster with MaRio's Sorcerer effect. That made Lakshmi
    /// "MaRio's summon" and her blows on the party his damage. The Bittercold Wind (25323) that MaRio
    /// summons next, hitting Lakshmi with a Sorcerer skill, is his.
    /// </summary>
    private static bool RunAion2ShieldIsNoSummonScenario()
    {
        Console.WriteLine("[selftest] Aion 2 shield tick vs Sorcerer summon (real Draupnir frames):");
        const string lakshmiSpawn = "4136A9B1020C2200014123000002B9331CC7DC0BA3C6005C28C600600142001701E0C65BE0C65B640000006400000000000000000000000000000000000000000000000000000001000000000000000000000000000000000000000603110181969800FFFFFFFFFFFFFFFF8075D52ABB030000A9B1020128B9331CC7DC0BA3C6005C28C6110284969800FFFFFFFFFFFFFFFF8075D52ABB030000A9B10201B9331CC7DC0BA3C6005C28C61103BC060000FFFFFFFFFFFFFFFF8075D52ABB030000A9B10205B9331CC7DC0BA3C6005C28C601002D0000000301EE020000EE020000B67153BE00";
        const string barrierTick = "0538BE780AA9B102C4020B535C5AEA02C052E700";
        const string windSpawn = "4136EBC5011F00004B8E2C004002B9331CC7DC0BA3C6005C28C648E9AE43C3F801B645B6457A0D00007A0D0000000000000000000000000000508B010064000000F04902000100000000000000A08601000000000000E20400010101110181969800FFFFFFFFFFFFFFFF8075D52ABB030000EBC5010102B9331CC7DC0BA3C6005C28C60702063E3C000002CD008C050000D000310100002D00000000";
        const string windHit = "0438A9B1021400EBC5018227E9000302D36E135B01000000F2529105010100";
        var wire = new List<byte>();
        foreach (string hex in new[] { lakshmiSpawn, barrierTick, windSpawn, windHit })
        {
            byte[] body = Convert.FromHexString(hex);
            int length = body.Length + 4;
            while (length >= 0x80)
            {
                wire.Add((byte)(length & 0x7f | 0x80));
                length >>= 7;
            }

            wire.Add((byte)length);
            wire.AddRange(body);
        }

        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        var dir = (Aion2EntityDirectory)source.Entities;
        dir.Register(15422, "MaRio");
        dir.NoteClass(15422, "Sorcerer");
        dir.NoteParty(new[] { "MaRio" }, new DateTime(2026, 9, 22, 20, 0, 0, DateTimeKind.Utc));
        source.Ingest(Segment(9900, wire.ToArray()));
        var hits = source.Poll(false).Damage;
        bool lakshmiStaysMonster = dir.SummonOwnerOf(39081) is null && dir.IsKnownMonster(39081)
            && !hits.Any(h => h.SourceObjectId == 15422 && h.TargetObjectId == 15422);
        bool windIsMaRios = hits.Any(h => h.SourceObjectId == 15422 && h.TargetObjectId == 39081 && h.Skill == "Bittercold Wind")
            && dir.SummonOwnerOf(25323) == 15422;
        Console.WriteLine($"  -> Lakshmi stays a monster: {lakshmiStaysMonster}, Bittercold Wind is MaRio's: {windIsMaRios}");
        return lakshmiStaysMonster && windIsMaRios;
    }

    /// <summary>
    /// Two Sorcerers in one party (Draupnir capture, 2026-10-02 23:00): Lumy (15882) and Aurulio
    /// (16061) both summon Bittercold Winds on the same monster (46522). Each cast is announced by a
    /// no-damage frame just before the wind appears, and each wind strikes with its owner's variant
    /// of the skill (Lumy 1528024x, Aurulio 1528003x). Frames in capture order.
    /// </summary>
    private static bool RunAion2TwoSorcerersScenario()
    {
        Console.WriteLine("[selftest] Aion 2 two Sorcerers' summons (real Draupnir frames):");
        const string monsterSpawn = "4136BAEB020C22000341230000028B6CCCC64DE0044600C8D7C50098B24300FE01E0C65BE0C65B640000006400000000000000000000000000000000000000000000000000000001000000000000000000000000000000000000000603110181969800FFFFFFFFFFFFFFFF8075D52ABB030000BAEB0201288B6CCCC64DE0044600C8D7C5110284969800FFFFFFFFFFFFFFFF8075D52ABB030000BAEB02018B6CCCC64DE0044600C8D7C51103BC060000FFFFFFFFFFFFFFFF8075D52ABB030000BAEB02058B6CCCC64DE0044600C8D7C501002D0000000301EE020000EE0200000E7253C500";
        const string lumyCast = "0438BAEB0200008A7C7028E9004B02D5CB135B020000008A640200";
        const string lumyWindSpawn = "4136FC80011F0000D2902C004002B1E2BDC649CAFD4500B8D8C5D61EC340560401D24BD24B5A0E00005A0E0000000000000000000000000000E4B5010064000000F04902000100000000000000A086010000000000CE180500010101110181969800FFFFFFFFFFFFFFFF8075D52ABB030000FC80010102B1E2BDC649CAFD4500B8D8C50702060A3E000002CD001A090000D000330100002D00000000";
        const string lumyWindHit = "0438BAEB021400FC80017328E9000203F7CC135B010000008A64C30C010100";
        const string aurulioCast = "0438BAEB020000BD7D9E27E900CF02CD79135B020000009E550200";
        const string aurulioWindSpawn = "4136E3A3021F00000A8F2C004002B1E2BDC649CAFD4500B8D8C5E7420543C35E01EF3FEF3FD00E0000D00E0000000000000000000000000000508B010064000000F04902000100000000000000A08601000000000000E20400010101110181969800FFFFFFFFFFFFFFFF8075D52ABB030000E3A3020102B1E2BDC649CAFD4500B8D8C5070206BD3E000002CD00AA000000D0003C0100002D00000000";
        const string aurulioWindHit = "0438BAEB020400E3A302A127E9000203EF7A135B010000009E55DC070100";
        var wire = new List<byte>();
        foreach (string hex in new[] { monsterSpawn, lumyCast, lumyWindSpawn, lumyWindHit, aurulioCast, aurulioWindSpawn, aurulioWindHit })
        {
            byte[] body = Convert.FromHexString(hex);
            int length = body.Length + 4;
            while (length >= 0x80)
            {
                wire.Add((byte)(length & 0x7f | 0x80));
                length >>= 7;
            }

            wire.Add((byte)length);
            wire.AddRange(body);
        }

        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        var dir = (Aion2EntityDirectory)source.Entities;
        dir.Register(15882, "Lumy");
        dir.Register(16061, "Aurulio");
        dir.NoteClass(15882, "Sorcerer");
        dir.NoteClass(16061, "Sorcerer");
        dir.NoteParty(new[] { "Lumy", "Aurulio" }, new DateTime(2026, 9, 22, 20, 0, 0, DateTimeKind.Utc));
        source.Ingest(Segment(9900, wire.ToArray()));
        var hits = source.Poll(false).Damage;
        bool lumys = hits.Any(h => h.SourceObjectId == 15882 && h.Skill == "Bittercold Wind") && dir.SummonOwnerOf(16508) == 15882;
        bool aurulios = hits.Any(h => h.SourceObjectId == 16061 && h.Skill == "Bittercold Wind") && dir.SummonOwnerOf(37347) == 16061;
        Console.WriteLine($"  -> Lumy's wind is Lumy's: {lumys}, Aurulio's wind is Aurulio's: {aurulios}");
        return lumys && aurulios;
    }

    /// <summary>
    /// A player's guild on a server other than Kaisinel (Draupnir capture, 2026-10-02 22:35): the
    /// nickname frame of Miliria (16372) carries her server id 1303 (17 05) and her guild
    /// "Convèrgence" further on. The frame is its real prefix, cut after the guild name.
    /// </summary>
    private static bool RunAion2GuildScenario()
    {
        Console.WriteLine("[selftest] Aion 2 guild behind the server id (real Draupnir nickname frame, server 1303):");
        const string nickname = "4536F47F0320A00107074D696C697269611E00000001028012869FD3C63950104700CB0947753F804366B601F142F1420A0C00000A0C00000000000000000000B0940100B094010000000000F049020001000000A0860100A086010084DE010000E2040001000000017FD3CC011705EA000000000017050C436F6E76C3A87267656E636501000200";
        byte[] body = Convert.FromHexString(nickname);
        var wire = new List<byte>();
        int length = body.Length + 4;
        while (length >= 0x80)
        {
            wire.Add((byte)(length & 0x7f | 0x80));
            length >>= 7;
        }

        wire.Add((byte)length);
        wire.AddRange(body);

        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        var dir = (Aion2EntityDirectory)source.Entities;
        source.Ingest(Segment(9900, wire.ToArray()));
        source.Poll(false);
        bool ok = dir.NameFor(16372) == "Miliria" && dir.GuildOf(16372) == "Convèrgence";
        Console.WriteLine($"  -> Miliria of guild Convèrgence: {ok}");

        // A player without a guild (Jungkook, 3999, Draupnir capture 23:00): no guild run in the frame, and the
        // bytes near its end that look like "server id + name" ("odd") are no guild.
        const string noGuild = "45369F1F0530A40107084A756E676B6F6F6B1100000001010892A84BE347FD81C3C7C01134467AE0B0438FFB181395443D8802C398BFCF4101B41ACD4E64090000640900000000000000000000B8B40100B8B4010048580000F049020001000000A0860100A08601000A1400005C120500017FD3CC01A96B220117050A1111E1FA2B08FFFFFFFFFFFFFFFF8075D52ABB0300009F1F04001E0CD147F387324700FEDA461116019C3308FFFFFFFFFFFFFFFF8075D52ABB0300009F1F031E0CD147F387324700FEDA46111D41A93608FFFFFFFFFFFFFFFF8075D52ABB0300009F1F031E0CD147F387324700FEDA46111E61153208FFFFFFFFFFFFFFFF8075D52ABB0300009F1F081E0CD147F387324700FEDA461121E12F3808FFFFFFFFFFFFFFFF8075D52ABB0300009F1F021E0CD147F387324700FEDA46112281B63908FFFFFFFFFFFFFFFF8075D52ABB0300009F1F021E0CD147F387324700FEDA46112321082F08FFFFFFFFFFFFFFFF8075D52ABB0300009F1F031E0CD147F387324700FEDA46113B81812D08FFFFFFFFFFFFFFFF8075D52ABB0300009F1F0C7D39DF47E2B03AC700504C46113CC18E3008FFFFFFFFFFFFFFFF8075D52ABB0300009F1F08B87D39DF47E2B03AC700504C46119704A1223508FFFFFFFFFFFFFFFF8075D52ABB0300009F1F07C2A4DE472F4E3BC700304C460FB7A793060A0100000000000000000000000000030000000000000000000000000EB688890C0A0200000000000000005758CA010003000000000000000000000000BB0E570F8B0C000300000000000000002860CA0100030000000000000000000000000E787B860C000400000000000000001D0ECA010003000000000000000000000000BB0E1802880C000500000000000000001E0ECA0100030000000000000000000000000EF8958C0C00060000000000000000098FCA010003000000000000000000000000BB0E9E1C8E0C000700000000000000000000000000030000000000000000000000000E82CA8F0C000800000000000000002B60CA010003000000000000000000000000BB0E785C7C12000900000000000000000000000000030000000000000000000000000E1EE37D12000A0000000000000000000000000003000000000000000000000000BB0E25317E12000B00000000000000000000000000030000000000000000000000000E00000000000C0000000000000000000000000000BB0E00000000000D00000000000000000000000000000E00000000000E00000000000000000000000000007B0E00000000000F00000000000000000000000000000E0801409403000000170504CD003C000000CE0048F4FFFFD00037010000270248F4FFFF2900000000000000DE020000DE020C80646456646456643072646E6464641664647864646464647488644C64646464646464648C9794947C64645A64649C31646D799465206F3B64656464345465657952746F64655B6F786465977952649C799F30276488486C00006464646664646464646451977F6464646457B96C7201010201010264646479736766FF0026FF4D00220D0D271A1A64FFFFFFFFFFFF0000FFFFFF591C1C650202020202023C28660000006F32549A825EC766300F0683006773493C64006E6E5B1C0A3D3C006E8484846E6F0066260D0D460000680A0808500000006F571F1F6E6F00006E7E7E7E6F6F006F0100002900006A3D30233D000066380B0DAC0079056EFFFFFF6E6F0065200C083D6C190C036F6464650D0E0E";
        byte[] noGuildBody = Convert.FromHexString(noGuild);
        var noGuildWire = new List<byte>();
        int noGuildLength = noGuildBody.Length + 4;
        while (noGuildLength >= 0x80)
        {
            noGuildWire.Add((byte)(noGuildLength & 0x7f | 0x80));
            noGuildLength >>= 7;
        }

        noGuildWire.Add((byte)noGuildLength);
        noGuildWire.AddRange(noGuildBody);
        source.Ingest(Segment(9900 + (uint)wire.Count, noGuildWire.ToArray()));
        source.Poll(false);
        bool none = dir.NameFor(3999) == "Jungkook" && dir.GuildOf(3999) is null;
        Console.WriteLine($"  -> Jungkook without a guild gets none: {none}");
        return ok && none;
    }

    /// <summary>Name, guild and local-player frames from real captures: the "player seen" frame
    /// (0x048d, your own entry from the 20:16 session) and the party roster + nickname frames (the
    /// 23:02 instance run; nickname frames are the real prefix of the frame, cut after the name).</summary>
    private static bool RunAion2NamesScenario()
    {
        Console.WriteLine("[selftest] Aion 2 names, guild and local player (real frames):");
        static byte[] Record(string hex)
        {
            byte[] body = Convert.FromHexString(hex);
            var wire = new List<byte>();
            int length = body.Length + 4;
            while (length >= 0x80)
            {
                wire.Add((byte)(length & 0x7f | 0x80));
                length >>= 7;
            }

            wire.Add((byte)length);
            wire.AddRange(body);
            return wire.ToArray();
        }

        Aion2Protocol protocol = Aion2Protocol.Load();

        // 1) Open world: one "player seen" frame names the combat id 1086 and its guild.
        using var world = new Aion2PacketCombatSource(protocol);
        world.Ingest(Segment(100, Record("048d8484030e4ea800be081805044161687a08416b617473756b69010000000000000100")));
        var worldEntities = (Aion2EntityDirectory)world.Entities;
        bool seenNamed = worldEntities.NameFor(1086) == "Aahz" && worldEntities.GuildOf(1086) == "Akatsuki";

        // 2) Configured own name marks the local player without any party.
        worldEntities.SetConfiguredLocalName("Aahz");
        bool configuredLocal = worldEntities.IsLocalPlayer(1086) && !worldEntities.IsLocalPlayer(1087);

        // 3) Party: roster lists five names; four members get nickname frames, the fifth - the
        //    local player, who casts Gladiator skills - is the one left over.
        using var party = new Aion2PacketCombatSource(protocol);
        const string rosterHex = "02971c590000134573206765687420736f666f7274206c6f732e05cb2709000003ae2e030000001805ff0103051e01ae2e0300000018050950656e63696c676f6e120000001e000000c40100001805d810048b5800000000000000210000000000000001011e02353b030000001805044161687a060000001e000000b10100001805d81004e25600000000000000570000000000000001011e03434a0300000018050a426f6168616e636f6f6b1e0000001e000000a10100001805d81004a44e00000000000000200000000000000001011e042b3003000000180509436172616d656c6c79220000001e00000070010000011805d810040749000000000000001c00000000000000010100050000000000000000000000000000000000000000000400000000000000000000000004";
        string[] nicknameHex =
        {
            "4536ea1b17b0a001070950656e63696c676f6e12000000",           // 3562 Pencilgon
            "4536b81c1730a001070a426f6168616e636f6f6b1e000000",         // 3640 Boahancook
            "4536e11b17b0a0010709436172616d656c6c7922000000",           // 3553 Caramelly (same layout, built like the two above)
        };
        var wire = new List<byte>(Record(rosterHex));
        foreach (string hex in nicknameHex)
        {
            wire.AddRange(Record(hex));
        }

        // The local player's own Gladiator hit (real frame, actor id 1086), so a known player
        // without a nickname frame exists.
        wire.AddRange(Record("04388484030600be08fe26a8004f02000002433baf4101000000ba4ffd020100"));
        party.Ingest(Segment(5000, wire.ToArray()));
        var partyEntities = (Aion2EntityDirectory)party.Entities;
        bool othersNamed = partyEntities.NameFor(3562) == "Pencilgon" && partyEntities.NameFor(3640) == "Boahancook" && partyEntities.NameFor(3553) == "Caramelly";
        bool localFromRoster = partyEntities.LocalPlayerId == 1086 && partyEntities.NameFor(1086) == "Aahz";
        // Shown, but not saved as the own name: only the own character record is trusted for that
        // (a leftover guess once saved a team mate's name once the roster was read on every server).
        bool learned = partyEntities.LearnedLocalName is null;

        Console.WriteLine($"  -> player-seen frame names the id and its guild (Aahz / Akatsuki): {seenNamed}");
        Console.WriteLine($"  -> the configured character name marks the local player: {configuredLocal}");
        Console.WriteLine($"  -> nickname frames name the other members: {othersNamed}");
        Console.WriteLine($"  -> the roster's leftover name is the local player's (Aahz), shown but not saved: {localFromRoster && learned}");
        return seenNamed && configuredLocal && othersNamed && localFromRoster && learned;
    }

    /// <summary>A capture that starts in the middle of a connection begins in the middle of a frame.
    /// The reassembler must find the next real frame boundary and decode from there on, instead of
    /// locking onto a bogus length and swallowing the rest (what a mid-session meter start did).</summary>
    private static bool RunAion2MidStreamScenario()
    {
        Console.WriteLine("[selftest] Aion 2 capture starting mid-connection:");
        string[] bodies =
        {
            "04388484030600be08fe26a8004f02000002433baf4101000000ba4ffd020100",
            "04388484032600be08fe26a8005703000002433baf4101000000ba4fcc0401020100",
            "04388484032600be080e4ea8004b02000002837dbe4101000000ba4fb00301010100",
            "0438be080400be0857fcb2004a020792ea4501000000ba4f410100",
        };
        var wire = new List<byte>();
        for (int round = 0; round < 6; round++)
        {
            foreach (string hex in bodies)
            {
                byte[] body = Convert.FromHexString(hex);
                int length = body.Length + 4;
                while (length >= 0x80)
                {
                    wire.Add((byte)(length & 0x7f | 0x80));
                    length >>= 7;
                }

                wire.Add((byte)length);
                wire.AddRange(body);
            }
        }

        Aion2Protocol protocol = Aion2Protocol.Load();
        int total = 24;
        var results = new List<(int Cut, int Events)>();
        foreach (int cut in new[] { 0, 3, 11, 25, 40 })
        {
            using var source = new Aion2PacketCombatSource(protocol);
            source.Ingest(Segment(7000, wire.Skip(cut).ToArray()));
            results.Add((cut, source.Poll(false).Damage.Count));
        }

        // From offset 0 everything decodes; from any cut the first frame is damaged, then sync
        // needs three frames in a row, so at most a few of the 24 may be lost - never most of them.
        bool clean = results[0].Events == total;
        bool recovers = results.Skip(1).All(r => r.Events >= total - 6);
        Console.WriteLine($"  -> decoded frames by cut offset (of {total}): {string.Join(", ", results.Select(r => $"{r.Cut}:{r.Events}"))}");
        Console.WriteLine($"  -> whole stream decodes / a mid-stream start loses at most the first few frames: {clean && recovers}");
        return clean && recovers;
    }

    /// <summary>The local player's own record (opcode 0x3336) from the 2026-10-01 capture: the real
    /// head (combat id, name, class, level) and the real item block, with the long middle section
    /// cut out. Level 33 and the eleven items match what the game showed.</summary>
    private static bool RunAion2CharacterScenario()
    {
        Console.WriteLine("[selftest] Aion 2 character record (name, level, equipment, local player):");
        byte[] body = Convert.FromHexString("3336cb025fa1c12837044161687a18050600000001210000000000000000000000b6ec460f8ac1900600010000000000000000000000000003000000000000000000000000990eb788890c000200000000000000000000000000030000000000000000000000000e570f8b0c00030000000000000000000000000003000000000000000000000000dd0e777b860c000400000000000000000000000000030000000000000000000000000e1702880c00050000000000000000000000000003000000000000000000000000dd0efe958c0c000600000000000000000000000000030000000000000000000000000e961c8e0c00070000000000000000000000000003000000000000000000000000dd0e36a38f0c000800000000000000000000000000030000000000000000000000000e7e5c7c1200090000000000000000000000000003000000000000000000000000dd0e2e0a7e12000a00000000000000000000000000030000000000000000000000000e2c0a7e12000b0000000000000000000000000003000000000000000000000000dd0e0000000000");
        var wire = new List<byte>();
        int length = body.Length + 4;
        while (length >= 0x80)
        {
            wire.Add((byte)(length & 0x7f | 0x80));
            length >>= 7;
        }

        wire.Add((byte)length);
        wire.AddRange(body);

        Aion2Protocol protocol = Aion2Protocol.Load();
        using var source = new Aion2PacketCombatSource(protocol);
        source.Ingest(Segment(300, wire.ToArray()));
        var entities = (Aion2EntityDirectory)source.Entities;
        Aion2CharacterInfo? character = entities.LocalCharacter;

        bool header = character is { Name: "Aahz", Level: 33, ClassCode: 6, CombatId: 331 };
        var names = character?.Equipment.Select(e => Aion2ItemCatalog.Find(e.ItemId)?.Name).ToList() ?? new List<string?>();
        bool gear = character?.Equipment.Count == 11 && names[0] == "Wind Breeze Greatsword" && names[1] == "Faith Helm" && names[10] == "Facade Earrings";
        bool local = entities.LocalPlayerId == 331 && entities.IsLocalPlayer(331) && entities.NameFor(331) == "Aahz";
        Console.WriteLine($"  -> name Aahz, class code 6, level 33, combat id 331: {header}");
        Console.WriteLine($"  -> eleven items, first Wind Breeze Greatsword, last Facade Earrings: {gear}");
        Console.WriteLine($"  -> the record makes id 331 the local player, named Aahz: {local}");
        return header && gear && local;
    }

    /// <summary>The two login lists from the 2026-10-01 relog: equipment (three real item entries -
    /// Noble Belt +4, Revelation Amulet +3 and the plain Faith Helm, exactly as the in-game window
    /// showed them) and the complete real skill list with its levels.</summary>
    private static bool RunAion2LoginListsScenario()
    {
        Console.WriteLine("[selftest] Aion 2 login lists (equipment with enchant, skill levels):");
        static byte[] Record(string hex)
        {
            byte[] body = Convert.FromHexString(hex);
            var wire = new List<byte>();
            int length = body.Length + 4;
            while (length >= 0x80)
            {
                wire.Add((byte)(length & 0x7f | 0x80));
                length >>= 7;
            }

            wire.Add((byte)length);
            wire.AddRange(body);
            return wire.ToArray();
        }

        Aion2Protocol protocol = Aion2Protocol.Load();
        using var source = new Aion2PacketCombatSource(protocol);
        var wire = new List<byte>(Record("1156ae00006000005174d40c01000000000000000b110000000000000000000000000004000000000000000000000000001100000011000000000000000000000300000000000000000000000000000000000000000159cba80000a1000001188a1201000000000000000b16000000000000000000000000030000000000000000000000000027000000160000000000000000000003000000000000000000000000000000000000000001147eb90000a90000b788890c01000000000000000b03000000000000000000000000000000000000000000000000000021000000000303000000000000000000000000000000000000000000353b0300000018050103000000000000000000000000000129009400000001290000000000029098b30001704ab30001029098b30000704ab30000000000015503"));
        wire.AddRange(Record("00513f01eb0300000101000000000000000000012d010000010100000000000000000001e9030000010100000000000000000001ea030000010100000000000000000001ec030000010100000000000000000001109ca800010100000000000000000001ed0300000101000000000000000000010075a8000c0c0000000000000000000160d0ab00040400000000000000000001ee030000010100000000000000000001d490ae000c0c000000000000000000015476a8000c0c000000000000000000015200a8000c0a02000000000000000001d070aa000c0a020000000000000000016023b300010100000000000000000001ef030000010100000000000000000001f0030000010100000000000000000001f10300000101000000000000000000016041ae000c0c0000000000000000000127a5ad000101000000000000000000014d0400000101000000000000000000016a5fa9000806020000000000000000014e040000010100000000000000000001b1040000010100000000000000000001b20400000101000000000000000000019045ac00010100000000000000000001b30400000101000000000000000000010ebfaa00090801000000000000000001401f000001010000000000000000000177230000010100000000000000000001c00db4000303000000000000000000056ac54a00010100000000000000000003017cc54a00010100000000000000000001e241ae000c0c00000000000000000001c0d8a70001010000000000000000000150a9ab0005050000000000000000000124d9a70001010000000000000000000120c3a800010100000000000000000001f0beaa00090801000000000000000001605fa900080602000000000000000001f570aa0001010000000000000000000100e6aa00010100000000000000000001a06cac00010100000000000000000001c56cac0001010000000000000000000130aeb20001010000000000000000000140d5b200010100000000000000000001b0e6b300010100000000000000000001801eac000707000000000000000000010057ad0007070000000000000000000130ccad00060600000000000000000001a0bfb300060600000000000000000001d0ffa7000c0a02000000000000000001e026a8000c0a02000000000000000001f04da8000c0c000000000000000000012472aa000c0a020000000000000000013428a8000c0a020000000000000000018071b3000605000001000000000000019098b300020100000100000000000001704ab30008070000010000000000000150fcb2000b0a00000100000000000001444fa8000c0c000000000000000000015aa9ab000505000000000000000000013accad000606000000000000000000"));
        wire.AddRange(Record("26e2030b0000001121ae0100d1ad0100d2ad0100d3ad0100e2ad0100f0ad0100f1ad0100ffad01000eae01000fae010010ae010011ae010012ae010030ae010031ae010032ae010033ae01000c0000000b31d50100e8d40100e9d40100ead40100ebd40100f7d4010005d5010006d5010014d5010023d5010032d501000d0000001041fc010032fc010033fc010034fc010035fc010036fc010037fc010046fc01004cfc01004dfc01004efc01004ffc010050fc01005bfc01006afc01006bfc0100"));
        source.Ingest(Segment(400, wire.ToArray()));
        var entities = (Aion2EntityDirectory)source.Entities;

        var gear = entities.LocalEquipment.ToDictionary(i => Aion2ItemCatalog.Find(i.ItemId)?.Name ?? "?", i => i.Enchant);
        bool equipment = gear.Count == 3 && gear.GetValueOrDefault("Noble Belt") == 4 && gear.GetValueOrDefault("Revelation Amulet") == 3 && gear.GetValueOrDefault("Faith Helm") == 0;

        var skills = entities.LocalSkills.ToDictionary(k => k.SkillId, k => k);
        bool skillLevels = skills.Count >= 40
            && skills[11010000] is { Level: 12, BaseLevel: 10 }      // Rending Blow 10+2
            && skills[11030000] is { Level: 12, BaseLevel: 12 }      // Rupture Strike
            && skills[11730000] is { Level: 11, BaseLevel: 10 }      // Blood Absorption 10+1
            && skills[11100000] is { Level: 8, BaseLevel: 6 }        // Ruinous Blow 6+2
            && skills[11390000] is { Level: 6, BaseLevel: 6 };       // Rage Burst
        var boards = entities.LocalDaevanion.ToDictionary(x => x.BoardId, x => Aion2DaevanionCatalog.Summarize(x.BoardId, x.NodeIds));
        bool daevanion = boards.Count == 3
            && boards[11] is { Name: "Nezekan", ActiveNodes: 16 } n11 && n11.SkillBonuses.GetValueOrDefault(11010000) == 1      // Rending Blow +1
            && boards[12] is { Name: "Zikel", ActiveNodes: 10 } n12 && n12.SkillBonuses.GetValueOrDefault(11190000) == 1       // Leaping Slam +1
            && boards[13] is { Name: "Vaizel", ActiveNodes: 15 } n13 && n13.SkillBonuses.GetValueOrDefault(11050000) == 1 && n13.SkillBonuses.GetValueOrDefault(11200000) == 1;
        Console.WriteLine($"  -> Daevanion: Nezekan 16 / Zikel 10 / Vaizel 15 activated nodes, skill bonuses match the skill list's bonus levels: {daevanion}");
        Console.WriteLine($"  -> belt +4, amulet +3, plain helm +0 (and nothing invented elsewhere): {equipment}");
        Console.WriteLine($"  -> {skills.Count} skills with total/base level (Rending Blow 12 = 10+2, Blood Absorption 11 = 10+1 ...): {skillLevels}");
        return equipment && skillLevels && daevanion;
    }

    /// <summary>The local player's data survives a meter restart: it is written when the game sends it
    /// and read back by the next session, which shows it (marked restored) without making the stale
    /// combat id the local player.</summary>
    private static bool RunAion2CharacterStoreScenario()
    {
        Console.WriteLine("[selftest] Aion 2 character data kept across restarts:");
        string path = Path.Combine(Path.GetTempPath(), "aiondps-char-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var directory = new Aion2EntityDirectory();
            // Mirror what the source does: save on every fresh change.
            directory.CharacterChanged += _ =>
            {
                if (directory.ToSaved() is { } snapshot)
                {
                    Aion2CharacterStore.Save(path, snapshot);
                }
            };
            directory.SetLocalCharacter(new Aion2CharacterInfo(331, "Aahz", 6, 33, new[] { new Aion2EquippedItem(1, 110150026) }, DateTime.Now));
            directory.SetLocalEquipment(new[] { new Aion2EquippedItem(1, 110150026), new Aion2EquippedItem(17, 215250001, 4) });
            directory.SetLocalSkills(new[] { new Aion2SkillEntry(11010000, 12, 10) });
            directory.SetLocalDaevanion(new[] { new Aion2DaevanionBoard(11, new[] { 110113, 110033 }) });
            bool written = File.Exists(path);

            // A "new session": nothing yet, then the saved record is restored.
            var next = new Aion2EntityDirectory();
            if (Aion2CharacterStore.Load(path) is { } saved)
            {
                next.RestoreFrom(saved);
            }

            Aion2CharacterInfo? restored = next.LocalCharacter;
            bool header = restored is { Name: "Aahz", Level: 33, ClassCode: 6, Restored: true };
            bool lists = next.LocalEquipment.Any(i => i.ItemId == 215250001 && i.Enchant == 4)
                && next.LocalSkills.Any(s => s.SkillId == 11010000 && s.Level == 12 && s.BaseLevel == 10)
                && next.LocalDaevanion.Count == 1 && next.LocalDaevanion[0].NodeIds.Count == 2;
            bool notLocal = next.LocalPlayerId == -1; // the saved combat id belongs to a past session

            // Fresh data replaces the restored record.
            next.SetLocalCharacter(new Aion2CharacterInfo(2037, "Aahz", 6, 34, Array.Empty<Aion2EquippedItem>(), DateTime.Now));
            bool replaced = next.LocalCharacter is { Level: 34, Restored: false } && next.LocalPlayerId == 2037;
            bool className = Aion2SkillNames.ClassFromCode(6) == "Gladiator" && Aion2SkillNames.ClassFromCode(34) == "Chanter" && Aion2SkillNames.ClassFromCode(18) == "Assassin" && Aion2SkillNames.ClassFromCode(7) is null;

            Console.WriteLine($"  -> the data is written to disk when the game sends it: {written}");
            Console.WriteLine($"  -> a new session restores name, level, gear with enchant, skills and boards: {header && lists}");
            Console.WriteLine($"  -> the restored record does not claim the old combat id as the local player: {notLocal}");
            Console.WriteLine($"  -> fresh data from the game replaces it: {replaced}");
            Console.WriteLine($"  -> class from the record's class code (Gladiator 6, Assassin 18, Chanter 34): {className}");
            return written && header && lists && notLocal && replaced && className;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>What the "player appeared" frame says about another player - class and faction from
    /// the class code, and the visible equipment: a real frame's head and item block (the long
    /// middle cut out), from the 2026-10-01 relog capture.</summary>
    private static bool RunAion2BossSpawnScenario()
    {
        Console.WriteLine("[selftest] Aion 2 boss recognition (monster-appears frames from a recorded Krao Cave run):");
        // Entity 30410 announced with NPC id 2300104 (Enhanced Harcon), entity 24155 with 2300171 (Ultimate Berk).
        string[] frames =
        {
            "4136caed010c2200c81823000002291745c609d96e4600000fc300a00c4300640180dddb0180dddb01640000006400000000000000000000000000000000000000504600005046000001000000000000000000000000000000000000000602110181969800ffffffffffffffff8075d52abb030000caed010114291745c609d96e4600000fc3110284969800ffffffffffffffff8075d52abb030000caed0101291745c609d96e4600000fc301002d0000000301b0040000b004000006b6e88b00",
            "4136dbbc010c22000b19230000026df41ac6973a684300002cc500983243007f0180859f0380859f03640000006400000000000000000000000000000000000000409c0000409c000001000000000000000000000000000000000000000603110181969800ffffffffffffffff8075d52abb030000dbbc0101086df41ac6973a684300002cc5110284969800ffffffffffffffff8075d52abb030000dbbc01016df41ac6973a684300002cc511045dae1201ffffffffffffffff8075d52abb030000dbbc01016df41ac6973a684300002cc501002d0000000301f0000000f0000000a5b5688a00",
        };
        Aion2Protocol protocol = Aion2Protocol.Load();
        using var source = new Aion2PacketCombatSource(protocol);
        uint seq = 900;
        foreach (string hex in frames)
        {
            byte[] body = Convert.FromHexString(hex);
            var wire = new List<byte>();
            int length = body.Length + 4;
            while (length >= 0x80)
            {
                wire.Add((byte)(length & 0x7f | 0x80));
                length >>= 7;
            }

            wire.Add((byte)length);
            wire.AddRange(body);
            source.Ingest(Segment(seq, wire.ToArray()));
            seq += (uint)wire.Count;
        }

        var entities = (Aion2EntityDirectory)source.Entities;
        bool harcon = entities.BossNpcIdOf(30410) == 2300104 && entities.NameFor(30410) == "Enhanced Harcon";
        bool berk = entities.BossNpcIdOf(24155) == 2300171 && entities.NameFor(24155) == "Ultimate Berk";
        bool mobIgnored = entities.BossNpcIdOf(12345) is null;
        Console.WriteLine($"  -> entity 30410 is Enhanced Harcon (NPC 2300104): {harcon}");
        Console.WriteLine($"  -> entity 24155 is Ultimate Berk (NPC 2300171): {berk}");
        Console.WriteLine($"  -> an unknown entity is no boss: {mobIgnored}");
        return harcon && berk && mobIgnored;
    }

    private static bool RunRestartSnapshotScenario()
    {
        Console.WriteLine("[selftest] Session survives a client restart (snapshot file):");
        string path = Path.Combine(Path.GetTempPath(), "aiondps-selftest-" + Guid.NewGuid().ToString("N") + History.SessionFile.Extension);
        try
        {
            // The old process knew who is who ...
            var before = new Aion2EntityDirectory();
            before.Register(5875, "Aahz");
            before.NoteClass(5875, "Gladiator");
            before.RegisterNpc(30410, 2300104);
            before.SetGuild(5875, "Akatsuki");
            var events = new List<DamageEvent> { new(DateTime.Now, 5875, 30410, 1234, IsHeal: false, Skill: "Rending Blow") };
            History.SessionFile.Save(path, events, new List<AvoidEvent>(), new List<KillEvent>(), new Dictionary<int, string> { [5875] = "Aahz" }, before.Snapshot());

            // ... the new one starts empty and takes it over from the file.
            var loaded = History.SessionFile.Load(path);
            var after = new Aion2EntityDirectory();
            after.RestoreFrom(loaded.Directory!);
            bool eventsBack = loaded.Events.Count == 1 && loaded.Events[0].Amount == 1234;
            bool named = after.NameFor(5875) == "Aahz" && after.ClassOf(5875) == "Gladiator" && after.GuildOf(5875) == "Akatsuki";
            bool boss = after.NameFor(30410) == "Enhanced Harcon" && after.BossNpcIdOf(30410) == 2300104;

            // What the live stream already knows wins over the snapshot.
            var live = new Aion2EntityDirectory();
            live.Register(5875, "Someone");
            live.RestoreFrom(loaded.Directory!);
            bool liveWins = live.NameFor(5875) == "Someone";
            Console.WriteLine($"  -> events come back: {eventsBack}");
            Console.WriteLine($"  -> names, classes and legion come back: {named}");
            Console.WriteLine($"  -> a recognised boss keeps its name: {boss}");
            Console.WriteLine($"  -> what the live stream already knows wins: {liveWins}");
            return eventsBack && named && boss && liveWins;
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private static bool RunAion2SeenProfileScenario()
    {
        Console.WriteLine("[selftest] Aion 2 other players' profile (class, faction, visible gear):");
        byte[] body = Convert.FromHexString("4536fc3e0320a0010707496363617275732100000000000000000000e0f9460fde489b06000100000000000000000000000000030000000000000000000000000eb888890c000200000000000000005758ca010003000000000000000000000000dd0e5e0f8b0c000300000000000000005858ca0100030000000000000000000000000e7e7b860c000400000000000000005558ca010003000000000000000000000000dd0e1702880c000500000000000000000000000000030000000000000000000000000ef7958c0c00060000000000000000000000000003000000000000000000000000dd0e981c8e0c000700000000000000000000000000030000000000000000000000000e3ea38f0c00080000000000000000ab21ca010003000000000000000000000000dd0e7e5c7c12000900000000000000000000000000030000000000000000000000000e18e37d12000a0000000000000000000000000003000000000000000000000000dd0e18e37d12000b00000000000000000000000000030000");
        var wire = new List<byte>();
        int length = body.Length + 4;
        while (length >= 0x80)
        {
            wire.Add((byte)(length & 0x7f | 0x80));
            length >>= 7;
        }

        wire.Add((byte)length);
        wire.AddRange(body);

        Aion2Protocol protocol = Aion2Protocol.Load();
        using var source = new Aion2PacketCombatSource(protocol);
        source.Ingest(Segment(600, wire.ToArray()));
        var entities = (Aion2EntityDirectory)source.Entities;
        Aion2SeenProfile? seen = entities.SeenProfileOf(8060);
        string? name = entities.NameFor(8060);
        bool named = name == "Iccarus";
        bool classFaction = seen is { ClassId: 8, Faction: 1 };
        var gear = seen?.Gear.Select(g => Aion2ItemCatalog.Find(g.ItemId)?.Name).ToList() ?? new List<string?>();
        bool items = gear.Count == 11 && gear[0] == "Drifter Staff" && gear[1] == "Judicator Helm" && gear[10] == "Judicator Earrings";
        Console.WriteLine($"  -> player named Iccarus: {named}");
        Console.WriteLine($"  -> class id 8 and faction bit 1 read from the class code: {classFaction}");
        Console.WriteLine($"  -> eleven visible items (Drifter Staff ... Judicator Earrings): {items}");
        return named && classFaction && items;
    }

    private static bool RunTcpReassemblerScenario()
    {
        Console.WriteLine("[selftest] TCP reassembly + frame cutting (synthetic 2-byte-length frames):");
        byte[] a = Frame(1, 0xAA, 0xBB, 0xCC, 0xDD);
        byte[] b = Frame(2, 0xEE, 0xFF);
        byte[] stream = a.Concat(b).ToArray();
        byte[] first = stream[..5];
        byte[] middle = stream[5..9];
        byte[] last = stream[9..];

        // The first segment seen defines where the stream starts (a capture always begins
        // mid-connection); reordering is only meaningful for what follows it.
        var reassembler = new TcpReassembler();
        bool firstAloneIncomplete = reassembler.Push(Segment(1000, first), TestLayout).Count == 0;
        // The middle segment is delayed: the last one has to wait for it.
        bool outOfOrderWaits = reassembler.Push(Segment(1009, last), TestLayout).Count == 0;
        IReadOnlyList<ReadOnlyMemory<byte>> frames = reassembler.Push(Segment(1005, middle), TestLayout);
        bool bothFramesCut = firstAloneIncomplete && frames.Count == 2 && frames[0].Span.SequenceEqual(a) && frames[1].Span.SequenceEqual(b);
        // A retransmission of the first segment changes nothing.
        bool retransmissionIgnored = reassembler.Push(Segment(1000, first), TestLayout).Count == 0 && reassembler.Retransmissions == 1;
        // A frame split across three tiny segments still comes out whole.
        byte[] c = Frame(3, 1, 2, 3, 4, 5, 6);
        int produced = 0;
        uint seq = 1000 + (uint)stream.Length;
        foreach (byte[] piece in new[] { c[..2], c[2..5], c[5..] })
        {
            produced += reassembler.Push(Segment(seq, piece), TestLayout).Count;
            seq += (uint)piece.Length;
        }
        bool splitFrameWhole = produced == 1 && reassembler.Frames == 3;

        Console.WriteLine($"  -> out-of-order segment waits for the gap: {outOfOrderWaits}");
        Console.WriteLine($"  -> both frames cut once the gap fills: {bothFramesCut}");
        Console.WriteLine($"  -> retransmission ignored and counted: {retransmissionIgnored}");
        Console.WriteLine($"  -> frame split over three segments comes out whole: {splitFrameWhole}");
        return outOfOrderWaits && bothFramesCut && retransmissionIgnored && splitFrameWhole;
    }

    private static bool RunAion2ProtocolScenario()
    {
        Console.WriteLine("[selftest] Aion 2 protocol description + frame decoder:");
        Aion2Protocol shipped = Aion2Protocol.Load();
        bool shippedUncalibrated = shipped.IsCalibrated && shipped.FrameLayout.IsVarint && shipped.ServerPorts.Contains(13328);

        const string json = """
            {
              "calibrated": true,
              "gameVersion": "selftest",
              "serverPorts": [7777],
              "frame": { "lengthOffset": 0, "lengthSize": 2, "littleEndian": true, "lengthIncludesHeader": true, "headerSize": 4, "opcodeOffset": 2, "opcodeSize": 2, "maxFrameLength": 4096 },
              "opcodes": { "damage": [1], "nickname": [2], "session": [3], "kill": [4] },
              "fields": {
                "damage": { "sourceId": { "offset": 4, "size": 4 }, "targetId": { "offset": 8, "size": 4 }, "amount": { "offset": 12, "size": 4 }, "skillId": { "offset": 16, "size": 4 }, "flags": { "offset": 20, "size": 1, "mask": "0x01" } },
                "nickname": { "objectId": { "offset": 4, "size": 4 }, "name": { "offset": 8, "size": 0 } },
                "session": { "localPlayerId": { "offset": 4, "size": 4 } },
                "kill": { "victimId": { "offset": 4, "size": 4 }, "killerId": { "offset": 8, "size": 4 }, "victimIsPlayer": { "offset": 12, "size": 1 } }
              }
            }
            """;
        Aion2Protocol protocol = Aion2Protocol.FromJson(json);
        bool parsed = protocol.IsCalibrated && protocol.ServerPorts.SequenceEqual(new[] { 7777 })
            && protocol.FamilyOf(1) == OpcodeFamily.Damage && protocol.FamilyOf(2) == OpcodeFamily.Nickname && protocol.FamilyOf(99) == OpcodeFamily.Unknown;

        var entities = new Aion2EntityDirectory();
        var decoder = new Aion2FrameDecoder(protocol, entities);
        var ts = new DateTime(2026, 9, 22, 20, 0, 0, DateTimeKind.Utc);

        decoder.Decode(Frame(3, Int(4242)), ts);
        decoder.Decode(Frame(2, Int(4242).Concat(Utf16("Skeeve")).ToArray()), ts);
        decoder.Decode(Frame(2, Int(9001).Concat(Utf16("Ultimate Berk")).ToArray()), ts);
        DamageEvent[] hits = decoder.Decode(Frame(1, Int(4242).Concat(Int(9001)).Concat(Int(12345)).Concat(Int(11010000)).Concat(new byte[] { 0x01 }).ToArray()), ts).ToArray();
        DamageEvent[] unknown = decoder.Decode(Frame(99, 1, 2, 3), ts).ToArray();
        decoder.Decode(Frame(4, Int(9001).Concat(Int(4242)).Concat(new byte[] { 0 }).ToArray()), ts);

        bool localPlayer = entities.LocalPlayerId == 4242 && entities.IsLocalPlayer(4242) && entities.NameFor(4242) == "Skeeve";
        bool damageDecoded = hits.Length == 1 && hits[0].SourceObjectId == 4242 && hits[0].TargetObjectId == 9001 && hits[0].Amount == 12345 && hits[0].IsCritical && hits[0].Skill == "Rending Blow";
        bool unknownSkipped = unknown.Length == 0 && decoder.UnknownOpcodes == 1;
        var skillUses = decoder.DrainSkillUses();
        bool skillUseRaised = skillUses.Count == 1 && skillUses[0].Actor == "Skeeve";
        var kills = decoder.DrainKills();
        bool killDecoded = kills.Count == 1 && kills[0].VictimObjectId == 9001 && kills[0].KillerObjectId == 4242 && !kills[0].VictimIsPlayer;

        // The whole source, fed through Ingest as the capture would: same result, via the seam.
        using var source = new Aion2PacketCombatSource(protocol);
        byte[] wire = Frame(3, Int(7)).Concat(Frame(1, Int(7).Concat(Int(8)).Concat(Int(500)).Concat(Int(0)).Concat(new byte[] { 0 }).ToArray())).ToArray();
        source.Ingest(Segment(500, wire));
        CombatBatch batch = source.Poll(false);
        bool sourceDelivers = batch.Damage.Count == 1 && batch.Damage[0].Amount == 500 && source.Entities.IsLocalPlayer(7) && source.Capabilities.HasFlag(SourceCapabilities.ExactIds);

        Console.WriteLine($"  -> shipped opcodes.json is the calibrated varint layout (port 13328): {shippedUncalibrated}");
        Console.WriteLine($"  -> synthetic description parses (ports/opcode families): {parsed}");
        Console.WriteLine($"  -> session + nickname frames name the local player: {localPlayer}");
        Console.WriteLine($"  -> damage frame decodes ids/amount/skill/crit flag: {damageDecoded}");
        Console.WriteLine($"  -> unknown opcode skipped and counted: {unknownSkipped}");
        Console.WriteLine($"  -> skill use raised for the named actor: {skillUseRaised}");
        Console.WriteLine($"  -> kill frame decodes victim/killer: {killDecoded}");
        Console.WriteLine($"  -> Aion2PacketCombatSource delivers via Ingest/Poll: {sourceDelivers}");
        return shippedUncalibrated && parsed && localPlayer && damageDecoded && unknownSkipped && skillUseRaised && killDecoded && sourceDelivers;
    }

    private static byte[] Int(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Utf16(string text)
    {
        byte[] chars = Encoding.Unicode.GetBytes(text);
        var bytes = new byte[2 + chars.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)text.Length);
        chars.CopyTo(bytes, 2);
        return bytes;
    }

    private static bool RunClassCatalogScenario()
    {
        Console.WriteLine("[selftest] Class catalog:");
        bool roster = ClassCatalog.Classes.Count == 9 && ClassCatalog.IsKnownClass("Elementalist") && !ClassCatalog.IsKnownClass("Spiritmaster");
        bool abbreviations = ClassCatalog.Abbreviation("Elementalist") == "ELE" && ClassCatalog.Abbreviation("Templar") == "TPL";
        Console.WriteLine($"  -> nine classes incl. Elementalist, no Spiritmaster: {roster}");
        Console.WriteLine($"  -> badge abbreviations match the website's: {abbreviations}");
        return roster && abbreviations;
    }

    private static bool RunHotkeyBindingScenario()
    {
        Console.WriteLine("[selftest] Global hotkey bindings:");
        var defaults = new MeterSettings();
        bool roundTrip = new[] { defaults.HotkeyHideUi, defaults.HotkeyPause, defaults.HotkeyCopyDamage, defaults.HotkeyClear }
            .All(text => !Ui.HotkeyBinding.Parse(text).IsEmpty && Ui.HotkeyBinding.Parse(text).ToString() == text);
        bool needsModifier = Ui.HotkeyBinding.Parse("H").IsEmpty && Ui.HotkeyBinding.Parse("Ctrl+").IsEmpty && Ui.HotkeyBinding.Parse("").IsEmpty;
        bool parsesModifiers = Ui.HotkeyBinding.Parse("ctrl+shift+F9") is { Ctrl: true, Shift: true, Alt: false } b && b.Key == System.Windows.Input.Key.F9;
        Console.WriteLine($"  -> the default bindings read back as written: {roundTrip}");
        Console.WriteLine($"  -> a key without a modifier, or nothing, is no binding: {needsModifier}");
        Console.WriteLine($"  -> modifiers and keys parse whatever the case: {parsesModifiers}");
        return roundTrip && needsModifier && parsesModifiers;
    }

    private static bool RunSettingsMigrationScenario()
    {
        Console.WriteLine("[selftest] Settings written by earlier versions:");
        // Files from the versions that also tracked classic Aion still carry fields this build no
        // longer has; they must load (the unknown ones are ignored) and keep what is still used.
        var old = JsonSerializer.Deserialize<MeterSettings>("""{"Theme":"Dark","Game":"aion","Characters":[{"Name":"Old","ClassName":"Cleric"}],"AionInstallFolder":"D:\\Aion","ShowShareBars":false,"Aion2CharacterName":"Aahz"}""")!;
        bool loads = old.Theme == "Dark" && !old.ShowShareBars && old.Aion2CharacterName == "Aahz";
        string written = JsonSerializer.Serialize(old);
        bool noClassicFields = !written.Contains("AionInstallFolder") && !written.Contains("Characters");
        Console.WriteLine($"  -> an old file loads and keeps the fields still in use: {loads}");
        Console.WriteLine($"  -> classic Aion fields are dropped when saved again: {noClassicFields}");
        return loads && noClassicFields;
    }
}
