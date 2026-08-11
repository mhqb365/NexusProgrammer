using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;

namespace NexusProgrammer;

public partial class MainWindow : Window
{
    private const string AppName = "Nexus Programmer";
    private const string ProjectUrl = "https://github.com/mhqb365/NexusProgrammer";
    private const int MaxHexPreviewRows = 4096;
    private const int BytesPerHexRow = 16;
    private const int SearchHitContextBytes = 16;
    private const string MoonIconPath = "M12 3a6 6 0 0 0 9 9a9 9 0 1 1-9-9";
    private const string SunIconPath = "M12 8a4 4 0 1 0 0 8a4 4 0 0 0 0-8 M12 2v2 M12 20v2 M4.93 4.93l1.41 1.41 M17.66 17.66l1.41 1.41 M2 12h2 M20 12h2 M4.93 19.07l1.41-1.41 M17.66 6.34l1.41-1.41";
    private static readonly byte[] XgproMetadataMarker =
    [
        0x2D, 0x43, 0x6F, 0x6E, 0x66, 0x69, 0x67, 0x75,
        0x72, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x2D, 0x00
    ];
    private readonly ObservableCollection<HexRow> _rows = [];
    private readonly ObservableCollection<SearchHit> _searchHits = [];
    private readonly List<ChipProfile> _chips = [];

    private List<IcCandidate> _icCatalog = [];
    private readonly DispatcherTimer _programmerMonitorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IChipProgrammer _programmer = new MockProgrammer();
    private string _activeProgrammerKey = "none";
    private byte[] _buffer = [];
    private int _previewStartOffset;
    private int _currentOffset;
    private bool _isBusy;
    private bool _isApplyingDetectedChip;
    private bool _isSearching;
    private bool _updatingHexScrollBar;
    private ReplaceDialog? _replaceDialog;

    public MainWindow()
    {
        InitializeComponent();
        _icCatalog = IcCatalogLoader.LoadSpiCatalog();
        _chips.AddRange(_icCatalog.Select(x => x.Profile));
        if (_chips.Count == 0)
        {
            throw new InvalidOperationException("No SPI IC catalog entries found.");
        }
        SearchHitsGrid.ItemsSource = _searchHits;
        HexEditor.SetBuffer(_buffer, OnHexCellChanged);
        UpdateHexScrollBar();
        Title = $"{AppName} v{AppVersion}";
        AppendLog($"{AppName} v{AppVersion}");

        _isApplyingDetectedChip = true;
        try
        {
            LoadControls();
        }
        finally
        {
            _isApplyingDetectedChip = false;
        }

        ResizeBuffer(_chips[0].SizeBytes, fill: 0xFF);
        UpdateDeviceInfo(_chips[0]);
        UpdateProgrammerControls();

        _programmerMonitorTimer.Tick += ProgrammerMonitorTimer_Tick;
        Loaded += MainWindow_Loaded;
    }

    private void LoadControls()
    {
        ChipCombo.ItemsSource = _chips;
        ChipCombo.DisplayMemberPath = nameof(ChipProfile.Name);
        ChipCombo.SelectedIndex = 0;

        SizeCombo.ItemsSource = new[]
        {
            new SizeOption("256 B", 256),
            new SizeOption("4 KB", 4096),
            new SizeOption("32 KB", 32768),
            new SizeOption("1 MB", 1024 * 1024),
            new SizeOption("2 MB", 2 * 1024 * 1024),
            new SizeOption("4 MB", 4 * 1024 * 1024),
            new SizeOption("8 MB", 8 * 1024 * 1024),
            new SizeOption("16 MB", 16 * 1024 * 1024)
        };
        SizeCombo.DisplayMemberPath = nameof(SizeOption.Label);

        PageCombo.ItemsSource = new[] { "8", "16", "32", "64", "128", "256" };
        CommandCombo.ItemsSource = new[] { "25xx", "24xx", "93xx", "Custom" };
    }

