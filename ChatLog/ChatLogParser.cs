using System.Globalization;
using System.IO;
using System.Text;
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
///
/// Multi-language support (German/French/Russian): per the user's explicit instruction ("Alle vier
/// bauen (EN/DE/FR/RU), best-effort") this parser also tries German, French, and Russian sentence
/// shapes for every line the English patterns above don't match. UNLIKE the English patterns,
/// these three are NOT validated against any real Chat.log -- no non-English sample has ever been
/// seen. They were written from general language knowledge, mirroring the English patterns'
/// sentence shapes and capture-group names one-for-one so the same dispatch logic
/// (TryParseWithPatternSet) can run all four languages without duplicating the attribution rules.
/// Confirmed real (via aioncodex.com's own localized skill/item query buckets, byte-size-diffed
/// against each other) that Aion is genuinely localized into exactly these three languages besides
/// English -- other tested locale codes (es/it/pl/tr/us) all silently fall back to one identical
/// default response, i.e. not real localizations. That only proves the DATA exists in four
/// languages, not that these specific SENTENCES are phrased the way this file guesses.
/// Confidence, highest to lowest: German (native-adjacent grammar knowledge) > French > Russian.
/// Russian carries an extra, structural risk beyond just "wrong words": Aion's real sentences would
/// grammatically gender-agree past-tense verbs with the (unknown, per-line) grammatical gender of
/// whoever performed the action -- these patterns therefore accept multiple gender endings
/// (masculine/feminine/neuter) wherever that applies, but the exact endings Aion's own localizers
/// chose, and whether foreign character names decline in the way assumed here, are both unverified.
/// A real non-English Chat.log sample would let all of this move from "best-effort" to "confirmed"
/// the same way the English patterns already are -- until then, expect these three to under-match
/// (miss real lines) more than to mis-match (attribute a line wrongly); the patterns are written
/// tight (anchored start/end) specifically to fail closed rather than guess.
/// </summary>
public sealed partial class ChatLogParser
{
    private const string YouName = "You";

    // ===================================================================================
    // English -- confirmed real, see class remarks. Unchanged from the validated version.
    // ===================================================================================

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
    // The optional "and the <X> effect" clause covers hits that apply a rider on landing: "Badigadi
    // inflicted 568 damage and the rune carve effect on Guard Captain Rohuka by using Rune Carve V."
    // Without it the whole line failed to match (it has no literal "damage on"), silently dropping
    // every such hit -- 1014 lines in the validation log, worth 427.217 damage in a single Sauro
    // run, i.e. 27% of that Assassin's real total. "rune carve" is the only rider observed so far,
    // hence the generic word capture rather than hardcoding it.
    [GeneratedRegex(@"^(?:Critical Hit!\s?)?(?<attacker>.+) inflicted (?<amount>[\d.]+)(?: critical)? damage(?: and the [\w ]+ effect)? on (?<target>.+?)(?: by (?:using (?<skill>.+)|.+))?\.$")]
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
    // Second alternative: "Relock used Aegis Breaker I to deal you 1.234 damage and dispel some of
    // your magical buffs." A plain hit that happens to strip buffs, narrated in a sentence shape of
    // its own. Found in a 1v1 arena log where it carried 18.477 damage -- a fifth of everything the
    // opponent dealt -- and matched nothing, so their row was short by that much.
    [GeneratedRegex(@"^(?:Critical Hit!\s?)?(?:(?<attacker>.+) has inflicted (?<amount>[\d.]+) damage on you(?: by .+)?|(?<attacker>.+?) used (?<skill>.+?) to deal you (?<amount>[\d.]+) damage(?: and .+?)?)\.$")]
    private static partial Regex DamageInflictedOnYouPattern();

    /// <summary>
    /// The cast that starts a damage-over-time effect, English. Carries no damage itself; it is
    /// what makes the ticks below attributable, since those name only the skill. Two real shapes:
    /// third person ("Nicorobin used Flame Cage V to inflict the continuous damage effect on Guard
    /// Captain Ahuradim.") and the local player as victim ("You received continuous damage because
    /// Relock used Erosion VI.").
    /// </summary>
    [GeneratedRegex(@"^(?:(?<caster>.+?) used (?<skill>.+?) to inflict the continuous damage effect on (?<target>.+?)|(?<target>You) received continuous damage because (?<caster>.+?) used (?<skill>.+?))\.$")]
    private static partial Regex DotAnnouncementPattern();

    /// <summary>
    /// A damage-over-time tick, English. Names the skill but never the caster, which is why this
    /// file used to drop them all as unattributable -- documented as a deliberate gap for a long
    /// time. It is only a gap without the announcement above: with it, the skill identifies who
    /// cast it, exactly as the German path has always worked. A second player casting the same
    /// skill on the same target takes ownership from that point, because the effect is replaced
    /// rather than stacked.
    ///
    /// <para>What this was costing: 35.019 damage from one Spiritmaster in a single 1v1 arena
    /// match, and in a Sauro Supply Base run a Sorcerer's Flame Cage ticks across every boss.</para>
    ///
    /// <para>Note the tense. Ticks on someone else read "received", ticks on the local player read
    /// "receive" -- present -- and drop the words "the effect of".</para>
    /// </summary>
    [GeneratedRegex(@"^(?:(?<target>.+?) received (?<amount>[\d.]+)(?: \w+)? damage due to the effect of (?<skill>.+?)|(?<target>You) receive (?<amount>[\d.]+)(?: \w+)? damage due to (?<skill>.+?))\.$")]
    private static partial Regex DotTickPattern();

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

    // ===================================================================================
    // German (DE) -- CONFIRMED for damage (self and on-you), self-heal, heal-by-other, and loot.
    // The user's Chat.log turned out to contain real German lines too (he tested a second client
    // language after French, see the FR block below for how this file shares one log across two
    // simultaneous clients) -- so this is direct real-gameplay evidence, not just the client
    // string table lookup (ids 900389/900390) an earlier pass of this file relied on. That earlier
    // lookup DID correctly predict the single biggest fix -- formal "Ihr/Euch/Euer" address, never
    // informal "du/dir/dich" -- but got the skill-attribution word order wrong; real lines fixed
    // that too. Confirmed real lines (verbatim, only numbers/names vary):
    //   "Ihr habt Übungsziel 450 Schaden zugefügt."                                      (no skill)
    //   "Kritischer Treffer! Ihr habt Übungsziel 1.028 kritischen Schaden zugefügt."     (crit, no skill)
    //   "Ihr habt Übungsziel durch Benutzung von Klinge der Provokation I 577 Schaden zugefügt."
    //   "Kritischer Treffer!Ihr habt Übungsziel durch Benutzung von Wilder Schlag VI 1.418 Schaden zugefügt."
    //   "Katzugawa hat Euch 98 Schaden zugefügt."                                        (on you, no skill)
    //   "Katzugawa hat Euch durch Geheiligter Schlag I 84 Schaden zugefügt."             (on you, with skill)
    //   "Katzugawa hat durch Licht der Erneuerung I 17 TP wiederhergestellt."             (self-heal)
    //   "Healmimi hat 1.156 TP wiederhergestellt, weil Shibui Blitz-Wiederherstellung I eingesetzt hat."
    //   "Ihr habt [item:167000769;ver6;;;;] erhalten."                                    (loot)
    // Corrections this forced beyond the formal-address fix: (1) the skill clause comes BEFORE
    // the amount, not after ("durch Benutzung von X 577 Schaden", not "577 Schaden durch X") --
    // the string-table lookup's two bare-bodied ids never showed a skill-attributed example at
    // all, so this word order was pure guesswork before; (2) German uses "TP" (Trefferpunkte) for
    // HP, not "HP" -- another guess the string table simply had no opportunity to catch since
    // none of its confirmed strings mentioned HP/TP; (3) the general (self-as-attacker) form says
    // "durch Benutzung von X" (through the use of X) while the on-you and self-heal forms say just
    // "durch X" -- a real, confirmed asymmetry between templates, not a simplification; (4) in
    // HealByOtherPatternDe, the healer name must be captured as a single token (\S+), not greedily
    // (.+) -- unlike English's "because X used Y", German's "weil X Y eingesetzt hat" has no
    // keyword between the healer's name and the skill name, so a greedy capture swallows the first
    // word of a multi-word skill name into the healer's name (confirmed by testing against the
    // Shibui/"Blitz-Wiederherstellung I" line above, which mismatched under English's greedy
    // approach); a bare, space-free capture works because Aion character names never contain
    // spaces. Full-log coverage check (candidate lines containing "Schaden"/"TP wiederherge.../
    // "[item:" near "Ihr"/"habt"): 98.9% match (181/183), with the 2 unmatched being the German
    // counterparts of this file's own documented deliberate gaps -- an unattributed effect-only
    // DoT tick ("Ihr erhaltet durch Effekt X 48 Schaden.") and a no-amount continuous-regen notice
    // ("Die TP von X werden ... fortwährend wiederhergestellt.") -- correctly left unmatched
    // rather than guessed at, same as English's and French's equivalents.
    // Still UNCONFIRMED, best-effort: Reflect, the third-party generalization of "received damage"
    // (DamageReceivedPatternDe), the DoT-after-your-own-skill form, healing someone ELSE by name
    // (HealOtherPatternDe -- only self-heals were observed), and AP/GP/Kinah/XP (no such event
    // occurred during the played German session either).
    // ===================================================================================

