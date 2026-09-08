using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionSniffer.Ui;

public partial class SettingsWindow : Window
{
    private readonly MeterSettings _settings;
    private string? _aionInstallFolder;
    private readonly List<CharacterProfile> _characters;

    public SettingsWindow(MeterSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _characters = settings.Characters.Select(c => new CharacterProfile { Name = c.Name, ClassName = c.ClassName }).ToList();

        ShowPlayersBox.IsChecked = settings.ShowPlayers;
        ShowMinionNpcsBox.IsChecked = settings.ShowMinionNpcs;
        ShowCommonNpcsBox.IsChecked = settings.ShowCommonNpcs;
        ShowEliteNpcsBox.IsChecked = settings.ShowEliteNpcs;
        ShowHeroicNpcsBox.IsChecked = settings.ShowHeroicNpcs;
        ShowLegendaryNpcsBox.IsChecked = settings.ShowLegendaryNpcs;
        CheckForUpdatesBox.IsChecked = settings.CheckForUpdates;
        SelectComboItem(ThemeBox, settings.Theme);
        SelectComboItem(FontSizeBox, settings.FontSize);

        _aionInstallFolder = settings.AionInstallFolder;
        AionInstallFolderBox.Text = _aionInstallFolder ?? "(not set)";
        UpdateAionFolderStatus();

        RefreshCharacterLists();
        if (settings.ActiveCharacterName is string activeName && _characters.Any(c => c.Name == activeName))
        {
            ActiveCharacterBox.SelectedItem = activeName;
        }

        // Setting IsChecked here also fires OnAutoDetectActiveCharacterChanged (ToggleButton
        // raises Checked/Unchecked from a property-value change same as from a real click), which
        // is what sets ActiveCharacterBox's initial IsEnabled -- no separate call needed for that.
        AutoDetectActiveCharacterBox.IsChecked = settings.AutoDetectActiveCharacter;
    }

    private void OnAutoDetectActiveCharacterChanged(object sender, RoutedEventArgs e)
    {
        // While auto-detect is on, ActiveCharacterBox just displays whatever the last skill use
        // picked -- disabled so a manual selection here doesn't look meaningful when it would be
        // overwritten again the moment a skill is seen (see MeterSettings.AutoDetectActiveCharacter).
        ActiveCharacterBox.IsEnabled = AutoDetectActiveCharacterBox.IsChecked != true;
    }

    private void RefreshCharacterLists()
    {
        // Bound to the CharacterProfile objects themselves now, not pre-formatted strings -- the
        // ItemTemplate (icon + name, see XAML) needs Name/ClassName as separate bindings.
        CharactersList.ItemsSource = _characters.ToList();

        string? previouslySelected = ActiveCharacterBox.SelectedItem as string;
        ActiveCharacterBox.ItemsSource = _characters.Select(c => c.Name).ToList();
        if (previouslySelected is not null && _characters.Any(c => c.Name == previouslySelected))
        {
            ActiveCharacterBox.SelectedItem = previouslySelected;
        }
    }

    private void OnAddCharacterClicked(object sender, RoutedEventArgs e)
    {
        string name = NewCharacterNameBox.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }

