using System.Windows;
using System.Windows.Controls;

namespace AionSniffer.Ui;

public partial class SettingsWindow : Window
{
    private readonly MeterSettings _settings;

    public SettingsWindow(MeterSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        ShowAutoDetectedRankedBossesBox.IsChecked = settings.ShowAutoDetectedRankedBosses;
        ShowManuallySelectedTargetBox.IsChecked = settings.ShowManuallySelectedTarget;
        ShowAutoDetectedTargetBox.IsChecked = settings.ShowAutoDetectedTarget;
        ShowDpsBox.IsChecked = settings.ShowDps;
        ShowLevelBox.IsChecked = settings.ShowLevel;
        ShowPlayersBox.IsChecked = settings.ShowPlayers;
        ShowMinionNpcsBox.IsChecked = settings.ShowMinionNpcs;
        ShowCommonNpcsBox.IsChecked = settings.ShowCommonNpcs;
        ShowEliteNpcsBox.IsChecked = settings.ShowEliteNpcs;
        ShowHeroicNpcsBox.IsChecked = settings.ShowHeroicNpcs;
        ShowLegendaryNpcsBox.IsChecked = settings.ShowLegendaryNpcs;
        SelectComboItem(ThemeBox, settings.Theme);
        SelectComboItem(FontSizeBox, settings.FontSize);
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
        _settings.ShowAutoDetectedRankedBosses = ShowAutoDetectedRankedBossesBox.IsChecked ?? false;
        _settings.ShowManuallySelectedTarget = ShowManuallySelectedTargetBox.IsChecked ?? false;
        _settings.ShowAutoDetectedTarget = ShowAutoDetectedTargetBox.IsChecked ?? false;
        _settings.ShowDps = ShowDpsBox.IsChecked ?? false;
        _settings.ShowLevel = ShowLevelBox.IsChecked ?? false;
        _settings.ShowPlayers = ShowPlayersBox.IsChecked ?? false;
        _settings.ShowMinionNpcs = ShowMinionNpcsBox.IsChecked ?? false;
        _settings.ShowCommonNpcs = ShowCommonNpcsBox.IsChecked ?? false;
        _settings.ShowEliteNpcs = ShowEliteNpcsBox.IsChecked ?? false;
        _settings.ShowHeroicNpcs = ShowHeroicNpcsBox.IsChecked ?? false;
        _settings.ShowLegendaryNpcs = ShowLegendaryNpcsBox.IsChecked ?? false;
        _settings.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Content as string ?? _settings.Theme;
        _settings.FontSize = (FontSizeBox.SelectedItem as ComboBoxItem)?.Content as string ?? _settings.FontSize;

        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
