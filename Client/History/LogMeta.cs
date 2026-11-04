using AionDPS.Aion2;

namespace AionDPS.History;

/// <summary>
/// The capture's own counters at one moment (running totals since the capture started). A saved log keeps the change over a stay, so a reader can tell a gap in the data
/// (packets the capture could not keep, frames that lost their place) from a stretch in which nothing happened.
/// </summary>
public sealed record CaptureCounters(long Frames, long Dropped, long Desyncs, long SkippedFrames, long CallbackErrors)
{
    /// <summary>The change since <paramref name="start"/>; when the capture was restarted in between (the totals went back) this is the end reading itself.</summary>
    public CaptureCounters Since(CaptureCounters start) =>
        Frames < start.Frames
            ? this
            : new CaptureCounters(Frames - start.Frames, Dropped - start.Dropped, Desyncs - start.Desyncs, SkippedFrames - start.SkippedFrames, CallbackErrors - start.CallbackErrors);
}

/// <summary>
/// What the own character's setup looked like at <see cref="TakenAt"/>: the skill lists of the presets with their levels (base and effective), stigmas, specialisation stones
/// and the skill bar, the worn gear with its skill bonuses, the Daevanion boards, the Pantheon and the action bars. <see cref="ListsAt"/> is when the game last sent one of
/// these lists - a reading taken after that moment is the setup of the fight only when nothing changed since, which is why a log holds one at the start and one at the end of a stay.
/// </summary>
public sealed record BuildSnapshot(
    DateTime TakenAt,
    DateTime? ListsAt,
    int Preset,
    IReadOnlyList<Aion2PresetInfo> Presets,
    IReadOnlyList<Aion2EquippedItem> Gear,
    IReadOnlyList<Aion2DaevanionBoard> Daevanion,
    Aion2Pantheon? Pantheon,
    Aion2ActionBars? ActionBars);

/// <summary>What a log is written with besides the events, for a whole session (a stay carries this itself, see <see cref="SessionInfo"/>).</summary>
public sealed record SessionMeta(BuildSnapshot? Build, CaptureCounters? Capture);
