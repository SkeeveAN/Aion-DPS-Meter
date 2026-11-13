using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.History;
using AionDPS.Schedule;
using AionDPS.Update;

namespace AionDPS.Ui;

/// <summary>What the dashboard pages read from the running meter.</summary>
/// <summary>A page of the app a dashboard can take as a widget: its id, the group it belongs to and its name.</summary>
public sealed record HubPageInfo(string Id, string Group, string Title);

public sealed record DashboardSources(Func<HubStatus> Status, Action<string> OpenTool, Func<IReadOnlyList<SessionInfo>> Sessions, Action OpenSessions, string Version,
    Func<IReadOnlyList<HubPageInfo>> Pages, Func<string, ToolPage?> BuildPage);

/// <summary>
/// The pages of the Dashboard tab of the new interface, all on real data and drawn again every second: the overview (game and character, today's checklist, what is on now,
/// the world bosses, the figures of the recorded fights, shortcuts), the fights of the recording, the best fights, the update and the news.
/// </summary>
public static class HubDashboard
{
    private static readonly Dictionary<(int Number, DateTime? End), IReadOnlyList<BossAttempt>> Attempts = new();

    public static ToolPage Build(string page, DashboardSources src)
    {
        if (page is not ("fights" or "best" or "updates" or "news"))
        {
            var board = new DashboardBoard(src);
            return new ToolPage { Content = board, Cleanup = board.Stop };
        }

        var wrap = new WrapPanel { Margin = new Thickness(8, 8, 8, 20) };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(page is "updates" or "news" ? 5 : 1) };
        bool built = false;
        void Refresh()
        {
            // the update and news pages ask the network once; they are not drawn again every second
            if (built && page is "updates" or "news")
            {
                return;
            }

            built = true;
            wrap.Children.Clear();
            DateTime now = DateTime.Now;
            switch (page)
            {
                case "fights": Fights(wrap, src, now); break;
                case "best": Best(wrap, src); break;
                case "updates": Updates(wrap, src); break;
                case "news": News(wrap, src); break;
                default: break;
            }
        }

