using System.Windows;
using System.Windows.Controls;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>What the combat overlay shows at one moment: the casts (soonest first) and the target of the target. Each is null/empty when there is nothing to show.</summary>
public sealed record CombatFrame(IReadOnlyList<CastRowData> Casts, TargetOfTargetData? TargetOfTarget, BossHpData? Boss = null);

/// <summary>The fought boss's health: what it has now and the most it was seen with.</summary>
public sealed record BossHpData(long Hp, long Max, int Id = 0, string Check = "", DateTime? Since = null);

/// <summary>Works out the combat overlay's frame from what the packet stream said (see <see cref="Aion2CombatState"/>); no windows, so a test can run it.</summary>
public static class CombatOverlayData
{
    /// <summary>How long an entity counts as aiming at what it last used a skill on.</summary>
    private static readonly TimeSpan AimAge = TimeSpan.FromSeconds(10);

    /// <summary>A cast stays on the bar this long after it ended (its damage is about to land).</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(700);

    /// <summary>A monster this tough counts as a boss even when the game data does not say so (new bosses, Abyss bosses).</summary>
    private const long BossHitPoints = 500_000;

    private static readonly TimeSpan BossHold = TimeSpan.FromSeconds(15);
    private static (int Id, DateTime At, int Epoch)? _lastBoss;

    public static CombatFrame Build(Aion2EntityDirectory dir, MeterSettings settings, DateTime now, string language)
    {
        IReadOnlyList<CastRowData> casts = settings.CombatShowBossCast ? Casts(dir, settings, now, language) : Array.Empty<CastRowData>();
        TargetOfTargetData? tot = settings.CombatShowTargetOfTarget ? TargetOfTarget(dir, settings, now, language) : null;
        return new CombatFrame(casts, tot, settings.ShowBossHp ? BossHp(dir, now) : null);
    }

    /// <summary>The boss being fought: the own target when it is a boss, else the one that cast most recently.</summary>
    public static BossHpData? BossHp(Aion2EntityDirectory dir, DateTime now)
    {
        int? boss = dir.LocalTarget is { } clicked && !dir.IsKnownPlayer(clicked.EntityId) && IsBoss(dir, clicked.EntityId) ? clicked.EntityId : null;
        if (boss is null && dir.LocalHitTarget is { } hit && DateTime.UtcNow - hit.At < BossHold && !dir.IsKnownPlayer(hit.EntityId) && IsBoss(dir, hit.EntityId))
        {
            boss = hit.EntityId;
        }

        if (boss is null)
        {
            foreach (CastView view in dir.Combat.Casts(now, Grace).OrderByDescending(v => v.Notice.Start))
            {
                if (!dir.IsKnownPlayer(view.Notice.Actor) && IsBoss(dir, view.Notice.Actor))
                {
                    boss = view.Notice.Actor;
                    break;
                }
            }
        }

        // Aiming with the camera sends no mark message and casts come in gaps: hold the last boss for a while so the bar does not blink.
        if (boss is int found)
        {
            _lastBoss = (found, DateTime.UtcNow, dir.ZoneEpoch);
        }
        else if (_lastBoss is { } last && last.Epoch == dir.ZoneEpoch && DateTime.UtcNow - last.At < BossHold && dir.HitPoints.Latest(last.Id)?.Hp > 0)
        {
            boss = last.Id;
        }

        if (boss is not int id || dir.HitPoints.Latest(id)?.Hp is not long hp)
        {
            return null;
        }

        if (hp <= 0)
        {
            _lastBoss = null; // a dead boss leaves no bar behind, even when it is still marked as the target
            return null;
        }

        // The server sends no maximum for most bosses, and a meter that joined mid-fight saw only a worn-down value first: the table knows the real one.
        long known = dir.BossNpcIdOf(id) is int npc ? AionDPS.Aion2.Protocol.Aion2BossCatalog.Find(npc)?.MaxHp ?? 0 : 0;
        long max = Math.Max(known, Math.Max(dir.HitPoints.MaxOf(id) ?? 0, dir.HitPoints.HighestSeen(id) ?? 0));
        return max > 0 ? new BossHpData(Math.Min(hp, max), max, id, Since: dir.EngagedSince(id)) : null;
    }

