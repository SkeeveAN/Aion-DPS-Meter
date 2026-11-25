using System.Windows;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>The "Farm map" card of the Pets tab: which maps are worked out, the automatic change, and the buttons that make the pictures and say what to do with them.</summary>
public partial class SettingsWindow
{
    private void ReadFarmMap(MeterSettings s)
    {
        s.FarmMapVerteron = FarmVerteronBox.IsChecked ?? true;
        s.FarmMapAltgard = FarmAltgardBox.IsChecked ?? true;
        s.FarmMapInstances = FarmInstancesBox.IsChecked ?? false;
        s.FarmMapAbyssLower = FarmAbyssLowerBox.IsChecked ?? true;
        s.FarmMapAbyssOuter = FarmAbyssOuterBox.IsChecked ?? true;
        s.FarmMapAutoSwitch = FarmAutoSwitchBox.IsChecked ?? true;
    }

    private void ShowFarmMap(MeterSettings s)
    {
        FarmVerteronBox.IsChecked = s.FarmMapVerteron;
        FarmAltgardBox.IsChecked = s.FarmMapAltgard;
        FarmInstancesBox.IsChecked = s.FarmMapInstances;
        FarmAbyssLowerBox.IsChecked = s.FarmMapAbyssLower;
        FarmAbyssOuterBox.IsChecked = s.FarmMapAbyssOuter;
        FarmAutoSwitchBox.IsChecked = s.FarmMapAutoSwitch;
        ShowFarmOffers();
    }

    /// <summary>The folder the pictures were saved to, under the buttons ("Saved in: ..."); empty hides the line.</summary>
    private void ShowFarmSavedPath(string folder)
    {
        FarmSavedPathText.Text = string.IsNullOrEmpty(folder) ? "" : string.Format(LocalizationManager.Instance["Settings.FarmMap.SavedIn"], folder);
        FarmSavedPathText.Visibility = string.IsNullOrEmpty(folder) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The overlay / widget buttons are read-only while the farm map is on offer there already.</summary>
    private void ShowFarmOffers() => ShowFarmOffers(FarmMapView.Ids.All(id => WidgetOverlayWindow.IsOn(MeterSettings.Load(), id)), FarmMapView.Ids.All(DashboardBoard.Has));

    private void ShowFarmOffers(bool isOverlay, bool isWidget)
    {
        FarmOverlayButton.IsEnabled = !isOverlay;
        FarmWidgetButton.IsEnabled = !isWidget;
    }

    /// <summary>For layout checks (render-settings): shows the result block as it looks after the images were made.</summary>
    public void ShowFarmResult(string names, bool isOverlay = false, bool isWidget = false, string savedTo = "")
    {
        ShowFarmSavedPath(savedTo);
        FarmResultText.Text = names;
        ShowFarmOffers(isOverlay, isWidget);
        FarmResultCard.Visibility = Visibility.Visible;
    }

    private IReadOnlyList<FarmImage> _farmImages = Array.Empty<FarmImage>();

    /// <summary>Works out the spots of every chosen map from what the player lacks and draws one picture per map (replacing the last ones).</summary>
    private async void OnFarmGenerateClicked(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        FarmGenerateButton.IsEnabled = false;
        object? label = FarmGenerateButton.Content;
        FarmGenerateButton.Content = loc["Settings.FarmMap.Generating"];
        try
        {
            var states = (Aion2CharacterStore.Load(Aion2CharacterStore.DefaultPath)?.Pets ?? new())
                .ToDictionary(p => p.Id, p => new Aion2PetState(p.Id, p.Level, p.Progress));
            string? faction = Aion2Gather.OwnFaction();
            var keys = new List<string>();
            if (FarmVerteronBox.IsChecked == true) keys.Add("verteron");
            if (FarmAltgardBox.IsChecked == true) keys.Add("altgard");
            if (FarmAbyssLowerBox.IsChecked == true) keys.Add("abyss");
            if (FarmAbyssOuterBox.IsChecked == true) keys.Add("abyss_outer");
            var maps = Aion2Maps.All.Where(m => keys.Contains(m.Key)).OrderBy(m => keys.IndexOf(m.Key)).ToList();
            if (FarmInstancesBox.IsChecked == true)
            {
                maps.AddRange(Aion2Maps.Instances);
            }

            var made = new List<FarmMade>();
            string stamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var map in maps)
            {
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                var plan = FarmMapEngine.Plan(map, states, faction, loc.Language, FarmMapRenderer.LandTest(map));
                if (plan.Spots.Count == 0)
                {
                    continue;
                }

                string name = FarmMapRenderer.NameOf(map);
                string subtitle = string.Format(loc["Settings.FarmMap.Img.Subtitle"], plan.MissingPets, stamp);
                made.Add(new FarmMade(map.Key, name, FarmMapRenderer.Render(plan, name, subtitle, false), FarmMapRenderer.Render(plan, name, subtitle, true), FarmMapStore.LegendOf(plan, subtitle)));
            }

            if (made.Count == 0)
            {
                _farmImages = Array.Empty<FarmImage>();
                ShowFarmResult(loc["Settings.FarmMap.None"], true, true, "");
                return;
            }

            _farmImages = FarmMapStore.Replace(made);
            ShowFarmResult(string.Format(loc["Settings.FarmMap.Made"], _farmImages.Count, string.Join(", ", _farmImages.Select(i => i.Name))),
                FarmMapView.Ids.All(id => WidgetOverlayWindow.IsOn(MeterSettings.Load(), id)), FarmMapView.Ids.All(DashboardBoard.Has), "");
        }
        finally
        {
            FarmGenerateButton.Content = label;
            FarmGenerateButton.IsEnabled = true;
        }
    }

