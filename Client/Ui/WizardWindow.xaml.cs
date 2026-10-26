using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Globalization;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;
using AionDPS.Data;

namespace AionDPS.Ui;

/// <summary>
/// First-start wizard (a fresh install, a settings file that was lost or broken, or an install from before
/// the wizard existed - see <see cref="MeterSettings.SetupCompleted"/>), one small window: the language and
/// the upload options that are off by default for privacy reasons, each one for the player to switch on. Closing the window any other way
/// than Finish keeps everything off, and the wizard is not shown again; every choice can be changed
/// later in Settings. A "getting ready" card shows live whether Npcap is installed and whether the meter
/// has recognised the player's character yet (the capture already runs behind the window).
/// </summary>
public partial class WizardWindow : Window
{
    private readonly MeterSettings _settings;

    private readonly Func<string?>? _characterProbe;
    private readonly Func<Aion2EntityDirectory?>? _directory;
    private DateTime? _recognisedAt;
    private bool _hasCurrent;

    /// <summary>A character of the last page: the one logged in is seen live (<see cref="Current"/>), the others of the account were only told by the game at the login.</summary>
    internal sealed record SeenCharacter(string Name, int ClassCode, int Faction, int Level, int ServerId, bool Current);

    /// <summary>The picture mode shows the last page with these characters instead of the live capture.</summary>
    internal (bool Npcap, IReadOnlyList<SeenCharacter> Characters, DateTime At)? PictureSample { get; set; }
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <param name="characterProbe">The name of the character the meter has recognised so far, or null; polled once a second.</param>
    public WizardWindow(MeterSettings settings, Func<string?>? characterProbe = null, Func<Aion2EntityDirectory?>? directory = null)
    {
        InitializeComponent();
        ThemedChrome.Apply(this);
        _settings = settings;

        foreach (var (code, nativeName) in LocalizationManager.SupportedLanguages)
        {
            LanguageBox.Items.Add(new ComboBoxItem { Content = nativeName, Tag = code });
        }

        foreach (ComboBoxItem item in LanguageBox.Items)
        {
            if ((string?)item.Tag == LocalizationManager.Instance.Language)
            {
                LanguageBox.SelectedItem = item;
            }
        }

        // as tall as the whole wizard needs on this screen: it scrolls only where the screen is too small
        MaxHeight = SystemParameters.WorkArea.Height - 20;
        _characterProbe = characterProbe;
        _directory = directory;
        ShowPolicyText();
        _pages = new UIElement[] { PageLanguage, PagePolicy, PageUploads, PageReady };
        ShowPage(0);
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();

        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            Finish();
        };
    }

    /// <summary>The characters the meter knows: the one logged in (live, from the character record) and the others of the account (from the list of the login).</summary>
    private List<SeenCharacter> Characters()
    {
        var list = new List<SeenCharacter>();
        Aion2EntityDirectory? directory = _directory?.Invoke();
        if (directory is not null)
        {
            if (directory.LocalCharacter is { Restored: false } live)
            {
                list.Add(new SeenCharacter(live.Name, live.ClassCode, live.ServerId / 1000 == 2 ? 2 : 1, live.Level, live.ServerId, true));
            }

            foreach (AccountCharacter other in directory.AccountCharacters())
            {
                if (!list.Any(c => c.Name == other.Name))
                {
                    list.Add(new SeenCharacter(other.Name, other.ClassCode, other.Faction, other.Level, other.ServerId, false));
                }
            }

            // Characters of other servers that earlier logins showed (a login lists only its own server's account), kept on this PC.
            var listedServers = list.Select(c => c.ServerId).ToHashSet();
            foreach (var remembered in Aion2RememberedCharacters.Load().Where(r => !listedServers.Contains(r.ServerId) && !list.Any(c => c.Name == r.Name)))
            {
                list.Add(new SeenCharacter(remembered.Name, remembered.ClassCode, remembered.Faction, remembered.Level, remembered.ServerId, false));
            }
        }
        else if (_characterProbe?.Invoke() is { Length: > 0 } name)
        {
            list.Add(new SeenCharacter(name, 0, 1, 0, 0, true));
        }

        return list;
    }

    private string _rowsKey = "";

    /// <summary>Npcap present? Character recognised? Re-read every second, in the language chosen above; the last page is a checklist with a green tick behind what is done.</summary>
    private void RefreshStatus()
    {
        var loc = LocalizationManager.Instance;
        bool npcap = PictureSample?.Npcap ?? NpcapAvailability.Detect().IsInstalled;
        List<SeenCharacter> characters;
        try
        {
            characters = PictureSample?.Characters.ToList() ?? Characters();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            characters = new List<SeenCharacter>(); // the capture is still starting: nothing recognised yet
        }

        SeenCharacter? current = characters.FirstOrDefault(c => c.Current);
        _hasCurrent = current is not null;
        if (current is not null && _recognisedAt is null)
        {
            _recognisedAt = PictureSample?.At ?? DateTime.Now;
        }

        Mark(NpcapMark, npcap);
        NpcapText.Text = loc[npcap ? "Wizard.ItemNpcapOk" : "Wizard.ItemNpcapMissing"];
        NpcapButton.Visibility = npcap ? Visibility.Collapsed : Visibility.Visible;
        NpcapTip.ToolTip = Tip(loc["Wizard.TipNpcap"]);
        Mark(CharMark, current is not null);
        CharacterText.Text = current is null ? loc["Wizard.CharWaiting"] : loc["Wizard.CharFound"].TrimStart('✓', ' ');
        SwitchHint.Visibility = current is null ? Visibility.Visible : Visibility.Collapsed;
        CharTip.ToolTip = Tip(loc["Wizard.TipCharacter"]);

        CharsCard.Visibility = characters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        string key = LocalizationManager.Instance.Language + "|" + string.Join(";", characters.Select(c => $"{c.Name}/{c.ClassCode}/{c.Level}/{c.ServerId}/{c.Current}"));
        if (key != _rowsKey)
        {
            _rowsKey = key;
            CharRows.Children.Clear();
            foreach (SeenCharacter c in characters)
            {
                CharRows.Children.Add(CharacterRow(c));
            }
        }

        CharsTime.Text = _recognisedAt is { } at ? string.Format(loc["Wizard.CharsTime"], at.ToString("HH:mm:ss", CultureInfo.CurrentCulture)) : "";
        UpdateNextState();
    }

    /// <summary>A green tick behind what is done, an empty circle before.</summary>
    private void Mark(TextBlock mark, bool done)
    {
        mark.Text = done ? "✓" : "○";
        mark.SetResourceReference(TextBlock.ForegroundProperty, done ? "Brush.Heal" : "Brush.TextMuted");
    }

    private ToolTip Tip(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
        return new ToolTip { Content = block };
    }

    /// <summary>One line of the list: faction emblem, server, name in the class colour, class, level and, right-aligned, a green tick for the one logged in now (the others are only known by what the game listed).</summary>
    private UIElement CharacterRow(SeenCharacter c)
    {
        var loc = LocalizationManager.Instance;
        string? className = c.ClassCode > 0 ? Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        foreach (GridLength width in new[] { new GridLength(26), new GridLength(110), new GridLength(1, GridUnitType.Star), new GridLength(120), new GridLength(80), new GridLength(28) })
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        if (new RaceIconConverter().Convert(c.Faction == 2 ? "Asmodian" : "Elyos", typeof(System.Windows.Media.ImageSource), null, CultureInfo.CurrentCulture) is System.Windows.Media.ImageSource flag)
        {
            row.Children.Add(new Image { Source = flag, Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
        }

        void Cell(int column, string text, System.Windows.Media.Brush? brush, string brushKey, bool bold = false)
        {
            var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 8, 0) };
            if (brush is not null)
            {
                block.Foreground = brush;
            }
            else
            {
                block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            }

            Grid.SetColumn(block, column);
            row.Children.Add(block);
        }

        Cell(1, c.ServerId > 0 ? Aion2Servers.NameOf(c.ServerId).Replace("Europe - ", "", StringComparison.Ordinal) : "", null, "Brush.TextMuted");
        Cell(2, c.Name, className is not null ? ClassBrushes.Solid(className) : null, "Brush.Text", bold: true);
        Cell(3, className is not null ? ClassCatalog.DisplayName(className, loc.Language) : "", null, "Brush.TextSubtle");
        Cell(4, c.Level > 0 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.Level"], c.Level) : "", null, "Brush.TextSubtle");
        if (c.Current)
        {
            var tick = new TextBlock { Text = "✓", FontSize = 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            tick.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Heal");
            Grid.SetColumn(tick, 5);
            row.Children.Add(tick);
        }

        return row;
    }

    /// <summary>Next waits for an answer on the policy page, and Finish waits until the character logged in was recognised.</summary>
    private void UpdateNextState()
    {
        if (_pages.Length == 0)
        {
            return;
        }

        List<int> sequence = Sequence();
        bool last = sequence.IndexOf(_page) == sequence.Count - 1;
        bool enabled = _page switch
        {
            1 => PolicyAccept.IsChecked == true || PolicyDecline.IsChecked == true,
            3 => _hasCurrent,
            _ => true,
        };
        NextButton.IsEnabled = enabled;
        NextButton.ToolTip = last && !enabled ? LocalizationManager.Instance["Wizard.FinishLocked"] : null;
    }

    private void OnNpcapDownloadClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(NpcapAvailability.DownloadUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser: the Npcap hint after the wizard names the address as well
        }
    }

    private bool _finished;

    /// <summary>The policy in the chosen language, as paragraphs and bullet items (the rest of the wizard changes language by itself).</summary>
    private void ShowPolicyText() => PolicyHost.Content = PolicyTextView.Build(LocalizationManager.Instance["Privacy.Text"]);

    /// <summary>Takes effect at once, like in Settings: the wizard's own texts change language with it.</summary>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is ComboBoxItem { Tag: string code })
        {
            LocalizationManager.Instance.Language = code;
            ShowPolicyText();
            if (_pages.Length > 0)
            {
                ShowPage(_page); // the button and step texts come from code
            }
        }
    }

    // ---- one question per page: language, privacy policy, the uploads (only after the policy was accepted), ready to start ----

    private UIElement[] _pages = Array.Empty<UIElement>();
    private int _page;

    /// <summary>The upload page is dropped for somebody who declined the policy (before an answer the wizard counts four steps).</summary>
    private bool Skipped(int page) => page == 2 && PolicyDecline.IsChecked == true;

    private List<int> Sequence() => Enumerable.Range(0, 4).Where(i => !Skipped(i)).ToList();

    /// <summary>Shows a page, with "Step x of y" and the buttons that fit: no Back on the first page, Finish on the last, Next waits for an answer on the policy page.</summary>
    private void ShowPage(int page)
    {
        _page = page;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visibility = i == page ? Visibility.Visible : Visibility.Hidden;
        }

        var loc = LocalizationManager.Instance;
        List<int> sequence = Sequence();
        int position = Math.Max(0, sequence.IndexOf(page));
        StepText.Text = string.Format(loc["Wizard.Step"], position + 1, sequence.Count);
        BackButton.Visibility = position > 0 ? Visibility.Visible : Visibility.Hidden;
        NextButton.Content = position == sequence.Count - 1 ? loc["Wizard.Finish"] : loc["Wizard.Next"];
        UpdateNextState();
    }

    /// <summary>The picture mode draws each page (0 language, 1 policy, 2 uploads, 3 ready) as it looks.</summary>
    internal void ShowPageForPicture(int page, bool accepted, (bool Npcap, IReadOnlyList<SeenCharacter> Characters, DateTime At)? sample = null)
    {
        PictureSample = sample;
        if (sample is not null)
        {
            RefreshStatus();
        }

        if (accepted)
        {
            PolicyAccept.IsChecked = true;
        }

        ShowPage(page);
    }

    private void OnNextClicked(object sender, RoutedEventArgs e)
    {
        List<int> sequence = Sequence();
        int position = sequence.IndexOf(_page);
        if (position >= sequence.Count - 1)
        {
            Close();
            return;
        }

        ShowPage(sequence[position + 1]);
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        List<int> sequence = Sequence();
        int position = sequence.IndexOf(_page);
        if (position > 0)
        {
            ShowPage(sequence[position - 1]);
        }
    }

    /// <summary>Declining takes the uploads off and drops the upload page; accepting brings it back.</summary>
    private void OnPolicyChanged(object sender, RoutedEventArgs e)
    {
        if (PolicyAccept.IsChecked != true)
        {
            BossBox.IsChecked = false;
            ProfileBox.IsChecked = false;
            OthersBox.IsChecked = false;
        }

        ShowPage(_page);
    }

    private void OnPolicyLinkNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser: the address is in the text
        }

        e.Handled = true;
    }

    /// <summary>Writes the choices. Runs once, however the window was closed.</summary>
    private void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _settings.Language = LocalizationManager.Instance.Language;
        // closing the window without an answer is no answer: nothing is uploaded, the meter stays complete and the question comes again at the next start
        // (26.10.2026: a player who closed it was in the restricted mode without knowing why)
        bool accepted = PolicyAccept.IsChecked == true;
        bool answered = accepted || PolicyDecline.IsChecked == true;
        _settings.PrivacyDecision = !answered ? PrivacyConsent.NotAsked : accepted ? PrivacyConsent.Accepted : PrivacyConsent.Declined;
        _settings.PrivacyVersion = accepted ? PrivacyConsent.Version : 0;
        _settings.AutoUploadBoss = accepted && BossBox.IsChecked == true;
        _settings.AutoUploadProfile = accepted && ProfileBox.IsChecked == true;
        _settings.UploadOtherPlayersProfiles = accepted && OthersBox.IsChecked == true;
        _settings.SetupCompleted = true;
        _settings.Save();
        PrivacyConsent.NotifyChanged();
    }
}
