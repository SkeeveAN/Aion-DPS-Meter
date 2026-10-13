using System.Text;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>One player under several combat ids: the game hands out a new id whenever a player comes into view again, so a name seen under a second id is the same player unless the servers, the classes or the permanent character ids say otherwise.</summary>
public static class SelfCheckIdentity
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] One player under several combat ids:");
        bool ok = true;

        var directory = new Aion2EntityDirectory();
        var moves = new List<(int Old, int New)>();
        directory.IdentityMoved += (o, n) => moves.Add((o, n));

        // the nickname frame knows no server: the same name under a second id is the same player
        directory.Register(1, "Aahz");
        directory.NoteIdentity(1, "Aahz", 0);
        directory.Register(2, "Aahz");
        directory.NoteIdentity(2, "Aahz", 0);
        bool moved = moves.SequenceEqual(new[] { (1, 2) });
        Console.WriteLine($"  -> a name that comes back under a new combat id is the same player: {moved}");
        ok &= moved;

        // an id handed to another player must not keep the first one's name
        directory.Register(2, "Kuro");
        bool recycled = directory.NameFor(2) == "Kuro";
        Console.WriteLine($"  -> an id that now belongs to another player carries the new name: {recycled}");
        ok &= recycled;

        // two classes under one name are two players
        moves.Clear();
        directory.Register(10, "Namesake");
        directory.NoteClass(10, "Gladiator");
        directory.NoteIdentity(10, "Namesake", 0);
        directory.Register(11, "Namesake");
        directory.NoteClass(11, "Ranger");
        directory.NoteIdentity(11, "Namesake", 0);
        bool classes = moves.Count == 0;
        Console.WriteLine($"  -> the same name on two classes is not merged: {classes}");
        ok &= classes;

        // an appearance frame names the server: another server is another player
        directory.Register(20, "Bob");
        directory.NoteIdentity(20, "Bob", 1304);
        directory.Register(21, "Bob");
        directory.NoteIdentity(21, "Bob", 2304);
        bool servers = moves.Count == 0;
        Console.WriteLine($"  -> the same name on two servers is not merged: {servers}");
        ok &= servers;

        // the permanent character id from a player list: a name listed for two servers stays open without a server in the frame
        directory.NoteCharacterId("Cid", 1304, 210000);
        directory.NoteCharacterId("Cid", 2304, 300000);
        directory.Register(30, "Cid");
        directory.NoteIdentity(30, "Cid", 0);
        directory.Register(31, "Cid");
        directory.NoteIdentity(31, "Cid", 0);
        bool listed = moves.Count == 0;
        Console.WriteLine($"  -> a name listed for two servers is not merged without a server in the frame: {listed}");
        ok &= listed;

        // a player list frame: Aahz (1304, id 211765) and a legion (id 397) behind him
        var frame = new List<byte> { 0x03, 0x60, 0, 0, 0xED, 0xE0, 0x08, 0x03, 0x01, 0x80, 0x33, 0, 0 };
        frame.AddRange(new byte[] { 0x18, 0x05, 0x35, 0x3B, 0x03, 0x00, 0x00, 0x00, 0x18, 0x05, 0x04 });
        frame.AddRange(Encoding.UTF8.GetBytes("Aahz"));
        frame.AddRange(new byte[] { 0x18, 0x05, 0x8D, 0x01, 0x00, 0x00, 0x00, 0x00, 0x18, 0x05, 0x06 });
        frame.AddRange(Encoding.UTF8.GetBytes("Legion"));
        frame.AddRange(new byte[] { 0, 0, 0, 0 });
        var listing = new Aion2EntityDirectory();
        var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), listing);
        decoder.Decode(frame.ToArray(), new DateTime(2026, 10, 13, 20, 0, 0));
        bool decoded = listing.CharacterIdOf("Aahz", 1304) == 211765 && listing.CharacterIdOf("Legion", 1304) == 0;
        Console.WriteLine($"  -> a player list gives the permanent character id of a player, a legion in it is skipped: {decoded}");
        ok &= decoded;

        return ok;
    }
}
