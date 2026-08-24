using System.IO;
using AionSniffer.Combat;

namespace AionSniffer.ChatLog;

/// <summary>
/// Watches Chat.log going forward ONLY -- never the past. Explicit rule from the user: a
/// DMG-meter "recording" must behave like a tape recorder switched on now, not an archive
/// scanner that happens to start reading at the end. Seeks to the file's current length on
/// construction and never looks before that point, for the life of the instance.
/// </summary>
public sealed class ChatLogTailer
{
    private readonly string _path;
    private readonly ChatLogParser _parser;
    private long _position;

    public ChatLogTailer(string path, ChatLogParser parser)
    {
        _path = path;
        _parser = parser;
        _position = new FileInfo(path).Length;
    }

    /// <summary>
    /// Reads whatever text was appended since the last call (or since construction, on the first
    /// call) and advances the read position regardless of <paramref name="paused"/> -- paused
    /// time is discarded, not deferred, so resuming never replays what happened while paused (the
    /// same "tape recorder" reasoning as the initial seek-to-end above: pausing a recorder and
    /// unpausing it later does not play back what happened in between). Returns an empty list of
    /// DAMAGE events while paused, or when nothing new was appended -- newly appended lines are
    /// still always parsed even while paused (only the resulting damage events are discarded),
    /// specifically so ChatLogParser.CommandReceived still fires: the in-game ".resume" command has
    /// to work precisely while paused, or it could never un-pause anything. That doesn't violate
    /// the "never look into the past" rule -- these are still only ever newly-appended lines, never
    /// history; the rule is about not recording damage from before recording started/resumed, not
    /// about ignoring live control commands.
    ///
    /// Opens the file fresh on every call (FileShare.ReadWrite, since the running Aion client
    /// holds it open for writing) rather than keeping a long-lived handle -- simpler, and this is
    /// only ever called once a second or so from a DispatcherTimer, not in a hot loop.
    /// </summary>
    public List<DamageEvent> Poll(bool paused)
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (stream.Length < _position)
        {
            // The file shrank (log rotated, or the client truncated/recreated it on a fresh
            // launch) -- start over from its new end rather than throwing or seeking past EOF.
            _position = stream.Length;
            return new List<DamageEvent>();
        }

        if (stream.Length == _position)
        {
            return new List<DamageEvent>();
        }

        stream.Seek(_position, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var newLines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            newLines.Add(line);
        }

        // Drained to EOF above, so the stream's true position is now its length -- reading this
        // via stream.Position instead would be unreliable here because of StreamReader's internal
        // read-ahead buffering (fine when draining fully, as we always do, but a known trap
        // otherwise; using Length sidesteps the question entirely).
        _position = stream.Length;

        var parsedEvents = _parser.Parse(newLines);
        return paused ? new List<DamageEvent>() : parsedEvents;
    }
}
