using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using AionSniffer.Combat;

namespace AionSniffer.ChatLog;

/// <summary>
/// Turns Chat.log lines into DamageEvents (damage AND heals, incoming AND outgoing). This is the
/// chat-log-based fallback path (see README "Netzwerk vs. Chat-Log"): the game-stream cipher on
/// port 7777 turned out to sit behind a commercial packer (aegisty64/SecureEngineSDK64) on both
/// the 32- and 64-bit client, so the key only ever exists in the unpacked, running process --
/// reaching it would mean live-process memory access, which is the one thing ruled out from the
/// start (anti-cheat/ban risk). This parser reads the same plaintext log file the client already
/// writes for itself.
///
/// Built and validated against a real ~44k-line OriginAion play session (2026-08-24), not just
/// hand-picked samples: every regex below was run against the full file first (a throwaway Python
/// port of the same patterns, since dotnet isn't available in this environment) to measure actual
/// coverage before writing this. Result: 95.5% of all lines containing "damage" or "HP" are
/// attributed by exactly one of the patterns below, zero double-matches. The remaining ~4.5% is
/// DELIBERATELY unhandled, not missed -- see "Known, deliberate gaps" below.
///
/// That full-file validation is also what caught ReflectedDamagePattern's necessity: without it,
/// "Your attack on X was reflected and inflicted N damage on you." satisfied the general
/// DamagePattern too (a garbled attacker capture, target literally "you" lowercase) and silently
/// split the local player's identity into "You" and "you" as two different rows in the name
/// registry -- found only by cross-checking the full distinct-name list after a first pass, not by
/// reasoning about the regex in isolation.
///
/// Number format: Aion writes amounts with "." as a THOUSANDS separator, not a decimal point --
/// "1.911" is 1911, "514.675" is 514675. Verified across the full real session: every amount
/// inside a matched line groups cleanly into 3-digit chunks after the first, zero exceptions (the
/// only non-conforming "."-numbers in the file are dates and LFG chat's embedded RGB/position
/// coordinates, which never appear inside a matched line shape). An earlier version of this parser
/// used `\d+` for the amount, which would have silently read "1.911" as just "1" -- a ~1000x
/// undercount on every critical hit, not a rejected/skipped line, so nothing would have flagged it.
///
/// Known, deliberate gaps (real, frequent shapes -- not overlooked, just not attributable or not
/// worth the added regex complexity for their share of real traffic):
///   "Drakan Crewhand received 2.649 damage due to the effect of Spray Drana Acid."  (DoT tick,
///     no character named as the source -- only a skill/effect name. Could be the player's own DoT
///     or someone else's; guessing would misattribute roughly as often as it helps. ~9% of lines.)
///   "You receive 56 damage due to Fire Strike."               (same problem, present tense)
///   "Your HP has been boosted by using Improved Stamina I."   (buff notice, no concrete amount)
///   "You inflicted continuous damage on X by using Y."        (DoT application notice, no amount)
///   "Naduka...inflicted 460 damage AND the rune carve effect on..." (compound-effect phrasing,
///     &lt;0.2% of lines -- different literal structure around "on", not worth a second branch for)
/// </summary>
public sealed partial class ChatLogParser
{
    private const string YouName = "You";

    // "Your attack on Torch Spirit Iprita was reflected and inflicted 246 damage on you." --
    // MUST be tried before DamagePattern for the same reason as DamageInflictedOnYouPattern: this
    // sentence also satisfies DamagePattern's shape (a garbled "attacker" capture swallowing "Your
    // attack on X was reflected and", target "you" lowercase) and was caught doing exactly that
    // during validation against the full real log -- it silently split the local player's identity
    // into "You" and "you" as two different rows. The mob whose reflect caused this becomes the
    // source; it dealt the damage back, even though the sentence is phrased around "your attack".
    // The leading "Critical Hit!" prefix is included defensively (0 occurrences in the ~44k-line
    // validation file, unlike the other two "on you" patterns below where it was real) -- cheap to
    // cover and the alternative is the exact same identity-split bug resurfacing the moment it does.
    [GeneratedRegex(@"^(?:Critical Hit!\s?)?Your attack on (?<attacker>.+) was reflected and inflicted (?<amount>[\d.]+) damage on you\.$")]
    private static partial Regex ReflectedDamagePattern();

