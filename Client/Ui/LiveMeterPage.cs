using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace AionDPS.Ui;

/// <summary>
/// The live meter inside the new interface: the same rows the meter window shows (rank, name and class, a bar with the share, DPS, damage, share), bound to the
/// meter's list, so the page moves with the fight. The meter window stays the place for the modes, the filters and the reset.
/// </summary>
public static class LiveMeterPage
{
    private const string RowXaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
  <Grid Margin='0,3,0,3'>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width='34'/>
      <ColumnDefinition Width='230'/>
      <ColumnDefinition Width='*'/>
      <ColumnDefinition Width='90'/>
      <ColumnDefinition Width='110'/>
      <ColumnDefinition Width='70'/>
      <ColumnDefinition Width='90'/>
      <ColumnDefinition Width='90'/>
    </Grid.ColumnDefinitions>
    <TextBlock Text='{Binding RankDisplay}' Foreground='{DynamicResource Brush.TextMuted}' VerticalAlignment='Center' HorizontalAlignment='Center'/>
    <StackPanel Grid.Column='1' VerticalAlignment='Center' Margin='0,0,10,0'>
      <TextBlock Text='{Binding NameDisplay}' FontWeight='SemiBold' Foreground='{DynamicResource Brush.Text}' TextTrimming='CharacterEllipsis'/>
      <TextBlock Text='{Binding ClassName}' FontSize='11' Foreground='{DynamicResource Brush.TextMuted}'/>
    </StackPanel>
    <ProgressBar Grid.Column='2' Height='12' Maximum='100' Value='{Binding FillPercent, Mode=OneWay}' Foreground='{Binding ClassBrush}' Background='{DynamicResource Brush.Control}' BorderThickness='0' VerticalAlignment='Center' Margin='0,0,12,0'/>
    <TextBlock Grid.Column='3' Text='{Binding DpsDisplay}' FontWeight='SemiBold' Foreground='{DynamicResource Brush.Text}' HorizontalAlignment='Right' VerticalAlignment='Center'/>
    <TextBlock Grid.Column='4' Text='{Binding DamageDisplay}' Foreground='{DynamicResource Brush.TextMuted}' HorizontalAlignment='Right' VerticalAlignment='Center'/>
    <TextBlock Grid.Column='5' Text='{Binding ShareDisplay}' Foreground='{DynamicResource Brush.TextMuted}' HorizontalAlignment='Right' VerticalAlignment='Center'/>
    <TextBlock Grid.Column='6' Text='{Binding HealCompact}' Foreground='{DynamicResource Brush.Heal}' HorizontalAlignment='Right' VerticalAlignment='Center'>
      <TextBlock.Style><Style TargetType='TextBlock'><Setter Property='Visibility' Value='Collapsed'/><Style.Triggers><DataTrigger Binding='{Binding AllMode}' Value='True'><Setter Property='Visibility' Value='Visible'/></DataTrigger><DataTrigger Binding='{Binding AllCompact}' Value='True'><Setter Property='Visibility' Value='Visible'/></DataTrigger></Style.Triggers></Style></TextBlock.Style>
    </TextBlock>
    <TextBlock Grid.Column='7' Text='{Binding TakenCompact}' Foreground='{DynamicResource Brush.Danger}' HorizontalAlignment='Right' VerticalAlignment='Center'>
      <TextBlock.Style><Style TargetType='TextBlock'><Setter Property='Visibility' Value='Collapsed'/><Style.Triggers><DataTrigger Binding='{Binding AllMode}' Value='True'><Setter Property='Visibility' Value='Visible'/></DataTrigger><DataTrigger Binding='{Binding AllCompact}' Value='True'><Setter Property='Visibility' Value='Visible'/></DataTrigger></Style.Triggers></Style></TextBlock.Style>
    </TextBlock>
  </Grid>
</DataTemplate>";

    public static ToolPage Build(ObservableCollection<PlayerRow> rows, Action openMeter, Func<int>? getMode = null, Action<int>? setMode = null)
    {
        var loc = LocalizationManager.Instance;
        // with nothing recorded the list shows sample rows, tagged as such, so the player sees how it could look
        var sample = new ObservableCollection<PlayerRow>();
        (string Name, string Class, long Damage, double Dps, double Fill, double Share, long Heal, long Taken)[] mock =
        {
            ("Aahz", "Gladiator", 2_310_000, 48210, 100, 31.2, 0, 410_000), ("Skeeve", "Sorcerer", 1_910_000, 39874, 83, 25.8, 0, 220_000),
            ("Chumley", "Assassin", 1_460_000, 30502, 63, 19.7, 0, 380_000), ("Nunzio", "Cleric", 860_000, 17940, 37, 11.6, 1_240_000, 150_000),
        };
        int n = 1;
        foreach (var m in mock)
        {
            sample.Add(new PlayerRow(-n) { Name = m.Name, ClassName = m.Class, Damage = m.Damage, Dps = m.Dps, FillPercent = m.Fill, SharePercent = m.Share, Healing = m.Heal, DamageTaken = m.Taken, Rank = n++ });
        }

        var list = new ItemsControl { ItemsSource = rows.Count > 0 ? rows : sample, ItemTemplate = (DataTemplate)XamlReader.Parse(RowXaml) };
        var mockTag = HubDashboard.MockTag();
        mockTag.Margin = new Thickness(16, 0, 0, 0);
        mockTag.VerticalAlignment = VerticalAlignment.Center;
        mockTag.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        rows.CollectionChanged += (_, _) =>
        {
            list.ItemsSource = rows.Count > 0 ? rows : sample;
            mockTag.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        };
        var card = new Border { Padding = new Thickness(16, 10, 16, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(14, 10, 14, 14), VerticalAlignment = VerticalAlignment.Top, Child = list };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var head = new DockPanel { Margin = new Thickness(14, 12, 14, 0) };
        var title = new TextBlock { Text = loc["Hub.LiveMeter"], FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        DockPanel.SetDock(title, Dock.Left);
        head.Children.Add(title);
        head.Children.Add(mockTag);
        var page = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        page.Children.Add(head);
        if (getMode is not null && setMode is not null)
        {
            // the mode of the list (it is also what the copy hotkey copies)
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 0) };
            string[] names = { loc["Main.ModeDamage"], loc["Main.ModeHeal"], loc["Main.ModeTaken"], loc["Main.ModeAll"], loc["Main.ModeAllCompact"] };
            string group = "LiveMode" + Guid.NewGuid();
            for (int i = 0; i < names.Length; i++)
            {
                int mode = i;
                var chip = new RadioButton { GroupName = group, Content = names[i], IsChecked = getMode() == i, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 5, 14, 5) };
                chip.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='RadioButton'><Border x:Name='Bd' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='15' BorderThickness='1' BorderBrush='{DynamicResource Brush.Border}' Padding='14,5'><ContentPresenter x:Name='Cp' TextElement.Foreground='{DynamicResource Brush.TextMuted}'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Brush.Accent}'/><Setter TargetName='Cp' Property='TextElement.Foreground' Value='{DynamicResource Brush.Accent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
                chip.Checked += (_, _) => setMode(mode);
                chips.Children.Add(chip);
            }

            DockPanel.SetDock(chips, Dock.Top);
            page.Children.Add(chips);
        }

        page.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = card });
        return new ToolPage { Content = page };
    }
}