    [GeneratedRegex(@"^(?:Kritischer Treffer!\s?)?Euer Angriff auf (?<attacker>.+) wurde reflektiert und hat Euch (?<amount>[\d.]+) Schaden zugefügt\.$")]
    private static partial Regex ReflectedDamagePatternDe();

    // Two real sentence shapes, not one: the perfect ("X hat Y ... Schaden zugefügt.") and a
    // present-tense form ("Suno fügt Goldur durch Magische Umkehr VII 1.304 Schaden zu und löst
    // einige der magischen Verstärkungen auf."), the latter used by skills that do something
    // besides damage and therefore carry a trailing clause. Both confirmed against a real German
    // Chat.log from a Cleric's client; before this every present-tense line was silently dropped.
    // Duplicate group names across alternatives are fine in .NET -- the matching branch wins.
    [GeneratedRegex(@"^(?:Kritischer Treffer!\s?)?(?:(?<attacker>Ihr|.+?) (?:habt|hat) (?<target>.+?)(?: durch Benutzung von (?<skill>.+?))? (?<amount>[\d.]+)(?: kritischen)? Schaden zugefügt|(?<attacker>.+?) fügt (?<target>.+?) durch (?<skill>.+?) (?<amount>[\d.]+) Schaden zu(?: und .+?)?)\.$")]
    private static partial Regex DamagePatternDe();

    /// <summary>
    /// Damage redirected onto someone else by a protective effect ("Ein Schutzeffekt überträgt die
    /// von Goldur bei Suno angerichteten 439 Schaden auf Erdgeist."). Must be parsed, because the
    /// ordinary damage line accompanying it reports ZERO ("Goldur hat Suno durch Benutzung von
    /// Durchdringende Welle I 0 Schaden zugefügt.") -- the real number appears nowhere else. Found
    /// in a real German log where a Spiritmaster redirected onto their spirit: 49 such lines, with
    /// the meter showing the protected player as taking 42 hits for 0 damage. Attributed to the
    /// attacker, and to whoever actually absorbed it rather than the original victim.
    /// </summary>
    [GeneratedRegex(@"^Ein Schutzeffekt überträgt die von (?<attacker>.+?) bei .+? angerichteten (?<amount>[\d.]+) Schaden auf (?<target>.+?)\.$")]
    private static partial Regex DamageRedirectedPatternDe();

    /// <summary>
    /// The cast that starts a damage-over-time effect ("Suno hat Kette der Erde V eingesetzt und
    /// Goldur erleidet fortwährend Schaden."). Carries no damage itself; it is what makes the
    /// following ticks attributable, since those name only the skill (see DotTickPatternDe).
    /// </summary>
    [GeneratedRegex(@"^(?:(?<caster>.+?) hat (?<skill>.+?) eingesetzt und (?<target>.+?) erleidet fortwährend Schaden|(?<caster>Ihr) fügt (?<target>.+?) durch (?<skill>.+?) fortwährend Schaden zu)\.$")]
    private static partial Regex DotAnnouncementPatternDe();

    /// <summary>
    /// A damage-over-time tick ("Goldur erhält durch Erosion VI 386 Schaden."). Names the skill but
    /// not who cast it, so by itself it is exactly the unattributable case English's DoT rule
    /// drops. Here it need not be dropped: the German client logs the cast first, so the skill name
    /// identifies the caster (see DotAnnouncementPatternDe). When a second player casts the same
    /// skill on the same target, the newer cast takes over -- it replaces the effect rather than
    /// stacking with it, so the later caster owns every tick from then on.
    /// </summary>
    [GeneratedRegex(@"^(?<target>.+?) erhält durch (?<skill>.+?) (?<amount>[\d.]+) Schaden\.$")]
    private static partial Regex DotTickPatternDe();

    [GeneratedRegex(@"^(?:Kritischer Treffer!\s?)?(?<attacker>.+) hat Euch(?: durch (?<skill>.+?))? (?<amount>[\d.]+) Schaden zugefügt\.$")]
    private static partial Regex DamageInflictedOnYouPatternDe();

    [GeneratedRegex(@"^(?:Kritischer Treffer!\s?)?(?<target>.+) hat (?<amount>[\d.]+) Schaden von (?<attacker>.+) erhalten\.$")]
    private static partial Regex DamageReceivedPatternDe();

    // Two alternatives. The first ("X hat N Schaden erhalten, nachdem Ihr Y eingesetzt habt.") was
    // best-effort guesswork from the start -- still unconfirmed, kept rather than dropped in case
    // it is a real template this project simply has not seen fire yet.
    //
    // The second is confirmed verbatim, from a German Assassin's own client recorded during the
    // same Sauro Supply Base run as the English log used for the rest of this file's validation:
    // "Kritischer Treffer!Susu-Arbeiter erhält durch Euren Einsatz von Siegelgravur V 1.181 Schaden
    // und den Effekt 'Siegelgravur'." It is the German rendering of English's "X inflicted N damage
    // and the rune carve effect on Y" -- note the word order is inverted (target first, "durch
    // Euren Einsatz von" instead of a named attacker), so it could never have matched DamagePatternDe
    // no matter how that one was widened. 3714 lines in that log, 427.217 damage in the Sauro run
    // alone, cross-checked to the unit against the same player's total in two other players'
    // English logs of the same run. The "Kritischer Treffer!" prefix attaches to the TARGET here
    // (same subject-side attachment as English's "Critical Hit!You received...") and must be
    // stripped, or the target's name splits into a crit and a non-crit variant.
    //
    // The trailing effect clause is optional even though every observed line carries one: the
    // damage is what matters and a rider-less variant of the same sentence would otherwise be
    // dropped for a purely cosmetic difference.
    [GeneratedRegex(@"^(?:Kritischer Treffer!\s?)?(?:(?<target>.+) hat (?<amount>[\d.]+)(?: \w+)? Schaden erhalten, nachdem Ihr (?<skill>.+) eingesetzt habt|(?<target>.+?) erhält durch Euren Einsatz von (?<skill>.+?) (?<amount>[\d.]+) Schaden(?: und den Effekt '.+?')?)\.$")]
    private static partial Regex DotDamageAttributedToYouPatternDe();

