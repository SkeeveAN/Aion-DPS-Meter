using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>The replay of one boss fight: the map, the timeline under it, play / pause, speed and the names.</summary>
public sealed class FightReplayWindow : Window
{
    private readonly ReplayData _data;
    private readonly ReplayView _view = new();
    private readonly ReplayTimeline _timeline = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Button _play = new() { Width = 34, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _speed = new() { Width = 64, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
    private double _time;
    private double _speedFactor = 1;
    private DateTime _last;
    private bool _playing;

    public FightReplayWindow(ReplayData data)
    {
        _data = data;
        var loc = LocalizationManager.Instance;
        Title = loc["Replay.Title"] + " - " + data.Title;
        Width = 960;
        Height = 700;
        MinWidth = 480;
        MinHeight = 360;
        Background = (System.Windows.Media.Brush)FindResource("Brush.Window");
        Foreground = (System.Windows.Media.Brush)FindResource("Brush.Text");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var names = new CheckBox { Content = loc["Replay.Names"], IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("Brush.Text") };
        names.Checked += (_, _) => _view.ShowNames = true;
        names.Unchecked += (_, _) => _view.ShowNames = false;
        foreach (double factor in new[] { 0.5, 1, 2, 4 })
        {
            _speed.Items.Add(new ComboBoxItem { Content = factor + "×", Tag = factor });
        }

        _speed.SelectedIndex = 1;
        _speed.SelectionChanged += (_, _) =>
        {
            if (_speed.SelectedItem is ComboBoxItem { Tag: double f })
            {
                _speedFactor = f;
            }
        };
        _play.Click += (_, _) => Toggle();
        var fit = new Button { Content = loc["Replay.Fit"], Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(16, 0, 0, 0) };
        fit.Click += (_, _) => _view.Fit();
        var early = new TextBlock { Text = loc["Replay.Early"], Foreground = (System.Windows.Media.Brush)FindResource("Brush.TextMuted"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 11, FontStyle = FontStyles.Italic };

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        controls.Children.Add(_play);
        controls.Children.Add(_speed);
        controls.Children.Add(_clock);
        controls.Children.Add(names);
        controls.Children.Add(fit);

        var panel = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(early, Dock.Bottom);
        DockPanel.SetDock(controls, Dock.Bottom);
        DockPanel.SetDock(_timeline, Dock.Bottom);
        panel.Children.Add(early);
        panel.Children.Add(controls);
        panel.Children.Add(_timeline);
        panel.Children.Add(new System.Windows.Controls.Border { BorderBrush = (System.Windows.Media.Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1), Child = _view, Margin = new Thickness(0, 0, 0, 6) });
        Content = panel;
        ThemedChrome.Apply(this);

        _view.Show(data);
        _timeline.Show(data);
        _timeline.Seek += seconds => SetTime(seconds);
        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) => _timer.Stop();
        PreviewKeyDown += OnKey;
        SetTime(0);
        UpdatePlayLabel();
    }

    /// <summary>Puts the replay at a moment (the picture command uses it).</summary>
    public void ShowAt(double seconds) => SetTime(seconds);

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                Toggle();
                e.Handled = true;
                break;
            case Key.Left:
                SetTime(_time - 5);
                e.Handled = true;
                break;
            case Key.Right:
                SetTime(_time + 5);
                e.Handled = true;
                break;
        }
    }

    private void Toggle()
    {
        if (_playing)
        {
            _playing = false;
            _timer.Stop();
        }
        else
        {
            if (_time >= _data.DurationSeconds - 0.05)
            {
                SetTime(0);
            }

            _playing = true;
            _last = DateTime.UtcNow;
            _timer.Start();
        }

        UpdatePlayLabel();
    }

    private void UpdatePlayLabel() => _play.Content = _playing ? "❚❚" : "▶";

    private void Tick()
    {
        DateTime now = DateTime.UtcNow;
        double next = _time + (now - _last).TotalSeconds * _speedFactor;
        _last = now;
        if (next >= _data.DurationSeconds)
        {
            SetTime(_data.DurationSeconds);
            _playing = false;
            _timer.Stop();
            UpdatePlayLabel();
            return;
        }

        SetTime(next);
    }

    private void SetTime(double seconds)
    {
        _time = Math.Clamp(seconds, 0, _data.DurationSeconds);
        _view.Time = _time;
        _timeline.Time = _time;
        _clock.Text = TimeSpan.FromSeconds(_time).ToString(@"m\:ss") + " / " + TimeSpan.FromSeconds(_data.DurationSeconds).ToString(@"m\:ss");
    }
}
