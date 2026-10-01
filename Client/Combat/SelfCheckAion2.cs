using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat.Sources;
using AionDPS.Data;
using AionDPS.Game;
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
        ok &= RunAion2NamesScenario();
        ok &= RunAion2MidStreamScenario();
        ok &= RunAion2CharacterScenario();
        ok &= RunAion2LoginListsScenario();
        ok &= RunAion2CharacterStoreScenario();
        ok &= RunAion2SeenProfileScenario();
        ok &= RunClassCatalogScenario();
        ok &= RunSettingsMigrationScenario();
        return ok;
    }

    private static bool RunCombatSourceSeamScenario()
    {
        Console.WriteLine("[selftest] Combat-source seam (ChatLogCombatSource over a temp Chat.log, FakeCombatSource):");
        string dir = Path.Combine(Path.GetTempPath(), "aiondps-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "Chat.log");
        string[] history = { "2026.08.23 21:31:08 : Ulgorn Raider inflicted 1 damage on Training Dummy. " };
        string[] live =
        {
            "2026.08.23 21:32:07 : You inflicted 1 damage on Training Dummy. ",
            "2026.08.23 21:32:08 : You inflicted 2 damage on Training Dummy. ",
        };
        string[] whilePaused = { "2026.08.23 21:32:09 : You inflicted 3 damage on Training Dummy. " };

        try
        {
            File.WriteAllLines(path, history, Encoding.Latin1);
            using var source = new ChatLogCombatSource(path);
            source.Start();

            // Never the past: what was in the file before Start() is not delivered.
            bool historySkipped = source.Poll(false).IsEmpty;

            File.AppendAllLines(path, live, Encoding.Latin1);
            CombatBatch batch = source.Poll(false);
            var direct = new ChatLog.ChatLogParser().Parse(live);
            bool sameCount = batch.Damage.Count == direct.Count && direct.Count == 2;
            bool localPlayerIsYou = source.Entities.NameFor(source.Entities.LocalPlayerId) == "You"
                && source.Entities.IsLocalPlayer(batch.Damage[0].SourceObjectId);
            bool sameAmounts = batch.Damage.Select(e => e.Amount).SequenceEqual(direct.Select(e => e.Amount));

            // Paused time is discarded, not deferred.
            File.AppendAllLines(path, whilePaused, Encoding.Latin1);
            bool pausedEmpty = source.Poll(true).IsEmpty;
            bool notReplayed = source.Poll(false).IsEmpty;

            // Reload reads everything, history included, and live tailing continues afterwards.
            int reloaded = source.ReloadFromDisk().Damage.Count;
            bool reloadedAll = reloaded == history.Length + live.Length + whilePaused.Length;
            bool capabilities = source.Capabilities.HasFlag(SourceCapabilities.Reparse) && source.Capabilities.HasFlag(SourceCapabilities.Loot);

            var fake = new FakeCombatSource();
            int you = fake.Entities.LocalPlayerId;
            fake.Enqueue(new DamageEvent(DateTime.UtcNow, you, fake.IdOf("Dummy"), 10, IsHeal: false));
            bool fakePausedDiscards = fake.Poll(true).IsEmpty && fake.Poll(false).IsEmpty;
            fake.Enqueue(new DamageEvent(DateTime.UtcNow, you, fake.IdOf("Dummy"), 10, IsHeal: false));
            bool fakeDelivers = fake.Poll(false).Damage.Count == 1 && fake.Entities.IsLocalPlayer(you);

            Console.WriteLine($"  -> lines from before Start() are never delivered: {historySkipped}");
            Console.WriteLine($"  -> live poll matches a direct parse (count/amounts): {sameCount && sameAmounts}");
            Console.WriteLine($"  -> \"You\" is the local player id: {localPlayerIsYou}");
            Console.WriteLine($"  -> paused lines are discarded, not replayed: {pausedEmpty && notReplayed}");
            Console.WriteLine($"  -> ReloadFromDisk returns the whole file ({reloaded}): {reloadedAll}");
            Console.WriteLine($"  -> capabilities advertise Reparse+Loot: {capabilities}");
            Console.WriteLine($"  -> FakeCombatSource honours pause and delivers queued batches: {fakePausedDiscards && fakeDelivers}");
            return historySkipped && sameCount && sameAmounts && localPlayerIsYou && pausedEmpty && notReplayed && reloadedAll && capabilities && fakePausedDiscards && fakeDelivers;
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
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
    /// frames of which ten are damage. Before bundles were unpacked those hits were silently
    /// missing - roughly 40 % of all damage frames in a real session.</summary>
    private static bool RunAion2BundleScenario()
    {
        Console.WriteLine("[selftest] Aion 2 bundle frame (LZ4) from a real capture:");
        const string bundleHex = "ffff08020000ff13200438b81c0400ec1b5e7d14011a02c3f8006c01000000c24e87080100200438f81b1d000150c40701001e1d0015003a0015c43a000c1b002700c51b004f200438ea53000212cd53001fea530007071b000c53001fe153000212b353001fe1530007071b00095300f100332a38f81b011335ade5cc0ae02e0001006065a020f4a0019e00f0090c5e7d1401004f576fc60aa7724600c021440d0e92f81b012e0090160538f81b09ec1b354a0120c00b2a0080332a38ea1b0113391f000f4d001415ea4d0010ea4d0010394d00118d77004f332a38e19a001c15e14d0010e14d00019a0011854d00b00e0036857120f4a0010000";
        long[] expected = { 1031, 964, 1, 1, 973, 1, 1, 947, 1, 1 };

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
        bool learned = partyEntities.LearnedLocalName == "Aahz";

        Console.WriteLine($"  -> player-seen frame names the id and its guild (Aahz / Akatsuki): {seenNamed}");
        Console.WriteLine($"  -> the configured character name marks the local player: {configuredLocal}");
        Console.WriteLine($"  -> nickname frames name the other members: {othersNamed}");
        Console.WriteLine($"  -> the roster's leftover name is the local player's (Aahz), and is learned: {localFromRoster && learned}");
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
        Console.WriteLine("[selftest] Class catalog per game:");
        bool aion2Roster = ClassCatalog.ClassesFor(GameKind.Aion2).Count == 9 && ClassCatalog.IsKnownClass(GameKind.Aion2, "Elementalist") && !ClassCatalog.IsKnownClass(GameKind.Aion2, "Spiritmaster");
        bool aionRoster = ClassCatalog.IsKnownClass(GameKind.Aion, "Spiritmaster") && !ClassCatalog.IsKnownClass(GameKind.Aion, "Brawler");
        bool abbreviations = ClassCatalog.Abbreviation("Elementalist") == "ELE" && ClassCatalog.Abbreviation("Templar") == "TPL";
        bool tokens = GameKind.Aion2.ToToken() == "aion2" && GameKindExtensions.ParseToken("aion2") == GameKind.Aion2 && GameKindExtensions.ParseToken(null) == GameKind.Aion;
        Console.WriteLine($"  -> Aion 2 has nine classes incl. Elementalist, no Spiritmaster: {aion2Roster}");
        Console.WriteLine($"  -> classic roster has Spiritmaster, no Brawler: {aionRoster}");
        Console.WriteLine($"  -> badge abbreviations match the website's: {abbreviations}");
        Console.WriteLine($"  -> game tokens round-trip: {tokens}");
        return aion2Roster && aionRoster && abbreviations && tokens;
    }

    private static bool RunSettingsMigrationScenario()
    {
        Console.WriteLine("[selftest] Settings migration (game field):");
        var legacy = JsonSerializer.Deserialize<MeterSettings>("""{"Theme":"Dark","Characters":[{"Name":"Old","ClassName":"Cleric"}]}""")!;
        bool legacyIsAion = legacy.Game == GameKind.Aion && legacy.Characters[0].Game == GameKind.Aion;
        bool legacyIsAutomatic = legacy.GameDetectionMode == GameDetectionMode.Automatic;

        var aion2 = JsonSerializer.Deserialize<MeterSettings>("""{"Game":"aion2","Characters":[{"Name":"New","ClassName":"Templar","Game":"aion2"}]}""")!;
        bool aion2Read = aion2.Game == GameKind.Aion2 && aion2.Characters[0].Game == GameKind.Aion2;

        string written = JsonSerializer.Serialize(aion2);
        bool writtenAsToken = written.Contains("\"Game\":\"aion2\"") && !written.Contains("\"Game\":1");

        var manual = JsonSerializer.Deserialize<MeterSettings>("""{"GameDetectionMode":"manual"}""")!;
        bool manualRead = manual.GameDetectionMode == GameDetectionMode.Manual;
        bool manualWrittenAsToken = JsonSerializer.Serialize(manual).Contains("\"GameDetectionMode\":\"manual\"");

        Console.WriteLine($"  -> settings without a game field mean classic Aion: {legacyIsAion}");
        Console.WriteLine($"  -> settings without a detection-mode field default to Automatic: {legacyIsAutomatic}");
        Console.WriteLine($"  -> \"aion2\" reads back as Aion2 for settings and characters: {aion2Read}");
        Console.WriteLine($"  -> serialized as the backend's token, not a number: {writtenAsToken}");
        Console.WriteLine($"  -> \"manual\" reads back and round-trips as a token, not a number: {manualRead && manualWrittenAsToken}");
        return legacyIsAion && legacyIsAutomatic && aion2Read && writtenAsToken && manualRead && manualWrittenAsToken;
    }
}