    [GeneratedRegex(@"^Ihr habt durch (?<skill>.+) (?<amount>[\d.]+) von (?<target>.+)s TP wiederhergestellt\.$")]
    private static partial Regex HealOtherPatternDe();

    // "weil <Name> ... eingesetzt hat" and "weil Ihr ... benutzt habt" are both real: the second
    // is what the log says when the healer is the local player, and without it every heal the
    // user themselves landed on someone else went uncounted.
    [GeneratedRegex(@"^(?<target>.+) hat (?<amount>[\d.]+) TP wiederhergestellt, weil (?:(?<healer>Ihr) .+? benutzt habt|(?<healer>\S+) .+? eingesetzt hat)\.$")]
    private static partial Regex HealByOtherPatternDe();

    [GeneratedRegex(@"^(?<who>Ihr|.+?) (?:habt|hat)(?: durch (?<skill>.+?))? (?<amount>[\d.]+) TP wiederhergestellt\.$")]
    private static partial Regex HealSelfPatternDe();

    // ===================================================================================
    // French (FR) -- CONFIRMED for the core damage/heal-self shapes. The user's Chat.log turned
    // out to contain thousands of real French lines: he runs two simultaneous Aion clients
    // sharing one Chat.log (see PlayerNameRegistry remarks elsewhere in this file), and switched
    // ONE of them to French while the other stayed English -- so this same file, read directly
    // (no client string extraction needed this time), IS the real sample this class's own remarks
    // said would be needed. Confirmed real lines (verbatim, only the numbers/names vary):
    //   "Vous avez infligé 319 points de dégâts à Mannequin d'entraînement en utilisant Tourment I."
    //   "Redamha a infligé 371 points de dégâts à Mannequin d'entraînement."
    //   "Coup critique !Ddx a infligé 1.483 points de dégâts à Mannequin d'entraînement en utilisant Coup divin II."
    //   "Vous avez subi 144 points de dégâts de la part de : Mitzuhiko."
    //   "Vous avez récupéré 17 PV en utilisant Lumière du renouveau I."
    //   "Katzugawa a récupéré 269 PV grâce à Halo de soins II."
    // Two corrections this forced, both parallel to the German fix above: (1) formal address --
    // "Vous avez", never the "Tu as" this file originally guessed (same systemic formal-register
    // convention as German's "Ihr/Euch", just French's own form); (2) "points de dégâts", not
    // bare "dégâts" as guessed. Also newly confirmed: incoming damage on you is phrased as "Vous
    // avez subi ... de la part de : X" (suffered ... from X), NOT the attacker-first "X t'a
    // infligé..." this file originally guessed -- a structurally different sentence, not just a
    // wrong pronoun. Skill attribution has TWO real interchangeable connectors, "en utilisant"
    // (by using) and "grâce à" (thanks to) -- both confirmed for the exact same skill in adjacent
    // real lines, so both are accepted rather than picking one.
    // Still UNCONFIRMED, best-effort: Reflect, DoT-after-your-skill, healing someone ELSE (only
    // self-heals were seen), AP/GP/Kinah/XP, and loot -- the played French session never happened
    // to produce any of those events. One related real, NON-matchable shape found along the way,
    // noted here so it isn't rediscovered and mistaken for a bug: "X voit sa PV s'améliorer car Y
    // a utilisé Z." (X's HP is seen to improve because Y used Z) is a real regen/buff notice with
    // NO concrete amount, the French counterpart of English's "Your HP has been boosted by using
    // X." deliberate gap -- correctly left unmatched, not something to force an amount onto. A
    // third-party generalization of "a subi ... de la part de" (some other mob/character as the
    // one receiving named damage, not just "Vous") was not observed either but is kept as a
    // plausible, still-unconfirmed extension in DamageReceivedPatternFr, mirroring how English's
    // own DamageReceivedPattern generalizes beyond only what was directly observed.
    // ===================================================================================

    [GeneratedRegex(@"^(?:Coup critique !\s?)?Votre attaque sur (?<attacker>.+) a été reflétée et vous a infligé (?<amount>[\d.]+) points de dégâts\.$")]
    private static partial Regex ReflectedDamagePatternFr();

    // "critiques" (plural adjective on "dégâts") confirmed by terminal_windows as a real,
    // separate crit marker from the "Coup critique !" prefix -- both can occur on the same line
    // ("Coup critique ! Vous avez infligé 649 points de dégâts critiques à ..."), mirroring
    // English's own "Critical Hit!X inflicted N critical damage on Y." dual-marker shape.
    [GeneratedRegex(@"^(?:Coup critique !\s?)?(?<attacker>Vous|.+?) (?:avez|a) infligé (?<amount>[\d.]+) points de dégâts(?: critiques)? à (?<target>.+?)(?: (?:en utilisant|grâce à) (?<skill>.+))?\.$")]
    private static partial Regex DamagePatternFr();

    [GeneratedRegex(@"^(?:Coup critique !\s?)?Vous avez subi (?<amount>[\d.]+) points de dégâts de la part de\s*:\s*(?<attacker>.+)\.$")]
    private static partial Regex DamageInflictedOnYouPatternFr();

    [GeneratedRegex(@"^(?:Coup critique !\s?)?(?<target>.+) a subi (?<amount>[\d.]+) points de dégâts de la part de\s*:\s*(?<attacker>.+)\.$")]
    private static partial Regex DamageReceivedPatternFr();

    [GeneratedRegex(@"^(?:Coup critique !\s?)?(?<target>.+) a subi (?<amount>[\d.]+)(?: \S+)? points de dégâts après que vous avez utilisé (?<skill>.+)\.$")]
    private static partial Regex DotDamageAttributedToYouPatternFr();

    [GeneratedRegex(@"^Vous avez restauré (?<amount>[\d.]+) PV de (?<target>.+) (?:en utilisant|grâce à) (?<skill>.+)\.$")]
    private static partial Regex HealOtherPatternFr();

    [GeneratedRegex(@"^(?<target>.+) a récupéré (?<amount>[\d.]+) PV car (?<healer>.+) a utilisé .+?\.$")]
    private static partial Regex HealByOtherPatternFr();

    [GeneratedRegex(@"^(?<who>Vous|.+?) (?:avez|a) (?:récupéré|restauré) (?<amount>[\d.]+) PV(?: (?:en utilisant|grâce à) (?<skill>.+))?\.$")]
    private static partial Regex HealSelfPatternFr();