    // Outgoing damage, the general/catch-all case -- tried LAST among the damage patterns since
    // it's the most permissive one (see DamageInflictedOnYouPattern remarks for why order here
    // matters, not just style). Covers, all confirmed against real lines: plain hits ("X inflicted
    // N damage on Y."), skill hits ("...by using Skill."), reflects ("...by reflecting the
    // attack."), titled names ("...on Popochina the Bruiser..."), and both real crit phrasings --
    // "Critical Hit!X inflicted N damage on Y by using Skill." (no space, skill-based attacks) and
    // "Critical Hit! X inflicted N critical damage on Y." (space, "critical" in the sentence,
    // basic/auto-attacks; confirmed via a full-file scan that these two phrasings correlate near-
    // perfectly with the space/no-space distinction, but both optional markers are independent
    // here so an unexpected combination still parses instead of failing closed).
    // Trailing clause captures the skill name specifically when it's "by using X" (the shape that
    // reveals what the attacker just cast) and falls back to matching-but-not-naming anything else
    // ("by reflecting the attack.") -- used both to auto-detect the local player's class from
    // which skill "You" just used, and to detect OTHER players' classes the same way, see
    // MainWindow's SkillUsed subscription.
    [GeneratedRegex(@"^(?:Critical Hit!\s?)?(?<attacker>.+) inflicted (?<amount>[\d.]+)(?: critical)? damage on (?<target>.+?)(?: by (?:using (?<skill>.+)|.+))?\.$")]
    private static partial Regex DamagePattern();

    // Incoming damage, skill-attributed form: "Icy Kalgolem has inflicted 994 damage on you by
    // using Power Attack." MUST be tried before DamagePattern: its text also satisfies
    // DamagePattern's shape (literally "X inflicted N damage on <target> by using Y"), which would
    // otherwise register the literal target text "you" as a character distinct from the "You"
    // used everywhere else -- silently splitting one identity (the local player) into two rows.
    // Canonicalizes to YouName rather than trusting the log's lowercase "you" for exactly that
    // reason. Leading "Critical Hit!" prefix (attacker side here) confirmed for real in a second,
    // larger Chat.log by terminal_windows after this file's first version shipped without it --
    // same identity-split failure mode as the "on you" fix above, just missed here initially
    // because the small validation sample happened not to include an incoming crit.
    [GeneratedRegex(@"^(?:Critical Hit!\s?)?(?<attacker>.+) has inflicted (?<amount>[\d.]+) damage on you(?: by .+)?\.$")]
    private static partial Regex DamageInflictedOnYouPattern();

    // Incoming damage, basic-attack form: "You received 172 damage from Icy Kalgolem." Subject
    // isn't always "You" -- a controlled pet/summon (seen in real data as "Superclyde") takes hits
    // this way too, hence a captured target rather than a hardcoded YouName. Leading "Critical
    // Hit!" prefix attaches to the SUBJECT here ("Critical Hit!You received..."), not the
    // attacker -- confirmed for real (16 occurrences) in the same second, larger Chat.log; without
    // this, that prefix would have landed inside the target capture and split the local player's
    // identity into "You" and "Critical Hit!You" on the TARGET side this time, which is worse for
    // the GUI than the source-side version: it shows up in the Mob/Boss dropdown, not just Summarize.
    [GeneratedRegex(@"^(?:Critical Hit!\s?)?(?<target>.+) received (?<amount>[\d.]+) damage from (?<attacker>.+)\.$")]
    private static partial Regex DamageReceivedPattern();