    private static bool IsBoss(Aion2EntityDirectory dir, int entity) =>
        dir.BossNpcIdOf(entity) is not null || (dir.HitPoints.MaxOf(entity) ?? 0) >= BossHitPoints;

    public static IReadOnlyList<CastRowData> Casts(Aion2EntityDirectory dir, MeterSettings settings, DateTime now, string language)
    {
        var loc = LocalizationManager.Instance;
        int local = dir.LocalPlayerId;
        long hp = local >= 0 ? dir.HitPoints.Latest(local)?.Hp ?? 0 : 0;
        var scored = new List<(double Score, DateTime End, CastRowData Row)>();
        foreach (CastView view in dir.Combat.Casts(now, Grace))
        {
            CastNotice cast = view.Notice;
            if (dir.IsKnownPlayer(cast.Actor))
            {
                continue; // a player's windup is no boss attack
            }

            bool boss = IsBoss(dir, cast.Actor);
            if (!boss && !settings.CombatCastsOfAllMonsters)
            {
                continue;
            }

            bool atYou = local >= 0 && cast.Target == local;
            long expected = view.MaxOnYou > 0 ? view.MaxOnYou : view.MaxAny;
            bool lethal = hp > 0 && expected >= hp && (atYou || view.IsArea);
            if (settings.CombatCastFilter == "you" && !atYou)
            {
                continue;
            }

            if (settings.CombatCastFilter == "area" && !atYou && !view.IsArea)
            {
                continue;
            }

            string target = atYou
                ? loc["Combat.You"]
                : cast.Target <= 0 || cast.Target == cast.Actor ? loc["Combat.AllAround"] : Name(dir, cast.Target, language);
            bool generic = Aion2SkillNames.IsGeneric(cast.SkillId);
            // a skill the client has no name for says what it paints on the floor ("Attack to the front") instead of a bare "Attack"
            string skill = generic && Aion2SkillShapes.KindOf(cast.SkillId) is { } shape ? loc["Combat.Shape." + shape] : Aion2SkillNames.DisplayOf(cast.SkillId);
            string caster = Name(dir, cast.Actor, language);
            var row = new CastRowData(skill, caster, target, atYou, view.IsArea, lethal, (now - cast.Start).TotalMilliseconds, cast.DurationMs, view.LandMs, Aion2SkillIcons.PathFor(cast.SkillId) ?? (dir.BossNpcIdOf(cast.Actor) is int bossNpc ? Aion2Artwork.BossThumbPath(bossNpc) : null), generic);
            double score = (atYou ? 200 : 0) + (lethal ? 100 : 0) + (view.IsArea ? 50 : 0) + (boss ? 40 : 0);
            scored.Add((score, cast.End, row));
        }

        int most = Math.Clamp(settings.CombatMaxCasts, 1, 3);
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.End).Take(most).OrderBy(s => s.End).Select(s => s.Row).ToList();
    }

    public static TargetOfTargetData? TargetOfTarget(Aion2EntityDirectory dir, MeterSettings settings, DateTime now, string language)
    {
        if (dir.LocalTarget is not { } clicked)
        {
            return null;
        }

        // the health bar is always the player's own target's; picture, name and effects are the target of the target's when there is one, else the own target's
        int shown = dir.Combat.TargetOf(clicked.EntityId, now, AimAge) is int aimed && aimed != clicked.EntityId ? aimed : clicked.EntityId;
        var (name, image, isBoss) = Describe(dir, shown, language);
        return new TargetOfTargetData(name, Name(dir, clicked.EntityId, language), image, isBoss,
            settings.CombatHealerHpBar ? dir.HitPoints.Latest(clicked.EntityId)?.Hp : null, settings.CombatHealerHpBar ? dir.HitPoints.MaxOf(clicked.EntityId) : null,
            settings.CombatHealerEffects ? Effects(dir, settings, shown, now) : null);
    }

    /// <summary>The debuffs on an entity (effects from monsters or from the room, no buffs); none when effects are switched off.</summary>
    private static IReadOnlyList<EffectData> Effects(Aion2EntityDirectory dir, MeterSettings settings, int id, DateTime now)
    {
        var effects = new List<EffectData>();
        if (!settings.CombatHealerEffects)
        {
            return effects;
        }

        foreach (BuffNotice buff in dir.Combat.BuffsOn(id, now))
        {
            // only debuffs: put on by a monster (the boss) or by nobody (the room)
            if (!dir.IsKnownMonster(buff.Caster) && buff.Caster > 0)
            {
                continue;
            }

            string time = buff.Ends is { } end ? Math.Max(0, (int)Math.Ceiling((end - now).TotalSeconds)).ToString() : "∞";
            string english = Aion2SkillNames.NameOf(buff.SkillId);
            if (System.Text.RegularExpressions.Regex.IsMatch(english, @"^Test\d*_"))
            {
                continue; // an internal test state of the game's data, not an effect the player knows
            }

            string? icon = Aion2SkillIcons.PathFor(buff.SkillId) ?? Aion2SkillIcons.PathForName(english, null) ?? Aion2Artwork.EffectIconPath(buff.SkillId);
            if (icon is null && buff.Caster <= 0 && long.TryParse(english, out _))
            {
                continue; // a nameless effect from nobody is a system state (rest, speed ...), not something the room does to you
            }

            effects.Add(new EffectData(Aion2SkillNames.Display(english), icon, time, true));
        }

        return effects;
    }

    /// <summary>The name of an entity: a player's, a monster's (in the chosen language), or its number when nothing is known.</summary>
    public static string Name(Aion2EntityDirectory dir, int id, string language) => Describe(dir, id, language).Name;

    public static (string Name, string? Image, bool IsBoss) Describe(Aion2EntityDirectory dir, int id, string language)
    {
        if (dir.IsKnownPlayer(id) || dir.IsLocalPlayer(id))
        {
            // the own character's class is also known from the login record, before it used a skill
            string? cls = dir.ClassOf(id) ?? (dir.IsLocalPlayer(id) && dir.LocalCharacter is { } own ? Aion2SkillNames.ClassFromCode(own.ClassCode) : null);
            return (dir.NameFor(id) ?? LocalizationManager.Instance["Combat.You"], cls is null ? null : Aion2Artwork.ClassEmblemPath(cls), false);
        }

        int? npc = dir.NpcIdOf(id);
        bool boss = dir.BossNpcIdOf(id) is not null;
        string name = (npc is int n ? Aion2Npcs.NameOf(n, language) : null) ?? dir.MonsterNameFor(id) ?? "#" + id;
        return (name, boss && npc is int b ? Aion2Artwork.BossThumbPath(b) : null, boss);
    }
}

