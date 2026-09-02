using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace NexusProgrammer;

public partial class ClearMeWindow : Window
{
    private readonly Func<MemoryBufferOption, IReadOnlyList<string>, IReadOnlyList<string>, bool, Action<string>, CancellationToken, Task> _clearSingleBios;
    private readonly Func<MemoryBufferOption, MemoryBufferOption, IReadOnlyList<string>, IReadOnlyList<string>, bool, Action<string>, CancellationToken, Task> _clearDualBios;
    private readonly Func<IReadOnlyList<MemoryBufferOption>, Task<ClearMeCandidates>> _analyzeBios;
    private readonly AppSettings _settings;

    public ClearMeWindow(
        IEnumerable<MemoryBufferOption> memoryTabs,
        Func<MemoryBufferOption, IReadOnlyList<string>, IReadOnlyList<string>, bool, Action<string>, CancellationToken, Task> clearSingleBios,
        Func<MemoryBufferOption, MemoryBufferOption, IReadOnlyList<string>, IReadOnlyList<string>, bool, Action<string>, CancellationToken, Task> clearDualBios,
        Func<IReadOnlyList<MemoryBufferOption>, Task<ClearMeCandidates>> analyzeBios,
        AppSettings settings,
        ClearMeCandidates candidates)
    {
        InitializeComponent();
        _clearSingleBios = clearSingleBios;
        _clearDualBios = clearDualBios;
        _analyzeBios = analyzeBios;
        _settings = settings;
        var memoryOptions = memoryTabs.ToList();
        MemoryCombo.ItemsSource = memoryOptions;
        MemoryCombo.DisplayMemberPath = nameof(MemoryBufferOption.Label);
        MemoryCombo.SelectedIndex = MemoryCombo.Items.Count > 0 ? 0 : -1;
        DualMemory1Combo.ItemsSource = memoryOptions;
        DualMemory1Combo.DisplayMemberPath = nameof(MemoryBufferOption.Label);
        DualMemory1Combo.SelectedIndex = memoryOptions.Count > 0 ? 0 : -1;
        DualMemory2Combo.ItemsSource = memoryOptions;
        DualMemory2Combo.DisplayMemberPath = nameof(MemoryBufferOption.Label);
        DualMemory2Combo.SelectedIndex = memoryOptions.Count > 1 ? 1 : -1;
        MeRegionCombo.DisplayMemberPath = nameof(FilePathOption.Name);
        FitCombo.DisplayMemberPath = nameof(FilePathOption.Name);
        DualMeRegionCombo.DisplayMemberPath = nameof(FilePathOption.Name);
        DualFitCombo.DisplayMemberPath = nameof(FilePathOption.Name);
        MemoryCombo.SelectionChanged += (_, _) => UpdateClearMeButton();
        MeRegionCombo.SelectionChanged += (_, _) => UpdateClearMeButton();
        FitCombo.SelectionChanged += (_, _) => UpdateClearMeButton();
        DualMemory1Combo.SelectionChanged += (_, _) => UpdateClearMeButton();
        DualMemory2Combo.SelectionChanged += (_, _) => UpdateClearMeButton();
        DualMeRegionCombo.SelectionChanged += (_, _) => UpdateClearMeButton();
        DualFitCombo.SelectionChanged += (_, _) => UpdateClearMeButton();
        LoadCandidateLists(candidates);

        ApplyTabHeight();
        UpdateClearMeButton();
    }

    private CancellationTokenSource? _clearMeCts;