    // DoT tick with an actual attributable source: "Engeius received 143 bleeding damage after you
    // used Rending Bite." -- unlike the unattributed "due to the effect of" DoT phrasing (see class
    // remarks), "after you used X" names the caster explicitly (always "you" in real data: the log
    // is from that player's own perspective, so only their own applied DoTs get this phrasing).
    // Leading "Critical Hit!" prefix included for the same reason as DamageReceivedPattern (same
    // subject-side attachment point), though not separately confirmed for this specific pattern --
    // cheap insurance against the identical failure mode rather than a second observed instance.
    // Skill name captured here too: source is always "You" for this pattern, so this is another
    // real signal for auto-detecting the local player's class (see DamagePattern's remarks).
    [GeneratedRegex(@"^(?:Critical Hit!\s?)?(?<target>.+) received (?<amount>[\d.]+)(?: \w+)? damage after you used (?<skill>.+)\.$")]
    private static partial Regex DotDamageAttributedToYouPattern();

    // "You restored 95 of Hestika's HP by using Major Recovery Potion." -- healer is always "You"
    // in this specific phrasing (the log only narrates the local player's own outgoing heals this
    // way), target is named explicitly. Skill name captured for the same class-auto-detect reason.
    [GeneratedRegex(@"^You restored (?<amount>[\d.]+) of (?<target>.+)'s HP by using (?<skill>.+)\.$")]
    private static partial Regex HealOtherPattern();

    // "Superclyde recovered 1.234 HP because Gsghost used Healing Light V." / "You recovered 157
    // HP because Gsghost used Healing Light VI on you." -- third person names the target plainly;
    // first person redundantly appends "on you" to the same template. One pattern covers both by
    // making that suffix optional. Tried before HealSelfPattern: its "because ..." clause can't
    // match HealSelfPattern's stricter shape anyway, but keeping the more specific pattern first is
    // the same defensive ordering principle as the damage patterns above.
    [GeneratedRegex(@"^(?<target>.+) recovered (?<amount>[\d.]+) HP because (?<healer>.+) used .+?(?: on you)?\.$")]
    private static partial Regex HealByOtherPattern();

    // Self-heal (own skill/potion/regen tick), ANY character -- not just "You": real data shows
    // e.g. "Mortelle recovered 156 HP by using Healing Light I." and "Sandra recovered 12 HP by
    // using Light of Renewal I." for other visible party members healing themselves, not just the
    // local player. Both "recovered" and "restored" occur for this exact shape (no semantic
    // difference found in the data -- "Hestika restored 154 HP." vs "You recovered 1.160 HP." are
    // structurally identical). Skill name optional: some real lines are bare "You recovered N HP."
    // with no attribution at all.
    // Skill name captured the same way as DamagePattern's trailing clause, for the same reason:
    // "who" is often "You" here, and the skill used to self-heal is just as good a class signal.
    [GeneratedRegex(@"^(?<who>.+) (?:recovered|restored) (?<amount>[\d.]+) HP(?: by (?:using (?<skill>.+)|.+))?\.$")]
    private static partial Regex HealSelfPattern();

    public PlayerNameRegistry Names { get; } = new();

    /// <summary>
    /// Fires whenever a parsed line names both an actor and the skill they used -- "You" for the
    /// local player (the signal the GUI uses to auto-detect which of the user's registered
    /// characters is currently active, since Chat.log has no other way to say who "You" is; see
    /// MeterSettings.Characters remarks), or a real name for anyone else's skill (used by the GUI
    /// to detect OTHER players' classes for the grid's icon column, per the user -- "es wurde
    /// keine Klasse der anderen Spieler erkannt"). Originally "You"-only (hence the generic name
    /// staying close to that history); generalized once the same signal turned out useful beyond
    /// auto-detecting the local player.
    /// </summary>
    public event Action<string, string>? SkillUsed;

