using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using SharpPcap;

namespace AionSniffer.Ui;

/// <summary>
/// Real device listing (SharpPcap.CaptureDeviceList) matching the myaion.eu reference's Network
/// Settings dialog. Selecting a device here and clicking Save only persists the choice
/// (MeterSettings.SelectedCaptureDeviceName) -- actually starting a capture from the GUI on that
/// device is a follow-up; Program.cs's console entry point is still the only thing that opens a
/// device and reads packets today.
/// </summary>
public partial class NetworkSettingsWindow : Window
{
    private readonly List<DeviceRow> _rows = new();

    public NetworkSettingsWindow()
    {
        InitializeComponent();

        var settings = MeterSettings.Load();
        foreach (var device in CaptureDeviceList.Instance)
        {
            var row = new DeviceRow(device.Name, device.Description ?? device.Name);
            if (device.Name == settings.SelectedCaptureDeviceName)
            {
                row.Status = "Active";
            }

            _rows.Add(row);
        }

        DeviceList.ItemsSource = _rows;
    }

    private void OnEnableClicked(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DeviceRow selected)
        {
            return;
        }

        foreach (var row in _rows)
        {
            row.Status = "Idle";
        }

        selected.Status = "Active";
    }

    private void OnDisableClicked(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is DeviceRow selected)
        {
            selected.Status = "Idle";
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var settings = MeterSettings.Load();
        settings.SelectedCaptureDeviceName = _rows.FirstOrDefault(r => r.Status == "Active")?.DeviceName;
        settings.Save();
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();

    private sealed class DeviceRow : INotifyPropertyChanged
    {
        private string _status = "Idle";

        public string DeviceName { get; }
        public string Description { get; }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public DeviceRow(string deviceName, string description)
        {
            DeviceName = deviceName;
            Description = description;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