    // ===================================================================================
    // Spanish (ES) -- CONFIRMED for the core damage/heal-self shapes, same as German/French
    // above: Spanish turned out to be the AION community's real 4th language here, not Russian
    // (0 occurrences of Russian in the user's ~497k-line Chat.log, vs. 3,991 Spanish lines --
    // Spanish was added to this file's language list once that became clear; the RU patterns
    // below are kept, unchanged, as harmless best-effort insurance rather than removed).
    // Confirmed real lines (verbatim, only numbers/names vary), extracted and coverage-tested by
    // terminal_windows against the user's real log:
    //   "Habéis infligido 98 de daño a Mitzuhiko."                                    (no skill)
    //   "Habéis infligido 84 de daño a Mitzuhiko utilizando la habilidad Golpe sagrado I."
    //   "Mitzuhiko ha infligido 1.086 de daño a Maniquí de entrenamiento."             (3rd person)
    //   "¡Golpe crítico!Ueeu ha infligido 751 de daño a Maniquí de entrenamiento."     (crit, glued)
    //   "Mitzuhiko os ha infligido 37 de daño."                                        (on you)
    //   "Habéis restaurado 17 PV mediante la habilidad Luz de la renovación I."        (self-heal)
    // Formal address again ("Habéis", 2nd person plural/formal "you have", not an informal "tú"
    // form) -- the same systemic pattern as German's "Ihr" and French's "Vous", just Spanish's own
    // shape. Two structural traps found and fixed here, both confirmed by testing against the real
    // lines above, not guessed: (1) "Habéis" already IS the conjugated verb (subject dropped, as
    // Spanish commonly does) with NO separate verb word following it ("Habéis infligido", not
    // "Habéis ha infligido"), while a third-party attacker needs an explicit " ha" ("Mitzuhiko ha
    // infligido") -- one regex handles both by making " ha" optional rather than writing two
    // separate templates; (2) incoming damage on the local player inserts the object pronoun "os"
    // BEFORE the verb ("Mitzuhiko os ha infligido..."), and this is the ONLY reliable marker that
    // distinguishes "attacked" from "attacking" here (unlike English's differently-worded
    // "received .../inflicted..." or German's differing verb-phrase target) -- a pattern that only
    // recognizes "ha infligido" without checking for "os" would misattribute every incoming hit as
    // the ATTACKER's own outgoing damage. Confirmed no separate "critical" adjective exists (no
    // "de daño crítico" the way French has "dégâts critiques") -- the crit prefix "¡Golpe
    // crítico!" (confirmed in both the spaced and glued-to-the-name forms, same dual shape as
    // "Critical Hit!"/"Coup critique !") is the only crit marker. The skill clause sits AFTER the
    // target ("a Ziel utilizando la habilidad X"), a different position than German's (before the
    // amount) -- each language's word order was taken from its own real lines, not assumed to
    // match another language's.
    // Still UNCONFIRMED, best-effort: Reflect, DoT-after-your-own-skill (a real unattributed
    // effect-only DoT line was found -- "Mitzuhiko recibe 48 puntos de daño mediante la habilidad
    // Efecto de Promesa del viento I." -- but that's the Spanish counterpart of this file's
    // documented deliberate gaps, an effect name standing in for a real source, not something to
    // match), healing someone else by name, the third-party generalization of "received damage",
    // and AP/GP/Kinah/XP/loot (none of these occurred in the played Spanish session either).
    // ===================================================================================

    [GeneratedRegex(@"^(?:¡Golpe crítico!\s?)?Vuestro ataque a (?<attacker>.+) ha sido reflejado y os ha infligido (?<amount>[\d.]+) de daño\.$")]
    private static partial Regex ReflectedDamagePatternEs();

    [GeneratedRegex(@"^(?:¡Golpe crítico!\s?)?(?<attacker>Habéis|.+?) (?:ha )?infligido (?<amount>[\d.]+) de daño a (?<target>.+?)(?: utilizando la habilidad (?<skill>.+))?\.$")]
    private static partial Regex DamagePatternEs();

    [GeneratedRegex(@"^(?:¡Golpe crítico!\s?)?(?<attacker>.+) os ha infligido (?<amount>[\d.]+) de daño(?: utilizando la habilidad (?<skill>.+))?\.$")]
    private static partial Regex DamageInflictedOnYouPatternEs();

    [GeneratedRegex(@"^(?<target>.+) ha recibido (?<amount>[\d.]+) de daño de (?<attacker>.+)\.$")]
    private static partial Regex DamageReceivedPatternEs();

    [GeneratedRegex(@"^(?<target>.+) recibe (?<amount>[\d.]+) puntos de daño después de que utilizasteis (?<skill>.+)\.$")]
    private static partial Regex DotDamageAttributedToYouPatternEs();

    [GeneratedRegex(@"^Habéis restaurado (?<amount>[\d.]+) PV de (?<target>.+) mediante la habilidad (?<skill>.+)\.$")]
    private static partial Regex HealOtherPatternEs();

    [GeneratedRegex(@"^(?<target>.+) ha restaurado (?<amount>[\d.]+) PV porque (?<healer>\S+) ha utilizado .+?\.$")]
    private static partial Regex HealByOtherPatternEs();

    [GeneratedRegex(@"^(?<who>Habéis|.+?) (?:ha )?restaurado (?<amount>[\d.]+) PV(?: mediante la habilidad (?<skill>.+))?\.$")]
    private static partial Regex HealSelfPatternEs();

    // ===================================================================================
    // Russian (RU) -- BEST-EFFORT, UNVALIDATED, HIGHEST RISK. See class remarks for why: past-
    // tense verbs here grammatically agree with the actor's gender, which this parser cannot know
    // per line, so every such verb is written with alternated masculine/feminine/(neuter) endings
    // to at least not fail-closed on that alone. Whether Aion's real Russian client phrases these
    // sentences this way at all -- word choice, case endings on foreign character names -- is
    // simply unverified; treat this block as the weakest guess in the file.
    // ===================================================================================

    [GeneratedRegex(@"^(?:Критический удар!\s?)?Твоя атака на (?<attacker>.+) была отражена и нанесла тебе (?<amount>[\d.]+) урона\.$")]
    private static partial Regex ReflectedDamagePatternRu();

    [GeneratedRegex(@"^(?:Критический удар!\s?)?(?<attacker>.+) нан(?:ёс|есла|есло) (?<target>.+?) (?<amount>[\d.]+)(?: критического)? урона(?: используя (?<skill>.+)| .+)?\.$")]
    private static partial Regex DamagePatternRu();

    [GeneratedRegex(@"^(?:Критический удар!\s?)?(?<attacker>.+) нан(?:ёс|есла|есло) тебе (?<amount>[\d.]+) урона(?: используя .+)?\.$")]
    private static partial Regex DamageInflictedOnYouPatternRu();

    [GeneratedRegex(@"^(?:Критический удар!\s?)?(?<target>.+) получил(?:а|о)? (?<amount>[\d.]+) урона от (?<attacker>.+)\.$")]
    private static partial Regex DamageReceivedPatternRu();

    [GeneratedRegex(@"^(?:Критический удар!\s?)?(?<target>.+) получил(?:а|о)? (?<amount>[\d.]+)(?: \S+)? урона после того, как ты использовал(?:а)? (?<skill>.+)\.$")]
    private static partial Regex DotDamageAttributedToYouPatternRu();

    [GeneratedRegex(@"^Ты восстановил(?:а)? (?<target>.+) (?<amount>[\d.]+) ОЗ, используя (?<skill>.+)\.$")]
    private static partial Regex HealOtherPatternRu();

    [GeneratedRegex(@"^(?<target>.+) восстановил(?:а|о)? (?<amount>[\d.]+) ОЗ, потому что (?<healer>.+) использовал(?:а|о)? .+?\.$")]
    private static partial Regex HealByOtherPatternRu();

