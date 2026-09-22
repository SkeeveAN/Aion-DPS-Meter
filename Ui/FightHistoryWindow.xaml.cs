using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// Browses the local fight history (History/FightStore): search by target or participant, see who
/// did what, and load a past fight back into the meter (see MainWindow.EnterHistoryMode). The
/// store is the caller's - opened once by MainWindow and shared with its recorder.
/// </summary>
public partial class FightHistoryWindow : Window
{
    private readonly FightStore _store;

    public FightHistoryWindow(FightStore store)
    {
        InitializeComponent();
        _store = store;
        Refresh();
    }

    /// <summary>Raised when the user asks to load a fight; MainWindow decides what that means.</summary>
    public event Action<FightDetail>? LoadRequested;

    public void Refresh()
    {
        List<FightSummary> fights = _store.Query(SearchBox.Text);
        FightsGrid.ItemsSource = fights;
        CountText.Text = fights.Count.ToString();
        ParticipantsGrid.ItemsSource = null;
        LoadButton.IsEnabled = false;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnFightSelected(object sender, SelectionChangedEventArgs e)
    {
        if (FightsGrid.SelectedItem is not FightSummary summary)
        {
            ParticipantsGrid.ItemsSource = null;
            LoadButton.IsEnabled = false;
            return;
        }

        FightDetail? detail = _store.Load(summary.Id);
        ParticipantsGrid.ItemsSource = detail?.Participants;
        LoadButton.IsEnabled = detail is not null;
    }

    private void OnFightDoubleClick(object sender, MouseButtonEventArgs e) => LoadSelected();

    private void OnLoadClicked(object sender, RoutedEventArgs e) => LoadSelected();

    private void LoadSelected()
    {
        if (FightsGrid.SelectedItem is FightSummary summary && _store.Load(summary.Id) is FightDetail detail)
        {
            LoadRequested?.Invoke(detail);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
