using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Schedule;

namespace AionDPS.Ui;

/// <summary>
/// The "Configuration" tab of the tool pages of the new interface: what used to sit in the settings window, next to the tool it belongs to. Every control writes the
/// settings at once (see <see cref="ConfigControls"/>).
/// </summary>
public static class ToolConfigs
{
    private static UIElement Page(params UIElement[] cards)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (UIElement card in cards)
        {
            stack.Children.Add(card);
        }

        return stack;
    }

    private static TextBlock Muted(string text, double size = 11)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return block;
    }

    private static CheckBox Box(bool on, Action<bool> set)
    {
        CheckBox box = ConfigControls.SwitchBox(on);
        box.Margin = new Thickness(0, 0, 12, 0);
        box.Click += (_, _) => set(box.IsChecked == true);
        return box;
    }

    // ───────────────────────────── timetable ─────────────────────────────

    /// <summary>Notification, lead time and one row per event: on the overlay, sound, volume, a button to try it.</summary>
    public static UIElement Timetable(Action<MeterSettings> applied)
    {
        var loc = LocalizationManager.Instance;
        var c = new ConfigControls(applied);
        MeterSettings now = MeterSettings.Load();
        string language = loc.Language;

        var top = ConfigControls.Card(loc["Settings.Nav.Timetable"],
            c.Slider(loc["Settings.Timetable.Lookahead"].TrimEnd(':'), null, 0, 60, 5, s => s.TimetableLookaheadMinutes, (s, v) => s.TimetableLookaheadMinutes = v, v => string.Format(loc["Settings.Timetable.LookaheadValue"], v)),
            c.Switch(loc["Settings.Timetable.Notify"], null, s => s.TimetableNotify, (s, v) => s.TimetableNotify = v),
            c.Slider(loc["Settings.Timetable.SoundMinutes"], null, 0, 60, 1, s => s.TimetableSoundMinutes, (s, v) => s.TimetableSoundMinutes = v, v => string.Format(loc["Settings.Timetable.LookaheadValue"], v)));

        var rows = new StackPanel();
        rows.Children.Add(ConfigControls.Note(loc["Settings.Timetable.Events.Hint"], 0));
        foreach (ScheduledEvent scheduled in EventSchedule.Events.OrderBy(e => e.Always))
        {
            string id = scheduled.Id;
            TimetableEventSetting state = now.TimetableEvents.GetValueOrDefault(id) ?? new TimetableEventSetting();
            var show = Box(state.Show, on => c.Change(s => Event(s, id).Show = on));
            show.ToolTip = loc["Settings.Timetable.ShowEvent"];
            var name = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MinWidth = 150, Margin = new Thickness(0, 0, 8, 0) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            name.Inlines.Add(new System.Windows.Documents.Run(scheduled.NameIn(language)));
            if (scheduled.Players is { Length: > 0 } players)
            {
                name.Inlines.Add(new System.Windows.Documents.LineBreak());
                var count = new System.Windows.Documents.Run(players) { FontSize = 11 };
                count.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
                name.Inlines.Add(count);
            }

            if (scheduled.Always)
            {
                // no start time: nothing to remind of
                var why = Muted(loc["Settings.Timetable.AlwaysTip"]);
                why.Margin = new Thickness(0, 0, 0, 0);
                rows.Children.Add(ConfigControls.ListRow(show, name, (why, 200)));
                continue;
            }

            var (sound, volume, test) = c.SoundControls(state.Sound, state.Volume, v => c.Change(s => Event(s, id).Sound = v), v => c.Change(s => Event(s, id).Volume = v));
            rows.Children.Add(ConfigControls.ListRow(show, name, (sound, 150), (volume, 100), (test, 0)));
        }

        return Page(top, ConfigControls.Card(loc["Settings.Timetable.Events"], rows));
    }

    private static TimetableEventSetting Event(MeterSettings s, string id)
    {
        if (!s.TimetableEvents.TryGetValue(id, out var state))
        {
            s.TimetableEvents[id] = state = new TimetableEventSetting();
        }

        return state;
    }

    // ───────────────────────────── world boss ─────────────────────────────

    /// <summary>The master switch of the sounds and, per map, one row per boss: on the overlay with its sound, volume and a button to try it.</summary>
    public static UIElement Boss(Action<MeterSettings> applied)
    {
        var loc = LocalizationManager.Instance;
        var c = new ConfigControls(applied);
        MeterSettings now = MeterSettings.Load();
        string language = loc.Language;
        var top = ConfigControls.Card(loc["Settings.Nav.Boss"],
            c.Switch(loc["Settings.Boss.Notify"], null, s => s.BossNotify, (s, v) => s.BossNotify = v),
            ConfigControls.Note(loc["Settings.Boss.Hint"]));

        var cards = new List<UIElement> { top };
        foreach (var map in Aion2FieldBosses.Maps)
        {
            var body = new StackPanel();
            var count = Muted("");
            Action showCount = () =>
            {
                MeterSettings s = MeterSettings.Load();
                count.Text = string.Format(loc["Settings.Boss.Count"], map.Bosses.Count(b => s.BossAlerts.TryGetValue(b.Npc.ToString(), out var a) && a.Enabled), map.Bosses.Count);
            };
            foreach (var boss in map.Bosses)
            {
                string key = boss.Npc.ToString();
                BossAlertSetting alert = now.BossAlerts.GetValueOrDefault(key) ?? new BossAlertSetting();
                var on = Box(alert.Enabled, v => { c.Change(s => Alert(s, key).Enabled = v); showCount(); });
                var name = new TextBlock { Text = Aion2FieldBosses.DisplayName(map, boss, language), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                var (sound, volume, test) = c.SoundControls(alert.Sound, alert.Volume, v => c.Change(s => Alert(s, key).Sound = v), v => c.Change(s => Alert(s, key).Volume = v));
                body.Children.Add(ConfigControls.ListRow(on, name, (sound, 150), (volume, 100), (test, 0)));
            }

            showCount();
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(count, Dock.Right);
            header.Children.Add(count);
            header.Children.Add(new TextBlock { Text = map.NameIn(language), FontWeight = FontWeights.SemiBold });
            cards.Add(ConfigControls.Card("", header, body));
        }

        return Page(cards.ToArray());
    }

    private static BossAlertSetting Alert(MeterSettings s, string key)
    {
        if (!s.BossAlerts.TryGetValue(key, out var alert))
        {
            s.BossAlerts[key] = alert = new BossAlertSetting();
        }

        return alert;
    }

    // ───────────────────────────── boss fight ─────────────────────────────

    /// <summary>Boss cast (which casts, how many, other monsters), the target of the target, and the speech output.</summary>
    public static UIElement Combat(Action<MeterSettings> applied)
    {
        var loc = LocalizationManager.Instance;
        var c = new ConfigControls(applied);
        var cast = ConfigControls.Card(loc["Settings.Combat.CardBossCast"],
            c.Choice(loc["Settings.Combat.CastFilter"], null, new[] { ("all", loc["Settings.Combat.Filter.All"]), ("area", loc["Settings.Combat.Filter.Area"]), ("you", loc["Settings.Combat.Filter.You"]) }, s => s.CombatCastFilter, (s, v) => s.CombatCastFilter = v, 260),
            c.Choice(loc["Settings.Combat.MaxCasts"], null, new[] { ("1", "1"), ("2", "2"), ("3", "3") }, s => s.CombatMaxCasts.ToString(), (s, v) => s.CombatMaxCasts = int.Parse(v), 80),
            c.Switch(loc["Settings.Combat.AllMonsters"], null, s => s.CombatCastsOfAllMonsters, (s, v) => s.CombatCastsOfAllMonsters = v),
            ConfigControls.Note(loc["Settings.Combat.CastHint"]));
        var target = ConfigControls.Card(loc["Settings.Combat.Elements"],
            ConfigControls.Note(loc["Settings.Combat.TargetAlways"], 0),
            c.Switch(loc["Settings.Combat.TargetHpBar"], null, s => s.CombatHealerHpBar, (s, v) => s.CombatHealerHpBar = v),
            c.Switch(loc["Settings.Combat.TargetEffects"], null, s => s.CombatHealerEffects, (s, v) => s.CombatHealerEffects = v),
            ConfigControls.Note(loc["Settings.Combat.TargetHint2"]));
        return Page(cast, target, Speech(c));
    }

    private static UIElement Speech(ConfigControls c)
    {
        var loc = LocalizationManager.Instance;
        MeterSettings now = MeterSettings.Load();
        var voices = CastSpeech.Voices();
        var rows = new List<UIElement>
        {
            c.Switch(loc["Settings.Speech.Title"], loc["Settings.Speech.Hint"], s => s.SpeechOn, (s, v) => s.SpeechOn = v),
        };
        if (voices.Count == 0)
        {
            rows.Add(ConfigControls.Note(loc["Settings.Speech.NoVoices"]));
        }
        else
        {
            var languages = voices.Select(v => v.Language).Distinct().Select(l => (l, CastSpeech.LanguageName(l))).ToList();
            string own = loc.Language;
            string chosen = languages.Any(l => l.l == now.SpeechLanguage) ? now.SpeechLanguage : languages.FirstOrDefault(l => CastSpeech.TableLanguage(l.l) == own).l ?? languages[0].l;
            rows.Add(c.Choice(loc["Settings.Speech.Language"], null, languages.Select(l => (l.l, l.Item2)).ToList(), s => chosen, (s, v) => { s.SpeechLanguage = v; s.SpeechVoiceId = ""; }, 240));
            rows.Add(c.Choice(loc["Settings.Speech.Voice"], null, voices.Where(v => v.Language == chosen).Select(v => (v.Id, v.Name + " – " + loc[v.Female ? "Settings.Speech.Female" : "Settings.Speech.Male"])).ToList(), s => s.SpeechVoiceId, (s, v) => s.SpeechVoiceId = v, 300));
        }

        var content = new TextBlock { Text = loc["Settings.Speech.Content"], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) };
        content.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        rows.Add(content);
        rows.Add(c.Switch(loc["Settings.Speech.SkillName"], null, s => s.SpeechSkillName, (s, v) => s.SpeechSkillName = v));
        (string Key, Func<MeterSettings, bool> On, Action<MeterSettings, bool> SetOn, Func<MeterSettings, int> Vol, Action<MeterSettings, int> SetVol)[] kinds =
        {
            ("Settings.Speech.OnYou", s => s.SpeechOnYou, (s, v) => s.SpeechOnYou = v, s => s.SpeechVolumeOnYou, (s, v) => s.SpeechVolumeOnYou = v),
            ("Settings.Speech.OnAll", s => s.SpeechOnAll, (s, v) => s.SpeechOnAll = v, s => s.SpeechVolumeOnAll, (s, v) => s.SpeechVolumeOnAll = v),
            ("Settings.Speech.LethalYou", s => s.SpeechLethalYou, (s, v) => s.SpeechLethalYou = v, s => s.SpeechVolumeLethalYou, (s, v) => s.SpeechVolumeLethalYou = v),
            ("Settings.Speech.LethalAll", s => s.SpeechLethalAll, (s, v) => s.SpeechLethalAll = v, s => s.SpeechVolumeLethalAll, (s, v) => s.SpeechVolumeLethalAll = v),
        };
        foreach (var k in kinds)
        {
            var on = Box(k.On(now), v => c.Change(s => k.SetOn(s, v)));
            var name = new TextBlock { Text = loc[k.Key], TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var vol = c.SliderControl(0, 100, 5, k.Vol(now), v => c.Change(s => k.SetVol(s, v)), v => v + " %");
            rows.Add(ConfigControls.ListRow(on, name, (vol, 270)));
        }

        rows.Add(ConfigControls.Note(loc["Settings.Speech.Note"]));
        return ConfigControls.Card(loc["Settings.Speech.Title"], rows.ToArray());
    }

    // ───────────────────────────── loot, network, stream ─────────────────────────────

    /// <summary>What the loot list counts and how many lines it has.</summary>
    public static UIElement Loot(Action<MeterSettings> applied)
    {
        var loc = LocalizationManager.Instance;
        var c = new ConfigControls(applied);
        int[] lines = { 3, 5, 8, 10, 15, 20, 30 };
        return Page(ConfigControls.Card(loc["Loot.Settings.Content"],
            c.Switch(loc["Loot.Settings.WholeSession"], null, s => s.LootOverlayWholeSession, (s, v) => s.LootOverlayWholeSession = v),
            c.Switch(loc["Loot.Settings.HideCommon"], null, s => s.LootOverlayHideCommon, (s, v) => s.LootOverlayHideCommon = v),
            c.Choice(loc["Loot.Settings.Lines"], null, lines.Select(l => (l.ToString(), l.ToString())).ToList(), s => (lines.Contains(s.LootOverlayLines) ? s.LootOverlayLines : 8).ToString(), (s, v) => s.LootOverlayLines = int.Parse(v), 80),
            ConfigControls.Note(loc["Loot.Settings.Hint"])));
    }

    /// <summary>The lines of the network overlay.</summary>
    public static UIElement Network(Action<MeterSettings> applied)
    {
        var loc = LocalizationManager.Instance;
        var c = new ConfigControls(applied);
        var rows = new List<UIElement> { ConfigControls.Note(loc["Settings.Network.Hint"], 0) };
        foreach (string id in new[] { "server", "ping", "jitter", "traffic", "loss", "stalls", "gateway", "internet", "connection", "load" })
        {
            string line = id;
            rows.Add(c.Switch(loc["Network.Row." + line], null, s => !s.NetworkOverlayHidden.Contains(line), (s, v) =>
            {
                s.NetworkOverlayHidden.Remove(line);
                if (!v)
                {
                    s.NetworkOverlayHidden.Add(line);
                }
            }));
        }

        return Page(ConfigControls.Card(loc["Settings.Network.Rows"], rows.ToArray()));
    }

    /// <summary>The stream overlay: what it shows and the port of its page.</summary>
    public static UIElement Stream(Action<MeterSettings> applied)
    {
        var loc = LocalizationManager.Instance;
        var c = new ConfigControls(applied);
        var port = new TextBox { Width = 80, Height = 26, MaxLength = 5, Text = MeterSettings.Load().StreamOverlayPort.ToString(), VerticalContentAlignment = VerticalAlignment.Center };
        port.LostFocus += (_, _) =>
        {
            int value = int.TryParse(port.Text, out int p) && p is >= 1024 and <= 65000 ? p : Streaming.StreamOverlayServer.DefaultPort;
            port.Text = value.ToString();
            c.Change(s => s.StreamOverlayPort = value);
        };
        var portRow = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        var label = new TextBlock { Text = loc["Settings.Stream.Port"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        portRow.Children.Add(label);
        portRow.Children.Add(port);
        portRow.Children.Add(new Border());
        return Page(ConfigControls.Card(loc["Settings.Nav.Stream"],
            c.Switch(loc["Settings.Stream.On"], null, s => s.StreamOverlayOn, (s, v) => s.StreamOverlayOn = v),
            c.Switch(loc["Settings.Stream.Network"], null, s => s.StreamOverlayNetwork, (s, v) => s.StreamOverlayNetwork = v),
            c.Switch(loc["Settings.Stream.Names"], null, s => s.StreamShowNames, (s, v) => s.StreamShowNames = v),
            portRow,
            ConfigControls.Note(loc["Settings.Stream.Hint"])));
    }

    // ───────────────────────────── pet map ─────────────────────────────

    /// <summary>The options of the pet map: radius, the guards, the width of the region list, the fallback of the pet farm.</summary>
    public static UIElement PetMap(Action<MeterSettings> applied)
    {
        var loc = LocalizationManager.Instance;
        var c = new ConfigControls(applied);
        return Page(ConfigControls.Card(loc["Settings.PetMap.CardMap"],
            c.Slider(loc["Settings.PetMap.Radius"], null, 50, 500, 10, s => s.PetMapRadius, (s, v) => s.PetMapRadius = v, v => string.Format(loc["Settings.PetMap.RadiusValue"], v)),
            c.Switch(loc["Settings.Gather.Guards"], loc["Settings.Gather.Guards.Tip"], s => s.ShowGuards, (s, v) => s.ShowGuards = v)));
    }
}
