using System.Windows;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// Runs the pet map and its list: twenty times a second the view glides towards the player's last reported position, once a second the
/// map is recognised (from the pet monsters around), and the chosen pets' spawn points are drawn. Both windows are shown while the game is
/// in front (always when unlocked, so they can be placed); with nothing to show they stay hidden.
/// </summary>
public sealed class PetMapController : IDisposable
{
    private readonly PetMapWindow _map = new();
    private readonly PetListWindow _list = new();
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private Aion2MapInfo? _current;
    private double _x, _y;
    private bool _haveView;
    private (float X, float Y)? _lastTarget;
    private List<(int PetId, float X, float Y)> _points = new();
    private string _pointsKey = "";
    private List<(string Kind, float X, float Y)> _gather = new();
    private DateTime _lastDetect = DateTime.MinValue;
    private int _ticks;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private string? _faction = Aion2Gather.OwnFaction();
    private HashSet<int> _savedMax = SavedMaxPets();

    public PetMapController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void ApplyLocked(bool locked)
    {
        _map.ApplyLocked(locked);
        _list.ApplyLocked(_settings.PetListLocked ?? locked);
    }

    /// <summary>The map and the list are two overlays, each with its own switch (the module's switch is the master): shown only when both are on.</summary>
    private void ShowMap(bool visible) => _map.ShowOverlay(visible && _settings.PetMapShowMap);

    private void ShowList(bool visible) => _list.ShowOverlay(visible && _settings.PetMapShowList);

    /// <summary>Sample rows for the list while it is positioned: a few pets and collectibles of the game, in the settings' size.</summary>
    private static IReadOnlyList<(Color, string, string, string, bool)> SampleRows()
    {
        string language = LocalizationManager.Instance.Language;
        var rows = new List<(Color, string, string, string, bool)>();
        int i = 0;
        foreach (int pet in Aion2Pets.AllPetIds.OrderBy(p => p).Take(3))
        {
            rows.Add((PetMapPalette.Of(i++), Aion2Pets.PetName(pet, language) ?? $"#{pet}", string.Format(LocalizationManager.Instance["PetFarm.Level"], 1) + " · 5/" + Aion2Pets.ProgressNeeded(1), $"{40 + i * 35} m", false));
        }

        foreach (var item in Aion2Gather.Items().Take(3))
        {
            rows.Add((PetMapPalette.OfGather(item.Kind), item.NameIn(language), "", $"~{60 + i++ * 20} m", true));
        }

        return rows;
    }

    public void RaiseToFront()
    {
        _map.RaiseToFront();
        _list.RaiseToFront();
    }

    public void Dispose()
    {
        _timer.Stop();
        _map.Close();
        _list.Close();
    }

    /// <summary>The pseudo pet id under which the live Hidden Cubes travel with the live monsters.</summary>
    private const int CubePet = -1;

    private (float X, float Y)? _steady;
    private DateTime _steadyAt;

