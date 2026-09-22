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
        bool shippedUncalibrated = !shipped.IsCalibrated && shipped.FrameLayout.LengthSize == 2;

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
        bool damageDecoded = hits.Length == 1 && hits[0].SourceObjectId == 4242 && hits[0].TargetObjectId == 9001 && hits[0].Amount == 12345 && hits[0].IsCritical && hits[0].Skill == "11010000";
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

        Console.WriteLine($"  -> shipped opcodes.json loads as uncalibrated template: {shippedUncalibrated}");
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
        bool aionRoster = ClassCatalog.IsKnownClass(GameKind.Aion, "Spiritmaster") && !ClassCatalog.IsKnownClass(GameKind.Aion, "Fighter");
        bool abbreviations = ClassCatalog.Abbreviation("Elementalist") == "ELE" && ClassCatalog.Abbreviation("Templar") == "TPL";
        bool tokens = GameKind.Aion2.ToToken() == "aion2" && GameKindExtensions.ParseToken("aion2") == GameKind.Aion2 && GameKindExtensions.ParseToken(null) == GameKind.Aion;
        Console.WriteLine($"  -> Aion 2 has nine classes incl. Elementalist, no Spiritmaster: {aion2Roster}");
        Console.WriteLine($"  -> classic roster has Spiritmaster, no Fighter: {aionRoster}");
        Console.WriteLine($"  -> badge abbreviations match the website's: {abbreviations}");
        Console.WriteLine($"  -> game tokens round-trip: {tokens}");
        return aion2Roster && aionRoster && abbreviations && tokens;
    }

    private static bool RunSettingsMigrationScenario()
    {
        Console.WriteLine("[selftest] Settings migration (game field):");
        var legacy = JsonSerializer.Deserialize<MeterSettings>("""{"Theme":"Dark","Characters":[{"Name":"Old","ClassName":"Cleric"}]}""")!;
        bool legacyIsAion = legacy.Game == GameKind.Aion && legacy.Characters[0].Game == GameKind.Aion;

        var aion2 = JsonSerializer.Deserialize<MeterSettings>("""{"Game":"aion2","Characters":[{"Name":"New","ClassName":"Templar","Game":"aion2"}]}""")!;
        bool aion2Read = aion2.Game == GameKind.Aion2 && aion2.Characters[0].Game == GameKind.Aion2;

        string written = JsonSerializer.Serialize(aion2);
        bool writtenAsToken = written.Contains("\"Game\":\"aion2\"") && !written.Contains("\"Game\":1");

        Console.WriteLine($"  -> settings without a game field mean classic Aion: {legacyIsAion}");
        Console.WriteLine($"  -> \"aion2\" reads back as Aion2 for settings and characters: {aion2Read}");
        Console.WriteLine($"  -> serialized as the backend's token, not a number: {writtenAsToken}");
        return legacyIsAion && aion2Read && writtenAsToken;
    }
}
