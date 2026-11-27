using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// A window of its own for the announcements of NC and the Twitch drops, with the content of the pages of aiondps.com drawn in the app (no browser): the lists come from the
/// site's own JSON (a plain GET without any identifier of the player, to the fixed host).
/// </summary>
public sealed class NewsWindow : Window
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private static NewsWindow? _announcements, _drops;
    private readonly StackPanel _list = new() { Margin = new Thickness(18, 14, 18, 18) };
    private readonly ScrollViewer _scroll;

    private NewsWindow(string title)
    {
        Title = title;
        // wide enough to read a notice, and still inside a 1080p screen
        Width = Math.Min(980, SystemParameters.WorkArea.Width - 60);
        Height = Math.Min(840, SystemParameters.WorkArea.Height - 60);
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
        Content = _scroll;
        ThemedChrome.Apply(this);
    }

    public static void ShowAnnouncements()
    {
        if (_announcements is { IsLoaded: true })
        {
            _announcements.Activate();
            return;
        }

        var loc = LocalizationManager.Instance;
        _announcements = new NewsWindow(loc["Hub.Dash.Announcements"]);
        _announcements.Closed += (_, _) => _announcements = null;
        _announcements.Show();
        _ = _announcements.FillAnnouncements();
    }

    public static void ShowDrops()
    {
        if (_drops is { IsLoaded: true })
        {
            _drops.Activate();
            return;
        }

        var loc = LocalizationManager.Instance;
        _drops = new NewsWindow(loc["Hub.Dash.Drops"]);
        _drops.Closed += (_, _) => _drops = null;
        _drops.Show();
        _ = _drops.FillDrops();
    }

    private TextBlock Text(string text, double size = 13, bool bold = false, string brush = "Brush.Text", double top = 0)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private Border Card(params UIElement[] rows)
    {
        var stack = new StackPanel();
        foreach (UIElement row in rows)
        {
            stack.Children.Add(row);
        }

        var card = new Border { Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = stack };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private async Task<JsonDocument?> Get(string url)
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(url);
            return response.IsSuccessStatusCode ? JsonDocument.Parse(await response.Content.ReadAsStringAsync()) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>A selectable text (copy with the mouse); the mouse wheel goes on to the window's scroll.</summary>
    private TextBox Reader(string text, double size, string brush, double top, double lineHeight)
    {
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            FontSize = size,
            Margin = new Thickness(0, top, 0, 0),
            Cursor = System.Windows.Input.Cursors.IBeam,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsTabStop = false,
        };
        box.SetResourceReference(TextBox.ForegroundProperty, brush);
        System.Windows.Documents.Block.SetLineHeight(box, lineHeight);
        TextBlock.SetLineHeight(box, lineHeight);
        box.PreviewMouseWheel += (_, e) =>
        {
            _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - e.Delta);
            e.Handled = true;
        };
        return box;
    }

    private TextBlock LinkText(string text, Action click)
    {
        var link = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 18, 0) };
        link.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.MouseLeave += (_, _) => link.TextDecorations = null;
        link.MouseLeftButtonUp += (_, _) => click();
        return link;
    }

    /// <summary>A notice: title, time, the first lines (selectable) with "show more" to open the whole text, and "open in the browser" to NC's page.</summary>
    private Border AnnouncementCard(string title, string when, Announcements.AnnouncementTexts text, string url, LocalizationManager loc)
    {
        var rows = new List<UIElement> { Text(title, 15, true), Text(when, 11, false, "Brush.TextMuted", 2) };
        if (text.Full.Length > 0)
        {
            TextBox reader = Reader(text.Preview, 13, "Brush.Text", 8, 20);
            rows.Add(reader);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            if (text.IsLong)
            {
                bool open = false;
                TextBlock more = null!;
                more = LinkText(loc["News.ShowMore"], () =>
                {
                    open = !open;
                    reader.Text = open ? text.Full : text.Preview;
                    more.Text = open ? loc["News.ShowLess"] : loc["News.ShowMore"];
                });
                actions.Children.Add(more);
            }

            if (Announcements.AnnouncementText.IsBrowserUrl(url))
            {
                actions.Children.Add(LinkText(loc["News.OpenBrowser"], () =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        // no browser: nothing to do
                    }
                }));
            }

            if (actions.Children.Count > 0)
            {
                rows.Add(actions);
            }
        }

        return Card(rows.ToArray());
    }

    private async Task FillAnnouncements()
    {
        var loc = LocalizationManager.Instance;
        _list.Children.Add(Text(loc["Hub.Dash.Checking"], 13, false, "Brush.TextMuted"));
        using JsonDocument? doc = await Get("https://aiondps.com/api/announcements?lang=" + Uri.EscapeDataString(loc.Language));
        _list.Children.Clear();
        if (doc is null || !doc.RootElement.TryGetProperty("items", out JsonElement items))
        {
            _list.Children.Add(Text(loc["Hub.Dash.CheckFailed"], 13, false, "Brush.TextMuted"));
            return;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            string title = item.GetProperty("title").GetString() ?? "";
            string summary = item.TryGetProperty("summary", out JsonElement s) ? s.GetString() ?? "" : "";
            string when = DateTime.TryParse(item.GetProperty("postedAt").GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime at)
                ? at.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "";
            string body = item.TryGetProperty("body", out JsonElement b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
            string url = item.TryGetProperty("url", out JsonElement u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "";
            _list.Children.Add(AnnouncementCard(title, when, Announcements.AnnouncementText.Prepare(body, summary), url, loc));
        }

        if (_list.Children.Count == 0)
        {
            _list.Children.Add(Text(loc["Hub.Dash.NothingNew"], 13, false, "Brush.TextMuted"));
        }
    }

    /// <summary>The channels that stream AION 2 with drops on right now (the server asks Twitch): their names, viewers, language and title, and "open on Twitch".</summary>
    /// <summary>A text of the site's JSON; a text the server cut in the middle of a character (an emoji) cannot be read and counts as empty instead of emptying the whole window.</summary>
    private static string SafeString(JsonElement element)
    {
        try
        {
            return element.GetString() ?? "";
        }
        catch (InvalidOperationException)
        {
            return "";
        }
    }

    private Border LiveCard(JsonElement live, LocalizationManager loc)
    {
        int streams = live.GetProperty("streams").GetInt32();
        int viewers = live.TryGetProperty("viewers", out JsonElement v) ? v.GetInt32() : 0;
        var rows = new List<UIElement> { Text(string.Format(loc["News.DropsLive"], streams.ToString("N0"), viewers.ToString("N0")), 14, true) };
        if (live.TryGetProperty("channels", out JsonElement channels))
        {
            foreach (JsonElement ch in channels.EnumerateArray())
            {
                string name = ch.TryGetProperty("name", out JsonElement n) ? SafeString(n) : "";
                string login = ch.TryGetProperty("login", out JsonElement l) ? SafeString(l) : "";
                int count = ch.TryGetProperty("viewers", out JsonElement cv) ? cv.GetInt32() : 0;
                string language = ch.TryGetProperty("language", out JsonElement lg) ? SafeString(lg).ToUpperInvariant() : "";
                string title = ch.TryGetProperty("title", out JsonElement t) ? SafeString(t) : "";
                rows.Add(Text($"{name}   ·   {count:N0}   ·   {language}", 12.5, true, "Brush.Text", 10));
                if (title.Length > 0)
                {
                    rows.Add(Text(title, 12, false, "Brush.TextMuted", 1));
                }

                if (System.Text.RegularExpressions.Regex.IsMatch(login, "^[A-Za-z0-9_]{1,25}$"))
                {
                    string url = "https://www.twitch.tv/" + login;
                    TextBlock open = LinkText(loc["News.OpenTwitch"], () =>
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                        }
                        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                        {
                            // no browser: nothing to do
                        }
                    });
                    open.Margin = new Thickness(0, 3, 0, 0);
                    rows.Add(open);
                }
            }
        }

        return Card(rows.ToArray());
    }

    private async Task FillDrops()
    {
        var loc = LocalizationManager.Instance;
        _list.Children.Add(Text(loc["Hub.Dash.Checking"], 13, false, "Brush.TextMuted"));
        using JsonDocument? doc = await Get("https://aiondps.com/api/twitch-drops");
        _list.Children.Clear();
        if (doc is null || !doc.RootElement.TryGetProperty("campaigns", out JsonElement campaigns))
        {
            _list.Children.Add(Text(loc["Hub.Dash.CheckFailed"], 13, false, "Brush.TextMuted"));
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (doc.RootElement.TryGetProperty("live", out JsonElement live) && live.ValueKind == JsonValueKind.Object && live.TryGetProperty("streams", out JsonElement streams) && streams.GetInt32() > 0)
        {
            try
            {
                _list.Children.Add(LiveCard(live, loc));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                // a broken live list does not hide the campaigns below it
            }
        }

        if (doc.RootElement.TryGetProperty("claimBy", out JsonElement claim) && DateTime.TryParse(claim.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime claimBy) && claimBy > now)
        {
            _list.Children.Add(Text(string.Format(loc["News.ClaimBy"], claimBy.ToLocalTime().ToString("dd.MM.yyyy HH:mm")), 12, true, "Brush.Heal", 0));
            _list.Children.Add(new Border { Height = 10 });
        }

        var shown = new List<(DateTime Start, UIElement Card)>();
        foreach (JsonElement c in campaigns.EnumerateArray())
        {
            if (!DateTime.TryParse(c.GetProperty("startAt").GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime start)
                || !DateTime.TryParse(c.GetProperty("endAt").GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime end))
            {
                continue;
            }

            string state = now < start ? loc["Hub.Dash.DropsSoon"] : now <= end ? loc["Hub.Dash.DropsRunning"] : loc["Hub.Dash.DropsOver"];
            string name = c.GetProperty("name").GetString() ?? "";
            var rows = new List<UIElement>
            {
                Text(name, 14, true),
                Text($"{state}  ·  {start.ToLocalTime():dd.MM.yyyy HH:mm} – {end.ToLocalTime():dd.MM.yyyy HH:mm}", 11, false, now >= start && now <= end ? "Brush.Heal" : "Brush.TextMuted", 2),
            };
            if (c.TryGetProperty("rewards", out JsonElement rewards))
            {
                // a running or coming campaign shows its rewards at once; a finished one folds them away behind "show more"
                var rewardRows = new StackPanel { Visibility = now > end ? Visibility.Collapsed : Visibility.Visible };
                foreach (JsonElement r in rewards.EnumerateArray())
                {
                    int minutes = r.GetProperty("minutes").GetInt32();
                    string time = minutes >= 60 ? $"{minutes / 60} h" + (minutes % 60 > 0 ? $" {minutes % 60} min" : "") : $"{minutes} min";
                    int qty = r.GetProperty("qty").GetInt32();
                    rewardRows.Children.Add(Text($"{time}   {(qty > 1 ? qty.ToString("N0") + "× " : "")}{r.GetProperty("name").GetString()}", 12.5, false, "Brush.Text", 5));
                }

                if (now > end && rewardRows.Children.Count > 0)
                {
                    TextBlock more = null!;
                    more = LinkText(loc["News.ShowMore"], () =>
                    {
                        bool open = rewardRows.Visibility != Visibility.Visible;
                        rewardRows.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                        more.Text = open ? loc["News.ShowLess"] : loc["News.ShowMore"];
                    });
                    more.Margin = new Thickness(0, 8, 0, 0);
                    rows.Add(more);
                }

                rows.Add(rewardRows);
            }

            shown.Add((start, Card(rows.ToArray())));
        }

        // running and coming ones first, the newest start on top
        foreach (var (_, card) in shown.OrderByDescending(s => s.Start))
        {
            _list.Children.Add(card);
        }

        if (_list.Children.Count == 0)
        {
            _list.Children.Add(Text(loc["Hub.Dash.NothingNew"], 13, false, "Brush.TextMuted"));
        }
    }
}