    [GeneratedRegex(@"^(?<who>.+) восстановил(?:а|о)? (?<amount>[\d.]+) ОЗ(?: используя (?<skill>.+))?\.$")]
    private static partial Regex HealSelfPatternRu();

    // ===================================================================================
    // Per-language pattern-set plumbing: one record per language holding the 8 damage/heal
    // regexes, all sharing English's group names, so a single dispatcher (TryParseWithPatternSet)
    // implements the attribution rules exactly once instead of once per language.
    // ===================================================================================

    private readonly record struct DamageHealPatternSet(
        Regex Reflected,
        Regex InflictedOnYou,
        Regex Received,
        Regex DotAttributedToYou,
        Regex General,
        Regex HealOther,
        Regex HealByOther,
        Regex HealSelf,
        string[] LocalPlayerLiterals,
        // English and German have confirmed lines for the DoT pair; only the redirect shape is
        // still German-only. Every other language keeps the defaults until a real log proves an
        // equivalent exists there too -- guessing one in would be the unvalidated-pattern habit
        // this file keeps warning against.
        Regex? DamageRedirected = null,
        Regex? DotAnnouncement = null,
        Regex? DotTick = null);

    // English first (validated, most likely to match), then DE/FR/ES/RU -- order only matters for
    // which language's SkillUsed/class-detection text a match reports, not for correctness, since
    // a single Chat.log is written consistently in one language and only that language's set will
    // ever actually match a given real file.
    private static IReadOnlyList<DamageHealPatternSet> DamageHealPatternSets { get; } = new[]
    {
        new DamageHealPatternSet(
            ReflectedDamagePattern(), DamageInflictedOnYouPattern(), DamageReceivedPattern(),
            DotDamageAttributedToYouPattern(), DamagePattern(), HealOtherPattern(),
            HealByOtherPattern(), HealSelfPattern(), new[] { "you" },
            DamageRedirected: null, DotAnnouncement: DotAnnouncementPattern(), DotTick: DotTickPattern()),
        new DamageHealPatternSet(
            ReflectedDamagePatternDe(), DamageInflictedOnYouPatternDe(), DamageReceivedPatternDe(),
            DotDamageAttributedToYouPatternDe(), DamagePatternDe(), HealOtherPatternDe(),
            HealByOtherPatternDe(), HealSelfPatternDe(), new[] { "euch", "ihr" },
            DamageRedirectedPatternDe(), DotAnnouncementPatternDe(), DotTickPatternDe()),
        new DamageHealPatternSet(
            ReflectedDamagePatternFr(), DamageInflictedOnYouPatternFr(), DamageReceivedPatternFr(),
            DotDamageAttributedToYouPatternFr(), DamagePatternFr(), HealOtherPatternFr(),
            HealByOtherPatternFr(), HealSelfPatternFr(), new[] { "vous" }),
        new DamageHealPatternSet(
            ReflectedDamagePatternEs(), DamageInflictedOnYouPatternEs(), DamageReceivedPatternEs(),
            DotDamageAttributedToYouPatternEs(), DamagePatternEs(), HealOtherPatternEs(),
            HealByOtherPatternEs(), HealSelfPatternEs(), new[] { "habéis" }),
        new DamageHealPatternSet(
            ReflectedDamagePatternRu(), DamageInflictedOnYouPatternRu(), DamageReceivedPatternRu(),
            DotDamageAttributedToYouPatternRu(), DamagePatternRu(), HealOtherPatternRu(),
            HealByOtherPatternRu(), HealSelfPatternRu(), new[] { "тебе", "тебя", "ты" }),
    };

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
    // Language-independent: the command word itself is typed verbatim by the user regardless of
    // which language their Aion client is running in, so this needs no DE/FR/RU counterpart.
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
    // Language-independent: this tag is the client's own internal markup for "who said this",
    // not translated chat text, so it's assumed (not confirmed against a non-English sample) to be
    // identical across locales -- same reasoning as CommandPattern above.
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
    // Language-independent for the same reason as ChatSpeakerPattern -- the "Name:" shape doesn't
    // depend on the client's display language, only the channel tag word would, and that's not
    // captured here.
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

    // German -- "Ihr habt" (formal address, see class remarks above) and "EP" for XP are confirmed
    // real German client terms (id 901692 STR_QUEST_REWARD_EXP "%0 XP" -> "%0 EP"; formal address
    // confirmed systemically). "Abyss-Punkte" (WITH a hyphen) is confirmed too (id 903488
    // STR_ABYSS_POINT "Abyss Points" -> "Abyss-Punkte"; this file originally guessed the wrong,
    // unhyphenated "Abysspunkte"). Still an unconfirmed guess: "Ruhmespunkte" for Glory Points --
    // no matching string was found by keyword search in the client's own locale data.
    [GeneratedRegex(@"^Ihr habt (?<amount>[\d.]+) Abyss-Punkte erhalten\.$")]
    private static partial Regex ApGainedPatternDe();

    [GeneratedRegex(@"^Ihr habt (?<amount>[\d.]+) Abyss-Punkte verloren\.$")]
    private static partial Regex ApLostPatternDe();

    [GeneratedRegex(@"^Ihr habt (?<amount>[\d.]+) Kinah erhalten\.$")]
    private static partial Regex KinahEarnedPatternDe();

    [GeneratedRegex(@"^Ihr habt (?<amount>[\d.]+) Kinah ausgegeben\.$")]
    private static partial Regex KinahSpentPatternDe();

    // Trailing parenthetical is real ("... erhalten (Energie der Rast 2)." -- the rested-XP
    // bonus marker), and an anchored pattern without it drops those lines entirely.
    [GeneratedRegex(@"^Ihr habt (?<amount>[\d.]+) EP von .+ erhalten(?: \([^)]*\))?\.$")]
    private static partial Regex XpGainedPatternDe();

    [GeneratedRegex(@"^Ihr habt (?<amount>[\d.]+) Ruhmespunkte erhalten\.$")]
    private static partial Regex GpGainedPatternDe();

    // French -- still BEST-EFFORT/UNVALIDATED (the real French session that confirmed the damage/
    // heal-self patterns above never happened to gain AP/GP/Kinah/XP), but corrected to the
    // confirmed formal "Vous avez" register rather than the originally-guessed informal "Tu as".
    [GeneratedRegex(@"^Vous avez gagné (?<amount>[\d.]+) points d'Abysse\.$")]
    private static partial Regex ApGainedPatternFr();

    [GeneratedRegex(@"^Vous avez perdu (?<amount>[\d.]+) points d'Abysse\.$")]
    private static partial Regex ApLostPatternFr();

    [GeneratedRegex(@"^Vous avez gagné (?<amount>[\d.]+) Kinah\.$")]
    private static partial Regex KinahEarnedPatternFr();

    [GeneratedRegex(@"^Vous avez dépensé (?<amount>[\d.]+) Kinah\.$")]
    private static partial Regex KinahSpentPatternFr();

    [GeneratedRegex(@"^Vous avez gagné (?<amount>[\d.]+) XP grâce à .+\.$")]
    private static partial Regex XpGainedPatternFr();

    [GeneratedRegex(@"^Vous avez gagné (?<amount>[\d.]+) points de Gloire\.$")]
    private static partial Regex GpGainedPatternFr();

    // Spanish -- still BEST-EFFORT/UNVALIDATED (no such event occurred during the played Spanish
    // session either), using the confirmed formal "Habéis" register from the damage/heal patterns.
    [GeneratedRegex(@"^Habéis ganado (?<amount>[\d.]+) Puntos del Abismo\.$")]
    private static partial Regex ApGainedPatternEs();