        Refresh();
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        return new ToolPage { Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = wrap }, Cleanup = timer.Stop };
    }

    // ───────────────────────────── overview ─────────────────────────────

    /// <summary>The widgets of the overview: id, title key, the page the arrow leads to (null: none), what it shows and its first size.</summary>
    internal sealed record Widget(string Id, string TitleKey, string? Opens, double W, double H, bool Static);

    internal static readonly Widget[] Widgets =
    {
        new("connection", "Hub.Dash.Connection", null, 420, 170, false),
        new("todo", "Hub.Dash.TodoTitle", "checklist", 420, 260, false),
        new("soon", "Hub.Dash.Soon", "timetable", 520, 290, false),
        new("bosses", "Settings.Nav.Boss", "boss", 520, 250, false),
        new("figures", "Hub.Dash.Figures", null, 420, 210, false),
        new("last", "Hub.Dash.Last", "sessions", 520, 260, false),
        new("update", "Hub.Dash.Update", null, 420, 190, true),
        new("announcements", "Hub.Dash.Announcements", null, 420, 170, true),
        new("drops", "Hub.Dash.Drops", null, 420, 170, true),
    };

    /// <summary>The body of one widget, drawn from the live data.</summary>
    internal static UIElement WidgetBody(string id, DashboardSources src, DateTime now)
    {
        var loc = LocalizationManager.Instance;
        switch (id)
        {
            case "connection": return Connection(src.Status(), loc);
            case "todo": return Todo(loc, src.OpenTool);
            case "soon":
            {
                var (active, soon) = EventSchedule.Evaluate(EventSchedule.Events, now, TimeSpan.FromHours(3));
                return Events(active, soon, now, loc);
            }

            case "bosses": return Bosses(now, loc);
            case "figures":
            {
                var sessions = FightHistory.All(src.Sessions());
                var attempts = sessions.SelectMany(s => s.Attempts).ToList();
                var total = TimeSpan.FromSeconds(sessions.Sum(s => (s.End - s.Start).TotalSeconds));
                var bestDps = attempts.Where(a => a.Duration.TotalSeconds >= 10).Select(a => (double?)(a.Counted / a.Duration.TotalSeconds)).DefaultIfEmpty(null).Max();
                var figures = new StackPanel();
                if (sessions.Count == 0)
                {
                    figures.Children.Add(MockTag());
                    figures.Children.Add(Line(loc["Hub.Dash.Recording"], WorldBossText.Duration(TimeSpan.FromMinutes(72.5))));
                    figures.Children.Add(Line(loc["Hub.Dash.Sessions"], "4"));
                    figures.Children.Add(Line(loc["Hub.Dash.BossFights"], "3"));
                    figures.Children.Add(Line(loc["Hub.Dash.BestDps"], 220478.ToString("N0")));
                    return figures;
                }

                figures.Children.Add(Line(loc["Hub.Dash.Recording"], WorldBossText.Duration(total)));
                figures.Children.Add(Line(loc["Hub.Dash.Sessions"], sessions.Count.ToString()));
                figures.Children.Add(Line(loc["Hub.Dash.BossFights"], attempts.Count.ToString()));
                figures.Children.Add(Line(loc["Hub.Dash.BestDps"], bestDps is double d ? d.ToString("N0") : "–"));
                return figures;
            }

            case "last": return LastFight(FightHistory.All(src.Sessions()).SelectMany(s => s.Attempts).ToList(), loc);
            case "update": return UpdateBody(src, loc);
            case "announcements":
                return NewsCard(loc["Hub.Dash.AnnouncementsText"], Announcements.AnnouncementsClient.PageUrl, async () =>
                {
                    long? latest = await Announcements.AnnouncementsClient.GetLatestAsync();
                    return latest is null ? loc["Hub.Dash.CheckFailed"] : Announcements.AnnouncementsClient.IsNew(latest, MeterSettings.Load().AnnouncementsSeen) ? loc["Hub.Dash.NewOnes"] : loc["Hub.Dash.NothingNew"];
                }, loc);
            default:
                return NewsCard(loc["Hub.Dash.DropsText"], Announcements.TwitchDropsClient.PageUrl, async () =>
                {
                    var status = await Announcements.TwitchDropsClient.GetStatusAsync();
                    return status is null ? loc["Hub.Dash.CheckFailed"] : status.Value.Running ? loc["Hub.Dash.DropsRunning"] : loc["Hub.Dash.NothingNew"];
                }, loc);
        }
    }

    /// <summary>The tag every sample shows: where there is nothing real yet, the card shows how it could look and says so.</summary>
    internal static TextBlock MockTag()
    {
        var tag = new TextBlock { Text = "◈ " + LocalizationManager.Instance["Hub.Mock"], FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        tag.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        return tag;
    }

    private static UIElement Connection(HubStatus status, LocalizationManager loc)
    {
        var stack = new StackPanel();
        stack.Children.Add(Line(loc["Hub.Dash.Game"], status.GameOn ? loc["Hub.GameOn"] : loc["Hub.GameOff"], false, status.GameOn ? "Brush.Heal" : "Brush.TextMuted"));
        stack.Children.Add(Line(loc["Hub.Dash.Character"], status.CharacterName ?? loc["Hub.NoCharacter"]));
        if (status.ClassName is { Length: > 0 })
        {
            stack.Children.Add(Line(loc["Hub.Dash.Class"], status.ClassName));
        }

        if (status.Server is { Length: > 0 })
        {
            stack.Children.Add(Line(loc["Hub.Dash.Server"], status.Server));
        }

        return stack;
    }

    /// <summary>"Heute zu erledigen" as the HTML draft has it: the Odyle energy with its bar, the four entry counters (tickets 1, 4, 8 and 101) and a link to the checklist.</summary>
    private static UIElement Todo(LocalizationManager loc, Action<string> openTool)
    {
        var stack = new StackPanel();
        TicketCharacter? me = null;
        try
        {
            // the character in the game, else the one the checklist saw last (its counters are kept on disk)
            var known = Aion2Tickets.Snapshot();
            me = known.FirstOrDefault(c => string.Equals(c.Name, Aion2Tickets.CurrentName, StringComparison.OrdinalIgnoreCase)) ?? known.OrderByDescending(c => c.SavedAt).FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // the checklist reads what the game sent; with nothing sent the card stays empty
        }

        TicketValue Value(int id) => me is not null && me.Tickets.TryGetValue(id, out TicketValue v) ? v : default;
        TicketDef? ode = Aion2Tickets.Find(Aion2Tickets.OdyleId);
        bool mock = me is null;
        if (mock)
        {
            stack.Children.Add(MockTag());
        }

        if (ode is not null)
        {
            TicketValue o = mock ? new TicketValue(Math.Max(1, ode.Max * 6 / 10), Math.Max(1, ode.Max / 10), 0) : Value(ode.Id);
            stack.Children.Add(Line(ode.ShortIn(loc.Language), $"{o.Count} (+{o.Bonus}) / {ode.Max}"));
            var bar = new ProgressBar { Height = 8, Maximum = 100, Value = ode.Max > 0 ? Math.Min(100.0, o.Count * 100.0 / ode.Max) : 0, BorderThickness = new Thickness(0), Margin = new Thickness(0, 2, 0, 8) };
            bar.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
            bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
            stack.Children.Add(bar);
            int[] sample = { 3, 2, 5, 1 };
            int n = 0;
            foreach (int id in new[] { 1, 4, 8, 101 })
            {
                if (Aion2Tickets.Find(id) is not { } def)
                {
                    continue;
                }

                TicketValue v = mock ? new TicketValue(sample[n], 0, 0) : Value(id);
                n++;
                int cap = def.SubscribeMax > 0 ? def.SubscribeMax : def.Max;
                stack.Children.Add(Line(def.ShortIn(loc.Language), $"{v.Count} / {cap}", false, v.Count > 0 ? "Brush.Accent" : "Brush.TextMuted"));
            }
        }
        else
        {
            stack.Children.Add(Text(loc["Hub.Dash.TodoNone"], false, "Brush.TextMuted"));
        }

        var link = new TextBlock { Text = loc["Hub.Dash.ToChecklist"], Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 8, 0, 0) };
        link.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        link.MouseLeftButtonUp += (_, _) => openTool("checklist");
        stack.Children.Add(link);
        return stack;
    }

    private static UIElement Events(List<EventOccurrence> active, List<EventOccurrence> soon, DateTime now, LocalizationManager loc)
    {
        var stack = new StackPanel();
        string language = loc.Language;
        foreach (EventOccurrence o in active.Where(a => !a.IsAlways).Take(4))
        {
            stack.Children.Add(Line(o.Event.NameIn(language), string.Format(loc["Hub.Dash.Until"], o.End.ToString("HH:mm")), true, "Brush.Heal"));
        }

        foreach (EventOccurrence o in soon.Take(6))
        {
            stack.Children.Add(Line(o.Event.NameIn(language), WorldBossText.Duration(o.Start - now) + "  ·  " + o.Start.ToString("HH:mm")));
        }

        if (stack.Children.Count == 0)
        {
            stack.Children.Add(Text(loc["Hub.Dash.NothingSoon"], false, "Brush.TextMuted"));
        }

        return stack;
    }

    private static UIElement Bosses(DateTime now, LocalizationManager loc)
    {
        var stack = new StackPanel();
        string language = loc.Language;
        var rows = new List<(string Name, string Text, bool Now, DateTime At)>();
        foreach (var map in Aion2FieldBosses.Maps)
        {
            foreach (var boss in map.Bosses)
            {
                BossStatus st = Aion2FieldBosses.StatusOf(boss, now);
                // "there now" counts only when it is fresh: a fixed window that is open, or a list the game sent a few minutes ago (a boss seen alive hours ago may be long dead)
                bool fresh = boss.Rule is not null || (st.SeenAt is DateTime seen && now - seen < TimeSpan.FromMinutes(10));
                if (st.Phase == BossPhase.Unknown || (st.Phase == BossPhase.Now && !fresh))
                {
                    continue;
                }

                rows.Add(($"{boss.NameIn(language)}  (X {boss.X / 100:0}, Y {boss.Y / 100:0})", WorldBossText.Of(st, now), st.Phase == BossPhase.Now, st.At ?? DateTime.MaxValue));
            }
        }

        foreach (var row in rows.OrderBy(r => r.Now ? 0 : 1).ThenBy(r => r.At).Take(8))
        {
            stack.Children.Add(Line(row.Name, row.Text, row.Now, row.Now ? "Brush.Heal" : "Brush.Accent"));
        }

        if (rows.Count == 0)
        {
            stack.Children.Add(MockTag());
            var first = Aion2FieldBosses.Maps.SelectMany(m => m.Bosses.Select(b => b.NameIn(language))).Distinct().Take(4).ToList();
            string[] times = { "in 12 min 30 s", "in 47 min 05 s", "in 2 h 10 min", "in 5 h 28 min" };
            for (int i = 0; i < first.Count; i++)
            {
                stack.Children.Add(Line(first[i], times[i], false));
            }

            stack.Children.Add(Text(loc["Hub.Dash.BossesNone"], false, "Brush.TextMuted", 11));
        }

        return stack;
    }

    private static UIElement LastFight(List<AttemptRecord> attempts, LocalizationManager loc)
    {
        var stack = new StackPanel();
        AttemptRecord? last = attempts.OrderByDescending(a => a.End).FirstOrDefault();
        if (last is null)
        {
            stack.Children.Add(MockTag());
            stack.Children.Add(Line("Transcendent Bakarma", loc["Hub.Dash.Killed"], true, "Brush.Heal"));
            stack.Children.Add(Line(loc["Hub.Dash.Duration"], WorldBossText.Duration(TimeSpan.FromSeconds(127))));
            stack.Children.Add(Line(loc["Hub.Dash.Damage"], 28000755L.ToString("N0")));
            stack.Children.Add(Line("DPS", 220478.ToString("N0")));
            stack.Children.Add(Line(loc["Hub.Dash.When"], DateTime.Now.AddMinutes(-42).ToString("dd.MM. HH:mm")));
            return stack;
        }

        stack.Children.Add(Line(last.Boss, last.Killed ? loc["Hub.Dash.Killed"] : loc["Hub.Dash.NotKilled"], true, last.Killed ? "Brush.Heal" : "Brush.Danger"));
        stack.Children.Add(Line(loc["Hub.Dash.Duration"], WorldBossText.Duration(last.Duration)));
        stack.Children.Add(Line(loc["Hub.Dash.Damage"], last.Counted.ToString("N0")));
        stack.Children.Add(Line("DPS", last.Duration.TotalSeconds >= 1 ? (last.Counted / last.Duration.TotalSeconds).ToString("N0") : "–"));
        stack.Children.Add(Line(loc["Hub.Dash.When"], last.End.ToString("dd.MM. HH:mm")));
        return stack;
    }

    // ───────────────────────────── fights, best ─────────────────────────────

    private static IReadOnlyList<BossAttempt> AttemptsOf(SessionInfo session)
    {
        // an open stay is not analysed (it is still growing); a closed one once
        if (session.IsOpen)
        {
            return Array.Empty<BossAttempt>();
        }

        var key = (session.Number, session.End);
        if (!Attempts.TryGetValue(key, out var list))
        {
            try
            {
                list = SessionAnalyzer.Analyze(session).Attempts;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                list = Array.Empty<BossAttempt>();
            }

            Attempts[key] = list;
        }

        return list;
    }

    private static void Fights(WrapPanel wrap, DashboardSources src, DateTime now)
    {
        var loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        var sessions = FightHistory.All(src.Sessions());
        foreach (StayRecord s in sessions.Take(60))
        {
            string where = s.ZoneId == 0 ? loc["Sessions.UnknownMap"] : Aion2.Protocol.Aion2Zones.NameOf(s.ZoneId, loc.Language)
                ?? string.Format(loc[s.Kind == SessionKind.Instance ? "Sessions.UnknownInstance" : "Sessions.UnknownZone"], s.ZoneId);
            string when = (s.Start.Date == DateTime.Today ? s.Start.ToString("HH:mm") : s.Start.ToString("dd.MM. HH:mm")) + " – " + s.End.ToString("HH:mm");
            string detail = (s.Kind == SessionKind.Instance ? loc["Sessions.KindInstance"] : loc["Sessions.KindMap"]) + "  ·  " + WorldBossText.Duration(s.End - s.Start)
                + (s.Attempts.Count > 0 ? "  ·  " + s.Attempts.Count + " " + loc["Hub.Dash.BossFights"] : "");
            var row = Line(where + "   " + when, detail, true);
            row.Cursor = System.Windows.Input.Cursors.Hand;
            row.MouseLeftButtonUp += (_, _) => src.OpenSessions();
            stack.Children.Add(row);
            foreach (AttemptRecord a in s.Attempts.OrderBy(x => x.Start))
            {
                var sub = Line("      " + a.Boss, (a.Killed ? "✓ " : "✗ ") + WorldBossText.Duration(a.Duration) + "  ·  " + a.Counted.ToString("N0"), false, a.Killed ? "Brush.Heal" : "Brush.Danger");
                stack.Children.Add(sub);
            }
        }

        if (sessions.Count == 0)
        {
            stack.Children.Add(MockTag());
            DateTime t = DateTime.Now;
            (string Name, string Detail, (string Boss, bool Killed, string Text)[] Fights)[] sample =
            {
                ("Draupnir   " + t.AddMinutes(-42).ToString("HH:mm") + " – " + t.AddMinutes(-31).ToString("HH:mm"), loc["Sessions.KindInstance"] + "  ·  11 min  ·  4 " + loc["Hub.Dash.BossFights"],
                    new[] { ("Predator Saraswati", true, "1 min 26 s  ·  18.591.030"), ("Phantasmal Lakshmi", true, "1 min 14 s  ·  18.583.016"), ("Transcendent Bakarma", false, "1 min 48 s  ·  22.846.804"), ("Transcendent Bakarma", true, "2 min 07 s  ·  28.000.755") }),
                ("Verteron   " + t.AddMinutes(-70).ToString("HH:mm") + " – " + t.AddMinutes(-44).ToString("HH:mm"), loc["Sessions.KindMap"] + "  ·  26 min", Array.Empty<(string, bool, string)>()),
            };
            foreach (var (name, detail, fights) in sample)
            {
                stack.Children.Add(Line(name, detail, true));
                foreach (var (boss, killed, text) in fights)
                {
                    stack.Children.Add(Line("      " + boss, (killed ? "✓ " : "✗ ") + text, false, killed ? "Brush.Heal" : "Brush.Danger"));
                }
            }
        }

        wrap.Children.Add(Card(loc["Hub.Dash.Fights"], stack, 1100, src.OpenSessions));
    }

    private static void Best(WrapPanel wrap, DashboardSources src)
    {
        var loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        var best = FightHistory.All(src.Sessions()).SelectMany(s => s.Attempts).Where(a => a.Duration.TotalSeconds >= 10)
            .GroupBy(a => a.Boss).Select(g => g.OrderByDescending(a => a.Counted / a.Duration.TotalSeconds).First())
            .OrderByDescending(a => a.Counted / a.Duration.TotalSeconds).Take(20).ToList();
        foreach (AttemptRecord a in best)
        {
            stack.Children.Add(Line(a.Boss + "   " + a.End.ToString("dd.MM. HH:mm"), (a.Counted / a.Duration.TotalSeconds).ToString("N0") + " DPS  ·  " + WorldBossText.Duration(a.Duration), a.Killed, a.Killed ? "Brush.Heal" : "Brush.Accent"));
        }

        if (best.Count == 0)
        {
            stack.Children.Add(MockTag());
            stack.Children.Add(Line("Transcendent Bakarma   " + DateTime.Now.AddMinutes(-42).ToString("dd.MM. HH:mm"), "220.478 DPS  ·  2 min 07 s", true, "Brush.Heal"));
            stack.Children.Add(Line("Predator Saraswati   " + DateTime.Now.AddMinutes(-55).ToString("dd.MM. HH:mm"), "216.246 DPS  ·  1 min 26 s", true, "Brush.Heal"));
            stack.Children.Add(Line("Phantasmal Lakshmi   " + DateTime.Now.AddMinutes(-48).ToString("dd.MM. HH:mm"), "251.122 DPS  ·  1 min 14 s", true, "Brush.Heal"));
        }

        wrap.Children.Add(Card(loc["Hub.Dash.Best"], stack, 1100));
    }

    // ───────────────────────────── update, news ─────────────────────────────

    private static void Updates(WrapPanel wrap, DashboardSources src)
    {
        var loc = LocalizationManager.Instance;
        wrap.Children.Add(Card(loc["Hub.Dash.Update"], UpdateBody(src, loc), 520));
    }

    private static UIElement UpdateBody(DashboardSources src, LocalizationManager loc)
    {
        var stack = new StackPanel();
        stack.Children.Add(Line(loc["Hub.Dash.Version"], src.Version));
        var status = Text(UpdateService.CanUpdate ? loc["Hub.Dash.UpdateAsk"] : loc["Hub.Dash.NotInstalled"], false, "Brush.TextMuted");
        status.Margin = new Thickness(0, 6, 0, 6);
        stack.Children.Add(status);
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var check = new Button { Content = loc["Hub.Dash.CheckNow"], Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 6), IsEnabled = UpdateService.CanUpdate };
        var install = new Button { Content = loc["Hub.Dash.InstallRestart"], Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 6), Visibility = Visibility.Collapsed };
        Velopack.UpdateInfo? found = null;
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            status.Text = loc["Hub.Dash.Checking"];
            try
            {
                found = await UpdateService.CheckAsync();
                status.Text = found is null ? loc["Hub.Dash.UpToDate"] : string.Format(loc["Hub.Dash.Available"], found.TargetFullRelease.Version);
                install.Visibility = found is null ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                status.Text = loc["Hub.Dash.CheckFailed"];
            }

            check.IsEnabled = true;
        };
        install.Click += async (_, _) =>
        {
            if (found is null)
            {
                return;
            }

            install.IsEnabled = false;
            try
            {
                await UpdateService.DownloadAsync(found, percent => Application.Current.Dispatcher.Invoke(() => status.Text = string.Format(loc["Hub.Dash.Downloading"], percent)));
                UpdateService.ApplyAndRestart(found);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                status.Text = loc["Hub.Dash.CheckFailed"];
                install.IsEnabled = true;
            }
        };
        buttons.Children.Add(check);
        buttons.Children.Add(install);
        var changelog = new Button { Content = loc["Hub.Dash.Changelog"], Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 6) };
        changelog.Click += (_, _) => OpenUrl("https://aiondps.com/changelog");
        buttons.Children.Add(changelog);
        stack.Children.Add(buttons);
        return stack;
    }

    private static async Task CheckUpdateAsync(TextBlock status, LocalizationManager loc)
    {
        try
        {
            var info = await UpdateService.CheckAsync();
            status.Text = info is null ? loc["Hub.Dash.UpToDate"] : string.Format(loc["Hub.Dash.Available"], info.TargetFullRelease.Version);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            status.Text = loc["Hub.Dash.CheckFailed"];
        }
    }

    private static void News(WrapPanel wrap, DashboardSources src)
    {
        var loc = LocalizationManager.Instance;
        wrap.Children.Add(Card(loc["Hub.Dash.Announcements"], WidgetBody("announcements", src, DateTime.Now), 520));
        wrap.Children.Add(Card(loc["Hub.Dash.Drops"], WidgetBody("drops", src, DateTime.Now), 520));
    }

    private static UIElement NewsCard(string text, string url, Func<Task<string>> ask, LocalizationManager loc)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(text, false, "Brush.TextMuted", 12));
        var state = Text(loc["Hub.Dash.Checking"], true, "Brush.Accent");
        state.Margin = new Thickness(0, 8, 0, 8);
        stack.Children.Add(state);
        _ = Fill(state, ask, loc);
        var open = new Button { Content = loc["Hub.Dash.OpenPage"], Padding = new Thickness(14, 4, 14, 4), HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) =>
        {
            if (url == Announcements.AnnouncementsClient.PageUrl)
            {
                NewsWindow.ShowAnnouncements();
            }
            else
            {
                NewsWindow.ShowDrops();
            }
        };
        stack.Children.Add(open);
        return stack;
    }

    private static async Task Fill(TextBlock target, Func<Task<string>> ask, LocalizationManager loc)
    {
        try
        {
            target.Text = await ask();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            target.Text = loc["Hub.Dash.CheckFailed"];
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser: nothing to open
        }
    }

    // ───────────────────────────── building blocks ─────────────────────────────

    internal static Border Card(string title, UIElement body, double width, Action? open = null)
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var caption = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        if (open is not null)
        {
            var link = new TextBlock { Text = "↗", Cursor = System.Windows.Input.Cursors.Hand, FontSize = 14 };
            link.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            link.MouseLeftButtonUp += (_, _) => open();
            DockPanel.SetDock(link, Dock.Right);
            head.Children.Add(link);
        }

        head.Children.Add(caption);
        stack.Children.Add(head);
        stack.Children.Add(body);
        var card = new Border { Width = width, Margin = new Thickness(8), Padding = new Thickness(16, 12, 16, 14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Child = stack };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    internal static TextBlock Text(string text, bool bold = false, string brush = "Brush.Text", double size = 13)
    {
        var block = new TextBlock { Text = text, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = size < 13 || brush == "Brush.TextMuted" ? TextWrapping.Wrap : TextWrapping.NoWrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static Grid Line(string left, string right, bool strong = false, string rightBrush = "Brush.Accent")
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4), Background = System.Windows.Media.Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = Text(left, strong);
        var r = Text(right, true, rightBrush);
        r.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(r, 1);
        grid.Children.Add(l);
        grid.Children.Add(r);
        return grid;
    }
}