/// <summary>
/// The combat overlay: one transparent frame holding the boss cast bar, the target of the target and the healer's target. Each element has its own place,
/// size and scale inside the frame (<see cref="CombatElementHost"/>); the frame itself is placed, pulled and scaled like every overlay (<see cref="MovableOverlay"/>),
/// its scaling grip scaling all the elements together.
/// </summary>
public sealed class CombatOverlayWindow : MovableOverlay
{
    /// <summary>The overlay is three windows, each placed and scaled on its own: the boss cast bar, the target of the target (picture, name, effects) and its health bar.</summary>
    public enum Part { Cast, Target, Hp }

    public const string BossCastId = "BossCast",
        BossHpId = "BossHp", TargetBlockId = "TargetBlock", TargetIconId = "TargetIcon", TargetNameId = "TargetName", TargetHpId = "TargetHp", TargetEffectsId = "TargetEffects";

    private static readonly string[] TargetParts = { TargetIconId, TargetNameId, TargetHpId, TargetEffectsId };

    private static CombatStage? _building;
    private static readonly List<CombatOverlayWindow> Instances = new();

    private readonly Part _part;
    private readonly CombatStage _stage;
    private readonly BossCastView _bossCast = new();
    private readonly TargetIconView _targetIcon = new();
    private readonly TargetNameView _targetName = new();
    private readonly HpBarView _targetHp = new();
    private readonly HpBarView _bossHp = new();
    private readonly EffectBarView _targetEffects = new();
    private readonly Dictionary<string, CombatElementHost> _hosts = new();
    private bool _editing;

    /// <summary>Show sample values where nothing is going on, without the handles of the edit mode (a tool window's live view while its overlay is positioned).</summary>
    public bool SampleWhenIdle { get; set; }