    [GeneratedRegex(@"^Habéis perdido (?<amount>[\d.]+) Puntos del Abismo\.$")]
    private static partial Regex ApLostPatternEs();

    [GeneratedRegex(@"^Habéis ganado (?<amount>[\d.]+) Kinah\.$")]
    private static partial Regex KinahEarnedPatternEs();

    [GeneratedRegex(@"^Habéis gastado (?<amount>[\d.]+) Kinah\.$")]
    private static partial Regex KinahSpentPatternEs();

    [GeneratedRegex(@"^Habéis ganado (?<amount>[\d.]+) PE gracias a .+\.$")]
    private static partial Regex XpGainedPatternEs();

    [GeneratedRegex(@"^Habéis ganado (?<amount>[\d.]+) Puntos de Gloria\.$")]
    private static partial Regex GpGainedPatternEs();

    // Russian -- BEST-EFFORT, UNVALIDATED, HIGHEST RISK, see class remarks. "Ты получил(а)"
    // alternates the local player's unknown grammatical gender; "Кина" (Kinah) is a guessed,
    // unconfirmed transliteration.
    [GeneratedRegex(@"^Ты получил(?:а)? (?<amount>[\d.]+) очков Бездны\.$")]
    private static partial Regex ApGainedPatternRu();

    [GeneratedRegex(@"^Ты потерял(?:а)? (?<amount>[\d.]+) очков Бездны\.$")]
    private static partial Regex ApLostPatternRu();

    [GeneratedRegex(@"^Ты получил(?:а)? (?<amount>[\d.]+) Кина\.$")]
    private static partial Regex KinahEarnedPatternRu();

    [GeneratedRegex(@"^Ты потратил(?:а)? (?<amount>[\d.]+) Кина\.$")]
    private static partial Regex KinahSpentPatternRu();

    [GeneratedRegex(@"^Ты получил(?:а)? (?<amount>[\d.]+) очков опыта от .+\.$")]
    private static partial Regex XpGainedPatternRu();

    [GeneratedRegex(@"^Ты получил(?:а)? (?<amount>[\d.]+) очков Славы\.$")]
    private static partial Regex GpGainedPatternRu();

    private readonly record struct PersonalStatPatternSet(
        Regex ApGained, Regex ApLost, Regex KinahEarned, Regex KinahSpent, Regex XpGained, Regex GpGained);

    private static IReadOnlyList<PersonalStatPatternSet> PersonalStatPatternSets { get; } = new[]
    {
        new PersonalStatPatternSet(ApGainedPattern(), ApLostPattern(), KinahEarnedPattern(), KinahSpentPattern(), XpGainedPattern(), GpGainedPattern()),
        new PersonalStatPatternSet(ApGainedPatternDe(), ApLostPatternDe(), KinahEarnedPatternDe(), KinahSpentPatternDe(), XpGainedPatternDe(), GpGainedPatternDe()),
        new PersonalStatPatternSet(ApGainedPatternFr(), ApLostPatternFr(), KinahEarnedPatternFr(), KinahSpentPatternFr(), XpGainedPatternFr(), GpGainedPatternFr()),
        new PersonalStatPatternSet(ApGainedPatternEs(), ApLostPatternEs(), KinahEarnedPatternEs(), KinahSpentPatternEs(), XpGainedPatternEs(), GpGainedPatternEs()),
        new PersonalStatPatternSet(ApGainedPatternRu(), ApLostPatternRu(), KinahEarnedPatternRu(), KinahSpentPatternRu(), XpGainedPatternRu(), GpGainedPatternRu()),
    };

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
    [GeneratedRegex(@"^(?<subject>.+?) (?:have|has) acquired (?:(?<qty>[\d.]+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\])(?:\(s\)|s)?(?: and stored (?:it|them) in your special cube)?\.$")]
    private static partial Regex LootAcquiredPattern();

    // Survey reward: different verb and tail than "acquired" ("You received ... as reward for the
    // survey."), confirmed always "You" in real data (never seen for another character) so the
    // subject isn't captured, just assumed.
    [GeneratedRegex(@"^You received (?:(?<qty>[\d.]+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\]) items? as reward for the survey\.$")]
    private static partial Regex LootSurveyPattern();

    // German -- "erhalten" as the verb for "acquired" is corroborated (not just guessed) by id
    // 900708/900709 in the client's own strings ("You have acquired ... as a reward" -> "Ihr habt
    // ... erhalten"); formal "Ihr habt/hat" mirrors the confirmed damage-pattern fix above.
    // "Spezialwürfel" (special cube) remains an unconfirmed guess.
    [GeneratedRegex(@"^(?<subject>Ihr|.+?) (?:habt|hat) (?:(?<qty>[\d.]+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\]) erhalten(?: und (?:in Eurem Spezialwürfel|im Würfel für Quest-Gegenstände, Münzen und Tickets) verstaut)?\.$")]
    private static partial Regex LootAcquiredPatternDe();

    [GeneratedRegex(@"^Ihr habt (?:(?<qty>[\d.]+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\]) als Belohnung für die Umfrage erhalten\.$")]
    private static partial Regex LootSurveyPatternDe();

    // French -- still BEST-EFFORT/UNVALIDATED (no real loot event occurred during the French
    // session either), corrected to the confirmed formal "Vous avez" register.
    [GeneratedRegex(@"^(?<subject>Vous|.+?) (?:avez|a) obtenu (?:(?<qty>[\d.]+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\])(?: .+ cube spécial)?\.$")]
    private static partial Regex LootAcquiredPatternFr();

    [GeneratedRegex(@"^Vous avez reçu (?:(?<qty>[\d.]+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\]) comme récompense pour le sondage\.$")]
    private static partial Regex LootSurveyPatternFr();

    // Spanish -- still BEST-EFFORT/UNVALIDATED (no real loot event occurred during the played
    // Spanish session either), using the confirmed formal "Habéis"/"ha" alternation from the
    // damage/heal patterns above.
    [GeneratedRegex(@"^(?<subject>Habéis|.+?) (?:ha )?obtenido (?:(?<qty>[\d.]+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\])(?: .+ cubo especial)?\.$")]
    private static partial Regex LootAcquiredPatternEs();

    [GeneratedRegex(@"^Habéis recibido (?:(?<qty>[\d.]+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\]) como recompensa por la encuesta\.$")]
    private static partial Regex LootSurveyPatternEs();

    // Russian -- BEST-EFFORT, UNVALIDATED, HIGHEST RISK. Same unknown-gender caveat as the
    // personal-stat patterns above applies to "получил(а/о)" here too.
    [GeneratedRegex(@"^(?<subject>.+?) получил(?:а|о)? (?:(?<qty>[\d.]+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\])(?: и убрал(?:а|о)? (?:его|их) в специальный куб)?\.$")]
    private static partial Regex LootAcquiredPatternRu();

    [GeneratedRegex(@"^Ты получил(?:а)? (?:(?<qty>[\d.]+) )?(?<tag>\[item:(?<id>\d+)[^\]]*\]) в награду за опрос\.$")]
    private static partial Regex LootSurveyPatternRu();

