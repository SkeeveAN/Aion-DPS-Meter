using System.Windows;
using System.Windows.Controls;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>What the combat overlay shows at one moment: the casts (soonest first), the target of the target and the healer's target. Each is null/empty when there is nothing to show.</summary>
public sealed record CombatFrame(IReadOnlyList<CastRowData> Casts, TargetOfTargetData? TargetOfTarget, HealerData? Healer);

/// <summary>Works out the combat overlay's frame from what the packet stream said (see <see cref="Aion2CombatState"/>); no windows, so a test can run it.</summary>
public static class CombatOverlayData
{
    /// <summary>How long an entity counts as aiming at what it last used a skill on.</summary>
    private static readonly TimeSpan AimAge = TimeSpan.FromSeconds(10);

    /// <summary>A cast stays on the bar this long after it ended (its damage is about to land).</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(700);

    /// <summary>A monster this tough counts as a boss even when the game data does not say so (new bosses, Abyss bosses).</summary>
    private const long BossHitPoints = 500_000;

    public static CombatFrame Build(Aion2EntityDirectory dir, MeterSettings settings, DateTime now, string language)
    {
        IReadOnlyList<CastRowData> casts = settings.CombatShowBossCast ? Casts(dir, settings, now, language) : Array.Empty<CastRowData>();
        TargetOfTargetData? tot = settings.CombatShowTargetOfTarget ? TargetOfTarget(dir, settings, now, language) : null;
        HealerData? healer = settings.CombatShowHealer ? Healer(dir, settings, now, language) : null;
        return new CombatFrame(casts, tot, healer);
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
            string skill = Aion2SkillNames.Display(Aion2SkillNames.NameOf(cast.SkillId));
            string caster = Name(dir, cast.Actor, language);
            var row = new CastRowData(skill, caster, target, atYou, view.IsArea, lethal, (now - cast.Start).TotalMilliseconds, cast.DurationMs, view.LandMs, Aion2SkillIcons.PathFor(cast.SkillId) ?? (dir.BossNpcIdOf(cast.Actor) is int bossNpc ? Aion2Artwork.BossThumbPath(bossNpc) : null));
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

        if (dir.Combat.TargetOf(clicked.EntityId, now, AimAge) is not int aimed || aimed == clicked.EntityId)
        {
            return null;
        }

        var (name, image, isBoss) = Describe(dir, aimed, language);
        return new TargetOfTargetData(name, Name(dir, clicked.EntityId, language), image, isBoss,
            dir.HitPoints.Latest(aimed)?.Hp, dir.HitPoints.MaxOf(aimed), Effects(dir, settings, aimed, now));
    }

    public static HealerData? Healer(Aion2EntityDirectory dir, MeterSettings settings, DateTime now, string language)
    {
        if (dir.LocalTarget is not { } clicked)
        {
            return null;
        }

        int id = clicked.EntityId;
        long? hp = dir.HitPoints.Latest(id)?.Hp;
        long? max = dir.HitPoints.MaxOf(id);
        IReadOnlyList<EffectData> effects = Effects(dir, settings, id, now);
        return new HealerData(Name(dir, id, language), hp, max, effects);
    }

    /// <summary>The effects on an entity that the settings ask for (debuffs, buffs or both); none when effects are switched off.</summary>
    private static IReadOnlyList<EffectData> Effects(Aion2EntityDirectory dir, MeterSettings settings, int id, DateTime now)
    {
        var effects = new List<EffectData>();
        if (!settings.CombatHealerEffects)
        {
            return effects;
        }

        foreach (BuffNotice buff in dir.Combat.BuffsOn(id, now))
        {
            bool debuff = dir.IsKnownMonster(buff.Caster);
            if ((settings.CombatHealerEffectKind == "debuffs" && !debuff) || (settings.CombatHealerEffectKind == "buffs" && debuff))
            {
                continue;
            }

            string time = buff.Ends is { } end ? Math.Max(0, (int)Math.Ceiling((end - now).TotalSeconds)).ToString() : "∞";
            effects.Add(new EffectData(Aion2SkillNames.Display(Aion2SkillNames.NameOf(buff.SkillId)), Aion2SkillIcons.PathFor(buff.SkillId), time, debuff));
        }

        return effects;
    }

    /// <summary>The name of an entity: a player's, a monster's (in the chosen language), or its number when nothing is known.</summary>
    public static string Name(Aion2EntityDirectory dir, int id, string language) => Describe(dir, id, language).Name;