    /// <summary>A tool window's view of this overlay: it always shows something - what is going on, else sample entries - even where the overlay's own switch for it is off, so the page says what the display would be.</summary>
    public bool Preview { get; set; }

    public Part PartKind => _part;

    public CombatOverlayWindow(Part part) : base(part switch { Part.Cast => "Aion DPS Boss Cast", Part.Hp => "Aion DPS Target Health", _ => "Aion DPS Combat" }, NewStage(), () => Load(part), (l, t, sc) => Save(part, l, t, sc),
        SystemParameters.WorkArea.Left + 40, part switch { Part.Cast => 200, Part.Hp => 540, _ => 360 }, () => LoadSize(part), (w, h) => SaveSize(part, w, h), part == Part.Target ? OverlayEdgeMode.Uniform : OverlayEdgeMode.Crop)
    {
        _part = part;
        Instances.Add(this);
        Action<string> onReset = name => { if (name == Title) { Dispatcher.Invoke(ReloadLayouts); } };
        OverlayPlaces.WasReset += onReset;
        Closed += (_, _) => OverlayPlaces.WasReset -= onReset;
        _stage = _building!;
        _building = null;
        var settings = MeterSettings.Load();
        var loc = LocalizationManager.Instance;
        if (part == Part.Cast)
        {
            AddElement(BossCastId, _bossCast, loc["Combat.Element.BossCast"], settings, _stage);
        }
        else if (part == Part.Hp)
        {
            AddElement(BossHpId, _bossHp, loc["Combat.Element.BossHp"], settings, _stage);
        }
        else
        {
            AddElement(TargetIconId, _targetIcon, loc["Combat.Element.TargetIcon"], settings, _stage);
            AddElement(TargetNameId, _targetName, loc["Combat.Element.TargetName"], settings, _stage);
            AddElement(TargetHpId, _targetHp, loc["Combat.Element.TargetHp"], settings, _stage);
            AddElement(TargetEffectsId, _targetEffects, loc["Combat.Element.TargetEffects"], settings, _stage);
        }

        if (part != Part.Target)
        {
            // the cast bar and the health bar are alone in their window: the window's frame is their outline, so they simply fill it
            CombatElementHost only = _hosts.Values.Single();
            only.Chromeless = true;
            only.Layout.Left = 0;
            only.Layout.Top = 0;
            only.Layout.Scale = 1;
            only.Apply();
            _stage.SizeChanged += (_, e) =>
            {
                only.Layout.Width = e.NewSize.Width;
                only.Layout.Height = e.NewSize.Height;
                only.Apply();
            };
        }

        var (w, h) = LoadSize(part);
        _stage.Wanted = StageSize(part, w, h);
        Closed += (_, _) => Instances.Remove(this);
    }

    private static CombatStage NewStage()
    {
        _building = new CombatStage();
        return _building;
    }

    /// <summary>Where an element begins: on the stage's own units (the stage is <see cref="DefaultFrame"/> less the frame's rim).</summary>
    private static CombatElementLayout DefaultLayout(string id) => id switch
    {
        BossCastId => new CombatElementLayout { Left = 0, Top = 0, Width = 480, Height = 132 },
        TargetBlockId => new CombatElementLayout { Left = 0, Top = 0, Width = 240, Height = 152 },
        TargetIconId => new CombatElementLayout { Left = 0, Top = 0, Width = 52, Height = 52 },
        TargetNameId => new CombatElementLayout { Left = 56, Top = 0, Width = 176, Height = 52 },
        BossHpId => new CombatElementLayout { Left = 0, Top = 0, Width = 280, Height = 30 },
        TargetHpId => new CombatElementLayout { Left = 0, Top = 56, Width = 232, Height = 30 },
        TargetEffectsId => new CombatElementLayout { Left = 0, Top = 90, Width = 232, Height = 54 },
        _ => new CombatElementLayout { Left = 0, Top = 0, Width = 100, Height = 40 },
    };

    /// <summary>The frame's size before anyone pulled it: the stage plus the rim (2 + 1 px each side) and the grip's row.</summary>
    private static Size DefaultFrame(Part part) => part switch { Part.Cast => new Size(486, 152), Part.Hp => new Size(286, 50), _ => new Size(246, 172) };