    // AionRainMeter-style in-game commands: the user types e.g. ".ui" into an in-game chat box,
    // Aion writes it to Chat.log like any other chat message, and an external tool watching the
    // log reacts. A leading "\." preceded by a word character is excluded so this never fires on
    // ordinary numbers ("3.123" -- the digit after "." rules it out on its own) or on a dotted
    // abbreviation glued to a word.
    [GeneratedRegex(@"(?<!\w)\.(?<cmd>[a-zA-Z]+)\b[ \t]*(?<args>\S.*)?")]
    private static partial Regex CommandPattern();

    // Real chat lines (any channel, confirmed by terminal_windows against a genuine ~69k-line
    // session) carry the speaker's name machine-readably as "[charname:NAME;<color>]" -- this is
    // what makes CommandReceived safe to act on at all. Found the hard way: an unrestricted
    // version of this feature (matching ".word" anywhere, no speaker check) was validated against
    // that same real file and turned up 6 real dot-commands from OTHER players in public LFG chat
    // (".gear" x3, ".l", ".decompose", ".der") -- a stranger typing ".cleardmg" in LFG would have
    // silently wiped the local user's whole session with no visible cause. This capture lets the
    // caller compare against the locally active character and refuse anything else.
    [GeneratedRegex(@"\[charname:(?<charname>[^;\]]+)")]
    private static partial Regex ChatSpeakerPattern();

    // Fallback for channels that DON'T write the "[charname:...]" block -- found by
    // terminal_windows from the user's own real ".ui" attempts: a channel like
    // "[4.Chanter] Name: text" writes it only ONCE, with no charname block at all, so
    // ChatSpeakerPattern alone left every command from that channel stuck at fail-closed with no
    // visible reason. (Group chat, per the user, writes it TWICE in two different shapes -- plain
    // and with the charname block -- because he runs two Aion clients at once with both characters
    // in the same group, so each client logs the same broadcast in its own format; see the
    // per-bucket command dedup below for why that duplication matters here too.) Anchored to the
    // start of the line specifically
    // so this can't misfire mid-sentence -- a real chat line always opens with an optional
    // "[N.Channel] " tag followed immediately by "Name:", which ordinary damage/heal/system lines
    // never do (none of ChatLogParser's other patterns produce a line shaped like that).
    [GeneratedRegex(@"^(?:\[\d+\.\w+\]\s)?(?<charname>[^:\[\]]+):")]
    private static partial Regex ChatSpeakerFallbackPattern();

    /// <summary>
    /// Fires for every ".word[ args]" token found in any parsed line, command name lowercased,
    /// together with the speaker's name if one could be extracted (null if not -- e.g. an
    /// unrecognized line shape). Deliberately NOT filtered to "the local player" in here:
    /// ChatLogParser has no notion of which registered CharacterProfile is currently active, only
    /// MainWindow does (see _activeCharacterName) -- that comparison, and failing closed when
    /// speaker is null, belongs on the subscriber, not here.
    /// </summary>
    public event Action<string?, string, string>? CommandReceived;

    // Personal stats (Exp/AP/GP/Kinah) for the footer's "-" placeholders (see MainWindow.xaml
    // remarks on that row) -- confirmed real, regular sentence shapes by terminal_windows against
    // the same real session used to validate everything else in this file: "You have gained
    // N Abyss Points.", "You have lost N Abyss Points." (0 occurrences there, but the shape is the
    // obvious symmetric case and cheap to cover), "You have earned N Kinah.", "You spent N Kinah.",
    // "You have gained N XP from <Mob>." (mob name irrelevant to the running total, not captured),
    // and (found later, from a quest reward the user specifically remembered getting) "You have
    // gained N Glory Points." -- same shape as Abyss Points, "lost" variant unconfirmed (GP can
    // fall via rank decay in Aion, so a "lost" line plausibly exists, but not built without ever
    // having seen one; see this file's own repeated warnings against unvalidated patterns).
    // Only ever "You" -- these never occur for other players, unlike damage/heal, so unlike those
    // this has nothing to attribute per-row; it's a running total, not a PlayerNameRegistry lookup.
    [GeneratedRegex(@"^You have gained (?<amount>[\d.]+) Abyss Points?\.$")]
    private static partial Regex ApGainedPattern();