    private void ClearMeTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ClearMeTabs))
        {
            return;
        }

        ApplyTabHeight();
    }

    private void ApplyTabHeight()
    {
        ClearMeTabs.Height = ClearMeTabs.SelectedIndex == 1 ? 235 : 205;
    }

    private void ClearMemory_Click(object sender, RoutedEventArgs e)
    {
        MemoryCombo.SelectedIndex = -1;
        MeRegionCombo.SelectedIndex = -1;
        FitCombo.SelectedIndex = -1;
        UpdateClearMeButton();
    }

    private void BrowseMeRegion_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(MeRegionCombo);
    }

    private void BrowseFit_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(FitCombo);
    }

    private void BrowseDualMeRegion_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(DualMeRegionCombo);
    }

    private void BrowseDualFit_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(DualFitCombo);
    }

    private void ClearDualMemory_Click(object sender, RoutedEventArgs e)
    {
        DualMemory1Combo.SelectedIndex = -1;
        DualMemory2Combo.SelectedIndex = -1;
        DualMeRegionCombo.SelectedIndex = -1;
        DualFitCombo.SelectedIndex = -1;
        UpdateClearMeButton();
    }

    private void BrowseInto(System.Windows.Controls.ComboBox comboBox)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!comboBox.Items.OfType<FilePathOption>().Any(item => item.Path.Equals(dialog.FileName, StringComparison.OrdinalIgnoreCase)))
        {
            comboBox.Items.Add(FilePathOption.FromPath(dialog.FileName));
        }

        comboBox.SelectedItem = comboBox.Items
            .OfType<FilePathOption>()
            .First(item => item.Path.Equals(dialog.FileName, StringComparison.OrdinalIgnoreCase));
        UpdateClearMeButton();
    }

    private static void LoadCandidates(
        System.Windows.Controls.ComboBox comboBox,
        IEnumerable<string> rankedCandidates)
    {
        var ranked = rankedCandidates.Where(File.Exists).ToList();
        foreach (var candidate in ranked)
        {
            comboBox.Items.Add(FilePathOption.FromPath(candidate));
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private void UpdateClearMeButton()
    {
        ClearMeButton.IsEnabled =
            MemoryCombo.SelectedItem is MemoryBufferOption &&
            MeRegionCombo.SelectedItem is FilePathOption &&
            FitCombo.SelectedItem is FilePathOption;
        ClearDualMeButton.IsEnabled =
            DualMemory1Combo.SelectedItem is MemoryBufferOption memory1 &&
            DualMemory2Combo.SelectedItem is MemoryBufferOption memory2 &&
            !ReferenceEquals(memory1, memory2) &&
            DualMeRegionCombo.SelectedItem is FilePathOption &&
            DualFitCombo.SelectedItem is FilePathOption;
    }

    private void LoadCandidateLists(ClearMeCandidates candidates)
    {
        LoadCandidates(MeRegionCombo, candidates.MeRegions);
        LoadCandidates(FitCombo, candidates.FitTools);
        LoadCandidates(DualMeRegionCombo, candidates.MeRegions);
        LoadCandidates(DualFitCombo, candidates.FitTools);
        UpdateClearMeButton();
    }

    private static void ClearComboItems(System.Windows.Controls.ComboBox comboBox)
    {
        comboBox.Items.Clear();
        comboBox.SelectedIndex = -1;
    }

    private async void AnalyzeSingle_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryCombo.SelectedItem is not MemoryBufferOption memory)
        {
            MessageBox.Show(this, "Select a Memory tab first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await AnalyzeAndLoadCandidatesAsync([memory]);
    }

    private async void AnalyzeDual_Click(object sender, RoutedEventArgs e)
    {
        if (DualMemory1Combo.SelectedItem is not MemoryBufferOption memory1 ||
            DualMemory2Combo.SelectedItem is not MemoryBufferOption memory2 ||
            ReferenceEquals(memory1, memory2))
        {
            MessageBox.Show(this, "Select two different Memory tabs first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await AnalyzeAndLoadCandidatesAsync([memory1, memory2]);
    }

    private async Task AnalyzeAndLoadCandidatesAsync(IReadOnlyList<MemoryBufferOption> memories)
    {
        AnalyzeSingleButton.IsEnabled = false;
        AnalyzeDualButton.IsEnabled = false;
        try
        {
            AnalysisTextBox.Text = "Analyzing BIOS...";
            var candidates = await _analyzeBios(memories);
            AnalysisTextBox.Text = candidates.AnalysisSummary;
            ClearComboItems(MeRegionCombo);
            ClearComboItems(FitCombo);
            ClearComboItems(DualMeRegionCombo);
            ClearComboItems(DualFitCombo);
            LoadCandidateLists(candidates);
        }
        catch (Exception ex)
        {
            AnalysisTextBox.Text = $"Analyze failed{Environment.NewLine}{ex.Message}";
            MessageBox.Show(this, ex.Message, "Analyze BIOS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AnalyzeSingleButton.IsEnabled = true;
            AnalyzeDualButton.IsEnabled = true;
        }
    }

    private void CopyAnalysisLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(AnalysisTextBox.Text))
        {
            Clipboard.SetText(AnalysisTextBox.Text);
        }
    }

    private void ClearAnalysisLog_Click(object sender, RoutedEventArgs e)
    {
        AnalysisTextBox.Clear();
    }

    private void AppendClearMeLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            if (AnalysisTextBox.Text.Length > 0)
            {
                AnalysisTextBox.AppendText(Environment.NewLine);
            }

            AnalysisTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}");
            AnalysisTextBox.ScrollToEnd();
        });
    }

    private async void ClearMe_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryCombo.SelectedItem is not MemoryBufferOption memory)
        {
            MessageBox.Show(this, "Select a Memory tab first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var meRegions = SelectedMeRegionPaths(MeRegionCombo);
        var fitCandidates = SelectedFitPaths(FitCombo);
        if (meRegions.Count == 0 || fitCandidates.Count == 0)
        {
            MessageBox.Show(this, "Select ME Region and FIT first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetClearMeRunning(true);
        _clearMeCts = new CancellationTokenSource();
        try
        {
            await _clearSingleBios(memory, meRegions, fitCandidates, ManualFallbackCheckBox.IsChecked == true, AppendClearMeLog, _clearMeCts.Token);
            Close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            _clearMeCts?.Dispose();
            _clearMeCts = null;
            SetClearMeRunning(false);
        }
    }

    private static string SelectedPath(System.Windows.Controls.ComboBox comboBox) =>
        comboBox.SelectedItem is FilePathOption option ? option.Path : comboBox.Text.Trim();

    private static IReadOnlyList<string> SelectedMeRegionPaths(System.Windows.Controls.ComboBox comboBox)
    {
        var selected = SelectedPath(comboBox);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return [];
        }

        var fallbacks = comboBox.Items
            .OfType<FilePathOption>()
            .Select(option => option.Path)
            .Where(path => !path.Equals(selected, StringComparison.OrdinalIgnoreCase));

        return [selected, .. fallbacks];
    }

    private static IReadOnlyList<string> SelectedFitPaths(System.Windows.Controls.ComboBox comboBox)
    {
        var selected = SelectedPath(comboBox);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return [];
        }

        var selectedVersion = MeaAnalyzer.VersionParts(selected);
        var selectedRank = FitVersionRank(selected);
        var fallbacks = comboBox.Items
            .OfType<FilePathOption>()
            .Select(option => option.Path)
            .Where(path => !path.Equals(selected, StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Version = FitVersionParts(path), Rank = FitVersionRank(path) })
            .Where(item => selectedVersion.Major > 0 &&
                           item.Version.Major == selectedVersion.Major &&
                           item.Rank > selectedRank)
            .OrderBy(item => item.Rank - selectedRank)
            .Select(item => item.Path);

        return [selected, .. fallbacks];
    }

    private static (int Major, int Minor, int Hotfix, int Build) FitVersionParts(string path)
    {
        var fileVersion = MeaAnalyzer.VersionParts(System.IO.Path.GetFileName(path));
        return fileVersion.Major > 0 ? fileVersion : MeaAnalyzer.VersionParts(path);
    }

    private static long FitVersionRank(string path)
    {
        var version = FitVersionParts(path);
        return version.Major * 1_000_000_000L + version.Minor * 1_000_000L + version.Hotfix * 10_000L + version.Build;
    }

    private async void ClearDualMe_Click(object sender, RoutedEventArgs e)
    {
        if (DualMemory1Combo.SelectedItem is not MemoryBufferOption memory1 ||
            DualMemory2Combo.SelectedItem is not MemoryBufferOption memory2 ||
            ReferenceEquals(memory1, memory2))
        {
            MessageBox.Show(this, "Select two different Memory tabs first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var meRegions = SelectedMeRegionPaths(DualMeRegionCombo);
        var fitCandidates = SelectedFitPaths(DualFitCombo);
        if (meRegions.Count == 0 || fitCandidates.Count == 0)
        {
            MessageBox.Show(this, "Select ME Region and FIT first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetClearMeRunning(true);
        _clearMeCts = new CancellationTokenSource();
        try
        {
            await _clearDualBios(memory1, memory2, meRegions, fitCandidates, DualManualFallbackCheckBox.IsChecked == true, AppendClearMeLog, _clearMeCts.Token);
            Close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            _clearMeCts?.Dispose();
            _clearMeCts = null;
            SetClearMeRunning(false);
        }
    }

    private void StopClearMe_Click(object sender, RoutedEventArgs e)
    {
        StopClearMeButton.IsEnabled = false;
        StopDualClearMeButton.IsEnabled = false;
        _clearMeCts?.Cancel();
    }

    private void SetClearMeRunning(bool running)
    {
        AnalyzeSingleButton.IsEnabled = !running;
        AnalyzeDualButton.IsEnabled = !running;
        StopClearMeButton.IsEnabled = running;
        StopDualClearMeButton.IsEnabled = running;
        if (running)
        {
            ClearMeButton.IsEnabled = false;
            ClearDualMeButton.IsEnabled = false;
        }
        else
        {
            UpdateClearMeButton();
        }
    }
}

public sealed record MemoryBufferOption(string Label, byte[] Buffer, string SourceFileName);

public sealed record FilePathOption(string Name, string Path)
{
    public static FilePathOption FromPath(string path) => new(System.IO.Path.GetFileName(path), path);
}
