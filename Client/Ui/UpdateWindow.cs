using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AionDPS.Update;

namespace AionDPS.Ui;

/// <summary>
/// The question about an update ("install it?" or "restart now?"): the meter's own themed window, centred over the meter, with the list of what is new
/// between the installed version and the offered one (one short line per change, a block per release, newest first; with more than
/// <see cref="OpenReleases"/> releases the older ones are folded up). It replaced the card inside the main window, which had no room for the list
/// (the window is about 340 px wide).
/// </summary>
public static class UpdateWindow
{
    /// <summary>The newest releases that start unfolded.</summary>
    public const int OpenReleases = 3;

    private const double WindowWidth = 560;
    private const double WindowMaxHeight = 506;

    /// <summary>Shows the window and returns whether the user chose the second button ("install" / "restart").</summary>
    public static bool Ask(Window? owner, string heading, string message, string whatsNewHeader, IReadOnlyList<ChangelogVersion> changes, string noLabel, string yesLabel)
    {
        Window window = Build(owner, heading, message, whatsNewHeader, changes, noLabel, yesLabel, out Answer answer);
        window.ShowDialog();
        return answer.Yes;
    }

    /// <summary>The answer of a window that is still open.</summary>
    public sealed class Answer
    {
        public bool Yes { get; set; }
    }

    /// <summary>Builds the window without showing it (<see cref="Ask"/> shows it; the picture mode of the tests draws it).</summary>
    public static Window Build(Window? owner, string heading, string message, string whatsNewHeader, IReadOnlyList<ChangelogVersion> changes, string noLabel, string yesLabel, out Answer answer)
    {
        var holder = new Answer();
        answer = holder;
        var window = new Window
        {
            Title = "Aion DPS",
            Width = WindowWidth,
            SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Min(WindowMaxHeight, SystemParameters.WorkArea.Height),
            ShowInTaskbar = owner is not { IsVisible: true },
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }

        // the message is "<version line>\n\n<question>": the list goes between the two
        string[] parts = message.Split(new[] { "\n\n" }, 2, StringSplitOptions.None);
        string info = parts[0];
        string question = parts.Length > 1 ? parts[1] : "";

        var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        panel.Children.Add(Text(heading, 14, FontWeights.Bold, new Thickness(0, 0, 0, 8)));
        panel.Children.Add(Text(info, 13, FontWeights.Normal, new Thickness(0, 0, 0, 10)));
        if (changes.Count > 0)
        {
            panel.Children.Add(Text(whatsNewHeader, 13, FontWeights.SemiBold, new Thickness(0, 4, 0, 6)));
            panel.Children.Add(BuildList(changes));
        }

        if (question.Length > 0)
        {
            panel.Children.Add(Text(question, 13, FontWeights.Normal, new Thickness(0, 0, 0, 12)));
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(Button(noLabel, isCancel: true, () => window.Close()));
        row.Children.Add(Button(yesLabel, isCancel: false, () =>
        {
            holder.Yes = true;
            window.Close();
        }));
        panel.Children.Add(row);

        window.Content = panel;
        ThemedChrome.Apply(window);
        window.ResizeMode = ResizeMode.NoResize;
        return window;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, Thickness margin)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, FontWeight = weight, Margin = margin };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        return block;
    }

    private static Button Button(string label, bool isCancel, Action click)
    {
        var button = new Button { Content = label, MinWidth = 84, Height = 28, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0), IsCancel = isCancel, IsDefault = !isCancel };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>One block per release (version, date, the changes as bullet lines) in a scrolling panel; older releases than <see cref="OpenReleases"/> start folded up.</summary>
    private static UIElement BuildList(IReadOnlyList<ChangelogVersion> changes)
    {
        var stack = new StackPanel();
        CultureInfo culture = CultureOf(LocalizationManager.Instance.Language);
        for (int i = 0; i < changes.Count; i++)
        {
            ChangelogVersion release = changes[i];
            var lines = new StackPanel { Margin = new Thickness(0, 0, 0, 8), Visibility = i < OpenReleases ? Visibility.Visible : Visibility.Collapsed };
            foreach (string item in release.Items)
            {
                var line = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var bullet = Text("•", 12, FontWeights.Normal, new Thickness(0));
                bullet.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                var text = Text(item, 12, FontWeights.Normal, new Thickness(0));
                Grid.SetColumn(text, 1);
                line.Children.Add(bullet);
                line.Children.Add(text);
                lines.Children.Add(line);
            }

            string date = DateTime.TryParse(release.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed) ? parsed.ToString("d", culture) : release.Date;
            var arrow = Text(lines.Visibility == Visibility.Visible ? "▾" : "▸", 12, FontWeights.Normal, new Thickness(0, 0, 8, 0));
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            var version = Text(release.Version, 13, FontWeights.Bold, new Thickness(0, 0, 10, 0));
            version.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            var when = Text(date, 11, FontWeights.Normal, new Thickness(0, 1, 0, 0));
            when.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4), Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
            header.Children.Add(arrow);
            header.Children.Add(version);
            header.Children.Add(when);
            header.MouseLeftButtonUp += (_, _) =>
            {
                bool open = lines.Visibility != Visibility.Visible;
                lines.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                arrow.Text = open ? "▾" : "▸";
            };
            stack.Children.Add(header);
            stack.Children.Add(lines);
        }

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 266, Content = stack, Padding = new Thickness(0, 0, 6, 0) };
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(12, 6, 6, 0), Margin = new Thickness(0, 0, 0, 14), Child = scroll };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static CultureInfo CultureOf(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
