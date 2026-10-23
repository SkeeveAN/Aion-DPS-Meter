using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AionDPS.Aion2.Capture;

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
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <param name="characterProbe">The name of the character the meter has recognised so far, or null; polled once a second.</param>
    public WizardWindow(MeterSettings settings, Func<string?>? characterProbe = null)
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

    /// <summary>Npcap present? Character recognised? Re-read every second, in the language chosen above.</summary>
    private void RefreshStatus()
    {
        var loc = LocalizationManager.Instance;
        bool npcap = NpcapAvailability.Detect().IsInstalled;
        NpcapText.Text = loc[npcap ? "Wizard.NpcapOk" : "Wizard.NpcapMissing"];
        NpcapButton.Visibility = npcap ? Visibility.Collapsed : Visibility.Visible;

        string? character = null;
        try
        {
            character = _characterProbe?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            // the capture is still starting: nothing recognised yet
        }

        CharacterText.Text = string.IsNullOrEmpty(character) ? loc["Wizard.CharWaiting"] : string.Format(loc["Wizard.CharFound"], character);
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
        NextButton.IsEnabled = page != 1 || PolicyAccept.IsChecked == true || PolicyDecline.IsChecked == true;
    }

    /// <summary>The picture mode draws each page (0 language, 1 policy, 2 uploads, 3 ready) as it looks.</summary>
    public void ShowPageForPicture(int page, bool accepted)
    {
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
        // closing the window without an answer counts as declined: nothing is uploaded and the meter stays in its restricted mode
        bool accepted = PolicyAccept.IsChecked == true;
        _settings.PrivacyDecision = accepted ? PrivacyConsent.Accepted : PrivacyConsent.Declined;
        _settings.PrivacyVersion = accepted ? PrivacyConsent.Version : 0;
        _settings.AutoUploadBoss = accepted && BossBox.IsChecked == true;
        _settings.AutoUploadProfile = accepted && ProfileBox.IsChecked == true;
        _settings.UploadOtherPlayersProfiles = accepted && OthersBox.IsChecked == true;
        _settings.SetupCompleted = true;
        _settings.Save();
        PrivacyConsent.NotifyChanged();
    }
}
