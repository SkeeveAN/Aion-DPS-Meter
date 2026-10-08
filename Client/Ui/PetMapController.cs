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
    private DateTime _lastDetect = DateTime.MinValue;
    private int _ticks;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;

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
            _settingsAt = DateTime.UtcNow;
        }

        var settings = _settings;
        bool locked = settings.PetFarmLocked;
        _map.ApplyLockedIfChanged(locked);
        _list.ApplyLockedIfChanged(locked);
        bool inGame = GameWindow.ForegroundClientArea() is not null;
        var directory = _directory();
        if (directory is null || (!inGame && locked))
        {
            Hide();
            return;
        }

        // Which map? (once a second; the previous one stays when no pet monster is in sight)
        if ((DateTime.UtcNow - _lastDetect).TotalSeconds >= 1)
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
        var states = directory.LocalPetStates;
        var chosen = settings.PetMapPets.Where(id => !states.Any(s => s.PetId == id && s.Level >= Aion2Pets.TopLevel)).ToHashSet();
        var position = directory.LocalPosition;
        if (_current is null || position is null || chosen.Count == 0 || PositionIsStale(directory, position.Value))
        {
            ShowEmpty(locked);
            return;
        }

        string key = _current.Key + "|" + string.Join(",", chosen.OrderBy(i => i));
        if (key != _pointsKey)
        {
            _pointsKey = key;
            _points = Aion2Maps.PointsOf(_current, chosen).ToList();
        }

        // Glide to the reported position
        float tx = position.Value.X, ty = position.Value.Y;
        if (!_haveView || Math.Abs(_x - tx) + Math.Abs(_y - ty) > 8000)
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

        var petsHere = _points.Select(p => p.PetId).Distinct().OrderBy(i => i).ToList();
        var colors = petsHere.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => PetMapPalette.Of(t.i));
        _map.Render(_current, _x, _y, Math.Clamp(settings.PetMapRadius, 50, 500), Math.Clamp(settings.PetMapOpacity, 0.2, 1.0), _points, colors);
        _map.ShowOverlay(true);

        if (++_ticks % 10 == 0 || _lastTarget != (tx, ty))
        {
            _lastTarget = (tx, ty);
            _list.Render(Rows(petsHere, colors, tx, ty, states));
        }

        _list.ShowOverlay(true);
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

    private IReadOnlyList<(Color, string, string, string)> Rows(List<int> pets, Dictionary<int, Color> colors, float x, float y, IReadOnlyList<Aion2PetState> states)
    {
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        var rows = new List<(double Nearest, (Color, string, string, string) Row)>();
        foreach (int pet in pets)
        {
            double nearest = _points.Where(p => p.PetId == pet).Select(p => Math.Sqrt((p.X - x) * (double)(p.X - x) + (p.Y - y) * (double)(p.Y - y))).DefaultIfEmpty(double.NaN).Min();
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

            rows.Add((nearest, (colors[pet], Aion2Pets.PetName(pet, language) ?? $"#{pet}", level, double.IsNaN(nearest) ? "" : $"{nearest / 100:0} m")));
        }

        // nearest first; pets without a spawn point on this map (unknown distance) last
        return rows.OrderBy(r => double.IsNaN(r.Nearest) ? double.MaxValue : r.Nearest).Select(r => r.Row).ToList();
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
        var sample = new List<(Color, string, string, string)> { (Colors.Gray, loc["PetMap.Placeholder"], "", "") };
        for (int i = 0; i < names.Count; i++)
        {
            sample.Add((PetMapPalette.Of(i), names[i], string.Format(loc["PetFarm.Level"], 1 + i % 2) + $" · {3 + i % 20}/{(i % 2 == 0 ? 25 : 75)}", $"{(i + 1) * 23} m"));
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