    [GeneratedRegex(@"^You have lost (?<amount>[\d.]+) Abyss Points?\.$")]
    private static partial Regex ApLostPattern();

    [GeneratedRegex(@"^You have earned (?<amount>[\d.]+) Kinah\.$")]
    private static partial Regex KinahEarnedPattern();

    [GeneratedRegex(@"^You spent (?<amount>[\d.]+) Kinah\.$")]
    private static partial Regex KinahSpentPattern();

    [GeneratedRegex(@"^You have gained (?<amount>[\d.]+) XP from .+\.$")]
    private static partial Regex XpGainedPattern();

    [GeneratedRegex(@"^You have gained (?<amount>[\d.]+) Glory Points?\.$")]
    private static partial Regex GpGainedPattern();

    /// <summary>Fires once per matched personal-stat line, signed (positive for a gain, negative
    /// for a loss/spend) so the subscriber can just accumulate a running total per kind.</summary>
    public event Action<PersonalStatKind, long>? PersonalStatChanged;

    // Loot: "You have acquired [item:ID;...]." and its variants. Every literal below (including
    // the two DIFFERENT plural markers) was confirmed by terminal_windows against real lines, not
    // guessed -- "(s)" with literal parens ("acquired 5 [item:...](s).") and a bare "s" with none
    // ("acquired 20 [item:...]s and stored them..."), both real, a pattern that only knew one
    // would silently lose the other. The item tag itself is captured whole (as "tag", not just
    // "id") because it must be reproducible byte-for-byte for the ".loot" copy command -- pasting
    // the exact tag back into an Aion chat box is what makes it render as a clickable item there,
    // per the user; a rebuilt tag missing the original's suffix would not. That suffix is NOT
    // always ";ver6;;;;": real data also has a bare "[item:ID]" with nothing after the id (occurs
    // for "X has acquired" lines about pets/other characters) and, in a few lines, a suffix packed
    // with literal 0x3F ('?') bytes (confirmed on the byte level, not a mis-decoded character) --
    // so the id-then-anything-but-']' capture below is deliberate, not a shortcut.
    [GeneratedRegex(@"^(?<subject>.+?) (?:have|has) acquired (?:(?<qty>\d+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\])(?:\(s\)|s)?(?: and stored (?:it|them) in your special cube)?\.$")]
    private static partial Regex LootAcquiredPattern();

    // Survey reward: different verb and tail than "acquired" ("You received ... as reward for the
    // survey."), confirmed always "You" in real data (never seen for another character) so the
    // subject isn't captured, just assumed.
    [GeneratedRegex(@"^You received (?:(?<qty>\d+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\]) items? as reward for the survey\.$")]
    private static partial Regex LootSurveyPattern();

    /// <summary>Fires once per matched loot line -- see LootEvent remarks. Deliberately excludes
    /// "You have purchased [item:...]." (a shop purchase, not loot, per the user/terminal_windows)
    /// by simply not matching it with either pattern above; a purchased item never raises this.
    /// Chat/trade spam ("[3.LFG] ... WTS [item:...] ...") is excluded by Parse() checking the raw
    /// message doesn't start with "[" before trying either pattern -- every confirmed real loot
    /// line starts with a bare subject name, never a channel/charname tag, so that's a precise,
    /// not just a defensive, filter.</summary>
    public event Action<LootEvent>? LootAcquired;

