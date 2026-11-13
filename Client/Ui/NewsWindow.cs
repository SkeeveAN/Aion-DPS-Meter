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

    private NewsWindow(string title)
    {
        Title = title;
        Width = 780;
        Height = 760;
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
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
            _list.Children.Add(Card(Text(title, 14, true), Text(when, 11, false, "Brush.TextMuted", 2), Text(summary, 12, false, "Brush.TextSubtle", 6)));
        }

        if (_list.Children.Count == 0)
        {
            _list.Children.Add(Text(loc["Hub.Dash.NothingNew"], 13, false, "Brush.TextMuted"));
        }
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
                foreach (JsonElement r in rewards.EnumerateArray())
                {
                    int minutes = r.GetProperty("minutes").GetInt32();
                    string time = minutes >= 60 ? $"{minutes / 60} h" + (minutes % 60 > 0 ? $" {minutes % 60} min" : "") : $"{minutes} min";
                    int qty = r.GetProperty("qty").GetInt32();
                    rows.Add(Text($"{time}   {(qty > 1 ? qty.ToString("N0") + "× " : "")}{r.GetProperty("name").GetString()}", 12, false, "Brush.Text", 4));
                }
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