    /// <summary>
    /// The position the map follows. An exact report of the game is taken as it is; the guess from the monsters around is only a pull towards the
    /// estimate - slow (about a second) and at most 15 m/s - so a wrong or flipping guess cannot throw the view across the map. A jump of more than
    /// 300 m (a teleport, a new map) is taken at once.
    /// </summary>
    private (float X, float Y) Steady(Aion2EntityDirectory directory, (float X, float Y, DateTime At) p)
    {
        DateTime now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _steadyAt).TotalSeconds, 0, 1);
        _steadyAt = now;
        bool exact = directory.LocalPosition is { } lp && now - lp.At < TimeSpan.FromSeconds(6) && lp.X == p.X && lp.Y == p.Y;
        if (_steady is not { } cur)
        {
            return (_steady = (p.X, p.Y)).Value;
        }

        double dx = p.X - cur.X, dy = p.Y - cur.Y, dist = Math.Sqrt(dx * dx + dy * dy);
        if (exact || dist > 30000)
        {
            return (_steady = (p.X, p.Y)).Value;
        }

        double step = Math.Min(dist, Math.Min(dist * (1 - Math.Pow(0.94, dt * 20)), 1500 * dt));
        if (dist > 0)
        {
            _steady = ((float)(cur.X + dx / dist * step), (float)(cur.Y + dy / dist * step));
        }

        return _steady.Value;
    }

    private (float X, float Y)? _headingFrom;
    private double _heading;

    /// <summary>
    /// The game sends no facing, so the arrow points the way the player moves: from the point where he last stood to where he is now, once he has gone
    /// far enough (the reported position jitters by a few metres) - 8 m; standing still keeps the last direction.
    /// </summary>
    private void FollowHeading(float x, float y)
    {
        if (_headingFrom is not { } from)
        {
            _headingFrom = (x, y);
            return;
        }

        double dx = x - from.X, dy = y - from.Y;
        if (dx * dx + dy * dy >= 800.0 * 800.0)
        {
            _heading = Math.Atan2(dy, dx) * 180 / Math.PI + 90; // the arrow points up at 0 degrees; the map's y runs down the screen
            _headingFrom = (x, y);
        }
    }

    private DateTime _lastCubeBeep = DateTime.MinValue;

    /// <summary>
    /// The Hidden Cube sound works like a parking sensor: while a cube is in range the sound repeats, the faster the closer the player stands to the nearest one
    /// (about every 2 s from 60 m away down to 0.15 s within 3 m; 100 world units are a metre). Without a current own position it stays silent.
    /// </summary>
    private void AnnounceCubes(MeterSettings settings)
    {
        if (!settings.CubeSoundEnabled)
        {
            return;
        }

        var directory = _directory();
        var cubes = directory?.LiveCubes() ?? Array.Empty<(float X, float Y)>();
        if (cubes.Count == 0)
        {
            return;
        }

        // without a current own position the distance is unknown: silence
        if (directory!.BestPosition is not { } me || DateTime.UtcNow - me.At >= TimeSpan.FromSeconds(5))
        {
            return;
        }

        double meters = cubes.Min(c => Math.Sqrt((c.X - me.X) * (c.X - me.X) + (c.Y - me.Y) * (c.Y - me.Y))) / 100.0;
        double interval = Math.Clamp(0.15 + (meters - 3) / 57.0 * 1.85, 0.15, 2.0);

        if ((DateTime.UtcNow - _lastCubeBeep).TotalSeconds >= interval)
        {
            _lastCubeBeep = DateTime.UtcNow;
            NotifySounds.Play(settings.CubeSound, settings.CubeSoundVolume);
        }
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load(); // the settings file is read once a second, not every tick
            _faction = Aion2Gather.OwnFaction();
            _savedMax = SavedMaxPets();
            _settingsAt = DateTime.UtcNow;
        }

        var settings = _settings;
        AnnounceCubes(settings);
        bool mapLocked = settings.PetFarmLocked, listLocked = settings.PetListLocked ?? settings.PetFarmLocked;
        bool locked = mapLocked && listLocked;
        _map.ApplyLockedIfChanged(mapLocked);
        _list.ApplyLockedIfChanged(listLocked);
        bool inGame = GameWindow.ForegroundClientArea() is not null;
        if (!inGame && locked)
        {
            Hide();
            return;
        }

        var directory = _directory();

        // Which map? (once a second; the previous one stays when no pet monster is in sight)
        if (directory is not null && (DateTime.UtcNow - _lastDetect).TotalSeconds >= 1)
        {
            _lastDetect = DateTime.UtcNow;
            var found = Aion2Maps.Detect(directory.RecentMobPositions());
            if (found is not null && found != _current)
            {
                _current = found;
                _haveView = false;
                _pointsKey = "";
            }
        }

        // The chosen pets (those at the top level need no farming)
        var states = directory?.LocalPetStates ?? Array.Empty<Aion2PetState>();
        // before the game's pet list has arrived (the meter started after the login) the levels saved at the last login stand in
        var maxPets = states.Count > 0 ? states.Where(s => s.Level >= Aion2Pets.TopLevel).Select(s => s.PetId).ToHashSet() : _savedMax;
        var chosen = settings.PetMapPets.Where(id => !maxPets.Contains(id)).ToHashSet();
        // only what the player's own faction can use (an earlier pick of the other side's plants no longer counts)
        var gatherKinds = Aion2Gather.Items().Where(i => settings.PetMapGatherItems.Contains(i.Key) && i.IsFor(_faction)).Select(i => i.Key).ToHashSet();
        var shopRows = ShopRows(directory, states);
        if (chosen.Count == 0 && gatherKinds.Count == 0)
        {
            if (shopRows.Count == 0)
            {
                ShowEmpty(mapLocked, listLocked);
                return;
            }

            // only the pets of the open shop to show: the list without a map
            _list.Render(shopRows.Take(_list.RowCapacity).ToList());
            ShowList(true);
            ShowMap(!mapLocked);
            return;
        }

        // Where is the player? Unknown (game closed, nothing received yet, teleported): the last known place stays on the map, marked with a "?"
        var position = directory?.BestPosition;
        bool known = directory is not null && position is not null && _current is not null && !PositionIsStale(directory, position.Value);
        float tx, ty;
        if (known)
        {
            (tx, ty) = Steady(directory!, position!.Value);
            RememberPosition(tx, ty);
            FollowHeading(tx, ty);
        }
        else
        {
            if (_current is null && settings.PetMapLastMap is { } lastMap)
            {
                _current = Aion2Maps.All.FirstOrDefault(m => m.Key == lastMap);
                _pointsKey = "";
            }

            if (_lastKnown is null && settings.PetMapLastX is { } lx && settings.PetMapLastY is { } ly)
            {
                _lastKnown = ((float)lx, (float)ly);
            }

            (tx, ty) = _lastKnown ?? (0f, 0f);
            if (_lastKnown is null)
            {
                _current = null;
            }
        }

        string key = (_current?.Key ?? "-") + "|" + string.Join(",", chosen.OrderBy(i => i)) + "|" + string.Join(",", gatherKinds.OrderBy(k => k));
        if (key != _pointsKey)
        {
            _pointsKey = key;
            _points = _current is null ? new() : Aion2Maps.PointsOf(_current, chosen).ToList();
            _gatherStatic = _current is null ? new() : Aion2Gather.NamedPointsOf(_current, gatherKinds).ToList();
        }

        ShowLiveGather(gatherKinds, known, tx, ty);

        // Glide to the reported position
        if (!_haveView || !known || Math.Abs(_x - tx) + Math.Abs(_y - ty) > 8000)
        {
            _x = tx;
            _y = ty;
            _haveView = true;
        }
        else
        {
            _x += (tx - _x) * 0.22;
            _y += (ty - _y) * 0.22;
        }

        // without a known map the list names everything chosen (no distances); with one, only what stands on it
        var petsHere = _current is null ? chosen.OrderBy(i => i).ToList() : _points.Select(p => p.PetId).Distinct().OrderBy(i => i).ToList();
        var colors = petsHere.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => PetMapPalette.Of(t.i));
        var live = known
            ? directory!.LiveMobs().SelectMany(m => Aion2Pets.PetsOfNpc(m.NpcId).Where(chosen.Contains).Select(pet => (pet, m.X, m.Y))).ToList()
            : new List<(int PetId, float X, float Y)>();
        // the Hidden Cubes in range (the game announces them only when the player is near): large gold dots like the live monsters, when the map shows cubes
        if (gatherKinds.Contains("Hidden Cube") && known)
        {
            live.AddRange(directory!.LiveCubes().Select(c => (CubePet, c.X, c.Y)));
            colors[CubePet] = PetMapPalette.OfGather("Cube");
        }

        _map.MeHeading = _heading;
        if (_current is not null)
        {
            _map.Render(_current, _x, _y, Math.Clamp(settings.PetMapRadius, 50, 500), Math.Clamp(settings.PetMapOpacity, 0.0, 1.0), _points, live, colors, _gather, known, settings.ShowPetArrows, settings.ShowGatherArrows);
            ShowMap(true);
        }
        else
        {
            ShowMap(!mapLocked);
        }

        if (++_ticks % 10 == 0 || _lastTarget != (tx, ty) || _lastKnownFlag != known)
        {
            _lastTarget = (tx, ty);
            _lastKnownFlag = known;
            _list.Render(shopRows.Concat(Rows(petsHere, colors, tx, ty, states, live, known, gatherKinds)).Take(_list.RowCapacity).ToList()); // only what fits in the frame (plus a reserve)
        }

        ShowList(true);
    }

    /// <summary>
    /// The pets the Black Cloud shop offers on the tabs the player has opened, with what he has of them: the ones he lacks first, then by level.
    /// The right column names the tab and the price. Without the game's pet list (the meter started after the login) the pets saved at the last login stand in.
    /// </summary>
    private static IReadOnlyList<(Color, string, string, string, bool)> ShopRows(Aion2EntityDirectory? directory, IReadOnlyList<Aion2PetState> states) =>
        directory?.ShopPets is { Count: > 0 } offers ? ShopRowsOf(offers, states) : Array.Empty<(Color, string, string, string, bool)>();

    public static IReadOnlyList<(Color, string, string, string, bool)> ShopRowsOf(IReadOnlyList<Aion2ShopPet> offers, IReadOnlyList<Aion2PetState> states)
    {
        var loc = LocalizationManager.Instance;
        var owned = states.Count > 0
            ? states.ToDictionary(p => p.PetId)
            : (Aion2CharacterStore.Load(Aion2CharacterStore.DefaultPath)?.Pets ?? new()).ToDictionary(p => p.Id, p => new Aion2PetState(p.Id, p.Level, p.Progress));
        var rows = new List<(int Rank, int Level, (Color, string, string, string, bool) Row)>();
        foreach (var offer in offers)
        {
            owned.TryGetValue(offer.PetId, out var state);
            string status;
            Color color;
            if (state is null)
            {
                status = loc["PetShop.Missing"];
                color = Color.FromRgb(0xFF, 0x5A, 0x4F);
            }
            else if (state.Level >= Aion2Pets.TopLevel)
            {
                status = loc["PetFarm.Max"];
                color = Color.FromRgb(0x4C, 0xC8, 0x78);
            }
            else
            {
                status = string.Format(loc["PetFarm.Level"], state.Level) + $" · {state.Progress}/{Aion2Pets.ProgressNeeded(state.Level)}";
                color = Color.FromRgb(0x9D, 0xB3, 0xC2);
            }

            string tab = loc[offer.Tab == 0 ? "PetShop.TabSpecial" : "PetShop.TabPet"];
            rows.Add((state is null ? 0 : 1, state?.Level ?? 0, (color, Aion2Pets.PetName(offer.PetId, loc.Language) ?? $"#{offer.PetId}", status, $"{tab} · {offer.Price:N0}", true)));
        }

        return rows.OrderBy(r => r.Rank).ThenBy(r => r.Level).Select(r => r.Row).ToList();
    }

    private static HashSet<int> SavedMaxPets() =>
        (Aion2CharacterStore.Load(Aion2CharacterStore.DefaultPath)?.Pets ?? new()).Where(p => p.Level >= Aion2Pets.TopLevel).Select(p => p.Id).ToHashSet();

    private (float X, float Y)? _lastKnown;
    private bool _lastKnownFlag;
    private List<(Aion2GatherItem Item, float X, float Y)> _gatherNamed = new();
    private List<(Aion2GatherItem Item, float X, float Y)> _gatherStatic = new();

    /// <summary>How close to the player a fixed place must be for the game's announcements to be complete: nothing announced there means nothing there (gathered, not grown yet).</summary>
    private const double AnnounceRange = 4000;

    /// <summary>
    /// What the map shows of the collectibles: the fixed places of the map files, except those in the player's range where the game announces nothing
    /// (gathered or not there yet; for the Hidden Cubes: the candidate places far away, only the real cube near), plus what the game announces - which also covers the places the map files do not hold (Od, the Hidden Cubes).
    /// </summary>
    private void ShowLiveGather(ISet<string> keys, bool known, float px, float py)
    {
        var live = known && _current is not null ? (_directory()?.LiveEnv().Where(e => keys.Contains(e.Key)).ToList()) : null;
        var items = Aion2Gather.Items().Where(i => keys.Contains(i.Key)).ToDictionary(i => i.Key);
        var shown = new List<(Aion2GatherItem Item, float X, float Y)>();
        foreach (var point in _gatherStatic)
        {
            bool inRange = known && Math.Abs(point.X - px) < AnnounceRange && Math.Abs(point.Y - py) < AnnounceRange
                && (point.X - px) * (double)(point.X - px) + (point.Y - py) * (double)(point.Y - py) < AnnounceRange * AnnounceRange;
            if (inRange && live is not null && !live.Any(e => e.Key == point.Item.Key && Math.Abs(e.X - point.X) < 300 && Math.Abs(e.Y - point.Y) < 300))
            {
                continue;
            }

            shown.Add(point);
        }

        foreach (var e in live ?? new())
        {
            if (items.TryGetValue(e.Key, out var item) && !shown.Any(s => s.Item.Key == e.Key && Math.Abs(s.X - e.X) < 300 && Math.Abs(s.Y - e.Y) < 300))
            {
                shown.Add((item, e.X, e.Y));
            }
        }

        _gatherNamed = shown;
        _gather = shown.Select(g => (g.Item.Kind, g.X, g.Y)).ToList();
    }
    private DateTime _savedAt = DateTime.MinValue;

    /// <summary>Keeps the last known place (in memory at once, in the settings file every few seconds) for the times the position is not known.</summary>
    private void RememberPosition(float x, float y)
    {
        _lastKnown = (x, y);
        if (_current is null || (DateTime.UtcNow - _savedAt).TotalSeconds < 10)
        {
            return;
        }

        _savedAt = DateTime.UtcNow;
        var onDisk = MeterSettings.Load();
        if (onDisk.PetMapLastMap == _current.Key && onDisk.PetMapLastX is { } ox && onDisk.PetMapLastY is { } oy && Math.Abs(ox - x) + Math.Abs(oy - y) < 500)
        {
            return;
        }

        onDisk.PetMapLastMap = _current.Key;
        onDisk.PetMapLastX = x;
        onDisk.PetMapLastY = y;
        onDisk.Save();
    }

    /// <summary>The reported position is old when the monsters announced around the player stand far from it (he was teleported, e.g. into a city).</summary>
    private static bool PositionIsStale(Aion2EntityDirectory directory, (float X, float Y, DateTime At) position)
    {
        var mobs = directory.RecentMobPositions();
        if (mobs.Count < 5 || DateTime.UtcNow - position.At < TimeSpan.FromSeconds(30))
        {
            return false;
        }

        var distances = mobs.Select(m => Math.Sqrt((m.X - position.X) * (double)(m.X - position.X) + (m.Y - position.Y) * (double)(m.Y - position.Y))).OrderBy(d => d).ToList();
        return distances[distances.Count / 2] > 60000; // more than 600 m
    }

    private IReadOnlyList<(Color, string, string, string, bool)> Rows(List<int> pets, Dictionary<int, Color> colors, float x, float y, IReadOnlyList<Aion2PetState> states,
        List<(int PetId, float X, float Y)> live, bool known, ISet<string> gatherKeys)
    {
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        double Dist(float px, float py) => Math.Sqrt((px - x) * (double)(px - x) + (py - y) * (double)(py - y));
        var gatherRows = new List<(double Nearest, (Color, string, string, string, bool) Row)>();
        var petRows = new List<(double Nearest, (Color, string, string, string, bool) Row)>();

        // collectibles: the chosen ones that stand on this map (all of them while the map is unknown)
        var items = Aion2Gather.Items().Where(i => gatherKeys.Contains(i.Key)).ToList();
        foreach (var item in items)
        {
            var mine = _gatherNamed.Where(g => g.Item.Key == item.Key).ToList();
            if (mine.Count == 0 && _current is not null)
            {
                continue;
            }

            double nearest = known && mine.Count > 0 ? mine.Min(g => Dist(g.X, g.Y)) : double.NaN;
            gatherRows.Add((nearest, (PetMapPalette.OfGather(item.Kind), item.NameIn(language), "", known && !double.IsNaN(nearest) ? $"~{nearest / 100:0} m" : "", true)));
        }

        // nearest point per pet in one pass over all points (not one pass per pet: the lists hold thousands of spawn points)
        var nearestPoint = new Dictionary<int, double>();
        var nearestLiveOf = new Dictionary<int, double>();
        if (known)
        {
            foreach (var (id, px, py) in _points)
            {
                double d = Dist(px, py);
                if (!nearestPoint.TryGetValue(id, out double best) || d < best)
                {
                    nearestPoint[id] = d;
                }
            }
        }

        foreach (var (id, px, py) in live)
        {
            double d = Dist(px, py);
            if (!nearestLiveOf.TryGetValue(id, out double best) || d < best)
            {
                nearestLiveOf[id] = d;
            }
        }

        foreach (int pet in pets)
        {
            double nearest = known ? nearestPoint.GetValueOrDefault(pet, double.NaN) : double.NaN;
            // a monster the game has announced around the player beats the general spawn points: its distance counts, the others show "~"
            double nearestLive = nearestLiveOf.GetValueOrDefault(pet, double.NaN);
            bool isLive = !double.IsNaN(nearestLive);
            if (isLive)
            {
                nearest = nearestLive;
            }
            string level;
            if (states.Count == 0)
            {
                level = "";
            }
            else if (states.FirstOrDefault(s => s.PetId == pet) is { } state)
            {
                level = string.Format(loc["PetFarm.Level"], state.Level) + $" · {state.Progress}/{Aion2Pets.ProgressNeeded(state.Level)}";
            }
            else
            {
                level = loc["PetFarm.New"];
            }

            petRows.Add((nearest, (colors.GetValueOrDefault(pet, Colors.White), Aion2Pets.PetName(pet, language) ?? $"#{pet}", level, double.IsNaN(nearest) ? "" : $"{(isLive ? "" : "~")}{nearest / 100:0} m", false)));
        }

        if (!known)
        {
            // position unknown: the collectibles on top, the pets below, no distances
            return gatherRows.Concat(petRows).Select(r => r.Row).ToList();
        }

        // nearest first; entries without a place on this map (unknown distance) last
        return gatherRows.Concat(petRows).OrderBy(r => double.IsNaN(r.Nearest) ? double.MaxValue : r.Nearest).Select(r => r.Row).ToList();
    }

    private void ShowEmpty(bool mapLocked, bool listLocked)
    {
        if (mapLocked && listLocked)
        {
            Hide();
            return;
        }

        // positioned: the frames (the list with sample rows), to grab and place; a locked one stays hidden
        _list.Render(SampleRows());
        ShowList(!listLocked);
        ShowMap(!mapLocked);
    }

    private void Hide()
    {
        ShowMap(false);
        ShowList(false);
    }
}
