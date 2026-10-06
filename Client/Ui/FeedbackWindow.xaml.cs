using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using AionDPS.Feedback;

namespace AionDPS.Ui;

/// <summary>Bug report / suggestion form. Goes to aiondps.com, which opens the GitHub issue; an attached
/// recording is encrypted here first (<see cref="FeedbackCrypto"/>), so only the maintainer can read it.</summary>
public partial class FeedbackWindow : Window
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private readonly MeterSettings _settings;
    private bool _sending;

    public FeedbackWindow(MeterSettings settings)
    {
        InitializeComponent();
        ThemedChrome.Apply(this);
        _settings = settings;
        NameBox.Text = settings.FeedbackName;
        EmailBox.Text = settings.FeedbackEmail;

        long bytes = CaptureRingBuffer.Bytes;
        bool available = bytes > 0 && File.Exists(FeedbackCrypto.PublicKeyPath);
        RecordingBox.IsEnabled = available;
        RecordingHint.Text = available
            ? string.Format(LocalizationManager.Instance["Feedback.RecordingHint"], $"{bytes / 1024.0 / 1024.0:0.0}")
            : LocalizationManager.Instance["Feedback.RecordingNone"];
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();

    private async void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (_sending)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        string name = NameBox.Text.Trim();
        string email = EmailBox.Text.Trim();
        string message = MessageBox.Text.Trim();
        if (name.Length == 0 || message.Length < 10 || (email.Length > 0 && !EmailPattern.IsMatch(email)))
        {
            StatusText.Text = loc["Feedback.Invalid"];
            return;
        }

        _sending = true;
        SendButton.IsEnabled = false;
        StatusText.Text = loc["Feedback.Sending"];
        try
        {
            string? recording = null;
            if (RecordingBox.IsChecked == true)
            {
                string pem = await File.ReadAllTextAsync(FeedbackCrypto.PublicKeyPath);
                byte[] encrypted = await Task.Run(() => FeedbackCrypto.Encrypt(CaptureRingBuffer.SnapshotJsonLines(), pem));
                recording = Convert.ToBase64String(encrypted);
            }

            string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
            string type = (string)((ComboBoxItem)TypeBox.SelectedItem).Tag;
            var result = await FeedbackClient.SendAsync(new FeedbackRequest(type, name, email.Length > 0 ? email : null, message, "client", version, loc.Language, recording));
            if (result.Success)
            {
                _settings.FeedbackName = name;
                _settings.FeedbackEmail = email;
                MeterSettings onDisk = MeterSettings.Load();
                onDisk.FeedbackName = name;
                onDisk.FeedbackEmail = email;
                onDisk.Save();
                ThemedMessageBox.Show(Owner, loc["Feedback.Sent"], loc["Feedback.Title"]);
                Close();
                return;
            }

            StatusText.Text = string.Format(loc["Feedback.Failed"], result.Error);
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(loc["Feedback.Failed"], ex.Message);
        }
        finally
        {
            _sending = false;
            SendButton.IsEnabled = true;
        }
    }
}