    // Instance fields, not locals inside Parse(): ChatLogTailer calls Parse() repeatedly with
    // successive small batches of newly-appended lines (live tailing), not once with the whole
    // file. Bucket state must survive across those calls, or a duplicate broadcast pair split
    // across two poll ticks (the second copy arriving in a later batch than the first) would no
    // longer be recognized as a duplicate -- the dedup window would effectively shrink to "within
    // one poll interval" instead of "within one second", silently regressing the guarantee
    // documented below. A one-shot full-file Parse() call still works identically either way.
    private DateTime _bucketTimestamp;
    private readonly HashSet<string> _seenThisBucket = new();

    // Separate from _seenThisBucket above: that one dedups identical RAW TEXT, but group chat's
    // double-write (see ChatSpeakerFallbackPattern remarks) logs the SAME real command as two
    // lines with DIFFERENT text -- once plain, once with a "[charname:...]" block -- so the raw
    // text guard never catches it. Found by terminal_windows testing ".ui" for real: without this,
    // a toggle command fires twice in the same second and appears to do nothing (on, then
    // immediately off again). Keyed on speaker+command, not raw text, and cleared on the same
    // timestamp-bucket boundary as _seenThisBucket.
    private readonly HashSet<string> _commandsSeenThisBucket = new();

    /// <summary>
    /// Two clients sharing one Chat.log (see PlayerNameRegistry remarks) log every server-wide
    /// broadcast twice, byte-for-byte, at the same one-second timestamp -- confirmed in a real
    /// capture where an unrelated combat line from the OTHER client landed physically between the
    /// two copies (so a simple "skip if same as the previous line" check would have missed it).
    /// Guards against that specific, observed pattern: an exact duplicate message within the same
    /// timestamp is dropped. Never observed for genuine damage lines in practice (only one client
    /// was ever fighting at a time), but cheap to guard against since the evidence for it is
    /// sitting in our own capture.
    /// </summary>
    public List<DamageEvent> Parse(IEnumerable<string> lines)
    {
        var events = new List<DamageEvent>();

        foreach (string rawLine in lines)
        {
            var entry = ChatLogLineParser.TryParse(rawLine);
            if (entry is not { } e)
            {
                continue;
            }

            if (e.Timestamp != _bucketTimestamp)
            {
                _bucketTimestamp = e.Timestamp;
                _seenThisBucket.Clear();
                _commandsSeenThisBucket.Clear();
            }

            if (!_seenThisBucket.Add(e.Message))
            {
                continue; // exact duplicate within the same second -- second client's copy
            }

            if (TryParseDamageOrHeal(e.Message, out int sourceId, out int targetId, out long amount, out bool isHeal))
            {
                events.Add(new DamageEvent(e.Timestamp, sourceId, targetId, amount, isHeal));
            }

            if (CommandPattern().Match(e.Message) is { Success: true } commandMatch)
            {
                string? speaker = ChatSpeakerPattern().Match(e.Message) is { Success: true } speakerMatch
                    ? speakerMatch.Groups["charname"].Value
                    : ChatSpeakerFallbackPattern().Match(e.Message) is { Success: true } fallbackMatch
                        ? fallbackMatch.Groups["charname"].Value
                        : null;
                string cmd = commandMatch.Groups["cmd"].Value.ToLowerInvariant();

                // Same command from the same (possibly null) speaker, already fired this second --
                // skip re-firing it (see field remarks above for why this is needed, not defensive).
                if (_commandsSeenThisBucket.Add($"{speaker} {cmd}"))
                {
                    CommandReceived?.Invoke(
                        speaker,
                        cmd,
                        commandMatch.Groups["args"].Success ? commandMatch.Groups["args"].Value : "");
                }
            }

            RaisePersonalStatIfPresent(e.Message);

            // Chat/trade spam ("[3.LFG] ...", "[charname:...]: WTS [item:...] ...") always starts
            // with a channel or charname tag -- every confirmed real loot line starts with a bare
            // subject name instead, so this alone keeps trade chat out without needing to parse it.
            if (!e.Message.StartsWith('['))
            {
                RaiseLootIfPresent(e.Message);
            }
        }

        return events;
    }