        // Content is now the class icon <Image>, not text (see XAML) -- the class name lives in
        // Tag instead, since it's still needed as data even though it's no longer displayed.
        string className = (NewCharacterClassBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        string faction = (NewCharacterFactionBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        _characters.RemoveAll(c => c.Name == name); // re-adding an existing name replaces class+faction
        _characters.Add(new CharacterProfile { Name = name, ClassName = className, Faction = faction });
        NewCharacterNameBox.Text = "";
        RefreshCharacterLists();
    }

    private void OnRemoveCharacterClicked(object sender, RoutedEventArgs e)
    {
        int index = CharactersList.SelectedIndex;
        if (index >= 0 && index < _characters.Count)
        {
            _characters.RemoveAt(index);
            RefreshCharacterLists();
        }
    }

    private void OnSelectAionFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select the Aion install folder (containing Chat.log)",
        };
        if (!string.IsNullOrEmpty(_aionInstallFolder) && Directory.Exists(_aionInstallFolder))
        {
            dialog.InitialDirectory = _aionInstallFolder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _aionInstallFolder = dialog.FolderName;
            AionInstallFolderBox.Text = _aionInstallFolder;
            UpdateAionFolderStatus();
        }
    }

    /// <summary>
    /// Same idea as the AionRainMeter reference's red "Wrong path selected!" -- checks for
    /// bin64\game.dll or AION.bin (the 64-/32-bit client executables, see README's crypto
    /// investigation for how we know these paths) to confirm this is actually an Aion install
    /// root, not just some folder the user clicked into. Chat.log itself is checked separately and
    /// only as an informational note, not a hard failure: a fresh install with chatlog not yet
    /// enabled (see the client's builder_dev_dialog "Basic Chatlog" toggle, README) would otherwise
    /// look "wrong" even though the folder itself is correct.
    /// </summary>
    private void UpdateAionFolderStatus()
    {
        if (string.IsNullOrEmpty(_aionInstallFolder))
        {
            AionInstallFolderStatus.Text = "No folder selected yet.";
            AionInstallFolderStatus.Foreground = new SolidColorBrush(Colors.Gray);
            return;
        }

        bool looksLikeAionInstall = File.Exists(Path.Combine(_aionInstallFolder, "bin64", "game.dll"))
            || File.Exists(Path.Combine(_aionInstallFolder, "AION.bin"));

        if (!looksLikeAionInstall)
        {
            AionInstallFolderStatus.Text = "Wrong path selected! This doesn't look like an Aion install (no bin64\\game.dll or AION.bin found here).";
            AionInstallFolderStatus.Foreground = new SolidColorBrush(Colors.OrangeRed);
            return;
        }

        bool hasChatLog = File.Exists(Path.Combine(_aionInstallFolder, "Chat.log"));
        AionInstallFolderStatus.Text = hasChatLog
            ? "Aion install found, Chat.log present."
            : "Aion install found, but no Chat.log here yet -- chat logging may still need to be enabled in-game.";
        AionInstallFolderStatus.Foreground = new SolidColorBrush(hasChatLog ? Colors.LightGreen : Colors.Khaki);
    }

    private static void SelectComboItem(ComboBox box, string content)
    {
        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Content as string, content, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = cbi;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        _settings.ShowPlayers = ShowPlayersBox.IsChecked ?? false;
        _settings.ShowMinionNpcs = ShowMinionNpcsBox.IsChecked ?? false;
        _settings.ShowCommonNpcs = ShowCommonNpcsBox.IsChecked ?? false;
        _settings.ShowEliteNpcs = ShowEliteNpcsBox.IsChecked ?? false;
        _settings.ShowHeroicNpcs = ShowHeroicNpcsBox.IsChecked ?? false;
        _settings.ShowLegendaryNpcs = ShowLegendaryNpcsBox.IsChecked ?? false;
        _settings.CheckForUpdates = CheckForUpdatesBox.IsChecked ?? true;
        _settings.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Content as string ?? _settings.Theme;
        _settings.FontSize = (FontSizeBox.SelectedItem as ComboBoxItem)?.Content as string ?? _settings.FontSize;
        _settings.AionInstallFolder = _aionInstallFolder;
        _settings.Characters = _characters;
        _settings.ActiveCharacterName = ActiveCharacterBox.SelectedItem as string;
        _settings.AutoDetectActiveCharacter = AutoDetectActiveCharacterBox.IsChecked ?? true;

        Saved?.Invoke();
        Close();
    }

    /// <summary>
    /// Fired on Save, right before Close() -- replaces the old ShowDialog()/DialogResult flow, per
    /// the user's request that Settings open as an independent second window instead of a modal
    /// blocking MainWindow (Show(), not ShowDialog(), from MainWindow.OnSettingsClicked). Setting
    /// DialogResult only works for a window actually shown via ShowDialog() -- doing it here would
    /// throw at runtime the moment Show() is used instead, hence this event instead.
    /// </summary>
    public event Action? Saved;

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