    /// <summary>"Save locally": the player picks a folder, every picture goes there named after its map (in the player's language).</summary>
    private void OnFarmSaveClicked(object sender, RoutedEventArgs e)
    {
        var images = _farmImages.Count > 0 ? _farmImages : FarmMapStore.Load();
        if (images.Count == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = LocalizationManager.Instance["Settings.FarmMap.Save"], InitialDirectory = MeterSettings.Load().FarmMapSaveDir, Multiselect = false };
        if (DialogOwner.Show(dialog, this) != true || string.IsNullOrEmpty(dialog.FolderName))
        {
            return;
        }

        FarmMapStore.SaveTo(dialog.FolderName, images);
        var s = MeterSettings.Load();
        s.FarmMapSaveDir = dialog.FolderName;
        s.Save();
        _settings.FarmMapSaveDir = dialog.FolderName;
        ShowFarmSavedPath(dialog.FolderName);
    }

    /// <summary>"Offer as overlay": the map and its list are put in the middle of the screen (the list beside the map), the overlay designer opens (the hub goes to the background) so they can be placed.</summary>
    private void OnFarmOverlayClicked(object sender, RoutedEventArgs e)
    {
        var s = MeterSettings.Load();
        var area = SystemParameters.WorkArea;
        double total = 0, gap = 12;
        var sizes = FarmMapView.Ids.Select(id => WidgetKind.SizeOf(id)).ToList();
        total = sizes.Sum(z => z.Width) + gap * (sizes.Count - 1);
        double x = area.Left + (area.Width - total) / 2;
        for (int i = 0; i < FarmMapView.Ids.Count; i++)
        {
            string id = FarmMapView.Ids[i];
            if (!s.WidgetOverlays.TryGetValue(id, out WidgetOverlayPlace? place))
            {
                s.WidgetOverlays[id] = place = new WidgetOverlayPlace();
            }

            place.Width ??= sizes[i].Width;
            place.Height ??= sizes[i].Height;
            if (!place.On)
            {
                place.Left = x;
                place.Top = area.Top + (area.Height - place.Height.Value) / 2;
                place.Scale = 1;
            }

            place.On = true;
            x += place.Width.Value + gap;
        }

        s.Save();
        ShowFarmOffers();
        FarmMapLinks.OpenDesigner?.Invoke();
    }

    /// <summary>"Offer as widget": the widget is put on the dashboard that is shown, and the window changes to the dashboard so it can be placed there.</summary>
    private void OnFarmWidgetClicked(object sender, RoutedEventArgs e)
    {
        foreach (string id in FarmMapView.Ids)
        {
            DashboardBoard.AddToActive(id);
        }

        ShowFarmOffers();
        FarmMapLinks.ShowDashboard?.Invoke();
    }
}
