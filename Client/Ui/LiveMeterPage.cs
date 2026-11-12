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
  </Grid>
</DataTemplate>";

    public static ToolPage Build(ObservableCollection<PlayerRow> rows, Action openMeter)
    {
        var loc = LocalizationManager.Instance;
        var list = new ItemsControl { ItemsSource = rows, ItemTemplate = (DataTemplate)XamlReader.Parse(RowXaml) };
        var card = new Border { Padding = new Thickness(16, 10, 16, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(14, 10, 14, 14), VerticalAlignment = VerticalAlignment.Top, Child = list };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var head = new DockPanel { Margin = new Thickness(14, 12, 14, 0) };
        var open = new Button { Content = loc["Hub.OpenMeter"], Padding = new Thickness(14, 4, 14, 4) };
        open.Click += (_, _) => openMeter();
        DockPanel.SetDock(open, Dock.Right);
        var title = new TextBlock { Text = loc["Hub.LiveMeter"], FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        head.Children.Add(open);
        head.Children.Add(title);
        var page = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        page.Children.Add(head);
        page.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = card });
        return new ToolPage { Content = page };
    }
}
