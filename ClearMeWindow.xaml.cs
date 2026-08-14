using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace NexusProgrammer;

public partial class ClearMeWindow : Window
{
    private readonly Func<MemoryBufferOption, string, string, Task> _clearSingleBios;
    private readonly Func<MemoryBufferOption, MemoryBufferOption, string, string, Task> _clearDualBios;
    private readonly Func<IReadOnlyList<MemoryBufferOption>, Task<ClearMeCandidates>> _analyzeBios;
    private readonly AppSettings _settings;

    public ClearMeWindow(
        IEnumerable<MemoryBufferOption> memoryTabs,
        Func<MemoryBufferOption, string, string, Task> clearSingleBios,
        Func<MemoryBufferOption, MemoryBufferOption, string, string, Task> clearDualBios,
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
        Height = ClearMeTabs.SelectedIndex == 1 ? 265 : 235;
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
        IEnumerable<string> rankedCandidates,
        string root,
        string pattern)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = rankedCandidates.Where(File.Exists).ToList();
        foreach (var candidate in ranked)
        {
            comboBox.Items.Add(FilePathOption.FromPath(candidate));
            seen.Add(candidate);
        }

        if (ranked.Count > 0 && !string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).OrderBy(Path.GetFileName))
            {
                if (seen.Add(file))
                {
                    comboBox.Items.Add(FilePathOption.FromPath(file));
                }
            }
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
        LoadCandidates(MeRegionCombo, candidates.MeRegions, _settings.MeRegionRoot, "*.*");
        LoadCandidates(FitCombo, candidates.FitTools, _settings.FitRoot, "*.exe");
        LoadCandidates(DualMeRegionCombo, candidates.MeRegions, _settings.MeRegionRoot, "*.*");
        LoadCandidates(DualFitCombo, candidates.FitTools, _settings.FitRoot, "*.exe");
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
            var candidates = await _analyzeBios(memories);
            ClearComboItems(MeRegionCombo);
            ClearComboItems(FitCombo);
            ClearComboItems(DualMeRegionCombo);
            ClearComboItems(DualFitCombo);
            LoadCandidateLists(candidates);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Analyze BIOS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AnalyzeSingleButton.IsEnabled = true;
            AnalyzeDualButton.IsEnabled = true;
        }
    }

    private async void ClearMe_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryCombo.SelectedItem is not MemoryBufferOption memory)
        {
            MessageBox.Show(this, "Select a Memory tab first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var meRegion = SelectedPath(MeRegionCombo);
        var fit = SelectedPath(FitCombo);
        if (string.IsNullOrWhiteSpace(meRegion) || string.IsNullOrWhiteSpace(fit))
        {
            MessageBox.Show(this, "Select ME Region and FIT first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearMeButton.IsEnabled = false;
        try
        {
            await _clearSingleBios(memory, meRegion, fit);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Clear ME", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ClearMeButton.IsEnabled = true;
        }
    }

    private static string SelectedPath(System.Windows.Controls.ComboBox comboBox) =>
        comboBox.SelectedItem is FilePathOption option ? option.Path : comboBox.Text.Trim();

    private async void ClearDualMe_Click(object sender, RoutedEventArgs e)
    {
        if (DualMemory1Combo.SelectedItem is not MemoryBufferOption memory1 ||
            DualMemory2Combo.SelectedItem is not MemoryBufferOption memory2 ||
            ReferenceEquals(memory1, memory2))
        {
            MessageBox.Show(this, "Select two different Memory tabs first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var meRegion = SelectedPath(DualMeRegionCombo);
        var fit = SelectedPath(DualFitCombo);
        if (string.IsNullOrWhiteSpace(meRegion) || string.IsNullOrWhiteSpace(fit))
        {
            MessageBox.Show(this, "Select ME Region and FIT first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearDualMeButton.IsEnabled = false;
        try
        {
            await _clearDualBios(memory1, memory2, meRegion, fit);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Clear ME", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ClearDualMeButton.IsEnabled = true;
        }
    }
}

public sealed record MemoryBufferOption(string Label, byte[] Buffer, string SourceFileName);

public sealed record FilePathOption(string Name, string Path)
{
    public static FilePathOption FromPath(string path) => new(System.IO.Path.GetFileName(path), path);
}