    public static (string Name, string? Image, bool IsBoss) Describe(Aion2EntityDirectory dir, int id, string language)
    {
        if (dir.IsKnownPlayer(id) || dir.IsLocalPlayer(id))
        {
            string? cls = dir.ClassOf(id);
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
    public const string BossCastId = "BossCast", HealerId = "Healer",
        TargetBlockId = "TargetBlock", TargetIconId = "TargetIcon", TargetNameId = "TargetName", TargetHpId = "TargetHp", TargetEffectsId = "TargetEffects";

    private static readonly string[] TargetParts = { TargetIconId, TargetNameId, TargetHpId, TargetEffectsId };

    private static CombatStage? _building;
    private static CombatOverlayWindow? _instance;

    private readonly CombatStage _stage;
    /// <summary>The floor inside the target-of-the-target block: its four parts are placed on it, and only inside the block.</summary>
    private readonly CombatStage _blockStage = new() { FillAvailable = true };
    private readonly BossCastView _bossCast = new();
    private readonly TargetIconView _targetIcon = new();
    private readonly TargetNameView _targetName = new();
    private readonly HpBarView _targetHp = new();
    private readonly EffectBarView _targetEffects = new();
    private readonly HealerTargetView _healer = new();
    private readonly Dictionary<string, CombatElementHost> _hosts = new();
    private bool _editing;

    public CombatOverlayWindow() : base("Aion DPS Combat", NewStage(), Load, Save, SystemParameters.WorkArea.Left + 40, 200, LoadSize, SaveSize)
    {
        _instance = this;
        _stage = _building!;
        _building = null;
        var settings = MeterSettings.Load();
        var loc = LocalizationManager.Instance;
        AddElement(BossCastId, _bossCast, loc["Combat.Element.BossCast"], settings, _stage);
        AddElement(TargetIconId, _targetIcon, loc["Combat.Element.TargetIcon"], settings, _blockStage);
        AddElement(TargetNameId, _targetName, loc["Combat.Element.TargetName"], settings, _blockStage);
        AddElement(TargetHpId, _targetHp, loc["Combat.Element.TargetHp"], settings, _blockStage);
        AddElement(TargetEffectsId, _targetEffects, loc["Combat.Element.TargetEffects"], settings, _blockStage);
        AddElement(TargetBlockId, _blockStage, loc["Combat.Element.TargetOfTarget"], settings, _stage);
        AddElement(HealerId, _healer, loc["Combat.Element.Healer"], settings, _stage);
        _stage.Wanted = StageSize(settings.CombatOverlayWidth, settings.CombatOverlayHeight);
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        };
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
        TargetBlockId => new CombatElementLayout { Left = 0, Top = 140, Width = 240, Height = 152 },
        TargetIconId => new CombatElementLayout { Left = 0, Top = 0, Width = 52, Height = 52 },
        TargetNameId => new CombatElementLayout { Left = 56, Top = 0, Width = 176, Height = 52 },
        TargetHpId => new CombatElementLayout { Left = 0, Top = 56, Width = 232, Height = 30 },
        TargetEffectsId => new CombatElementLayout { Left = 0, Top = 90, Width = 232, Height = 54 },
        _ => new CombatElementLayout { Left = 240, Top = 140, Width = 240, Height = 106 },
    };

    /// <summary>The frame's size before anyone pulled it: the stage plus the rim (2 + 1 px each side) and the grip's row.</summary>
    private static readonly Size DefaultFrame = new(486, 316);

    private static Size StageSize(double? frameWidth, double? frameHeight) =>
        new(Math.Max(60, (frameWidth is > 0 ? frameWidth.Value : DefaultFrame.Width) - 6), Math.Max(40, (frameHeight is > 0 ? frameHeight.Value : DefaultFrame.Height) - 20));

    private void AddElement(string id, UIElement view, string caption, MeterSettings settings, CombatStage stage)
    {
        if (!settings.CombatElements.TryGetValue(id, out CombatElementLayout? layout) || layout.Width <= 0 || layout.Height <= 0)
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

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.CombatOverlayWidth ?? DefaultFrame.Width, s.CombatOverlayHeight ?? DefaultFrame.Height);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.CombatOverlayWidth = width;
        s.CombatOverlayHeight = height;
        s.Save();
        if (_instance is { } window && width is > 0 && height is > 0)
        {
            window._stage.Wanted = StageSize(width, height);
        }
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.CombatOverlayLeft, s.CombatOverlayTop, s.CombatOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.CombatOverlayLeft = left;
        s.CombatOverlayTop = top;
        s.CombatOverlayScale = scale;
        s.Save();
    }

