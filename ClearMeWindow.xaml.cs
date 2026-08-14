using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace NexusProgrammer;

public partial class ClearMeWindow : Window
{
    private readonly Func<MemoryBufferOption, string, string, Task> _clearSingleBios;

    public ClearMeWindow(
        IEnumerable<MemoryBufferOption> memoryTabs,
        Func<MemoryBufferOption, string, string, Task> clearSingleBios,
        AppSettings settings,
        ClearMeCandidates candidates,
        bool hasValidAnalysis)
    {
        InitializeComponent();
        _clearSingleBios = clearSingleBios;
        MemoryCombo.ItemsSource = memoryTabs.ToList();
        MemoryCombo.DisplayMemberPath = nameof(MemoryBufferOption.Label);
        MemoryCombo.SelectedIndex = MemoryCombo.Items.Count > 0 ? 0 : -1;
        MeRegionCombo.DisplayMemberPath = nameof(FilePathOption.Name);
        FitCombo.DisplayMemberPath = nameof(FilePathOption.Name);
        MemoryCombo.SelectionChanged += (_, _) => UpdateClearMeButton();
        MeRegionCombo.SelectionChanged += (_, _) => UpdateClearMeButton();
        FitCombo.SelectionChanged += (_, _) => UpdateClearMeButton();
        if (hasValidAnalysis)
        {
            LoadCandidates(MeRegionCombo, candidates.MeRegions, settings.MeRegionRoot, "*.*");
            LoadCandidates(FitCombo, candidates.FitTools, settings.FitRoot, "*.exe");
        }

        UpdateClearMeButton();
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
        foreach (var candidate in rankedCandidates.Where(File.Exists))
        {
            comboBox.Items.Add(FilePathOption.FromPath(candidate));
            seen.Add(candidate);
        }

        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
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
}

public sealed record MemoryBufferOption(string Label, byte[] Buffer, string SourceFileName);

public sealed record FilePathOption(string Name, string Path)
{
    public static FilePathOption FromPath(string path) => new(System.IO.Path.GetFileName(path), path);
}