    private static Size StageSize(Part part, double? frameWidth, double? frameHeight) =>
        new(Math.Max(60, (frameWidth is > 0 ? frameWidth.Value : DefaultFrame(part).Width) - 6), Math.Max(40, (frameHeight is > 0 ? frameHeight.Value : DefaultFrame(part).Height) - 20));

    /// <summary>A layout saved for the old single window can lie far outside the smaller window of its part (the cast bar was invisible then): at least a corner of it must be on the stage.</summary>
    private static bool LiesOnStage(CombatElementLayout layout, Size stage) =>
        layout.Left >= 0 && layout.Top >= 0 && layout.Left <= stage.Width - 36 && layout.Top <= stage.Height - 22;

    private void AddElement(string id, UIElement view, string caption, MeterSettings settings, CombatStage stage)
    {
        if (!settings.CombatElements.TryGetValue(id, out CombatElementLayout? layout) || layout.Width <= 0 || layout.Height <= 0
            || (ReferenceEquals(stage, _stage) && !LiesOnStage(layout, StageSize(_part, LoadSize(_part).Item1, LoadSize(_part).Item2))))
        {
            layout = DefaultLayout(id);
        }

        var host = new CombatElementHost(id, stage, view, layout, caption, SaveElement);
        _hosts[id] = host;
        stage.Children.Add(host);
    }

    private static void SaveElement(string id, CombatElementLayout layout)
    {
        var s = MeterSettings.Load();
        s.CombatElements[id] = new CombatElementLayout { Left = layout.Left, Top = layout.Top, Width = layout.Width, Height = layout.Height, Scale = layout.Scale };
        s.Save();
    }

    private static (double?, double?) LoadSize(Part part)
    {
        var s = MeterSettings.Load();
        return part switch
        {
            Part.Cast => (s.CombatCastOverlayWidth ?? DefaultFrame(part).Width, s.CombatCastOverlayHeight ?? DefaultFrame(part).Height),
            Part.Hp => (s.CombatHpOverlayWidth ?? DefaultFrame(part).Width, s.CombatHpOverlayHeight ?? DefaultFrame(part).Height),
            _ => (s.CombatOverlayWidth ?? DefaultFrame(part).Width, s.CombatOverlayHeight ?? DefaultFrame(part).Height),
        };
    }

    private static void SaveSize(Part part, double? width, double? height)
    {
        var s = MeterSettings.Load();
        if (part == Part.Cast)
        {
            s.CombatCastOverlayWidth = width;
            s.CombatCastOverlayHeight = height;
        }
        else if (part == Part.Hp)
        {
            s.CombatHpOverlayWidth = width;
            s.CombatHpOverlayHeight = height;
        }
        else
        {
            s.CombatOverlayWidth = width;
            s.CombatOverlayHeight = height;
        }

        s.Save();
        if (width is > 0 && height is > 0)
        {
            foreach (var window in Instances.Where(w => w._part == part))
            {
                window._stage.Wanted = StageSize(part, width, height);
            }
        }
    }

    /// <summary>Before the split the overlay was one window: the cast bar at its top, the target of the target 140 below. The cast bar takes over that
    /// window's place (and scale), the target of the target keeps its own spot below it - once, when the cast bar has no place of its own yet.</summary>
    private static MeterSettings LoadMigrated()
    {
        var s = MeterSettings.Load();
        if (s.CombatCastOverlayLeft is null && s.CombatCastOverlayTop is null && s.CombatOverlayLeft is double left && s.CombatOverlayTop is double top)
        {
            double scale = s.CombatOverlayScale;
            s.CombatCastOverlayLeft = left;
            s.CombatCastOverlayTop = top;
            s.CombatCastOverlayScale = scale;
            s.CombatOverlayTop = top + 140 * scale;
            s.Save();
        }

        return s;
    }

    private static (double?, double?, double) Load(Part part)
    {
        var s = LoadMigrated();
        return part switch
        {
            Part.Cast => (s.CombatCastOverlayLeft, s.CombatCastOverlayTop, s.CombatCastOverlayScale),
            Part.Hp => (s.CombatHpOverlayLeft, s.CombatHpOverlayTop, s.CombatHpOverlayScale),
            _ => (s.CombatOverlayLeft, s.CombatOverlayTop, s.CombatOverlayScale),
        };
    }

