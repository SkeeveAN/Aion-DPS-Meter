using System.IO;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;
using AionDPS.Upload;

namespace AionDPS;

/// <summary>
/// <c>aion2-profile-upload &lt;recording.jsonl&gt; &lt;data-folder&gt; &lt;name&gt;[,&lt;name&gt;...] [--send]</c>: catches up on the profile of players the local
/// player looked at (their character window) but whose profile never went online, for example because the meter crashed first. The data
/// folder is a COPY of the meter's folder (character record, inspected players, settings); the recording tells the faction and the server
/// of those players, which only the live stream knows. Builds the very request the meter's own profile upload builds (the own character
/// with its full profile, plus the named players) and prints it; only with --send it is posted.
/// </summary>
internal static class ProfileUploadTool
{
    private const int ServerPort = 13328;
    private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

    public static void Run(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Usage: AionDPS aion2-profile-upload <recording.jsonl> <data-folder (a copy)> <name[,name...]> [--send]");
            return;
        }

        string recording = args[1], dataDir = args[2];
        var wanted = new HashSet<string>(args[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
        bool send = args.Contains("--send");

        Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", dataDir);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

        var app = new System.Windows.Application();
        var settings = Ui.MeterSettings.Load();
        Ui.ThemeManager.Apply(app, settings.Theme, settings.FontSize);
        Ui.MainWindow.Headless = true;

        var type = typeof(Ui.MainWindow);
        var window = new Ui.MainWindow();
        (type.GetField("_liveSource", Flags)!.GetValue(window) as IDisposable)?.Dispose();
        var source = new Aion2PacketCombatSource(Aion2Protocol.Load(), null, null, Path.Combine(dataDir, "aion2-character.json"));
        type.GetField("_source", Flags)!.SetValue(window, source);
        type.GetField("_liveSource", Flags)!.SetValue(window, source);
        var tick = type.GetMethod("OnPollTimerTick", Flags)!;

        DateTime bucketStart = DateTime.MinValue;
        foreach (TcpSegment segment in SegmentRecording.Read(recording))
        {
            bool fromServer = segment.Source.EndsWith(":" + ServerPort, StringComparison.Ordinal);
            if (!fromServer && !segment.Destination.EndsWith(":" + ServerPort, StringComparison.Ordinal))
            {
                continue;
            }

            source.Ingest(segment with { FromServer = fromServer });
            if (bucketStart == DateTime.MinValue)
            {
                bucketStart = segment.Timestamp;
            }
            else if ((segment.Timestamp - bucketStart).TotalSeconds >= 2)
            {
                bucketStart = segment.Timestamp;
                tick.Invoke(window, new object?[] { null, EventArgs.Empty });
            }
        }

        tick.Invoke(window, new object?[] { null, EventArgs.Empty });

        object? identity = type.GetMethod("ResolveServerIdentity", Flags)!.Invoke(window, null);
        if (identity is null)
        {
            Console.WriteLine("The server of the own character is not known (no character record) - nothing built.");
            Environment.Exit(2);
        }

        var identityType = identity.GetType();
        string fingerprint = (string)identityType.GetField("Item1")!.GetValue(identity)!;
        string? displayName = (string?)identityType.GetField("Item2")!.GetValue(identity);
        var request = type.GetMethod("BuildProfilesUpload", Flags)!.Invoke(window, new object?[] { fingerprint, displayName, false, null }) as ProfilesUploadRequest;
        if (request is null)
        {
            Console.WriteLine("No request could be built.");
            Environment.Exit(2);
        }

        Console.WriteLine($"server {displayName} ({fingerprint}), game server {request.GameServer}, server id {request.ServerId}, client {request.ClientVersion}");
        Console.WriteLine($"the meter's own upload would carry {request.Participants.Count} character(s); picked: self + {string.Join(", ", wanted)}");
        var picked = request.Participants.Where(p => p.IsSelf || wanted.Contains(p.Name)).ToList();
        foreach (var p in picked)
        {
            Console.WriteLine($"  {(p.IsSelf ? "SELF " : "other")} {p.Name,-14} class {p.ClassName,-12} faction '{p.Faction}' server {p.ServerId?.ToString() ?? "?"} guild '{p.Guild}' charId {p.CharacterId?.ToString() ?? "?"} " +
                $"source {p.Profile.Source} level {p.Profile.Level} gear {p.Profile.Gear.Count} skills {p.Profile.Skills.Count} gearscore {p.Profile.GearScore?.ToString() ?? "-"}");
        }

        foreach (string name in wanted.Where(n => picked.All(p => p.Name != n)))
        {
            Console.WriteLine($"  NOT IN THE REQUEST: {name}");
        }

        if (!send)
        {
            Console.WriteLine("dry run - nothing sent (use --send)");
            Environment.Exit(0);
        }

        var result = UploadClient.SendProfilesAsync(request with { Participants = picked }).GetAwaiter().GetResult();
        Console.WriteLine(result.Success ? "SENT, accepted by the server" : $"FAILED: {result.Error}");
        Environment.Exit(result.Success ? 0 : 1);
    }
}
