using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using AionDPS.Aion2.Capture;
using AionDPS.Upload;
using Microsoft.Win32;

namespace AionDPS.Ui;

/// <summary>
/// Under the network tool: "Save report" writes the last fifteen minutes of the connection as a JSON file where the player chooses, "Send report" sends the same report to the
/// developer after the player has seen what it holds and what it does not (see <see cref="NetworkReport"/>).
/// </summary>
public sealed class NetworkReportPanel : Border
{
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _save = new();
    private readonly Button _send = new();

    public NetworkReportPanel()
    {
        var loc = LocalizationManager.Instance;
        SetResourceReference(BackgroundProperty, "Brush.Panel");
        SetResourceReference(BorderBrushProperty, "Brush.Border");
        BorderThickness = new Thickness(1);
        Padding = new Thickness(16, 14, 16, 14);
        Margin = new Thickness(0, 12, 0, 0);

        var stack = new StackPanel();
        var heading = new TextBlock { Text = loc["Network.Report.Title"].ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var text = new TextBlock { Text = loc["Network.Report.Text"], TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        _save.Content = loc["Network.Report.Save"];
        _save.Padding = new Thickness(12, 5, 12, 5);
        _save.Margin = new Thickness(0, 0, 8, 0);
        _save.Click += OnSave;
        _send.Content = loc["Network.Report.Send"];
        _send.Padding = new Thickness(12, 5, 12, 5);
        _send.Click += OnSend;
        buttons.Children.Add(_save);
        buttons.Children.Add(_send);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        stack.Children.Add(heading);
        stack.Children.Add(text);
        stack.Children.Add(buttons);
        stack.Children.Add(_status);
        Child = stack;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        DateTime now = DateTime.Now;
        var dialog = new SaveFileDialog
        {
            Title = loc["Network.Report.Save"],
            FileName = NetworkReport.FileName(NetworkReport.WindowStart(now)),
            Filter = "JSON (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, NetworkReport.Build(now, MeterSettings.Load().CaptureAdapterId, null), new UTF8Encoding(false));
            _status.Text = string.Format(loc["Network.Report.Saved"], dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = string.Format(loc["Network.Report.SaveFailed"], ex.Message);
        }
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        string text = loc["Network.Report.Send.Intro"] + "\n\n" + loc["StatCalc.Send.Contains"] + "\n" + string.Join("\n", NetworkReport.Contents.Select(k => "• " + loc[k]))
            + "\n\n" + loc["StatCalc.Send.NotContains"] + "\n" + string.Join("\n", NetworkReport.NotContained.Select(k => "• " + loc[k])) + "\n\n" + loc["StatCalc.Send.Question"];
        if (ThemedMessageBox.Show(Window.GetWindow(this), text, loc["Network.Report.Send.Title"], MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _send.IsEnabled = false;
        string json = NetworkReport.Build(DateTime.Now, MeterSettings.Load().CaptureAdapterId, MeterSettings.EnsureAnonymousId());
        UploadResult result = await UploadClient.SendNetworkReportAsync(json);
        _status.Text = result.Success ? loc["Network.Report.Sent"] : string.Format(loc["StatCalc.SendFailed"], result.Error ?? "");
        _send.IsEnabled = true;
    }
}