    private void ChipCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ChipCombo.SelectedItem is not ChipProfile chip)
        {
            return;
        }

        SelectSize(chip.SizeBytes);
        PageCombo.SelectedItem = chip.PageSize.ToString();
        CommandCombo.SelectedItem = chip.CommandSet;
        ResizeBuffer(chip.SizeBytes, fill: 0xFF);
        UpdateDeviceInfo(chip);
        if (!_isApplyingDetectedChip)
        {
            AppendLog($"Selected {chip.Name}: {chip.Protocol}, {FormatBytes(chip.SizeBytes)}, page {chip.PageSize}");
        }
    }

    private void SizeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SizeCombo.SelectedItem is SizeOption size && _buffer.Length != size.Bytes)
        {
            ResizeBuffer(size.Bytes, fill: 0xFF);
            AppendLog($"Buffer resized to {FormatBytes(size.Bytes)}");
        }
    }

    private void SelectSize(int bytes)
    {
        foreach (var item in SizeCombo.Items.OfType<SizeOption>())
        {
            if (item.Bytes == bytes)
            {
                SizeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void ResizeBuffer(int size, byte fill)
    {
        _buffer = new byte[size];
        if (fill != 0)
        {
            Array.Fill(_buffer, fill);
        }

        RebuildRows();
        UpdateStatus();
    }

    private void RebuildRows(int startOffset = 0)
    {
        _rows.Clear();
        if (_buffer.Length == 0)
        {
            _previewStartOffset = 0;
            HexEditor.SetBuffer(_buffer, OnHexCellChanged);
            UpdateHexScrollBar();
            return;
        }

        _previewStartOffset = AlignOffset(Math.Clamp(startOffset, 0, _buffer.Length - 1));
        var maxPreviewBytes = MaxHexPreviewRows * BytesPerHexRow;
        var endOffset = Math.Min(_buffer.Length, _previewStartOffset + maxPreviewBytes);
        for (var offset = _previewStartOffset; offset < endOffset; offset += BytesPerHexRow)
        {
            _rows.Add(new HexRow(_buffer, offset, OnHexCellChanged));
        }

        HexEditor.SetBuffer(_buffer, OnHexCellChanged);
        UpdateHexScrollBar();
    }

    private void OnHexCellChanged(int offset, byte value)
    {
        if ((uint)offset < _buffer.Length)
        {
            _buffer[offset] = value;
            var rowIndex = (offset - _previewStartOffset) / BytesPerHexRow;
            if ((uint)rowIndex < _rows.Count)
            {
                _rows[rowIndex].RefreshAscii();
            }
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        if (!await CheckForUpdatesOnStartupAsync())
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
        await ProbeProgrammerAsync(logWhenChanged: true);

        _programmerMonitorTimer.Start();
    }

    private async Task<bool> CheckForUpdatesOnStartupAsync()
    {
        OperationStatusText.Text = "Checking update";
        OperationProgress.IsIndeterminate = true;
        try
        {
            var result = await UpdateService.CheckLatestReleaseAsync();
            if (result.Status != UpdateCheckStatus.UpdateAvailable || result.Release is null)
            {
                return true;
            }

            var updateNow = MessageBox.Show(
                this,
                $"A new version is available: {result.DisplayLatestVersion}\nCurrent version: {UpdateService.DisplayCurrentVersion}\n\nChange log:\n{result.DisplayChangeLog}\n\nUpdate now?",
                "Update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information) == MessageBoxResult.Yes;
            if (!updateNow)
            {
                AppendLog($"Update skipped: {result.DisplayLatestVersion}");
                return true;
            }

            using var cts = new CancellationTokenSource();
            OperationProgress.IsIndeterminate = false;
            OperationProgress.Value = 0;
            var progress = new Progress<UpdateProgressInfo>(info =>
            {
                OperationProgress.IsIndeterminate = info.State != UpdateProgressState.Downloading;
                OperationProgress.Value = Math.Clamp(info.Percentage, 0, 100);
                OperationStatusText.Text = info.State switch
                {
                    UpdateProgressState.Downloading => "Downloading update",
                    UpdateProgressState.Extracting => "Extracting update",
                    UpdateProgressState.Preparing => "Preparing update",
                    _ => "Updating"
                };
            });

            AppendLog($"Downloading update {result.DisplayLatestVersion}");
            var update = await UpdateService.DownloadAndPrepareUpdateAsync(result.Release, progress, cts.Token);
            AppendLog("Installing update");
            UpdateService.InstallPreparedUpdate(update);
            return false;
        }
        catch (Exception ex)
        {
            AppendLog($"Update check skipped: {ex.Message}");
            return true;
        }
        finally
        {
            OperationProgress.IsIndeterminate = false;
            OperationProgress.Value = 0;
            OperationStatusText.Text = "Ready";
        }
    }

    private async void ProgrammerMonitorTimer_Tick(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        _programmerMonitorTimer.Stop();
        try
        {
            await ProbeProgrammerAsync(logWhenChanged: true);
        }
        finally
        {
            _programmerMonitorTimer.Start();
        }
    }

    private async Task ProbeProgrammerAsync(bool logWhenChanged)
    {
        await Task.Yield();
        var t48Detected = T48SDKProgrammer.CanOpenDevice();
        var ch347Detected = Ch347NativeProgrammer.IsAvailable && Ch347NativeProgrammer.CanOpenDevice();
        var chDetected = ChNativeProgrammer.IsAvailable && ChNativeProgrammer.CanOpenDevice();
        ApplyProgrammerDetection(t48Detected, ch347Detected, chDetected, logWhenChanged, forceLog: false);
    }

    private void ApplyProgrammerDetection(bool t48Detected, bool ch347Detected, bool chDetected, bool logWhenChanged, bool forceLog)
    {
        if (t48Detected)
        {
            var changed = _activeProgrammerKey != "t48";
            _programmer = new T48SDKProgrammer();
            _activeProgrammerKey = "t48";
            HardwareStatusText.Text = "XGecu T48 connected";
            UpdateProgrammerControls();
            if (forceLog || changed && logWhenChanged)
            {
                AppendLog("XGecu T48 connected. Active backend: XGecu T48 SDK");
            }
            return;
        }

        if (ch347Detected)
        {
            var changed = _activeProgrammerKey != "ch347";
            _programmer = new Ch347NativeProgrammer();
            _activeProgrammerKey = "ch347";
            HardwareStatusText.Text = "CH347 connected";
            UpdateProgrammerControls();
            if (forceLog || changed && logWhenChanged)
            {
                AppendLog("CH347 connected. Active backend: CH347 native DLL");
            }
            return;
        }

        if (chDetected)
        {
            var changed = _activeProgrammerKey != "ch341";
            _programmer = new ChNativeProgrammer();
            _activeProgrammerKey = "ch341";
            HardwareStatusText.Text = "CH341 connected";
            UpdateProgrammerControls();
            if (forceLog || changed && logWhenChanged)
            {
                AppendLog("CH341 connected. Active backend: CH341 native DLL");
            }
            return;
        }

        var wasConnected = _activeProgrammerKey != "none";
        _programmer = new MockProgrammer();
        _activeProgrammerKey = "none";
        HardwareStatusText.Text = "Programmer disconnected";
        UpdateProgrammerControls();
        if (forceLog || wasConnected && logWhenChanged)
        {
            AppendLog("Programmer disconnected");
        }
    }

    private async void ReadId_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProgrammerAvailable("Detect IC"))
        {
            return;
        }

        await DetectIcAsync(logLifecycle: true, autoApplySingle: true, openCatalogOnMiss: true);
    }

    private Task DetectIcAsync(bool logLifecycle, bool autoApplySingle, bool openCatalogOnMiss) =>
        RunOperationAsync("Read ID", async progress =>
        {
            var chip = CurrentChip();
            AppendLog($"Detect request: selected {chip.Name}, voltage profile {chip.Volts}");
            var id = await _programmer.ReadIdAsync(chip, progress);
            AppendLog($"IC ID: {BitConverter.ToString(id).Replace("-", " ")}");
            ShowChipSelectionForId(id, autoApplySingle, openCatalogOnMiss);
            await RefreshT48DetectedVoltageProfileAsync(chip, id, progress);
        }, logLifecycle: logLifecycle);

    private async Task RefreshT48DetectedVoltageProfileAsync(ChipProfile probeChip, byte[] detectedId, IProgress<int> progress)
    {
        if (_programmer is not T48SDKProgrammer)
        {
            return;
        }

        var detectedChip = CurrentChip();
        if (detectedChip.Name == probeChip.Name || SameVoltageProfile(detectedChip, probeChip) || !CurrentChipMatchesId(detectedChip, detectedId))
        {
            return;
        }

        AppendLog($"Detected profile changed to {detectedChip.Name}, voltage profile {detectedChip.Volts}. Re-applying T48 voltage profile.");
        var confirmedId = await _programmer.ReadIdAsync(detectedChip, progress);
        AppendLog($"Confirmed IC ID with {detectedChip.Volts} profile: {BitConverter.ToString(confirmedId).Replace("-", " ")}");
    }

    private bool HasProgrammer => _programmer is not MockProgrammer;

    private void UpdateProgrammerControls()
    {
        var enabled = HasProgrammer;
        ReadIdButton.IsEnabled = enabled;
        ReadIdMenuItem.IsEnabled = enabled;
        ReadButton.IsEnabled = enabled;
        ReadChipMenuItem.IsEnabled = enabled;
        WriteButton.IsEnabled = enabled;
        WriteChipMenuItem.IsEnabled = enabled;
        VerifyButton.IsEnabled = enabled;
        VerifyChipMenuItem.IsEnabled = enabled;
        EraseButton.IsEnabled = enabled;
        EraseChipMenuItem.IsEnabled = enabled;
        ReadVerifyScriptMenuItem.IsEnabled = enabled;
        EraseWriteVerifyScriptMenuItem.IsEnabled = enabled;
    }

    private bool EnsureProgrammerAvailable(string operationName)
    {
        if (HasProgrammer)
        {
            return true;
        }

        AppendLog($"{operationName} skipped: no programmer found");
        return false;
    }

    private void SearchIc_Click(object sender, RoutedEventArgs e)
    {
        ShowChipSelection(_icCatalog, "Search IC", null);
    }

    private void AddIc_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddIcWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Candidate is null)
        {
            return;
        }

        AddUserIc(dialog.Candidate);
    }

    private void ReloadIcCatalog()
    {
        _icCatalog = IcCatalogLoader.LoadSpiCatalog();
        foreach (var profile in _icCatalog.Select(x => x.Profile))
        {
            if (!_chips.Any(x => x.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _chips.Add(profile);
            }
        }
    }

    private async void ReadChip_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProgrammerAvailable("Read chip"))
        {
            return;
        }

        var chip = CurrentChip();
        if (!ConfirmVoltageAdapterIfNeeded(chip, "read"))
        {
            return;
        }

        await RunOperationAsync("Read chip", _buffer.Length, async progress =>
        {
            var startAddress = ParseStartAddress();
            AppendLog($"Read request: {FormatBytes(_buffer.Length)} from 0x{startAddress:X6}");
            _buffer = await _programmer.ReadAsync(chip, startAddress, _buffer.Length, progress);
            RebuildRows();
            UpdateStatus();
        });
    }

    private async void WriteChip_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProgrammerAvailable("Write chip"))
        {
            return;
        }

        var chip = CurrentChip();
        if (!ConfirmVoltageAdapterIfNeeded(chip, "write"))
        {
            return;
        }

        await RunOperationAsync("Write chip", _buffer.Length, async progress =>
        {
            var startAddress = ParseStartAddress();
            var skipBlankPages = SkipBlankPagesCheckBox.IsChecked == true;
            AppendLog($"Write request: {FormatBytes(_buffer.Length)} to 0x{startAddress:X6}{(skipBlankPages ? " (skip FF pages)" : "")}, voltage profile {chip.Volts}");
            await UnprotectIfRequestedAsync(chip, progress);
            await _programmer.WriteAsync(chip, startAddress, _buffer, progress, skipBlankPages);
        });
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProgrammerAvailable("Verify"))
        {
            return;
        }

        var chip = CurrentChip();
        if (!ConfirmVoltageAdapterIfNeeded(chip, "verify"))
        {
            return;
        }

        await RunOperationAsync("Verify", _buffer.Length, async progress =>
        {
            var startAddress = ParseStartAddress();
            AppendLog($"Verify request: {FormatBytes(_buffer.Length)} at 0x{startAddress:X6}");
            var ok = await _programmer.VerifyAsync(chip, startAddress, _buffer, progress);
            AppendLog(ok ? "Verify OK" : "Verify failed");
        });
    }

    private async void Erase_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProgrammerAvailable("Erase chip"))
        {
            return;
        }

        var chip = CurrentChip();
        if (!ConfirmVoltageAdapterIfNeeded(chip, "erase"))
        {
            return;
        }

        if (MessageBox.Show("Erase selected IC?", "Confirm erase", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunOperationAsync("Erase chip", chip.SizeBytes, async progress =>
        {
            await UnprotectIfRequestedAsync(chip, progress);
            await _programmer.EraseAsync(chip, progress);
        });
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        AppendLog("Stop requested. Current operation will finish its current block");
    }

    private async void HexSearchPrevious_Click(object sender, RoutedEventArgs e) => await RunSearchAsync(forward: false);

    private async void HexSearchNext_Click(object sender, RoutedEventArgs e) => await RunSearchAsync(forward: true);

    private async void HexSearchAll_Click(object sender, RoutedEventArgs e) => await RunSearchAllAsync();

    private async void WindowsKeySearch_Click(object sender, RoutedEventArgs e) => await RunWindowsKeySearchAsync();

    private async void HexReplace_Click(object sender, RoutedEventArgs e) => await RunReplaceDialogAsync();

    private void HexSearchClear_Click(object sender, RoutedEventArgs e)
    {
        HexSearchBox.Clear();
        HexSearchBox.Focus();
    }

    private void HexSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        HexSearchClearButton.Visibility = string.IsNullOrEmpty(HexSearchBox.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SearchHitsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SearchHitsGrid.SelectedItem is SearchHit { Offset: >= 0, Length: > 0 } hit)
        {
            ShowSearchResult(hit.Offset, hit.Length);
        }
    }

    private async void HexSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await RunSearchAsync(forward: true);
    }

    private void HexEditor_ScrollChanged(object sender, EventArgs e) => UpdateHexScrollBar();

    private void HexScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingHexScrollBar)
        {
            return;
        }

        HexEditor.SetFirstLine((int)e.NewValue);
    }

    private void UpdateHexScrollBar()
    {
        _updatingHexScrollBar = true;
        try
        {
            HexScrollBar.Maximum = Math.Max(0, HexEditor.TotalLines - HexEditor.VisibleLines);
            HexScrollBar.ViewportSize = HexEditor.VisibleLines;
            HexScrollBar.LargeChange = Math.Max(1, HexEditor.VisibleLines - 1);
            HexScrollBar.SmallChange = 1;
            HexScrollBar.Value = Math.Min(HexScrollBar.Maximum, HexEditor.FirstLine);
        }
        finally
        {
            _updatingHexScrollBar = false;
        }
    }

    private async Task RunSearchAsync(bool forward)
    {
        if (_isSearching)
        {
            AppendLog("Search is already running");
            return;
        }

        var query = HexSearchBox.Text?.Trim() ?? string.Empty;
        var mode = CurrentHexSearchMode();
        _isSearching = true;
        SetSearchControlsEnabled(false);
        try
        {
            await SearchHexViewAsync(mode, query, forward);
        }
        finally
        {
            SetSearchControlsEnabled(true);
            _isSearching = false;
        }
    }

    private async Task RunSearchAllAsync()
    {
        if (_isSearching)
        {
            AppendLog("Search is already running");
            return;
        }

        var query = HexSearchBox.Text?.Trim() ?? string.Empty;
        var mode = CurrentHexSearchMode();
        _isSearching = true;
        SetSearchControlsEnabled(false);
        try
        {
            await SearchAllHexViewAsync(mode, query);
        }
        finally
        {
            SetSearchControlsEnabled(true);
            _isSearching = false;
        }
    }

    private async Task RunWindowsKeySearchAsync()
    {
        if (_isSearching)
        {
            AppendLog("Search is already running");
            return;
        }

        _isSearching = true;
        SetSearchControlsEnabled(false);
        try
        {
            await SearchWindowsKeyMarkerAsync();
        }
        finally
        {
            SetSearchControlsEnabled(true);
            _isSearching = false;
        }
    }

    private Task RunReplaceDialogAsync()
    {
        if (_isSearching)
        {
            AppendLog("Search is already running");
            return Task.CompletedTask;
        }

        var mode = CurrentHexSearchMode();
        if (string.Equals(mode, "Offset", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("Replace supports Hex and Text modes only");
            return Task.CompletedTask;
        }

        if (_replaceDialog is not null)
        {
            _replaceDialog.Activate();
            return Task.CompletedTask;
        }

        var dialog = new ReplaceDialog(mode, RunReplaceFromDialogAsync)
        {
            Owner = this
        };
        _replaceDialog = dialog;
        dialog.Closed += (_, _) => _replaceDialog = null;
        dialog.Show();
        return Task.CompletedTask;
    }

    private async Task RunReplaceFromDialogAsync(string replacementText, bool replaceAll)
    {
        if (_isSearching)
        {
            AppendLog("Search is already running");
            return;
        }

        _isSearching = true;
        SetSearchControlsEnabled(false);
        try
        {
            await ReplaceHexViewAsync(replaceAll, replacementText);
        }
        finally
        {
            SetSearchControlsEnabled(true);
            _isSearching = false;
        }
    }

    private void SetSearchControlsEnabled(bool enabled)
    {
        HexSearchBox.IsEnabled = enabled;
        HexSearchModeCombo.IsEnabled = enabled;
        HexSearchPreviousButton.IsEnabled = enabled;
        HexSearchAllButton.IsEnabled = enabled;
        HexSearchNextButton.IsEnabled = enabled;
        HexReplaceButton.IsEnabled = enabled;
        WindowsKeySearchButton.IsEnabled = enabled;
    }

    private string CurrentHexSearchMode() => HexSearchModeCombo.SelectedItem as string ?? "Offset";

    private async Task SearchHexViewAsync(string mode, string query, bool forward)
    {
        try
        {
            if (string.IsNullOrEmpty(query))
            {
                return;
            }

            var result = await TryResolveSearchOffsetAsync(mode, query, forward);
            if (!result.Found)
            {
                AppendLog(result.Message);
                ShowSearchHits([], query, result.Message);
                return;
            }

            var offset = result.Offset;
            if ((uint)offset >= _buffer.Length)
            {
                AppendLog($"Offset 0x{offset:X6} is outside buffer range 0x000000-0x{Math.Max(0, _buffer.Length - 1):X6}");
                return;
            }

            if (string.Equals(mode, "Offset", StringComparison.OrdinalIgnoreCase))
            {
                ViewOffset(offset);
                return;
            }

            ShowSearchHits([offset], query, $"{query}: 1 match");
            ShowSearchResult(offset, result.Length);
        }
        catch (Exception ex)
        {
            AppendLog($"Search failed: {ex.Message}");
        }
    }

    private async Task SearchAllHexViewAsync(string mode, string query)
    {
        try
        {
            if (string.IsNullOrEmpty(query))
            {
                return;
            }

            if (string.Equals(mode, "Offset", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog("Search all supports Hex and Text modes only");
                return;
            }

            byte[] pattern;
            string label;
            if (string.Equals(mode, "Text", StringComparison.OrdinalIgnoreCase))
            {
                pattern = Encoding.ASCII.GetBytes(query);
                label = $"Text \"{query}\"";
            }
            else
            {
                if (!TryParseHexPattern(query, out pattern))
                {
                    AppendLog($"Invalid hex pattern: {query}");
                    return;
                }

                label = $"Hex {FormatHexPattern(pattern)}";
            }

            if (pattern.Length == 0)
            {
                AppendLog($"Nothing to search: {query}");
                return;
            }

            var buffer = _buffer;
            AppendLog($"Searching all {FormatBytes(buffer.Length)}...");
            var offsets = await Task.Run(() => string.Equals(mode, "Text", StringComparison.OrdinalIgnoreCase)
                ? FindAllAsciiText(buffer, pattern)
                : FindAllBytes(buffer, pattern));

            if (offsets.Count == 0)
            {
                AppendLog($"{label} not found");
                ShowSearchHits([], query, $"{label} not found");
                return;
            }

            AppendLog($"{label}: {offsets.Count} match(es); see Search tab");

            ShowSearchHits(offsets, query, $"{label}: {offsets.Count} match(es)");
            ShowSearchResult(offsets[0], pattern.Length);
        }
        catch (Exception ex)
        {
            AppendLog($"Search all failed: {ex.Message}");
        }
    }

    private async Task ReplaceHexViewAsync(bool replaceAll, string replacementText)
    {
        try
        {
            var mode = CurrentHexSearchMode();
            var query = HexSearchBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query))
            {
                return;
            }

            if (string.Equals(mode, "Offset", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog("Replace supports Hex and Text modes only");
                return;
            }

            if (!TryBuildSearchAndReplacement(mode, query, replacementText, out var pattern, out var replacement, out var label))
            {
                return;
            }

            if (replacement.Length > pattern.Length)
            {
                AppendLog("Replacement cannot be longer than the search pattern");
                return;
            }

            var replaceBytes = PadReplacement(replacement, pattern.Length);
            var offsets = replaceAll
                ? await Task.Run(() => string.Equals(mode, "Text", StringComparison.OrdinalIgnoreCase)
                    ? FindAllAsciiText(_buffer, pattern)
                    : FindAllBytes(_buffer, pattern))
                : await FindSingleReplaceOffsetAsync(mode, pattern);
            if (replaceAll)
            {
                offsets = RemoveOverlappingMatches(offsets, pattern.Length);
            }

            if (offsets.Count == 0)
            {
                AppendLog($"{label} not found");
                ShowSearchHits([], query, $"{label} not found");
                return;
            }

            var changed = 0;
            foreach (var offset in offsets)
            {
                if (HexEditor.ReplaceBytes(offset, replaceBytes))
                {
                    changed++;
                }
            }

            ShowSearchHits(offsets, query, $"{label}: {offsets.Count} replace candidate(s)");
            ShowSearchResult(offsets[0], pattern.Length, logFound: false);
            _currentOffset = replaceAll
                ? offsets[^1]
                : Math.Min(_buffer.Length - 1, offsets[0] + pattern.Length - 1);
            AppendLog(replaceAll
                ? $"Replaced {changed} of {offsets.Count} match(es)"
                : changed > 0 ? $"Replaced at 0x{offsets[0]:X6}" : $"Match at 0x{offsets[0]:X6} already has replacement value");
        }
        catch (Exception ex)
        {
            AppendLog($"Replace failed: {ex.Message}");
        }
    }

    private async Task SearchWindowsKeyMarkerAsync()
    {
        try
        {
            var buffer = _buffer;
            AppendLog("Searching Windows key");
            var candidates = await Task.Run(() => WindowsKeyFinder.Find(buffer));
            if (candidates.Count == 0)
            {
                AppendLog("Windows key not found");
                ShowSearchHits([], "Windows key", "Windows key not found");
                return;
            }

            foreach (var candidate in candidates)
            {
                AppendLog($"Windows key found at 0x{candidate.Offset:X6}: {candidate.Key} ({candidate.Description})");
            }

            ShowWindowsKeyHits(candidates);
            ShowSearchResult(candidates[0].Offset, candidates[0].Length, logFound: false);
        }
        catch (Exception ex)
        {
            AppendLog($"Windows key search failed: {ex.Message}");
        }
    }

    private void ShowWindowsKeyHits(IReadOnlyList<WindowsKeyCandidate> candidates)
    {
        _searchHits.Clear();
        foreach (var candidate in candidates)
        {
            _searchHits.Add(CreateSearchHit(candidate.Offset, candidate.Length));
        }
    }

    private async Task<SearchResult> TryResolveSearchOffsetAsync(string mode, string query, bool forward)
    {
        switch (mode)
        {
            case "Offset":
                if (TryParseOffset(query, out var parsedOffset))
                {
                    return SearchResult.Success(parsedOffset, 1);
                }

                return SearchResult.Fail($"Invalid offset: {query}");

            case "Text":
                return await SearchTextAsync(query, forward, $"Text not found: {query}");

            default:
                if (!TryParseHexPattern(query, out var pattern))
                {
                    return SearchResult.Fail($"Invalid hex pattern: {query}");
                }

                return await SearchPatternAsync(pattern, forward, $"Hex pattern not found: {query}");
        }
    }

    private async Task<SearchResult> SearchPatternAsync(byte[] pattern, bool forward, string notFoundMessage)
    {
        if (pattern.Length == 0)
        {
            return SearchResult.Fail(notFoundMessage);
        }

        var buffer = _buffer;
        var startOffset = Math.Clamp(_currentOffset + (forward ? 1 : -1), 0, Math.Max(0, buffer.Length - 1));
        AppendLog($"Searching {FormatBytes(buffer.Length)}...");
        var offset = await Task.Run(() => FindBytes(buffer, pattern, startOffset, forward));
        if (offset < 0 && startOffset != 0)
        {
            offset = await Task.Run(() => FindBytes(buffer, pattern, forward ? 0 : buffer.Length - 1, forward));
        }

        return offset >= 0 ? SearchResult.Success(offset, pattern.Length) : SearchResult.Fail(notFoundMessage);
    }

    private async Task<SearchResult> SearchTextAsync(string text, bool forward, string notFoundMessage)
    {
        var pattern = Encoding.ASCII.GetBytes(text);
        if (pattern.Length == 0)
        {
            return SearchResult.Fail(notFoundMessage);
        }

        var buffer = _buffer;
        var startOffset = Math.Clamp(_currentOffset + (forward ? 1 : -1), 0, Math.Max(0, buffer.Length - 1));
        AppendLog($"Searching {FormatBytes(buffer.Length)}...");
        var offset = await Task.Run(() => FindAsciiText(buffer, pattern, startOffset, forward));
        if (offset < 0 && startOffset != 0)
        {
            offset = await Task.Run(() => FindAsciiText(buffer, pattern, forward ? 0 : buffer.Length - 1, forward));
        }

        return offset >= 0 ? SearchResult.Success(offset, pattern.Length) : SearchResult.Fail(notFoundMessage);
    }

    private async Task<List<int>> FindSingleReplaceOffsetAsync(string mode, byte[] pattern)
    {
        var selectedOffset = HexEditor.SelectedOffset;
        if ((uint)selectedOffset < _buffer.Length && selectedOffset <= _buffer.Length - pattern.Length)
        {
            var matchesSelected = string.Equals(mode, "Text", StringComparison.OrdinalIgnoreCase)
                ? AsciiEqualsIgnoreCase(_buffer, pattern, selectedOffset)
                : _buffer.AsSpan(selectedOffset, pattern.Length).SequenceEqual(pattern);
            if (matchesSelected)
            {
                return [selectedOffset];
            }
        }

        var result = string.Equals(mode, "Text", StringComparison.OrdinalIgnoreCase)
            ? await SearchTextAsync(Encoding.ASCII.GetString(pattern), forward: true, $"{mode} not found")
            : await SearchPatternAsync(pattern, forward: true, "Hex pattern not found");
        return result.Found ? [result.Offset] : [];
    }

    private bool TryBuildSearchAndReplacement(string mode, string query, string replacementText, out byte[] pattern, out byte[] replacement, out string label)
    {
        pattern = [];
        replacement = [];
        label = string.Empty;

        if (string.Equals(mode, "Text", StringComparison.OrdinalIgnoreCase))
        {
            pattern = Encoding.ASCII.GetBytes(query);
            replacement = Encoding.ASCII.GetBytes(replacementText);
            label = $"Text \"{query}\"";
        }
        else
        {
            if (!TryParseHexPattern(query, out pattern))
            {
                AppendLog($"Invalid hex pattern: {query}");
                return false;
            }

            if (!TryParseHexPattern(replacementText, out replacement))
            {
                AppendLog($"Invalid replacement hex: {replacementText}");
                return false;
            }

            label = $"Hex {FormatHexPattern(pattern)}";
        }

        if (pattern.Length == 0)
        {
            AppendLog($"Nothing to replace: {query}");
            return false;
        }

        if (replacement.Length == 0)
        {
            AppendLog("Replacement is empty");
            return false;
        }

        return true;
    }

    private static byte[] PadReplacement(byte[] replacement, int length)
    {
        if (replacement.Length == length)
        {
            return replacement;
        }

        var padded = new byte[length];
        Buffer.BlockCopy(replacement, 0, padded, 0, replacement.Length);
        return padded;
    }

    private static List<int> RemoveOverlappingMatches(IEnumerable<int> offsets, int length)
    {
        var filtered = new List<int>();
        var nextAllowed = 0;
        foreach (var offset in offsets)
        {
            if (offset < nextAllowed)
            {
                continue;
            }

            filtered.Add(offset);
            nextAllowed = offset + length;
        }

        return filtered;
    }

    private void ShowSearchResult(int offset, int length = 1, bool logFound = true)
    {
        _currentOffset = offset;
        HexEditor.SelectRange(offset, length);
        if (logFound)
        {
            AppendLog($"Found at 0x{offset:X6}");
        }
    }

    private void ShowSearchHits(IReadOnlyList<int> offsets, string query, string status)
    {
        _searchHits.Clear();
        if (offsets.Count == 0)
        {
            _searchHits.Add(SearchHit.Message(status));
            return;
        }

        var length = Math.Max(1, CurrentHexSearchMode().Equals("Text", StringComparison.OrdinalIgnoreCase)
            ? Encoding.ASCII.GetByteCount(query)
            : TryParseHexPattern(query, out var pattern) ? pattern.Length : 1);
        foreach (var offset in offsets)
        {
            _searchHits.Add(CreateSearchHit(offset, length));
        }
    }

    private SearchHit CreateSearchHit(int offset, int length)
    {
        var contextStart = Math.Max(0, offset - SearchHitContextBytes);
        var contextEnd = Math.Min(_buffer.Length, offset + length + SearchHitContextBytes);
        var span = _buffer.AsSpan(contextStart, contextEnd - contextStart).ToArray();
        var hex = string.Join(" ", span.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
        var text = new string(span.Select(b => b is >= 32 and <= 126 ? (char)b : '.').ToArray());
        return new SearchHit(offset, length, $"0x{offset:X6}", hex, text);
    }

    private void ViewOffset(int offset)
    {
        _currentOffset = offset;
        RebuildRows(offset);
        UpdateStatus();
        HexEditor.ScrollToOffset(offset);
        AppendLog($"Viewing 0x{_previewStartOffset:X6}");
    }

    private static int AlignOffset(int offset) => offset / BytesPerHexRow * BytesPerHexRow;

    private static int FindBytes(byte[] buffer, byte[] pattern, int startOffset, bool forward)
    {
        if (pattern.Length == 0 || pattern.Length > buffer.Length)
        {
            return -1;
        }

        startOffset = Math.Clamp(startOffset, 0, buffer.Length - 1);
        if (forward)
        {
            var index = buffer.AsSpan(startOffset).IndexOf(pattern);
            return index < 0 ? -1 : startOffset + index;
        }

        for (var offset = Math.Min(startOffset, buffer.Length - pattern.Length); offset >= 0; offset--)
        {
            if (buffer.AsSpan(offset, pattern.Length).SequenceEqual(pattern))
            {
                return offset;
            }
        }

        return -1;
    }

    private static byte[] StripXgproMetadata(byte[] buffer, out int markerOffset, out int removedBytes)
    {
        markerOffset = -1;
        removedBytes = 0;

        var lastPossibleOffset = buffer.Length - XgproMetadataMarker.Length;
        if (lastPossibleOffset < 0)
        {
            return buffer;
        }

        markerOffset = FindBytes(buffer, XgproMetadataMarker, lastPossibleOffset, forward: false);
        if (markerOffset < 0)
        {
            return buffer;
        }

        removedBytes = buffer.Length - markerOffset;
        var trimmed = new byte[markerOffset];
        Buffer.BlockCopy(buffer, 0, trimmed, 0, markerOffset);
        return trimmed;
    }

    private static List<int> FindAllBytes(byte[] buffer, byte[] pattern)
    {
        var offsets = new List<int>();
        if (pattern.Length == 0 || pattern.Length > buffer.Length)
        {
            return offsets;
        }

        var offset = 0;
        while (offset <= buffer.Length - pattern.Length)
        {
            var index = buffer.AsSpan(offset).IndexOf(pattern);
            if (index < 0)
            {
                break;
            }

            var absolute = offset + index;
            offsets.Add(absolute);
            offset = absolute + 1;
        }

        return offsets;
    }

    private static int FindAsciiText(byte[] buffer, byte[] pattern, int startOffset, bool forward)
    {
        if (pattern.Length == 0 || pattern.Length > buffer.Length)
        {
            return -1;
        }

        startOffset = Math.Clamp(startOffset, 0, buffer.Length - 1);
        if (forward)
        {
            for (var offset = startOffset; offset <= buffer.Length - pattern.Length; offset++)
            {
                if (AsciiEqualsIgnoreCase(buffer, pattern, offset))
                {
                    return offset;
                }
            }

            return -1;
        }

        for (var offset = Math.Min(startOffset, buffer.Length - pattern.Length); offset >= 0; offset--)
        {
            if (AsciiEqualsIgnoreCase(buffer, pattern, offset))
            {
                return offset;
            }
        }

        return -1;
    }

    private static List<int> FindAllAsciiText(byte[] buffer, byte[] pattern)
    {
        var offsets = new List<int>();
        if (pattern.Length == 0 || pattern.Length > buffer.Length)
        {
            return offsets;
        }

        for (var offset = 0; offset <= buffer.Length - pattern.Length; offset++)
        {
            if (AsciiEqualsIgnoreCase(buffer, pattern, offset))
            {
                offsets.Add(offset);
            }
        }

        return offsets;
    }

    private static bool AsciiEqualsIgnoreCase(byte[] buffer, byte[] pattern, int offset)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (ToAsciiUpper(buffer[offset + i]) != ToAsciiUpper(pattern[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static byte ToAsciiUpper(byte value) => value is >= (byte)'a' and <= (byte)'z'
        ? (byte)(value - 32)
        : value;

    private static bool TryParseHexPattern(string text, out byte[] pattern)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (Uri.IsHexDigit(ch))
            {
                if (ch == '0' && i + 1 < text.Length && text[i + 1] is 'x' or 'X')
                {
                    i++;
                    continue;
                }

                builder.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is '-' or '_' or ',' or ';')
            {
                continue;
            }

            pattern = [];
            return false;
        }

        var hex = builder.ToString();
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            pattern = [];
            return false;
        }

        pattern = new byte[hex.Length / 2];
        for (var i = 0; i < pattern.Length; i++)
        {
            if (!byte.TryParse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out pattern[i]))
            {
                pattern = [];
                return false;
            }
        }

        return true;
    }

    private static string FormatHexPattern(byte[] pattern) =>
        string.Join(" ", pattern.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));

    private static bool TryParseOffset(string text, out int offset)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out offset);
    }

    private void ThemeToggleButton_Toggled(object sender, RoutedEventArgs e)
    {
        ApplyTheme(ThemeToggleButton.IsChecked == true);
    }

    private void ApplyTheme(bool darkMode)
    {
        ThemeIconPath.Data = Geometry.Parse(darkMode ? SunIconPath : MoonIconPath);
        ThemeToggleButton.ToolTip = darkMode ? "Switch to light mode" : "Switch to dark mode";

        if (darkMode)
        {
            SetBrush("AppBackgroundBrush", "#202225");
            SetBrush("PanelBackgroundBrush", "#25282C");
            SetBrush("ToolbarBackgroundBrush", "#2D3035");
            SetBrush("ToolbarButtonBackgroundBrush", "#2D3035");
            SetBrush("ToolbarButtonBorderBrush", "#2D3035");
            SetBrush("SurfaceBackgroundBrush", "#1F2226");
            SetBrush("SubtleBackgroundBrush", "#272B30");
            SetBrush("InputBackgroundBrush", "#2C3137");
            SetBrush("ReadOnlyBackgroundBrush", "#293036");
            SetBrush("HoverBackgroundBrush", "#353C44");
            SetBrush("PressedBackgroundBrush", "#3D4650");
            SetBrush("AccentBrush", "#69BDFD");
            SetBrush("AccentSoftBrush", "#19384F");
            SetBrush("AlternateRowBackgroundBrush", "#24282D");
            SetBrush("TextBrush", "#E7ECF2");
            SetBrush("MutedTextBrush", "#B8C1CC");
            SetBrush("BorderBrush", "#48515C");
            SetBrush("GridLineBrush", "#313740");
            SetBrush("LightGridLineBrush", "#2A3038");
            SetBrush("AddressBackgroundBrush", "#283A49");
            SetBrush("AddressForegroundBrush", "#6BCBFF");
            SetBrush("SplitterBrush", "#3A424C");
            SetBrush("SelectionBackgroundBrush", "#315B7C");
            SetBrush("SelectionForegroundBrush", "#FFFFFF");
            SetBrush("ProgressTrackBrush", "#30363D");
            SetBrush("StopBackgroundBrush", "#4A2328");
            SetBrush("StopForegroundBrush", "#FFB9C0");
            return;
        }

        SetBrush("AppBackgroundBrush", "#F0F0F0");
        SetBrush("PanelBackgroundBrush", "#EFEFEF");
        SetBrush("ToolbarBackgroundBrush", "#E8E8E8");
        SetBrush("ToolbarButtonBackgroundBrush", "#E8E8E8");
        SetBrush("ToolbarButtonBorderBrush", "#E8E8E8");
        SetBrush("SurfaceBackgroundBrush", "#FFFFFF");
        SetBrush("SubtleBackgroundBrush", "#F6F6F6");
        SetBrush("InputBackgroundBrush", "#F7F7F7");
        SetBrush("ReadOnlyBackgroundBrush", "#F7F7F7");
        SetBrush("HoverBackgroundBrush", "#E9F3FF");
        SetBrush("PressedBackgroundBrush", "#DDEEFF");
        SetBrush("AccentBrush", "#0067C0");
        SetBrush("AccentSoftBrush", "#E5F1FB");
        SetBrush("AlternateRowBackgroundBrush", "#FBFBFB");
        SetBrush("TextBrush", "#000000");
        SetBrush("MutedTextBrush", "#333333");
        SetBrush("BorderBrush", "#B8B8B8");
        SetBrush("GridLineBrush", "#E6E6E6");
        SetBrush("LightGridLineBrush", "#F6F6F6");
        SetBrush("AddressBackgroundBrush", "#A8A8A8");
        SetBrush("AddressForegroundBrush", "#FFFFFF");
        SetBrush("SplitterBrush", "#D8D8D8");
        SetBrush("SelectionBackgroundBrush", "#DDEEFF");
        SetBrush("SelectionForegroundBrush", "#000000");
        SetBrush("ProgressTrackBrush", "#E5E5E5");
        SetBrush("StopBackgroundBrush", "#FFF0F0");
        SetBrush("StopForegroundBrush", "#B00000");
    }

    private static void SetBrush(string key, string color)
    {
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Binary files (*.bin;*.rom)|*.bin;*.rom|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            LoadBufferFromFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            AppendLog($"Open file failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Open file", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HexEditor_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void HexEditor_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            {
                return;
            }

            var file = files.FirstOrDefault(File.Exists);
            if (file is null)
            {
                AppendLog("Drop ignored: no file found");
                return;
            }

            LoadBufferFromFile(file);
            if (files.Length > 1)
            {
                AppendLog($"Drop loaded first file; ignored {files.Length - 1} other file(s)");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Drop file failed: {ex.Message}");
        }
    }

    private void LoadBufferFromFile(string fileName)
    {
        _buffer = StripXgproMetadata(File.ReadAllBytes(fileName), out var markerOffset, out var removedBytes);
        _currentOffset = 0;
        _searchHits.Clear();
        RebuildRows();
        UpdateStatus();
        AppendLog($"Loaded {fileName} ({FormatBytes(_buffer.Length)})");
        if (removedBytes > 0)
        {
            AppendLog($"Removed XGecu metadata: {removedBytes} bytes from 0x{markerOffset:X6} to EOF");
        }
    }

    private void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Binary files (*.bin)|*.bin|ROM files (*.rom)|*.rom|All files (*.*)|*.*",
            FileName = $"{CurrentChip().Name}.bin"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllBytes(dialog.FileName, _buffer);
        AppendLog($"Saved {dialog.FileName} ({FormatBytes(_buffer.Length)})");
    }

    private void SaveLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LogBox.Text))
        {
            MessageBox.Show(this, "Log is empty", AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"NexusProgrammer-Log-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, LogBox.Text, Encoding.UTF8);
        AppendLog($"Log saved: {dialog.FileName}");
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
    }

    private void FillFF_Click(object sender, RoutedEventArgs e)
    {
        Array.Fill(_buffer, (byte)0xFF);
        RebuildRows();
        AppendLog("Buffer filled with FF");
    }

    private void Fill00_Click(object sender, RoutedEventArgs e)
    {
        Array.Fill(_buffer, (byte)0x00);
        RebuildRows();
        AppendLog("Buffer filled with 00");
    }

    private async void RunScript_Click(object sender, RoutedEventArgs e)
    {
        var script = (sender as FrameworkElement)?.Tag as string ?? "Script";
        if (!EnsureProgrammerAvailable(script))
        {
            return;
        }

        var chip = CurrentChip();
        if (!ConfirmVoltageAdapterIfNeeded(chip, script))
        {
            return;
        }

        await RunOperationAsync(script, _buffer.Length, async progress =>
        {
            var startAddress = ParseStartAddress();
            if (string.Equals(script, "Read + verify", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog($"Script request: read and verify {FormatBytes(_buffer.Length)} from 0x{startAddress:X6}");
                AppendLog("Script stage: read started");
                _buffer = await _programmer.ReadAsync(chip, startAddress, _buffer.Length, progress);
                RebuildRows();
                UpdateStatus();
                AppendLog("Script stage: read completed");
                AppendLog("Script stage: verify started");
                var readOk = await _programmer.VerifyAsync(chip, startAddress, _buffer, progress);
                AppendLog(readOk ? "Script stage: verify completed OK" : "Script stage: verify failed");
                AppendLog(readOk ? "Script completed: read + verify OK" : "Script completed: read + verify failed");
                return;
            }

            AppendLog($"Script request: erase, write and verify {FormatBytes(_buffer.Length)} at 0x{startAddress:X6}");
            await UnprotectIfRequestedAsync(chip, progress);
            AppendLog("Script stage: erase started");
            await _programmer.EraseAsync(chip, progress);
            AppendLog("Script stage: erase completed");
            AppendLog("Script stage: write started");
            await _programmer.WriteAsync(chip, startAddress, _buffer, progress, skipBlankPages: true);
            AppendLog("Script stage: write completed");
            AppendLog("Script stage: verify started");
            var ok = await _programmer.VerifyAsync(chip, startAddress, _buffer, progress);
            AppendLog(ok ? "Script stage: verify completed OK" : "Script stage: verify failed");
            AppendLog(ok ? "Script completed: verify OK" : "Script completed: verify failed");
        });
    }

    private async Task UnprotectIfRequestedAsync(ChipProfile chip, IProgress<int> progress)
    {
        if (UnprotectChipCheckBox.IsChecked != true)
        {
            return;
        }

        AppendLog($"Unprotect request: {chip.Name}");
        await _programmer.UnprotectAsync(chip, progress);
        AppendLog("Unprotect completed");
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(ProjectUrl)
        {
            UseShellExecute = true
        });
    }

    private static string AppVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";

    private Task RunOperationAsync(string name, Func<IProgress<int>, Task> operation) =>
        RunOperationAsync(name, null, operation);

    private Task RunOperationAsync(string name, Func<IProgress<int>, Task> operation, bool logLifecycle) =>
        RunOperationAsync(name, null, operation, logLifecycle);

    private async Task RunOperationAsync(string name, int? byteCount, Func<IProgress<int>, Task> operation, bool logLifecycle = true)
    {
        if (_isBusy)
        {
            AppendLog("Another operation is already running");
            return;
        }

        _isBusy = true;
        OperationStatusText.Text = name;
        OperationProgress.Value = 0;
        var progress = new Progress<int>(value => OperationProgress.Value = Math.Clamp(value, 0, 100));
        var stopwatch = Stopwatch.StartNew();
        if (logLifecycle)
        {
            AppendLog($"{name} started");
        }

        try
        {
            await operation(progress);
            stopwatch.Stop();
            OperationProgress.Value = 100;
            OperationStatusText.Text = "Ready";
            if (logLifecycle)
            {
                AppendLog(byteCount is > 0
                    ? $"{name} completed: {FormatBytes(byteCount.Value)} in {FormatDuration(stopwatch.Elapsed)} ({FormatSpeed(byteCount.Value, stopwatch.Elapsed)})"
                    : $"{name} completed in {FormatDuration(stopwatch.Elapsed)}");
            }

            PlayOperationSound(name, success: true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            OperationStatusText.Text = "Error";
            AppendLog($"ERROR after {FormatDuration(stopwatch.Elapsed)}: {ex.Message}");
            PlayOperationSound(name, success: false);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private static void PlayOperationSound(string operationName, bool success)
    {
        if (!ShouldPlayCompletionSound(operationName))
        {
            return;
        }

        try
        {
            (success ? SystemSounds.Asterisk : SystemSounds.Hand).Play();
        }
        catch
        {
            // Best-effort only; sound failures must never affect programmer operations.
        }
    }

    private static bool ShouldPlayCompletionSound(string operationName) =>
        operationName.StartsWith("Read chip", StringComparison.OrdinalIgnoreCase) ||
        operationName.StartsWith("Write chip", StringComparison.OrdinalIgnoreCase) ||
        operationName.StartsWith("Erase chip", StringComparison.OrdinalIgnoreCase) ||
        operationName.StartsWith("Verify", StringComparison.OrdinalIgnoreCase) ||
        operationName.Contains("verify", StringComparison.OrdinalIgnoreCase);

    private ChipProfile CurrentChip() => ChipCombo.SelectedItem as ChipProfile ?? _chips[0];

    private bool ConfirmVoltageAdapterIfNeeded(ChipProfile chip, string operationName)
    {
        if (_programmer is T48SDKProgrammer)
        {
            return true;
        }

        if (!Requires1V8Adapter(chip))
        {
            return true;
        }

        var result = MessageBox.Show(
            $"{chip.Name} is listed as {chip.Volts}.\n\nUse a 1.8V adapter / level shifter before {operationName}. Continue only if the adapter is connected.",
            "1.8V IC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            AppendLog($"1.8V adapter confirmed for {chip.Name}");
            return true;
        }

        AppendLog($"Cancelled {operationName}: {chip.Name} requires a 1.8V adapter");
        return false;
    }

    private static bool Requires1V8Adapter(ChipProfile chip)
    {
        var volts = chip.Volts.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        return volts.Contains("1.8", StringComparison.OrdinalIgnoreCase) ||
               volts.Contains("1V8", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameVoltageProfile(ChipProfile left, ChipProfile right) =>
        string.Equals(NormalizeVoltage(left.Volts), NormalizeVoltage(right.Volts), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeVoltage(string volts) =>
        volts.Replace(" ", "", StringComparison.OrdinalIgnoreCase).TrimEnd('V');

    private bool CurrentChipMatchesId(ChipProfile chip, byte[] id)
    {
        var idText = FormatId(id);
        return _icCatalog.Any(candidate =>
            string.Equals(candidate.Profile.Name, chip.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.JedecId, idText, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowChipSelectionForId(byte[] id, bool autoApplySingle = false, bool openCatalogOnMiss = true)
    {
        if (id.Length == 0)
        {
            AppendLog("IC ID is empty; skipped IC auto-detect");
            return;
        }

        var idText = FormatId(id);
        if (IsInvalidJedecId(id))
        {
            AppendLog($"Invalid IC ID {idText}. Check IC contact, orientation, pinout, adapter voltage, and clip wiring.");
            MessageBox.Show(
                $"Invalid IC ID {idText}.\n\nCheck IC contact, orientation, pinout, adapter voltage, and clip wiring.",
                "Detect IC",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var candidates = FindCandidatesByJedecId(id).ToList();
        if (candidates.Count == 0)
        {
            if (!openCatalogOnMiss)
            {
                AppendLog("IC ID is not in the detection table");
                return;
            }

            AppendLog("IC ID is not in the detection table. Opening full IC list");
            if (MessageBox.Show($"IC ID {idText} is not in the catalog. Add it as a new SPI NOR IC?", "Add IC", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                AddIcFromJedecId(idText);
                return;
            }

            ShowChipSelection(_icCatalog, "Search IC", null);
            return;
        }

        AppendLog(candidates.Count == 1
            ? $"Detected ID: {idText}. One compatible IC profile found"
            : $"Detected ID: {idText}. Multiple compatible IC profiles found. Please select the exact chip marking");

        if (autoApplySingle && candidates.Count == 1)
        {
            var candidate = candidates[0];
            ApplyChip(candidate.Profile);
            AppendLog($"Auto-selected IC: {candidate.Device}, {candidate.Size}, {candidate.Volts}, page {candidate.Page}, {candidate.Manuf}");
            return;
        }

        ShowChipSelection(candidates, "Search IC", idText);
    }

    private static bool IsInvalidJedecId(byte[] id)
    {
        if (id.Length == 0)
        {
            return true;
        }

        if (id.All(value => value == 0x00) || id.All(value => value == 0xFF))
        {
            return true;
        }

        return id.Length >= 3 && id[0] == 0x03 && id[1] == 0x00 && id[2] == 0x00;
    }

    private void AddIcFromJedecId(string jedecId)
    {
        var dialog = new AddIcWindow(jedecId) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Candidate is null)
        {
            return;
        }

        AddUserIc(dialog.Candidate);
    }

    private void AddUserIc(IcCandidate candidate)
    {
        IcCatalogLoader.SaveUserCandidate(candidate);
        ReloadIcCatalog();
        ApplyChip(candidate.Profile);
        AppendLog($"Added IC: {candidate.Device}, ID {candidate.JedecId}, {candidate.Size}");
    }

    private void ShowChipSelection(IEnumerable<IcCandidate> candidates, string title, string? idText)
    {
        var dialog = new SearchIcWindow(candidates, idText)
        {
            Owner = this,
            Title = title
        };

        if (dialog.ShowDialog() == true && dialog.SelectedCandidate is not null)
        {
            ApplyChip(dialog.SelectedCandidate.Profile);
            AppendLog($"Selected IC: {dialog.SelectedCandidate.Device}, {dialog.SelectedCandidate.Size}, {dialog.SelectedCandidate.Volts}, page {dialog.SelectedCandidate.Page}, {dialog.SelectedCandidate.Manuf}");
        }
    }

    private void ApplyChip(ChipProfile chip)
    {
        _isApplyingDetectedChip = true;
        try
        {
            var knownChip = _chips.FirstOrDefault(x => x.Name == chip.Name) ?? chip;
            if (!_chips.Any(x => x.Name == knownChip.Name))
            {
                _chips.Add(knownChip);
            }

            ChipCombo.SelectedItem = knownChip;
            SelectSize(knownChip.SizeBytes);
            PageCombo.SelectedItem = knownChip.PageSize.ToString();
            CommandCombo.SelectedItem = knownChip.CommandSet;
            ResizeBuffer(knownChip.SizeBytes, fill: 0xFF);
            UpdateDeviceInfo(knownChip);
        }
        finally
        {
            _isApplyingDetectedChip = false;
        }
    }

    private void UpdateDeviceInfo(ChipProfile chip)
    {
        DeviceNameBox.Text = chip.Name;
        DeviceTypeText.Text = chip.Type;
        BitSizeText.Text = FormatMbits(chip.SizeBytes);
        ManufacturerText.Text = chip.Manufacturer;
        DeviceSizeBox.Text = chip.SizeBytes.ToString();
    }

    private IEnumerable<IcCandidate> FindCandidatesByJedecId(byte[] id)
    {
        var idText = FormatId(id);
        return _icCatalog.Where(x => string.Equals(x.JedecId, idText, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatId(byte[] id) => string.Join(" ", id.Select(x => x.ToString("X2")));

    private static string FormatMbits(int bytes) => IcCatalogLoader.FormatMbits(bytes);

    private int ParseStartAddress()
    {
        var text = StartAddressBox.Text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value) ? value : 0;
    }

    private void UpdateStatus()
    {
        SizeStatusText.Text = $"Size: {_buffer.Length}";
        BufferStatusText.Text = $"Buffer: {FormatBytes(_buffer.Length)}";
    }

    private void AppendLog(string message)
    {
        message = message.TrimEnd('.');
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private static string FormatBytes(int bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }

        return bytes >= 1024 ? $"{bytes / 1024.0:0.##} KB" : $"{bytes} B";
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss\.fff")
            : elapsed.ToString(@"mm\:ss\.fff");
    }

    private static string FormatSpeed(int bytes, TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= 0)
        {
            return "n/a";
        }

        var bytesPerSecond = bytes / elapsed.TotalSeconds;
        return bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / (1024 * 1024):0.##} MB/s"
            : $"{bytesPerSecond / 1024:0.##} KB/s";
    }
}