    /// <summary>Puts the elements back where they began.</summary>
    public static void ResetLayout()
    {
        var s = MeterSettings.Load();
        s.CombatElements.Clear();
        s.Save();
        _instance?.ReloadLayouts();
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
        bool any = false;

        bool castOn = settings.CombatShowBossCast;
        if (castOn && frame.Casts.Count > 0)
        {
            _bossCast.Show(frame.Casts);
        }
        else if (castOn && _editing)
        {
            _bossCast.Show(new[] { new CastRowData(loc["Combat.Sample.Skill"], loc["Combat.Sample.Boss"], loc["Combat.You"], true, true, false, 700, 1500, 400, null) });
        }

        bool castShown = castOn && (frame.Casts.Count > 0 || _editing);
        _hosts[BossCastId].Visibility = castShown ? Visibility.Visible : Visibility.Collapsed;
        any |= castShown;

        bool totOn = settings.CombatShowTargetOfTarget;
        TargetOfTargetData? shownTot = totOn ? frame.TargetOfTarget : null;
        if (totOn && shownTot is null && _editing)
        {
            shownTot = new TargetOfTargetData(loc["Combat.Sample.Player"], loc["Combat.Sample.Boss"], null, false, 6400, 10000, new[]
            {
                new EffectData("Poison", null, "12", true), new EffectData("Slow", null, "5", true),
            });
        }

        if (shownTot is { } tot)
        {
            _targetIcon.Show(tot);
            _targetName.Show(tot);
            _targetHp.Show(tot.Hp, tot.MaxHp);
            _targetEffects.Show(tot.Effects ?? Array.Empty<EffectData>());
        }

        bool totShown = shownTot is not null;
        _hosts[TargetBlockId].Visibility = totShown ? Visibility.Visible : Visibility.Collapsed;
        _hosts[TargetIconId].Visibility = Visibility.Visible;
        _hosts[TargetNameId].Visibility = Visibility.Visible;
        _hosts[TargetHpId].Visibility = totShown && settings.CombatHealerHpBar ? Visibility.Visible : Visibility.Collapsed;
        _hosts[TargetEffectsId].Visibility = totShown && settings.CombatHealerEffects ? Visibility.Visible : Visibility.Collapsed;
        any |= totShown;

        bool healerOn = settings.CombatShowHealer;
        _healer.Configure(settings.CombatHealerHpBar, settings.CombatHealerEffects);
        if (healerOn && frame.Healer is { } healer)
        {
            _healer.Show(healer);
        }
        else if (healerOn && _editing)
        {
            _healer.Show(new HealerData(loc["Combat.Sample.Player"], 6400, 10000, new[]
            {
                new EffectData("Poison", null, "12", true), new EffectData("Slow", null, "5", true), new EffectData("Shield", null, "∞", false),
            }));
        }

        bool healerShown = healerOn && (frame.Healer is not null || _editing);
        _hosts[HealerId].Visibility = healerShown ? Visibility.Visible : Visibility.Collapsed;
        any |= healerShown;
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
    private CombatOverlayWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private DateTime _savedLessonsAt = DateTime.UtcNow;
    private Aion2EntityDirectory? _lessonsFor;

    public CombatOverlayController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        _timer.Stop();
        _directory()?.Combat.SaveIfChanged();
        _overlay?.Close();
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

        if (!settings.ShowCombatOverlay)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = settings.CombatOverlayLocked;
        _overlay ??= new CombatOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        _overlay.SetEditing(!locked);
        bool inGame = GameWindow.ForegroundClientArea() is not null;
        if (locked && !inGame)
        {
            _overlay.ShowOverlay(false);
            return;
        }

        CombatFrame frame = dir is null
            ? new CombatFrame(Array.Empty<CastRowData>(), null, null)
            : CombatOverlayData.Build(dir, settings, DateTime.Now, LocalizationManager.Instance.Language);
        bool any = _overlay.Render(frame, settings);
        _overlay.ShowOverlay(any || !locked);
    }
}