    // LocalPlayerLiterals mirrors DamageHealPatternSet's field of the same name: German's,
    // French's, and Spanish's Acquired patterns all use the same "Ihr/Vous/Habéis|any name"
    // alternation as their confirmed damage patterns do (see DamagePatternDe's/Fr's/Es's remarks).
    // German's loot line itself was directly confirmed real ("Ihr habt [item:...] erhalten.");
    // French's and Spanish's loot events were never actually observed, but applying the same
    // confirmed formal-register fix to them anyway is far more likely correct than leaving the
    // original informal guess in place. Only Russian's subject capture remains a plain,
    // unconstrained name with no such literal to canonicalize.
    private readonly record struct LootPatternSet(Regex Acquired, Regex Survey, string[] LocalPlayerLiterals);

    private static IReadOnlyList<LootPatternSet> LootPatternSets { get; } = new[]
    {
        new LootPatternSet(LootAcquiredPattern(), LootSurveyPattern(), Array.Empty<string>()),
        new LootPatternSet(LootAcquiredPatternDe(), LootSurveyPatternDe(), new[] { "ihr" }),
        new LootPatternSet(LootAcquiredPatternFr(), LootSurveyPatternFr(), new[] { "vous" }),
        new LootPatternSet(LootAcquiredPatternEs(), LootSurveyPatternEs(), new[] { "habéis" }),
        new LootPatternSet(LootAcquiredPatternRu(), LootSurveyPatternRu(), Array.Empty<string>()),
    };

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
    /// An exact duplicate message within the same timestamp is therefore dropped -- EXCEPT for
    /// damage/heal lines, which are always counted.
    ///
    /// That exception is the whole point, and it was a real bug: this guard used to apply to
    /// combat lines too, on the assumption (written into this comment) that genuine damage lines
    /// never repeat identically within one second. They do, constantly -- Chat.log has only
    /// one-second resolution, and equal hits on the same target with the same skill produce
    /// byte-identical lines. Measured against the user's own 18,378-line Chat.log: 7.7% of all
    /// damage lines were being discarded, and for his own character ("You", a Gladiator weaving
    /// fast attacks) 32% of his total damage simply vanished, while other players lost 1-2% --
    /// so the meter did not just undercount, it undercounted each player differently. The same
    /// file also settles which explanation is right: 163 seconds contain the same combat line
    /// three times and 9 contain it four times, which two clients writing one file cannot
    /// produce (that tops out at two copies).
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

            // Recorded either way, but acted on only for non-combat lines (see method remarks):
            // a repeated damage/heal line is a real second hit, a repeated broadcast is the other
            // client's copy of one event.
            bool duplicateInBucket = !_seenThisBucket.Add(e.Message);