    private static void Save(Part part, double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        if (part == Part.Cast)
        {
            s.CombatCastOverlayLeft = left;
            s.CombatCastOverlayTop = top;
            s.CombatCastOverlayScale = scale;
        }
        else if (part == Part.Hp)
        {
            s.CombatHpOverlayLeft = left;
            s.CombatHpOverlayTop = top;
            s.CombatHpOverlayScale = scale;
        }
        else
        {
            s.CombatOverlayLeft = left;
            s.CombatOverlayTop = top;
            s.CombatOverlayScale = scale;
        }

        s.Save();
    }

    /// <summary>Puts the elements back where they began.</summary>
    public static void ResetLayout()
    {
        var s = MeterSettings.Load();
        s.CombatElements.Clear();
        s.Save();
        foreach (var window in Instances.ToList())
        {
            window.ReloadLayouts();
        }
    }

    private void ReloadLayouts()
    {
        var settings = MeterSettings.Load();
        foreach (var (id, host) in _hosts)
        {
            CombatElementLayout layout = settings.CombatElements.GetValueOrDefault(id) ?? DefaultLayout(id);
            host.Layout.Left = layout.Left;
            host.Layout.Top = layout.Top;
            host.Layout.Width = layout.Width;
            host.Layout.Height = layout.Height;
            host.Layout.Scale = layout.Scale;
            host.Apply();
        }
    }

    /// <summary>Unlocked, the elements show their outline and handles and a sample where nothing is going on, so they can be placed.</summary>
    public void SetEditing(bool editing)
    {
        if (_editing == editing)
        {
            return;
        }

        _editing = editing;
        foreach (CombatElementHost host in _hosts.Values)
        {
            host.SetEditing(editing);
        }
    }

    /// <summary>Shows one frame. Returns whether anything is visible (live, or a sample while editing).</summary>
    public bool Render(CombatFrame frame, MeterSettings settings)
    {
        var loc = LocalizationManager.Instance;
        if (_part == Part.Cast)
        {
            return RenderCast(frame, settings, loc);
        }

        return _part == Part.Hp ? RenderHp(frame, settings, loc) : RenderTarget(frame, settings, loc);
    }

    private TargetOfTargetData? ShownTarget(CombatFrame frame, MeterSettings settings, LocalizationManager loc)
    {
        bool totOn = settings.CombatShowTargetOfTarget || Preview;
        TargetOfTargetData? shown = totOn ? frame.TargetOfTarget : null;
        if (totOn && shown is null && (_editing || SampleWhenIdle || Preview))
        {
            shown = new TargetOfTargetData(loc["Combat.Sample.Player"], loc["Combat.Sample.Boss"], null, false, 6400, 10000, new[]
            {
                new EffectData("Poison", null, "12", true), new EffectData("Slow", null, "5", true),
            });
        }

        return shown;
    }

    private bool RenderHp(CombatFrame frame, MeterSettings settings, LocalizationManager loc)
    {
        Opacity = Math.Clamp(settings.BossHpOpacity, 0.0, 1.0);
        BossHpData? boss = frame.Boss;
        if (boss is null && (_editing || SampleWhenIdle || Preview) && (settings.ShowBossHp || Preview))
        {
            boss = new BossHpData(6400, 10000, Since: DateTime.Now - TimeSpan.FromSeconds(151));
        }

        if (boss is not null)
        {
            _bossHp.Show(boss.Hp, boss.Max, false, boss.Check, boss.Since is { } since ? DateTime.Now - since : null);
        }

        bool visible = boss is not null && (settings.ShowBossHp || Preview);
        _hosts[BossHpId].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        return visible;
    }

    private bool RenderCast(CombatFrame frame, MeterSettings settings, LocalizationManager loc)
    {
        Opacity = Math.Clamp(settings.CombatCastOpacity, 0.0, 1.0);
        bool any = false;
        bool castOn = settings.CombatShowBossCast || Preview;
        if (castOn && frame.Casts.Count > 0)
        {
            _bossCast.Show(frame.Casts);
        }
        else if (castOn && (_editing || SampleWhenIdle || Preview))
        {
            // as many sample rows as the settings allow, so the frame can be sized to what it will hold
            _bossCast.Show(Enumerable.Range(0, Math.Clamp(settings.CombatMaxCasts, 1, 3))
                .Select(i => new CastRowData(loc["Combat.Sample.Skill"], loc["Combat.Sample.Boss"], loc["Combat.You"], true, true, false, 700 + i * 200, 1500, 400, null, Generic: true)).ToList());
        }

        bool castShown = castOn && (frame.Casts.Count > 0 || _editing || SampleWhenIdle || Preview);
        _hosts[BossCastId].Visibility = castShown ? Visibility.Visible : Visibility.Collapsed;
        any |= castShown;
        return any;
    }

