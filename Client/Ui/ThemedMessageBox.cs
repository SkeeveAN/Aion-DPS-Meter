using System.Windows;
using System.Windows.Controls;

namespace AionDPS.Ui;

/// <summary>
/// The meter's own message box: the same frame, colours and buttons as every other window instead of
/// the system's. Same call shape as <see cref="MessageBox.Show(Window, string, string, MessageBoxButton, MessageBoxImage)"/>
/// for the parts the meter uses (text, caption, buttons); the icon is shown as a glyph.
/// </summary>
public static class ThemedMessageBox
{
    public static MessageBoxResult Show(Window? owner, string text, string caption,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        string? checkboxLabel = null, Action<bool>? onCheckbox = null)
    {
        MessageBoxResult result = buttons switch
        {
            MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.No,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.OK,
        };

        var window = new Window
        {
            Title = caption,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 340,
            MaxWidth = 560,
            ShowInTaskbar = owner is null,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        if (owner is { IsLoaded: true })
        {
            window.Owner = owner;
        }

        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        message.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

        string glyph = image switch
        {
            MessageBoxImage.Error => "✖",
            MessageBoxImage.Warning => "⚠",
            MessageBoxImage.Question => "?",
            MessageBoxImage.Information => "ℹ",
            _ => "",
        };
        var body = new DockPanel { LastChildFill = true, Margin = new Thickness(20, 18, 20, 12) };
        if (glyph.Length > 0)
        {
            var icon = new TextBlock { Text = glyph, FontSize = 26, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.ForegroundProperty, image == MessageBoxImage.Error ? "Brush.Danger" : "Brush.Accent");
            DockPanel.SetDock(icon, Dock.Left);
            body.Children.Add(icon);
        }

        body.Children.Add(message);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 4, 20, 16) };
        void AddButton(string label, MessageBoxResult value, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button { Content = label, MinWidth = 84, Height = 28, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0), IsDefault = isDefault, IsCancel = isCancel };
            button.Click += (_, _) =>
            {
                result = value;
                window.Close();
            };
            row.Children.Add(button);
        }

        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                AddButton("OK", MessageBoxResult.OK, isDefault: true);
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No, isCancel: true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No);
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;
            default:
                AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true);
                break;
        }

        var content = new StackPanel();
        content.Children.Add(body);
        if (checkboxLabel is not null)
        {
            // e.g. "don't show this again": its state goes to onCheckbox when the box is closed
            var check = new CheckBox { Content = checkboxLabel, Margin = new Thickness(20, 0, 20, 8) };
            content.Children.Add(check);
            window.Closed += (_, _) => onCheckbox?.Invoke(check.IsChecked == true);
        }

        content.Children.Add(row);
        window.Content = content;
        ThemedChrome.Apply(window);
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowDialog();
        return result;
    }
}