    /// <summary>See the Ap/Kinah/Xp *Pattern remarks and PersonalStatChanged's own remarks --
    /// tried in this order for no particular reason (none of these five shapes can match more
    /// than one of the five patterns), unlike TryParseDamageOrHeal's order, which matters.</summary>
    private void RaisePersonalStatIfPresent(string message)
    {
        if (ApGainedPattern().Match(message) is { Success: true } apGained)
        {
            PersonalStatChanged?.Invoke(PersonalStatKind.AbyssPoints, ParseGroupedAmount(apGained.Groups["amount"].Value));
        }
        else if (ApLostPattern().Match(message) is { Success: true } apLost)
        {
            PersonalStatChanged?.Invoke(PersonalStatKind.AbyssPoints, -ParseGroupedAmount(apLost.Groups["amount"].Value));
        }
        else if (KinahEarnedPattern().Match(message) is { Success: true } kinahEarned)
        {
            PersonalStatChanged?.Invoke(PersonalStatKind.Kinah, ParseGroupedAmount(kinahEarned.Groups["amount"].Value));
        }
        else if (KinahSpentPattern().Match(message) is { Success: true } kinahSpent)
        {
            PersonalStatChanged?.Invoke(PersonalStatKind.Kinah, -ParseGroupedAmount(kinahSpent.Groups["amount"].Value));
        }
        else if (XpGainedPattern().Match(message) is { Success: true } xpGained)
        {
            PersonalStatChanged?.Invoke(PersonalStatKind.Experience, ParseGroupedAmount(xpGained.Groups["amount"].Value));
        }
        else if (GpGainedPattern().Match(message) is { Success: true } gpGained)
        {
            PersonalStatChanged?.Invoke(PersonalStatKind.GloryPoints, ParseGroupedAmount(gpGained.Groups["amount"].Value));
        }
    }

    /// <summary>See LootAcquiredPattern/LootSurveyPattern remarks. Tried in this order for no
    /// particular reason -- a survey-reward line's "received ... as reward for the survey" tail
    /// never matches the "acquired" shape.</summary>
    private void RaiseLootIfPresent(string message)
    {
        if (LootAcquiredPattern().Match(message) is { Success: true } acquired)
        {
            LootAcquired?.Invoke(new LootEvent(
                acquired.Groups["subject"].Value,
                int.Parse(acquired.Groups["id"].Value),
                acquired.Groups["tag"].Value,
                acquired.Groups["qty"].Success ? ParseGroupedAmount(acquired.Groups["qty"].Value) : 1));
        }
        else if (LootSurveyPattern().Match(message) is { Success: true } survey)
        {
            LootAcquired?.Invoke(new LootEvent(
                YouName,
                int.Parse(survey.Groups["id"].Value),
                survey.Groups["tag"].Value,
                survey.Groups["qty"].Success ? ParseGroupedAmount(survey.Groups["qty"].Value) : 1));
        }
    }

