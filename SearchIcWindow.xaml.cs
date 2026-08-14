using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace NexusProgrammer;

public partial class SearchIcWindow : Window
{
    private List<IcCandidate> _allCandidates;
    private readonly ObservableCollection<IcCandidate> _visibleCandidates = [];

    public SearchIcWindow(IEnumerable<IcCandidate> candidates, string? jedecId)
    {
        InitializeComponent();
        _allCandidates = candidates.ToList();
        IcGrid.ItemsSource = _visibleCandidates;

        if (!string.IsNullOrWhiteSpace(jedecId))
        {
            FilterBox.Text = jedecId;
            ExampleText.Text = $"JEDEC ID: {jedecId}";
        }

        RefreshFilter();
        if (_visibleCandidates.Count > 0)
        {
            IcGrid.SelectedIndex = 0;
        }

        UpdateUserActionButtons();
    }

    public IcCandidate? SelectedCandidate { get; private set; }

    public bool CatalogChanged { get; private set; }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshFilter();

    private void IcGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => SelectCurrent();

    private void IcGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateUserActionButtons();

    private void Select_Click(object sender, RoutedEventArgs e) => SelectCurrent();

    private void AddSpiNor_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddIcWindow(TryGetJedecFilter())
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.Candidate is null)
        {
            return;
        }

        IcCatalogLoader.SaveUserCandidate(dialog.Candidate);
        CatalogChanged = true;
        _allCandidates = IcCatalogLoader.LoadSpiCatalog();
        FilterBox.Text = dialog.Candidate.Device;
        RefreshFilter();
        IcGrid.SelectedItem = _visibleCandidates.FirstOrDefault(candidate =>
            candidate.Device.Equals(dialog.Candidate.Device, StringComparison.OrdinalIgnoreCase));
        UpdateUserActionButtons();
    }

    private void EditIc_Click(object sender, RoutedEventArgs e)
    {
        if (IcGrid.SelectedItem is not IcCandidate candidate || !candidate.IsUserAdded)
        {
            return;
        }

        var dialog = new AddIcWindow(candidate.JedecId, candidate)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.Candidate is null)
        {
            return;
        }

        var userCandidates = _allCandidates
            .Where(item => item.IsUserAdded && !ReferenceEquals(item, candidate))
            .Append(dialog.Candidate)
            .ToList();
        IcCatalogLoader.SaveUserCatalog(userCandidates);
        CatalogChanged = true;
        _allCandidates = IcCatalogLoader.LoadSpiCatalog();
        FilterBox.Text = dialog.Candidate.Device;
        RefreshFilter();
        IcGrid.SelectedItem = _visibleCandidates.FirstOrDefault(item =>
            item.IsUserAdded &&
            item.Device.Equals(dialog.Candidate.Device, StringComparison.OrdinalIgnoreCase) &&
            item.JedecId.Equals(dialog.Candidate.JedecId, StringComparison.OrdinalIgnoreCase));
        UpdateUserActionButtons();
    }

    private void DeleteIc_Click(object sender, RoutedEventArgs e)
    {
        if (IcGrid.SelectedItem is not IcCandidate candidate || !candidate.IsUserAdded)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"Delete user IC {candidate.Device}?",
                "Delete IC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var userCandidates = _allCandidates
            .Where(item => item.IsUserAdded && !ReferenceEquals(item, candidate))
            .ToList();
        IcCatalogLoader.SaveUserCatalog(userCandidates);
        CatalogChanged = true;
        _allCandidates = IcCatalogLoader.LoadSpiCatalog();
        RefreshFilter();
        if (_visibleCandidates.Count > 0)
        {
            IcGrid.SelectedIndex = 0;
        }

        UpdateUserActionButtons();
    }

    private string? TryGetJedecFilter()
    {
        var hex = new string(FilterBox.Text.Where(Uri.IsHexDigit).ToArray());
        return hex.Length >= 6 && hex.Length % 2 == 0 ? FilterBox.Text : null;
    }

    private void SelectCurrent()
    {
        if (IcGrid.SelectedItem is not IcCandidate candidate)
        {
            return;
        }

        SelectedCandidate = candidate;
        DialogResult = true;
        Close();
    }

    private void RefreshFilter()
    {
        var filter = FilterBox.Text.Trim();
        var terms = filter.Split([' ', '+', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var filtered = _allCandidates.Where(candidate => Matches(candidate, terms)).ToList();
        _visibleCandidates.Clear();
        foreach (var candidate in filtered)
        {
            _visibleCandidates.Add(candidate);
        }

        FoundText.Text = $"Found: {_visibleCandidates.Count} Chips";
        UpdateUserActionButtons();
    }

    private void UpdateUserActionButtons()
    {
        var isUserAdded = IcGrid.SelectedItem is IcCandidate candidate && candidate.IsUserAdded;
        EditIcButton.IsEnabled = isUserAdded;
        DeleteIcButton.IsEnabled = isUserAdded;
    }

    private static bool Matches(IcCandidate candidate, string[] terms)
    {
        if (terms.Length == 0)
        {
            return true;
        }

        var haystack = $"{candidate.Device} {candidate.Volts} {candidate.Size} {candidate.Page} {candidate.Manuf} {candidate.Type} {candidate.JedecId}".ToUpperInvariant();
        return terms.All(term => WildcardMatch(haystack, term.ToUpperInvariant()));
    }

    private static bool WildcardMatch(string haystack, string term)
    {
        if (!term.Contains('*'))
        {
            return haystack.Contains(term);
        }

        var pieces = term.Split('*', StringSplitOptions.RemoveEmptyEntries);
        var index = 0;
        foreach (var piece in pieces)
        {
            var found = haystack.IndexOf(piece, index, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            index = found + piece.Length;
        }

        return true;
    }
}