            if (TryParseDamageOrHeal(e.Message, out int sourceId, out int targetId, out long amount, out bool isHeal))
            {
                events.Add(new DamageEvent(e.Timestamp, sourceId, targetId, amount, isHeal));
            }
            else if (duplicateInBucket)
            {
                continue; // exact duplicate within the same second -- second client's copy
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

    /// <summary>See the Ap/Kinah/Xp *Pattern remarks and PersonalStatChanged's own remarks -- each
    /// language's set of five shapes is tried in this fixed internal order for no particular
    /// reason (none of the five can match more than one pattern within the same language), but
    /// English is tried before DE/FR/RU since it's the confirmed, validated case.</summary>
    private void RaisePersonalStatIfPresent(string message)
    {
        foreach (var p in PersonalStatPatternSets)
        {
            if (p.ApGained.Match(message) is { Success: true } apGained)
            {
                PersonalStatChanged?.Invoke(PersonalStatKind.AbyssPoints, ParseGroupedAmount(apGained.Groups["amount"].Value));
                return;
            }
            if (p.ApLost.Match(message) is { Success: true } apLost)
            {
                PersonalStatChanged?.Invoke(PersonalStatKind.AbyssPoints, -ParseGroupedAmount(apLost.Groups["amount"].Value));
                return;
            }
            if (p.KinahEarned.Match(message) is { Success: true } kinahEarned)
            {
                PersonalStatChanged?.Invoke(PersonalStatKind.Kinah, ParseGroupedAmount(kinahEarned.Groups["amount"].Value));
                return;
            }
            if (p.KinahSpent.Match(message) is { Success: true } kinahSpent)
            {
                PersonalStatChanged?.Invoke(PersonalStatKind.Kinah, -ParseGroupedAmount(kinahSpent.Groups["amount"].Value));
                return;
            }
            if (p.XpGained.Match(message) is { Success: true } xpGained)
            {
                PersonalStatChanged?.Invoke(PersonalStatKind.Experience, ParseGroupedAmount(xpGained.Groups["amount"].Value));
                return;
            }
            if (p.GpGained.Match(message) is { Success: true } gpGained)
            {
                PersonalStatChanged?.Invoke(PersonalStatKind.GloryPoints, ParseGroupedAmount(gpGained.Groups["amount"].Value));
                return;
            }
        }
    }

    /// <summary>See LootAcquiredPattern/LootSurveyPattern remarks. Each language's pair is tried
    /// in this order for no particular reason (a survey-reward line's tail never matches the
    /// "acquired" shape within the same language); English first as the confirmed, validated
    /// case, DE/FR/RU as best-effort fallbacks.</summary>
    private void RaiseLootIfPresent(string message)
    {
        foreach (var p in LootPatternSets)
        {
            if (p.Acquired.Match(message) is { Success: true } acquired)
            {
                // Canonicalize the same way TryParseWithPatternSet's General branch does for
                // German's "Ihr habt [item] erhalten." -- see LootPatternSet's remarks.
                string subject = acquired.Groups["subject"].Value;
                bool subjectIsLocalPlayer = p.LocalPlayerLiterals.Any(literal => string.Equals(subject, literal, StringComparison.OrdinalIgnoreCase));
                LootAcquired?.Invoke(new LootEvent(
                    subjectIsLocalPlayer ? YouName : subject,
                    int.Parse(acquired.Groups["id"].Value),
                    acquired.Groups["tag"].Value,
                    acquired.Groups["qty"].Success ? ParseGroupedAmount(acquired.Groups["qty"].Value) : 1));
                return;
            }
            if (p.Survey.Match(message) is { Success: true } survey)
            {
                LootAcquired?.Invoke(new LootEvent(
                    YouName,
                    int.Parse(survey.Groups["id"].Value),
                    survey.Groups["tag"].Value,
                    survey.Groups["qty"].Success ? ParseGroupedAmount(survey.Groups["qty"].Value) : 1));
                return;
            }
        }
    }

    /// <summary>
    /// Tries each language's pattern set in turn (English first -- confirmed/validated; DE/FR/RU
    /// as best-effort fallbacks, see class remarks) and dispatches through the shared, language-
    /// agnostic attribution logic in TryParseWithPatternSet.
    /// </summary>
    private bool TryParseDamageOrHeal(string message, out int sourceId, out int targetId, out long amount, out bool isHeal)
    {
        foreach (var set in DamageHealPatternSets)
        {
            if (TryParseWithPatternSet(set, message, out sourceId, out targetId, out amount, out isHeal))
            {
                return true;
            }
        }

        sourceId = targetId = 0;
        amount = 0;
        isHeal = false;
        return false;
    }

    /// <summary>
    /// Order matters: more specific patterns first, the broad catch-alls (General, HealSelf)
    /// last -- see DamageInflictedOnYouPattern's remarks for a concrete case (incoming skill
    /// damage) where checking the general pattern first would silently misattribute events, not
    /// just fail to match. Written once, generic over which language's DamageHealPatternSet is
    /// passed in, since every set shares English's group names by construction.
    /// </summary>
    // Who last cast a given damage-over-time skill on a given target, so a later tick line naming
    // only the skill can be attributed (see DotTickPattern).
    //
    // The most recent caster wins, which is not a guess but how the game works: the same skill from
    // a second player does not stack on one target, it REPLACES what was there. From that line on,
    // every tick belongs to the new caster, and the old one's effect is gone. This used to give up
    // instead -- a second caster set the entry to null and all further ticks were dropped -- which
    // threw away the entire DoT output of both players for the rest of the fight whenever a group
    // ran two Spiritmasters, two Sorcerers or two Clerics. Keyed by target as well as skill, so
    // two casters working different targets never interfere in the first place.
    private readonly Dictionary<(string Skill, string Target), string?> _dotCasterBySkillAndTarget = new();

    /// <summary>
    /// Any damage line that names both an attacker and a skill also settles who owns that skill on
    /// that target, so a later tick naming only the skill can be attributed even when no separate
    /// cast announcement was logged. Same last-caster-wins rule as the announcements: the newer
    /// application replaces the older effect rather than stacking with it.
    ///
    /// <para>Found via a 1v1 arena: the opponent's Magic Implosion appeared only as an ordinary
    /// hit, never as an announcement, so its 8.013 damage of ticks stayed unattributed while every
    /// other DoT of theirs was counted.</para>
    /// </summary>
    private void RememberDotCaster(Match match, string caster, string target)
    {
        if (match.Groups["skill"] is { Success: true, Value.Length: > 0 } skill)
        {
            _dotCasterBySkillAndTarget[(skill.Value, target)] = caster;
        }
    }

    private bool TryParseWithPatternSet(DamageHealPatternSet p, string message, out int sourceId, out int targetId, out long amount, out bool isHeal)
    {
        Match match;

        if (p.DotAnnouncement is { } dotAnnouncement && (match = dotAnnouncement.Match(message)).Success)
        {
            string rawCaster = match.Groups["caster"].Value;
            string caster = p.LocalPlayerLiterals.Any(l => string.Equals(rawCaster, l, StringComparison.OrdinalIgnoreCase))
                ? YouName
                : rawCaster;
            var key = (match.Groups["skill"].Value, match.Groups["target"].Value);
            _dotCasterBySkillAndTarget[key] = caster;
            RaiseSkillUsedIfPresent(match, caster);
            // The announcement itself carries no damage -- fall through to "no event", not to the
            // patterns below, which must not see a line already understood.
            sourceId = targetId = 0;
            amount = 0;
            isHeal = false;
            return false;
        }

        if (p.DotTick is { } dotTick && (match = dotTick.Match(message)).Success)
        {
            string target = match.Groups["target"].Value;
            if (_dotCasterBySkillAndTarget.TryGetValue((match.Groups["skill"].Value, target), out string? caster) && caster is not null)
            {
                isHeal = false;
                sourceId = Names.GetOrAssignId(caster);
                targetId = Names.GetOrAssignId(target);
                amount = ParseGroupedAmount(match.Groups["amount"].Value);
                return true;
            }

            sourceId = targetId = 0;
            amount = 0;
            isHeal = false;
            return false;
        }

        if (p.DamageRedirected is { } redirected && (match = redirected.Match(message)).Success)
        {
            isHeal = false;
            sourceId = Names.GetOrAssignId(match.Groups["attacker"].Value);
            targetId = Names.GetOrAssignId(match.Groups["target"].Value);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            return true;
        }

        if ((match = p.Reflected.Match(message)).Success)
        {
            isHeal = false;
            sourceId = Names.GetOrAssignId(match.Groups["attacker"].Value);
            targetId = Names.GetOrAssignId(YouName);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            return true;
        }

        if ((match = p.InflictedOnYou.Match(message)).Success)
        {
            isHeal = false;
            sourceId = Names.GetOrAssignId(match.Groups["attacker"].Value);
            targetId = Names.GetOrAssignId(YouName);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            RaiseSkillUsedIfPresent(match, match.Groups["attacker"].Value);
            RememberDotCaster(match, match.Groups["attacker"].Value, YouName);
            return true;
        }

        if ((match = p.Received.Match(message)).Success)
        {
            isHeal = false;
            sourceId = Names.GetOrAssignId(match.Groups["attacker"].Value);
            targetId = Names.GetOrAssignId(match.Groups["target"].Value);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            return true;
        }

        if ((match = p.DotAttributedToYou.Match(message)).Success)
        {
            isHeal = false;
            sourceId = Names.GetOrAssignId(YouName);
            targetId = Names.GetOrAssignId(match.Groups["target"].Value);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            RaiseSkillUsedIfPresent(match, YouName);
            return true;
        }

        if ((match = p.General.Match(message)).Success)
        {
            isHeal = false;
            // Canonicalize a literal local-player pronoun on EITHER side the same way
            // DamageInflictedOnYouPattern already does for its own target -- found necessary by
            // terminal_windows via the Spiritmaster-pet feature: a general-shaped "X inflicted N
            // damage on you." (no "has") hits this general pattern instead of the more specific
            // "on you" one, and without this, "you" registers as a SEPARATE identity from "You" --
            // the exact same identity-split failure this file's own remarks describe for the
            // reflect-damage case above. Applied defensively for every language, not just English,
            // even though only English has a real confirmed instance of this collision.
            // The ATTACKER side matters too for German specifically: its confirmed real template
            // (STR_MSG_ATTACK_DAMAGE, "Ihr habt %0 %num1 Schaden zugefügt.") uses the fixed literal
            // "Ihr" for the local player as attacker, captured through the same "attacker" group as
            // any other name -- without this canonicalization "Ihr" would register as a separate
            // identity from "You" for every German outgoing hit.
            string attackerName = match.Groups["attacker"].Value;
            bool attackerIsLocalPlayer = p.LocalPlayerLiterals.Any(literal => string.Equals(attackerName, literal, StringComparison.OrdinalIgnoreCase));
            sourceId = Names.GetOrAssignId(attackerIsLocalPlayer ? YouName : attackerName);
            string targetName = match.Groups["target"].Value;
            bool targetIsLocalPlayer = p.LocalPlayerLiterals.Any(literal => string.Equals(targetName, literal, StringComparison.OrdinalIgnoreCase));
            targetId = Names.GetOrAssignId(targetIsLocalPlayer ? YouName : targetName);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            RaiseSkillUsedIfPresent(match, attackerIsLocalPlayer ? YouName : attackerName);
            RememberDotCaster(match, attackerIsLocalPlayer ? YouName : attackerName,
                targetIsLocalPlayer ? YouName : targetName);
            return true;
        }

        if ((match = p.HealOther.Match(message)).Success)
        {
            isHeal = true;
            sourceId = Names.GetOrAssignId(YouName);
            targetId = Names.GetOrAssignId(match.Groups["target"].Value);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            RaiseSkillUsedIfPresent(match, YouName);
            return true;
        }

        if ((match = p.HealByOther.Match(message)).Success)
        {
            isHeal = true;
            sourceId = Names.GetOrAssignId(match.Groups["healer"].Value);
            targetId = Names.GetOrAssignId(match.Groups["target"].Value);
            amount = ParseGroupedAmount(match.Groups["amount"].Value);
            return true;
        }

        if ((match = p.HealSelf.Match(message)).Success)
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

    // See ChatLogTailer.Poll's matching remark: Chat.log is Windows-1252/Latin-1 on disk, not
    // UTF-8 -- File.ReadLines' default encoding silently mangles every accented character
    // otherwise.
    public List<DamageEvent> ParseFile(string path) => Parse(File.ReadLines(path, Encoding.Latin1));
}