    /// <summary>
    /// Order matters: more specific patterns first, the broad catch-alls (DamagePattern,
    /// HealSelfPattern) last -- see DamageInflictedOnYouPattern's remarks for a concrete case
    /// (incoming skill damage) where checking the general pattern first would silently misattribute
    /// events, not just fail to match.
    /// </summary>
    private bool TryParseDamageOrHeal(string message, out int sourceId, out int targetId, out long amount, out bool isHeal)
    {
        Match match;

        if ((match = ReflectedDamagePattern().Match(message)).Success)
        {
            isHeal = false;
            sourceId = Names.GetOrAssignId(match.Groups["attacker"].Value);
            targetId = Names.GetOrAssignId(YouName);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            return true;
        }

        if ((match = DamageInflictedOnYouPattern().Match(message)).Success)
        {
            isHeal = false;
            sourceId = Names.GetOrAssignId(match.Groups["attacker"].Value);
            targetId = Names.GetOrAssignId(YouName);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            return true;
        }

        if ((match = DamageReceivedPattern().Match(message)).Success)
        {
            isHeal = false;
            sourceId = Names.GetOrAssignId(match.Groups["attacker"].Value);
            targetId = Names.GetOrAssignId(match.Groups["target"].Value);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            return true;
        }

        if ((match = DotDamageAttributedToYouPattern().Match(message)).Success)
        {
            isHeal = false;
            sourceId = Names.GetOrAssignId(YouName);
            targetId = Names.GetOrAssignId(match.Groups["target"].Value);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            RaiseSkillUsedIfPresent(match, YouName);
            return true;
        }

        if ((match = DamagePattern().Match(message)).Success)
        {
            isHeal = false;
            sourceId = Names.GetOrAssignId(match.Groups["attacker"].Value);
            // Canonicalize a literal lowercase "you" target the same way
            // DamageInflictedOnYouPattern already does for its own target -- found necessary by
            // terminal_windows via the Spiritmaster-pet feature: "X inflicted N damage on you."
            // (no "has", 38 real occurrences alongside 678 with "has" and 3222 "received" for the
            // same real target) hits this general pattern instead of DamageInflictedOnYouPattern,
            // and without this, "you" registers as a SEPARATE identity from "You" -- the exact
            // same identity-split failure this file's own remarks describe for the reflect-damage
            // case above, just not caught here until a feature (pet-damage attribution) that
            // specifically checks "is the target You" made it externally visible.
            string targetName = match.Groups["target"].Value;
            targetId = Names.GetOrAssignId(string.Equals(targetName, "you", StringComparison.OrdinalIgnoreCase) ? YouName : targetName);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            RaiseSkillUsedIfPresent(match, match.Groups["attacker"].Value);
            return true;
        }

        if ((match = HealOtherPattern().Match(message)).Success)
        {
            isHeal = true;
            sourceId = Names.GetOrAssignId(YouName);
            targetId = Names.GetOrAssignId(match.Groups["target"].Value);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            RaiseSkillUsedIfPresent(match, YouName);
            return true;
        }

        if ((match = HealByOtherPattern().Match(message)).Success)
        {
            isHeal = true;
            sourceId = Names.GetOrAssignId(match.Groups["healer"].Value);
            targetId = Names.GetOrAssignId(match.Groups["target"].Value);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            return true;
        }

        if ((match = HealSelfPattern().Match(message)).Success)
        {
            isHeal = true;
            sourceId = Names.GetOrAssignId(match.Groups["who"].Value);
            targetId = sourceId;
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            RaiseSkillUsedIfPresent(match, match.Groups["who"].Value);
            return true;
        }

        sourceId = targetId = 0;
        amount = 0;
        isHeal = false;
        return false;
    }

    /// <summary>actorName is whoever the pattern says performed the action -- "You" literally for
    /// the three patterns where that's the only possibility, or the captured attacker/healer name
    /// otherwise (which happens to BE "You" when the local player is the one acting, same as
    /// before this was generalized beyond "You"). A mob's skill name simply won't match anything
    /// in SkillDatabase, so raising this unconditionally for every actor is safe.</summary>
    private void RaiseSkillUsedIfPresent(Match match, string actorName)
    {
        if (match.Groups["skill"] is { Success: true } skillGroup)
        {
            SkillUsed?.Invoke(actorName, skillGroup.Value);
        }
    }

    /// <summary>Strips Aion's "." thousands separators and parses the result -- see class remarks
    /// for why this can't just be long.Parse on the raw text.</summary>
    private static long ParseGroupedAmount(string raw) =>
        long.Parse(raw.Replace(".", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture);

    public List<DamageEvent> ParseFile(string path) => Parse(File.ReadLines(path));
}
