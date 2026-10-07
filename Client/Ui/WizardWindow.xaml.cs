using System.Windows;
using System.Windows.Controls;

namespace AionDPS.Ui;

/// <summary>
/// First-start wizard (a fresh install, or a settings file that was lost or broken - see
/// <see cref="MeterSettings.SetupCompleted"/>): the language, then the upload options that are off by
/// default for privacy reasons, each one for the player to switch on. Closing the window any other way
/// than Finish keeps everything off, and the wizard is not shown again; every choice can be changed
/// later in Settings.
/// </summary>
public partial class WizardWindow : Window
{
    private readonly MeterSettings _settings;

    public WizardWindow(MeterSettings settings)
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

        Closed += (_, _) => Finish();
    }

    private bool _finished;

    /// <summary>Takes effect at once, like in Settings: the wizard's own texts change language with it.</summary>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is ComboBoxItem { Tag: string code })
        {
            LocalizationManager.Instance.Language = code;
        }
    }

    private void OnNextClicked(object sender, RoutedEventArgs e)
    {
        if (PrivacyPage.Visibility != Visibility.Visible)
        {
            LanguagePage.Visibility = Visibility.Collapsed;
            PrivacyPage.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Visible;
            NextButton.Content = LocalizationManager.Instance["Wizard.Finish"];
            return;
        }

        Close();
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        PrivacyPage.Visibility = Visibility.Collapsed;
        LanguagePage.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        NextButton.Content = LocalizationManager.Instance["Wizard.Next"];
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
        _settings.AutoUploadBoss = BossBox.IsChecked == true;
        _settings.AutoUploadProfile = ProfileBox.IsChecked == true;
        _settings.UploadOtherPlayersProfiles = OthersBox.IsChecked == true;
        _settings.SetupCompleted = true;
        _settings.Save();
    }
}