    private bool RenderTarget(CombatFrame frame, MeterSettings settings, LocalizationManager loc)
    {
        Opacity = Math.Clamp(settings.CombatTargetOpacity, 0.0, 1.0);
        bool any = false;
        TargetOfTargetData? shownTot = ShownTarget(frame, settings, loc);
        if (shownTot is { } tot)
        {
            _targetIcon.Show(tot);
            _targetName.Show(tot);
            _targetHp.Show(tot.Hp, tot.MaxHp, tot.Effects is { Count: > 0 });
            _targetEffects.Show(tot.Effects ?? Array.Empty<EffectData>());
        }

        bool totShown = shownTot is not null;
        _hosts[TargetIconId].Visibility = totShown ? Visibility.Visible : Visibility.Collapsed;
        _hosts[TargetNameId].Visibility = totShown ? Visibility.Visible : Visibility.Collapsed;
        _hosts[TargetHpId].Visibility = totShown && settings.CombatHealerHpBar ? Visibility.Visible : Visibility.Collapsed;
        _hosts[TargetEffectsId].Visibility = totShown && settings.CombatHealerEffects ? Visibility.Visible : Visibility.Collapsed;
        any |= totShown;

        return any;
    }
}

/// <summary>
/// Runs the combat overlay: a few times a second it works out what is going on (<see cref="CombatOverlayData"/>) and shows it - only while the game is in
/// front, or while the overlay is unlocked so it can be placed. The settings are read once a second.
/// </summary>
public sealed class CombatOverlayController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    /// <summary>Held against the boss's hit points: whether the damage counted matches what it lost (the meter owns the damage events).</summary>
    private readonly Func<int, HpCheckResult?>? _hpCheck;
    private DateTime _checkAt;
    private int _checkFor;
    private string _checkText = "";
    private CombatOverlayWindow? _overlay, _castOverlay, _hpOverlay;
    private readonly List<CombatOverlayWindow> _views = new();

    /// <summary>The three overlays as never shown copies whose content a tool window shows live, with sample values while nothing is going on.</summary>
    public IReadOnlyList<(string TitleKey, CombatOverlayWindow View)> CreateViews()
    {
        var made = new List<(string, CombatOverlayWindow)>();
        foreach (var (key, part) in new[] { ("Settings.Combat.CardBossHp", CombatOverlayWindow.Part.Hp), ("Settings.Combat.CardBossCast", CombatOverlayWindow.Part.Cast), ("Combat.Element.TargetOfTarget", CombatOverlayWindow.Part.Target) })
        {
            var view = new CombatOverlayWindow(part) { Preview = true };
            _views.Add(view);
            made.Add((key, view));
        }

        return made;
    }

    public void RemoveViews(IEnumerable<CombatOverlayWindow> views)
    {
        foreach (var view in views.ToList())
        {
            _views.Remove(view);
            view.Close();
        }
    }
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private DateTime _savedLessonsAt = DateTime.UtcNow;
    private Aion2EntityDirectory? _lessonsFor;

    public CombatOverlayController(Func<Aion2EntityDirectory?> directory, Func<int, HpCheckResult?>? hpCheck = null)
    {
        _directory = directory;
        _hpCheck = hpCheck;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront()
    {
        _overlay?.RaiseToFront();
        _castOverlay?.RaiseToFront();
        _hpOverlay?.RaiseToFront();
    }

    public void Dispose()
    {
        _timer.Stop();
        _directory()?.Combat.SaveIfChanged();
        _overlay?.Close();
        _castOverlay?.Close();
        _hpOverlay?.Close();
    }

    /// <summary>What the overlay shows now: the casts, the target of the target and the boss's health (with the check mark of its hit points).</summary>
    private CombatFrame BuildFrame(Aion2EntityDirectory? dir, MeterSettings settings)
    {
        CombatFrame frame = dir is null
            ? new CombatFrame(Array.Empty<CastRowData>(), null)
            : CombatOverlayData.Build(dir, settings, DateTime.Now, LocalizationManager.Instance.Language);
        if (frame.Boss is { } boss && _hpCheck is not null)
        {
            if (boss.Id != _checkFor || (DateTime.UtcNow - _checkAt).TotalSeconds >= 1)
            {
                HpCheckResult? check = _hpCheck(boss.Id);
                _checkText = check is null ? "" : check.OverFullHealth || check.Verdict != HpCheckVerdict.Match ? "⚠ " + check.Ratio.ToString("P0", System.Globalization.CultureInfo.CurrentCulture) : "✓";
                _checkFor = boss.Id;
                _checkAt = DateTime.UtcNow;
            }

            frame = frame with { Boss = boss with { Check = _checkText } };
        }

        return frame;
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
        }

        var settings = _settings;
        Aion2EntityDirectory? dir = _directory();
        if (dir is not null && !ReferenceEquals(dir, _lessonsFor))
        {
            _lessonsFor = dir; // what was learnt about boss skills (area or not, when the damage lands) lives between sessions
            dir.Combat.FilePath = System.IO.Path.Combine(MeterSettings.DataDirectory, "cast-skills.json");
            dir.Combat.Load();
        }

        if (dir is not null && (DateTime.UtcNow - _savedLessonsAt).TotalSeconds >= 60)
        {
            _savedLessonsAt = DateTime.UtcNow;
            dir.Combat.SaveIfChanged();
        }

        if (_views.Count > 0)
        {
            CombatFrame viewFrame = BuildFrame(dir, settings);
            foreach (var view in _views)
            {
                // sample values only while that overlay is being positioned
                view.SampleWhenIdle = !(view.PartKind switch
                {
                    CombatOverlayWindow.Part.Cast => settings.CombatCastOverlayLocked ?? settings.CombatOverlayLocked,
                    CombatOverlayWindow.Part.Hp => settings.CombatHpOverlayLocked ?? settings.CombatOverlayLocked,
                    _ => settings.CombatOverlayLocked,
                });
                view.Render(viewFrame, settings);
            }
        }

        if (!settings.ShowCombatOverlay)
        {
            _overlay?.ShowOverlay(false);
            _castOverlay?.ShowOverlay(false);
            _hpOverlay?.ShowOverlay(false);
            return;
        }

        _overlay ??= new CombatOverlayWindow(CombatOverlayWindow.Part.Target);
        _castOverlay ??= new CombatOverlayWindow(CombatOverlayWindow.Part.Cast);
        _hpOverlay ??= new CombatOverlayWindow(CombatOverlayWindow.Part.Hp);
        // each of the three is positioned on its own; a window that is switched off is neither shown nor positioned
        bool lockedTarget = settings.CombatOverlayLocked, lockedCast = settings.CombatCastOverlayLocked ?? settings.CombatOverlayLocked, lockedHp = settings.CombatHpOverlayLocked ?? settings.CombatOverlayLocked;
        foreach (var (window, locked) in new[] { (_overlay, lockedTarget), (_castOverlay, lockedCast), (_hpOverlay, lockedHp) })
        {
            window.ApplyLockedIfChanged(locked);
            window.SetEditing(!locked);
        }

        bool inGame = GameWindow.ForegroundClientArea() is not null;
        if (!inGame && lockedTarget && lockedCast && lockedHp)
        {
            _overlay.ShowOverlay(false);
            _castOverlay.ShowOverlay(false);
            _hpOverlay.ShowOverlay(false);
            return;
        }

        CombatFrame frame = BuildFrame(dir, settings);

        // in the game, or while that window is being positioned (then only if its own switch is on)
        _overlay.ShowOverlay(_overlay.Render(frame, settings) && (inGame || !lockedTarget));
        _castOverlay.ShowOverlay(_castOverlay.Render(frame, settings) && (inGame || !lockedCast));
        _hpOverlay.ShowOverlay(_hpOverlay.Render(frame, settings) && (inGame || !lockedHp));
    }
}
