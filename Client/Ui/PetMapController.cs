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

    public PetMapController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void ApplyLocked(bool locked)
    {
        _map.ApplyLocked(locked);
        _list.ApplyLocked(locked);
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

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load(); // the settings file is read once a second, not every tick
            _faction = Aion2Gather.OwnFaction();
            _settingsAt = DateTime.UtcNow;
        }

        var settings = _settings;
        bool locked = settings.PetFarmLocked;
        _map.ApplyLockedIfChanged(locked);
        _list.ApplyLockedIfChanged(locked);
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
        var chosen = settings.PetMapPets.Where(id => !states.Any(s => s.PetId == id && s.Level >= Aion2Pets.TopLevel)).ToHashSet();
        // only what the player's own faction can use (an earlier pick of the other side's plants no longer counts)
        var gatherKinds = Aion2Gather.Items().Where(i => settings.PetMapGatherItems.Contains(i.Key) && i.IsFor(_faction)).Select(i => i.Key).ToHashSet();
        if (chosen.Count == 0 && gatherKinds.Count == 0)
        {
            ShowEmpty(locked);
            return;
        }

        // Where is the player? Unknown (game closed, nothing received yet, teleported): the last known place stays on the map, marked with a "?"
        var position = directory?.BestPosition;
        bool known = directory is not null && position is not null && _current is not null && !PositionIsStale(directory, position.Value);
        float tx, ty;
        if (known)
        {
            tx = position!.Value.X;
            ty = position.Value.Y;
            RememberPosition(tx, ty);
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
            _gatherNamed = _current is null ? new() : Aion2Gather.NamedPointsOf(_current, gatherKinds).ToList();
            _gather = _gatherNamed.Select(g => (g.Item.Kind, g.X, g.Y)).ToList();
        }

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
        if (_current is not null)
        {
            _map.Render(_current, _x, _y, Math.Clamp(settings.PetMapRadius, 50, 500), Math.Clamp(settings.PetMapOpacity, 0.2, 1.0), _points, live, colors, _gather, known);
            _map.ShowOverlay(true);
        }
        else
        {
            _map.ShowOverlay(!locked);
        }

        if (++_ticks % 10 == 0 || _lastTarget != (tx, ty) || _lastKnownFlag != known)
        {
            _lastTarget = (tx, ty);
            _lastKnownFlag = known;
            _list.Render(Rows(petsHere, colors, tx, ty, states, live, known, gatherKinds));
        }

        _list.ShowOverlay(true);
    }

    private (float X, float Y)? _lastKnown;
    private bool _lastKnownFlag;
    private List<(Aion2GatherItem Item, float X, float Y)> _gatherNamed = new();
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

        foreach (int pet in pets)
        {
            double nearest = known ? _points.Where(p => p.PetId == pet).Select(p => Dist(p.X, p.Y)).DefaultIfEmpty(double.NaN).Min() : double.NaN;
            // a monster the game has announced around the player beats the general spawn points: its distance counts, the others show "~"
            double nearestLive = live.Where(p => p.PetId == pet).Select(p => Dist(p.X, p.Y)).DefaultIfEmpty(double.NaN).Min();
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

    private void ShowEmpty(bool locked)
    {
        if (locked)
        {
            Hide();
            return;
        }

        // unlocked: show something to grab and place
        // sample rows so the length of the field can be judged while placing it (the real list fills as many as fit)
        var loc = LocalizationManager.Instance;
        var names = Aion2Pets.Species.SelectMany(sp => Aion2Pets.MapPetsOf(sp, loc.Language)).Select(p => p.Name).Take(60).ToList();
        var sample = new List<(Color, string, string, string, bool)> { (Colors.Gray, loc["PetMap.Placeholder"], "", "", false) };
        for (int i = 0; i < names.Count; i++)
        {
            sample.Add((PetMapPalette.Of(i), names[i], string.Format(loc["PetFarm.Level"], 1 + i % 2) + $" · {3 + i % 20}/{(i % 2 == 0 ? 25 : 75)}", $"{(i + 1) * 23} m", false));
        }

        _list.Render(sample);
        _list.ShowOverlay(true);
        _map.ShowOverlay(true);
    }

    private void Hide()
    {
        _map.ShowOverlay(false);
        _list.ShowOverlay(false);
    }
}
