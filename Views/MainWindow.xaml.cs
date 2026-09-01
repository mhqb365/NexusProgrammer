using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;

namespace NexusProgrammer;

public partial class MainWindow : Window
{
    private const string AppName = "Nexus Programmer";
    private const string ProjectUrl = "https://github.com/mhqb365/NexusProgrammer";
    public static readonly RoutedUICommand NewBufferCommand = new(
        "New",
        nameof(NewBufferCommand),
        typeof(MainWindow),
        [new KeyGesture(Key.N, ModifierKeys.Control)]);
    public static readonly RoutedUICommand NewWindowCommand = new(
        "New Window",
        nameof(NewWindowCommand),
        typeof(MainWindow),
        [new KeyGesture(Key.N, ModifierKeys.Control | ModifierKeys.Shift)]);
    public static readonly RoutedUICommand OpenFileCommand = new(
        "Open",
        nameof(OpenFileCommand),
        typeof(MainWindow),
        [new KeyGesture(Key.O, ModifierKeys.Control)]);
    public static readonly RoutedUICommand SaveFileCommand = new(
        "Save",
        nameof(SaveFileCommand),
        typeof(MainWindow),
        [new KeyGesture(Key.S, ModifierKeys.Control)]);
    public static readonly RoutedUICommand ExitCommand = new(
        "Exit",
        nameof(ExitCommand),
        typeof(MainWindow),
        [new KeyGesture(Key.Q, ModifierKeys.Control)]);
    private static readonly string SuccessSoundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "success.wav");
    private const int MaxHexPreviewRows = 4096;
    private const int BytesPerHexRow = 16;
    private const int SearchHitContextBytes = 16;
    private const int MaxTrailingMetadataBytes = 1024 * 1024;
    private static readonly bool MeaFeatureEnabled = true;
    private static readonly int[] ValidBiosSizes =
    [
        512 * 1024,
        1 * 1024 * 1024,
        2 * 1024 * 1024,
        4 * 1024 * 1024,
        8 * 1024 * 1024,
        12 * 1024 * 1024,
        16 * 1024 * 1024,
        20 * 1024 * 1024,
        24 * 1024 * 1024,
        32 * 1024 * 1024,
        40 * 1024 * 1024,
        48 * 1024 * 1024,
        64 * 1024 * 1024,
        128 * 1024 * 1024
    ];
    private static readonly byte[] XgproMetadataMarker =
    [
        0x2D, 0x43, 0x6F, 0x6E, 0x66, 0x69, 0x67, 0x75,
        0x72, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x2D, 0x00
    ];
    private readonly ObservableCollection<HexRow> _rows = [];
    private readonly ObservableCollection<SearchHit> _searchHits = [];
    private readonly List<ChipProfile> _chips = [];
    private readonly Dictionary<TabItem, MemoryTabState> _memoryTabs = [];

    private readonly List<ProgrammerOption> _programmerOptions =
    [
        new("auto", "Auto"),
        new("t48", "XGecu T48"),
        new("rt809f", "RT809F"),
        new("rt809h", "RT809H"),
        new("ch347", "CH347"),
        new("ch341", "CH341")
    ];
    private List<IcCandidate> _icCatalog = [];
    private readonly DispatcherTimer _programmerMonitorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private AppSettings _settings = AppSettingsService.Load();
    private IChipProgrammer _programmer = new MockProgrammer();
    private string _activeProgrammerKey = "none";
    private byte[] _buffer = [];
    private MemoryTabState? _activeMemoryTab;
    private int _nextBiosTabIndex = 2;
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
        CommandBindings.Add(new CommandBinding(NewBufferCommand, NewBufferCommand_Executed));
        CommandBindings.Add(new CommandBinding(NewWindowCommand, NewWindowCommand_Executed));
        CommandBindings.Add(new CommandBinding(OpenFileCommand, OpenFileCommand_Executed));
        CommandBindings.Add(new CommandBinding(SaveFileCommand, SaveFileCommand_Executed));
        CommandBindings.Add(new CommandBinding(ExitCommand, ExitCommand_Executed));
        _icCatalog = IcCatalogLoader.LoadSpiCatalog();
        _chips.AddRange(_icCatalog.Select(x => x.Profile));
        if (_chips.Count == 0)
        {
            throw new InvalidOperationException("No SPI IC catalog entries found.");
        }
        _activeMemoryTab = new MemoryTabState(1, Bios1Tab, HexEditor, HexScrollBar, _buffer);
        _memoryTabs[Bios1Tab] = _activeMemoryTab;
        SearchHitsGrid.ItemsSource = _searchHits;
        WireHexEditorActions(HexEditor);
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
        ProgrammerSelectorCombo.ItemsSource = _programmerOptions;
        ProgrammerSelectorCombo.SelectedIndex = 0;

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

    private void MemoryTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MemoryTabControl) || MemoryTabControl.SelectedItem is not TabItem selectedTab)
        {
            return;
        }

        if (selectedTab == AddMemoryTab)
        {
            MemoryTabControl.SelectedItem = _activeMemoryTab?.Tab ?? Bios1Tab;
            return;
        }

        if (ActivateMemoryTab(selectedTab) && IsLoaded && _activeMemoryTab is not null)
        {
            AppendLog($"Selected {MemoryTabLabel(_activeMemoryTab.Index)}");
        }
    }

    private TabItem AddBiosTab()
    {
        var index = _nextBiosTabIndex++;
        var tab = new TabItem
        {
            Tag = index
        };
        tab.Header = CreateClosableMemoryTabHeader(index, tab);
        _memoryTabs[tab] = CreateMemoryTabState(index, tab);

        MemoryTabControl.Items.Insert(Math.Max(0, MemoryTabControl.Items.Count - 1), tab);
        MemoryTabControl.SelectedItem = tab;
        return tab;
    }

    private TabItem AddMemoryTabWithBuffer(byte[] buffer, string sourceFileName)
    {
        var tab = AddBiosTab();
        MemoryTabControl.SelectedItem = tab;
        SetActiveBuffer(buffer);
        if (_activeMemoryTab is not null)
        {
            _activeMemoryTab.SourceFileName = sourceFileName;
        }

        RebuildRows();
        UpdateStatus();
        return tab;
    }

    private void AddMemoryTab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AddBiosTab();
    }

    private StackPanel CreateClosableMemoryTabHeader(int index, TabItem tab)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        header.Children.Add(new TextBlock
        {
            Text = MemoryTabLabel(index),
            VerticalAlignment = VerticalAlignment.Center
        });
        var closeButton = new TextBlock
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(6, 0, 0, 0),
            Text = "x",
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"Close {MemoryTabLabel(index)}",
            Tag = tab
        };
        closeButton.PreviewMouseLeftButtonDown += CloseMemoryTab_MouseDown;
        header.Children.Add(closeButton);
        return header;
    }

    private void CloseMemoryTab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBlock { Tag: TabItem tab })
        {
            return;
        }

        if (tab is null || tab == Bios1Tab)
        {
            return;
        }

        CloseMemoryTab(tab);
    }

    private void CloseMemoryTab(TabItem tab)
    {
        var tabIndex = MemoryTabControl.Items.IndexOf(tab);
        if (tab == Bios1Tab || tabIndex < 0)
        {
            return;
        }

        var closedLabel = _memoryTabs.TryGetValue(tab, out var closingState)
            ? MemoryTabLabel(closingState.Index)
            : "Memory";
        var countBefore = MemoryTabControl.Items.Count;
        var closingActiveTab = _activeMemoryTab?.Tab == tab;

        _memoryTabs.Remove(tab);
        tab.Content = null;
        tab.Header = null;
        MemoryTabControl.Items.RemoveAt(tabIndex);

        if (closingActiveTab)
        {
            var lastMemoryIndex = Math.Max(0, MemoryTabControl.Items.Count - 2);
            var nextIndex = Math.Clamp(tabIndex, 0, lastMemoryIndex);
            MemoryTabControl.SelectedIndex = nextIndex;
            if (MemoryTabControl.SelectedItem is TabItem selectedTab)
            {
                ActivateMemoryTab(selectedTab);
            }
        }

        if (MemoryTabControl.Items.Count < countBefore)
        {
            if (_memoryTabs.Count == 1)
            {
                _nextBiosTabIndex = 2;
            }

            AppendLog($"Closed {closedLabel}");
        }
    }

    private static string MemoryTabLabel(int index) => $"Memory {index}";

    private MemoryTabState CreateMemoryTabState(int index, TabItem tab)
    {
        var editor = new HexEditorView
        {
            Background = (Brush)FindResource("SurfaceBackgroundBrush"),
            Foreground = (Brush)FindResource("TextBrush")
        };
        editor.ScrollChanged += (_, args) => HexEditor_ScrollChanged(editor, args);
        WireHexEditorActions(editor);

        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0
        };
        scrollBar.ValueChanged += HexScrollBar_ValueChanged;

        var grid = new Grid
        {
            Background = (Brush)FindResource("SurfaceBackgroundBrush"),
            AllowDrop = true
        };
        grid.PreviewDragOver += HexEditor_DragOver;
        grid.Drop += HexEditor_Drop;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

        Grid.SetColumn(editor, 0);
        Grid.SetColumn(scrollBar, 1);
        grid.Children.Add(editor);
        grid.Children.Add(scrollBar);

        tab.Content = grid;
        var buffer = CreateBlankBuffer();
        editor.SetBuffer(buffer, OnHexCellChanged);
        return new MemoryTabState(index, tab, editor, scrollBar, buffer);
    }

    private void WireHexEditorActions(HexEditorView editor)
    {
        editor.ClearBufferRequested += HexEditor_ClearBufferRequested;
        editor.MergeBiosRequested += (_, _) => MergeBios_Click(editor, new RoutedEventArgs());
        editor.SplitBiosRequested += (_, _) => SplitBios_Click(editor, new RoutedEventArgs());
    }

    private bool ActivateMemoryTab(TabItem tab)
    {
        if (!_memoryTabs.TryGetValue(tab, out var state))
        {
            return false;
        }

        if (_activeMemoryTab == state)
        {
            return false;
        }

        if (_activeMemoryTab is not null)
        {
            _activeMemoryTab.Buffer = _buffer;
        }

        _activeMemoryTab = state;
        HexEditor = state.Editor;
        HexScrollBar = state.ScrollBar;
        _buffer = state.Buffer;
        HexEditor.SetBuffer(_buffer, OnHexCellChanged);
        UpdateHexScrollBar();
        UpdateStatus();
        return true;
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
        var buffer = new byte[size];
        if (fill != 0)
        {
            Array.Fill(buffer, fill);
        }

        SetActiveBuffer(buffer);
        RebuildRows();
        UpdateStatus();
    }

    private byte[] CreateBlankBuffer()
    {
        var size = CurrentChip().SizeBytes;
        var buffer = new byte[size];
        Array.Fill(buffer, (byte)0xFF);
        return buffer;
    }

    private void SetActiveBuffer(byte[] buffer)
    {
        _buffer = buffer;
        if (_activeMemoryTab is not null)
        {
            _activeMemoryTab.Buffer = buffer;
            _activeMemoryTab.MeaAnalysis = null;
        }
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
            if (_activeMemoryTab is not null)
            {
                _activeMemoryTab.MeaAnalysis = null;
            }

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

    private async void ProgrammerSelectorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ProgrammerSelectorCombo.SelectedItem is not ProgrammerOption)
        {
            return;
        }

        await ProbeProgrammerAsync(logWhenChanged: true, forceLog: true);
    }

    private async Task ProbeProgrammerAsync(bool logWhenChanged, bool forceLog = false)
    {
        await Task.Yield();
        var detection = ProgrammerDetectionService.DetectAvailable();

        UpdateProgrammerOptionStates(detection, logWhenChanged);
        ApplyProgrammerDetection(detection, logWhenChanged, forceLog);
    }

    private void UpdateProgrammerOptionStates(ProgrammerDetection detection, bool logWhenChanged)
    {
        foreach (var opt in _programmerOptions)
        {
            var wasConnected = opt.IsConnected;
            var isConnected = detection.IsConnected(opt.Key);
            opt.IsConnected = isConnected;
            if (logWhenChanged && opt.ShowsStatus && wasConnected != isConnected)
            {
                AppendLog($"{opt.Name} {(isConnected ? "connected" : "disconnected")}");
            }
        }
    }

    private void ApplyProgrammerDetection(ProgrammerDetection detection, bool logWhenChanged, bool forceLog)
    {
        var selectedMode = (ProgrammerSelectorCombo?.SelectedItem as ProgrammerOption)?.Key ?? "auto";

        if (selectedMode == "auto")
        {
            if (detection.T48Detected)
            {
                SetConnectedProgrammer("t48", new T48SDKProgrammer(), "XGecu T48 connected", logWhenChanged, forceLog);
                return;
            }

            if (detection.Rt809hDetected)
            {
                SetConnectedProgrammer("rt809h", new RT809HSDKProgrammer(), "RT809H connected", logWhenChanged, forceLog);
                return;
            }

            if (detection.Rt809fDetected)
            {
                SetConnectedProgrammer("rt809f", new RT809FSDKProgrammer(), "RT809F connected", logWhenChanged, forceLog);
                return;
            }

            if (detection.Ch347Detected)
            {
                SetConnectedProgrammer("ch347", new Ch347NativeProgrammer(), "CH347 connected", logWhenChanged, forceLog);
                return;
            }

            if (detection.Ch341Detected)
            {
                SetConnectedProgrammer("ch341", new ChNativeProgrammer(), "CH341 connected", logWhenChanged, forceLog);
                return;
            }
        }
        else
        {
            switch (selectedMode)
            {
                case "t48":
                    if (detection.T48Detected)
                    {
                        SetConnectedProgrammer("t48", new T48SDKProgrammer(), "XGecu T48 connected", logWhenChanged, forceLog);
                        return;
                    }
                    break;
                case "rt809f":
                    if (detection.Rt809fDetected)
                    {
                        SetConnectedProgrammer("rt809f", new RT809FSDKProgrammer(), "RT809F connected", logWhenChanged, forceLog);
                        return;
                    }
                    break;
                case "rt809h":
                    if (detection.Rt809hDetected)
                    {
                        SetConnectedProgrammer("rt809h", new RT809HSDKProgrammer(), "RT809H connected", logWhenChanged, forceLog);
                        return;
                    }
                    break;
                case "ch347":
                    if (detection.Ch347Detected)
                    {
                        SetConnectedProgrammer("ch347", new Ch347NativeProgrammer(), "CH347 connected", logWhenChanged, forceLog);
                        return;
                    }
                    break;
                case "ch341":
                    if (detection.Ch341Detected)
                    {
                        SetConnectedProgrammer("ch341", new ChNativeProgrammer(), "CH341 connected", logWhenChanged, forceLog);
                        return;
                    }
                    break;
            }
        }

        _programmer = new MockProgrammer();
        _activeProgrammerKey = "none";
        HardwareStatusText.Text = selectedMode == "auto" 
            ? "Programmer disconnected" 
            : $"{_programmerOptions.FirstOrDefault(x => x.Key == selectedMode)?.Name ?? "Programmer"} disconnected";
        UpdateProgrammerControls();
        if (forceLog)
        {
            AppendLog(HardwareStatusText.Text);
        }
    }

    private void SetConnectedProgrammer(string key, IChipProgrammer programmer, string statusText, bool logWhenChanged, bool forceLog)
    {
        _programmer = programmer;
        _activeProgrammerKey = key;
        HardwareStatusText.Text = statusText;
        UpdateProgrammerControls();
        if (forceLog)
        {
            AppendLog(statusText);
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
            AppendLog($"Detect request: reading JEDEC ID with {chip.Volts} probe profile");
            var id = await _programmer.ReadIdAsync(chip, progress);
            AppendLog($"IC ID: {BitConverter.ToString(id).Replace("-", " ")}");
            if (id.Length > 0 && !IsInvalidJedecId(id))
            {
                PlayOperationSound("Detect IC", success: true);
            }

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
        var selectedName = (ChipCombo.SelectedItem as ChipProfile)?.Name;
        _icCatalog = IcCatalogLoader.LoadSpiCatalog();
        _chips.Clear();
        foreach (var profile in _icCatalog.Select(x => x.Profile))
        {
            if (!_chips.Any(x => x.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _chips.Add(profile);
            }
        }

        ChipCombo.Items.Refresh();
        var selected = _chips.FirstOrDefault(chip => chip.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            ChipCombo.SelectedItem = selected;
        }
        else if (_chips.Count > 0 && ChipCombo.SelectedIndex < 0)
        {
            ChipCombo.SelectedIndex = 0;
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

        var readCompleted = false;
        await RunOperationAsync("Read chip", _buffer.Length, async progress =>
        {
            var startAddress = ParseStartAddress();
            AppendLog($"Read request: {FormatBytes(_buffer.Length)} from 0x{startAddress:X6}");
            SetActiveBuffer(await _programmer.ReadAsync(chip, startAddress, _buffer.Length, progress));
            RebuildRows();
            UpdateStatus();
            readCompleted = true;
        });
        if (readCompleted)
        {
            SaveCurrentBufferWithDialog();
        }
    }

    private async Task AnalyzeCurrentBufferWithMeaAsync()
    {
        if (!MeaFeatureEnabled)
        {
            return;
        }

        if (!MeaAnalyzer.IsLikelyIntelFirmware(_buffer))
        {
            AppendLog("MEA analysis skipped: maybe wrong layout or non-Intel image");
            return;
        }

        AppendLog("MEA analysis started");
        var stopwatch = Stopwatch.StartNew();
        var result = await MeaAnalyzer.AnalyzeAsync(_buffer);
        stopwatch.Stop();
        if (_activeMemoryTab is not null)
        {
            _activeMemoryTab.MeaAnalysis = result.Success ? result : null;
        }

        AppendLog(result.Success
            ? $"MEA analysis success in {FormatDuration(stopwatch.Elapsed)}{Environment.NewLine}{result.Summary}"
            : $"MEA analysis failed in {FormatDuration(stopwatch.Elapsed)}: {result.Summary}");
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

    private void ClearMe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ClearMeWindow(GetMemoryTabOptions(), ClearSingleBiosAsync, ClearDualBiosAsync, AnalyzeClearMeBiosAsync, _settings, new ClearMeCandidates([], []))
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private async Task<ClearMeCandidates> AnalyzeClearMeBiosAsync(IReadOnlyList<MemoryBufferOption> memories)
    {
        if (memories.Count == 0)
        {
            return new ClearMeCandidates([], [], "No BIOS memory selected.");
        }

        var buffer = memories.Count == 1
            ? memories[0].Buffer
            : MergeMemoryBuffers(memories);
        if (!MeaAnalyzer.IsLikelyIntelFirmware(buffer))
        {
            const string message = "Analyze skipped: the selected image may have the wrong layout or may not contain Intel firmware.";
            return new ClearMeCandidates([], [], message);
        }

        var analysis = await MeaAnalyzer.AnalyzeAsync(buffer);
        if (!analysis.Success)
        {
            return new ClearMeCandidates([], [], $"Analyze failed{Environment.NewLine}{analysis.Summary}");
        }

        var candidates = ClearMeCandidateFinder.Find(_settings, analysis.Info);
        return candidates with
        {
            AnalysisSummary = $"Analyze success{Environment.NewLine}{analysis.Summary}{Environment.NewLine}{Environment.NewLine}" +
                              $"Candidates: {candidates.MeRegions.Count} ME Region, {candidates.FitTools.Count} FIT"
        };
    }

    private static byte[] MergeMemoryBuffers(IEnumerable<MemoryBufferOption> memories)
    {
        var list = memories.ToList();
        var merged = new byte[list.Sum(memory => memory.Buffer.Length)];
        var offset = 0;
        foreach (var memory in list)
        {
            Buffer.BlockCopy(memory.Buffer, 0, merged, offset, memory.Buffer.Length);
            offset += memory.Buffer.Length;
        }

        return merged;
    }

    private IEnumerable<MemoryBufferOption> GetMemoryTabOptions() =>
        _memoryTabs.Values
            .OrderBy(tab => tab.Index)
            .Select(tab => new MemoryBufferOption(MemoryTabLabel(tab.Index), tab.Buffer.ToArray(), tab.SourceFileName));

    private async Task ClearSingleBiosAsync(MemoryBufferOption memory, string meRegionPath, IReadOnlyList<string> fitPaths, Action<string> log, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await RunDialogOperationAsync("Clear ME", null, async _ =>
        {
            log($"Clear ME request: {memory.Label}");
            var result = await ClearMeSingleBiosService.ClearAsync(memory.Buffer, meRegionPath, fitPaths, log, cancellationToken);
            stopwatch.Stop();
            var tab = AddBiosTab();
            MemoryTabControl.SelectedItem = tab;
            SetActiveBuffer(result.Bios);
            if (_activeMemoryTab is not null)
            {
                _activeMemoryTab.SourceFileName = ClearMeFileNameFor(memory);
            }

            RebuildRows();
            UpdateStatus();
            log($"Clear ME build completed: {memory.Label} -> {MemoryTabLabel(_activeMemoryTab?.Index ?? 0)} in {FormatDuration(stopwatch.Elapsed)}");
            foreach (var line in result.Summary.Split(Environment.NewLine))
            {
                log(line);
            }
            log($"Clear ME completed in {FormatDuration(stopwatch.Elapsed)}");
            var suggestedFileName = ClearMeFileNameFor(memory);
            var postClearOperation = Dispatcher.BeginInvoke(new Action(() =>
            {
                SaveCurrentBufferWithDialog(suggestedFileName);
            }));
        }, logCompletion: false, logger: log);
    }

    private async Task ClearDualBiosAsync(MemoryBufferOption memory1, MemoryBufferOption memory2, string meRegionPath, IReadOnlyList<string> fitPaths, Action<string> log, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await RunDialogOperationAsync("Clear ME", null, async _ =>
        {
            log($"Clear ME dual request: {memory1.Label} + {memory2.Label}");
            var merged = new byte[memory1.Buffer.Length + memory2.Buffer.Length];
            Buffer.BlockCopy(memory1.Buffer, 0, merged, 0, memory1.Buffer.Length);
            Buffer.BlockCopy(memory2.Buffer, 0, merged, memory1.Buffer.Length, memory2.Buffer.Length);
            var result = await ClearMeSingleBiosService.ClearAsync(merged, meRegionPath, fitPaths, log, cancellationToken);
            stopwatch.Stop();
            if (result.Bios.Length <= memory1.Buffer.Length)
            {
                throw new InvalidOperationException($"Cleared merged image is too small: {FormatBytes(result.Bios.Length)}.");
            }

            var first = result.Bios.Take(memory1.Buffer.Length).ToArray();
            var second = result.Bios.Skip(memory1.Buffer.Length).ToArray();
            var tab1 = AddMemoryTabWithBuffer(first, ClearMeFileNameFor(memory1));
            var tab2 = AddMemoryTabWithBuffer(second, ClearMeFileNameFor(memory2));
            MemoryTabControl.SelectedItem = tab1;
            log($"Clear ME dual build completed: {memory1.Label} + {memory2.Label} -> {MemoryTabLabel(_memoryTabs[tab1].Index)} + {MemoryTabLabel(_memoryTabs[tab2].Index)} in {FormatDuration(stopwatch.Elapsed)}");
            foreach (var line in result.Summary.Split(Environment.NewLine))
            {
                log(line);
            }
            log($"Clear ME completed in {FormatDuration(stopwatch.Elapsed)}");
            var fileName1 = ClearMeFileNameFor(memory1);
            var fileName2 = ClearMeFileNameFor(memory2);
            var postClearOperation = Dispatcher.BeginInvoke(new Action(() =>
            {
                MemoryTabControl.SelectedItem = tab1;
                SaveCurrentBufferWithDialog(fileName1);
                MemoryTabControl.SelectedItem = tab2;
                SaveCurrentBufferWithDialog(fileName2);
            }));
        }, logCompletion: false, logger: log);
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

        if (MessageBox.Show(this, $"Erase {chip.Name}?", "Confirm erase", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunOperationAsync("Erase chip", null, async progress =>
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

    private void MergeBios_Click(object sender, RoutedEventArgs e)
    {
        var memories = GetMemoryTabOptions().ToList();
        if (memories.Count < 2)
        {
            MessageBox.Show(this, "Need at least 2 Memory tabs to merge.", "Merge BIOS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new MergeBiosWindow(memories)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var selected = new[] { dialog.Bios1!, dialog.Bios2! };
        var merged = MergeMemoryBuffers(selected);
        var sourceName = UniqueMemoryTabFileName(MergedBiosFileNameFor(merged.Length));
        var tab = AddMemoryTabWithBuffer(merged, sourceName);
        MemoryTabControl.SelectedItem = tab;
        AppendLog($"Merge BIOS completed: {string.Join(" + ", selected.Select(memory => memory.Label))} -> {MemoryTabLabel(_memoryTabs[tab].Index)} ({FormatBytes(merged.Length)})");
        SaveCurrentBufferWithDialog(sourceName);
    }

    private void SplitBios_Click(object sender, RoutedEventArgs e)
    {
        var memories = GetMemoryTabOptions().ToList();
        if (memories.Count == 0)
        {
            MessageBox.Show(this, "Select a Memory tab first.", "Split BIOS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentMemoryLabel = _activeMemoryTab is null ? null : MemoryTabLabel(_activeMemoryTab.Index);
        var dialog = new SplitBiosWindow(memories, currentMemoryLabel)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var memory = dialog.Bios!;
        var firstLength = dialog.File1Length;
        var secondLength = dialog.File2Length;
        var first = memory.Buffer[..firstLength].ToArray();
        var second = memory.Buffer[firstLength..(firstLength + secondLength)].ToArray();
        var fileName1 = UniqueMemoryTabFileName(SplitedBiosFileNameFor(first.Length));
        var tab1 = AddMemoryTabWithBuffer(first, fileName1);
        var fileName2 = UniqueMemoryTabFileName(SplitedBiosFileNameFor(second.Length));
        var tab2 = AddMemoryTabWithBuffer(second, fileName2);
        MemoryTabControl.SelectedItem = tab1;
        AppendLog($"Split BIOS completed: {memory.Label} -> {MemoryTabLabel(_memoryTabs[tab1].Index)} ({FormatBytes(first.Length)}) + {MemoryTabLabel(_memoryTabs[tab2].Index)} ({FormatBytes(second.Length)})");
        SaveCurrentBufferWithDialog(fileName1);
        MemoryTabControl.SelectedItem = tab2;
        SaveCurrentBufferWithDialog(fileName2);
    }

    private void WindowsKeyMenuButton_Click(object sender, RoutedEventArgs e)
    {
        WindowsKeyMenuButton.ContextMenu.PlacementTarget = WindowsKeyMenuButton;
        WindowsKeyMenuButton.ContextMenu.IsOpen = true;
    }

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
        WindowsKeyMenuButton.IsEnabled = enabled;
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

    private static byte[] TrimBiosMetadata(byte[] buffer, out string reason, out int removedBytes)
    {
        var trimmed = StripXgproMetadata(buffer, out var markerOffset, out removedBytes);
        if (removedBytes > 0)
        {
            reason = $"XGecu metadata marker at 0x{markerOffset:X6}";
            return trimmed;
        }

        var targetSize = ValidBiosSizes
            .Where(size => size < buffer.Length)
            .DefaultIfEmpty(0)
            .Max();
        if (targetSize == 0)
        {
            reason = string.Empty;
            removedBytes = 0;
            return buffer;
        }

        var excess = buffer.Length - targetSize;
        if (excess <= 0 || excess > MaxTrailingMetadataBytes)
        {
            reason = string.Empty;
            removedBytes = 0;
            return buffer;
        }

        var result = new byte[targetSize];
        Buffer.BlockCopy(buffer, 0, result, 0, targetSize);
        reason = $"valid BIOS size {FormatBytes(targetSize)} with {FormatBytes(excess)} trailing bytes";
        removedBytes = excess;
        return result;
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

    private async void LoadFile_Click(object sender, RoutedEventArgs e)
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

            await LoadBufferFromFileAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            AppendLog($"Open file failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Open file", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NewBufferCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        FillFF_Click(sender, e);
    }

    private void OpenFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        LoadFile_Click(sender, e);
    }

    private void SaveFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SaveFile_Click(sender, e);
    }

    private void ExitCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Exit_Click(sender, e);
    }

    private void HexEditor_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void HexEditor_Drop(object sender, DragEventArgs e)
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

            await LoadBufferFromFileAsync(file);
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

    private Task LoadBufferFromFileAsync(string fileName)
    {
        var originalBytes = File.ReadAllBytes(fileName);
        var buffer = TrimBiosMetadata(originalBytes, out var trimReason, out var removedBytes);
        SetActiveBuffer(buffer);
        if (_activeMemoryTab is not null)
        {
            _activeMemoryTab.SourceFileName = fileName;
        }

        _currentOffset = 0;
        _searchHits.Clear();
        RebuildRows();
        UpdateStatus();
        AppendLog($"Loaded {fileName} ({FormatBytes(_buffer.Length)})");
        if (removedBytes > 0)
        {
            AppendLog($"Input file have {trimReason}. Trimmed {FormatBytes(removedBytes)}: {FormatBytes(originalBytes.Length)} -> {FormatBytes(_buffer.Length)}");
        }

        return Task.CompletedTask;
    }

    private void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentBufferWithDialog();
    }

    private void SaveCurrentBufferWithDialog(string? suggestedFileName = null)
    {
        var initialDirectory = SuggestedInitialDirectory();
        var fileName = suggestedFileName ?? SuggestedSaveFileName();
        var dialog = new SaveFileDialog
        {
            Filter = "Binary files (*.bin)|*.bin|ROM files (*.rom)|*.rom|All files (*.*)|*.*",
            InitialDirectory = initialDirectory,
            FileName = UniqueFileName(initialDirectory, fileName)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllBytes(dialog.FileName, _buffer);
        if (_activeMemoryTab is not null)
        {
            _activeMemoryTab.SourceFileName = dialog.FileName;
        }

        AppendLog($"Saved {dialog.FileName} ({FormatBytes(_buffer.Length)})");
    }

    private string SuggestedInitialDirectory()
    {
        var sourceFile = _activeMemoryTab?.SourceFileName;
        return !string.IsNullOrWhiteSpace(sourceFile) && Path.IsPathRooted(sourceFile)
            ? Path.GetDirectoryName(sourceFile) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private string SuggestedSaveFileName()
    {
        var sourceFile = _activeMemoryTab?.SourceFileName;
        return string.IsNullOrWhiteSpace(sourceFile)
            ? $"{CurrentChip().Name}.bin"
            : Path.GetFileName(sourceFile);
    }

    private string UniqueMemoryTabFileName(string fileName)
    {
        var directory = SuggestedInitialDirectory();
        var usedNames = _memoryTabs.Values
            .Select(tab => Path.GetFileName(tab.SourceFileName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = fileName;
        var index = 2;
        while (usedNames.Contains(candidate) || File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{stem}_{index}{extension}";
            index++;
        }

        return candidate;
    }

    private static string MergedBiosFileNameFor(int bytes) =>
        $"{FormatBinaryMegabytes(bytes)}_MERGED.bin";

    private static string SplitedBiosFileNameFor(int bytes) =>
        $"{FormatBinaryMegabytes(bytes)}_SPLITED.bin";

    private static string FormatBinaryMegabytes(int bytes)
    {
        const int mib = 1024 * 1024;
        if (bytes % mib == 0)
        {
            return $"{bytes / mib}MB";
        }

        return $"{bytes / (double)mib:0.##}MB";
    }

    private static string UniqueFileName(string directory, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = fileName;
        var index = 2;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{stem}_{index}{extension}";
            index++;
        }

        return candidate;
    }

    private string ClearMeFileNameFor(MemoryBufferOption memory)
    {
        var source = string.IsNullOrWhiteSpace(memory.SourceFileName)
            ? CurrentChip().Name
            : Path.GetFileNameWithoutExtension(memory.SourceFileName);
        return $"{SafeFileStem(source)}_CLEARME.bin";
    }

    private static string SafeFileStem(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        var stem = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(stem) ? "BIOS" : stem;
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

    private void HexEditor_ClearBufferRequested(object? sender, EventArgs e)
    {
        if (sender is not HexEditorView editor)
        {
            return;
        }

        var state = _memoryTabs.Values.FirstOrDefault(item => ReferenceEquals(item.Editor, editor));
        if (state is null)
        {
            return;
        }

        MemoryTabControl.SelectedItem = state.Tab;
        Array.Fill(state.Buffer, (byte)0xFF);
        state.MeaAnalysis = null;
        editor.SetBuffer(state.Buffer, OnHexCellChanged);
        RebuildRows();
        UpdateStatus();
        AppendLog($"{MemoryTabLabel(state.Index)} buffer cleared to FF");
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

        var isReadVerifyScript = string.Equals(script, "Read + verify", StringComparison.OrdinalIgnoreCase);
        if (!isReadVerifyScript &&
            MessageBox.Show(
                this,
                $"Erase {chip.Name}, then write and verify {FormatBytes(_buffer.Length)}?",
                "Confirm erase + write",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var saveAfterScript = false;
        await RunOperationAsync(script, null, async progress =>
        {
            var startAddress = ParseStartAddress();
            if (isReadVerifyScript)
            {
                AppendLog($"Script request: read and verify {FormatBytes(_buffer.Length)} from 0x{startAddress:X6}");
                AppendLog("Script stage: read started");
                TimeSpan readElapsed;
                TimeSpan verifyElapsed;
                bool readOk;
                if (_programmer is RT809HSDKProgrammer rt809hProgrammer)
                {
                    var result = await rt809hProgrammer.ReadAndVerifyAsync(
                        chip,
                        startAddress,
                        _buffer.Length,
                        progress,
                        progress,
                        (data, elapsed) =>
                        {
                            SetActiveBuffer(data);
                            readElapsed = elapsed;
                            AppendLog($"Script stage: read completed: {FormatBytes(_buffer.Length)} in {FormatDuration(readElapsed)} ({FormatSpeed(_buffer.Length, readElapsed)})");
                            RebuildRows();
                            UpdateStatus();
                        },
                        () => AppendLog("Script stage: verify started"));
                    readElapsed = result.ReadElapsed;
                    verifyElapsed = result.VerifyElapsed;
                    readOk = result.Verified;
                }
                else
                {
                    var stageWatch = Stopwatch.StartNew();
                    SetActiveBuffer(await _programmer.ReadAsync(chip, startAddress, _buffer.Length, progress));
                    stageWatch.Stop();
                    readElapsed = stageWatch.Elapsed;
                    stageWatch.Restart();
                    readOk = await _programmer.VerifyAsync(chip, startAddress, _buffer, progress);
                    stageWatch.Stop();
                    verifyElapsed = stageWatch.Elapsed;
                }

                if (_programmer is not RT809HSDKProgrammer)
                {
                    AppendLog($"Script stage: read completed: {FormatBytes(_buffer.Length)} in {FormatDuration(readElapsed)} ({FormatSpeed(_buffer.Length, readElapsed)})");
                    RebuildRows();
                    UpdateStatus();
                    AppendLog("Script stage: verify started");
                }

                AppendLog(readOk
                    ? $"Script stage: verify completed OK: {FormatBytes(_buffer.Length)} in {FormatDuration(verifyElapsed)} ({FormatSpeed(_buffer.Length, verifyElapsed)})"
                    : $"Script stage: verify failed: {FormatBytes(_buffer.Length)} in {FormatDuration(verifyElapsed)} ({FormatSpeed(_buffer.Length, verifyElapsed)})");
                AppendLog(readOk ? "Script completed: read + verify OK" : "Script completed: read + verify failed");
                saveAfterScript = true;
                return;
            }

            var skipBlankPages = SkipBlankPagesCheckBox.IsChecked == true;
            AppendLog($"Script request: erase, write and verify {FormatBytes(_buffer.Length)} at 0x{startAddress:X6}");
            await UnprotectIfRequestedAsync(chip, progress);
            AppendLog("Script stage: erase started");
            TimeSpan eraseElapsed;
            TimeSpan writeElapsed;
            TimeSpan finalVerifyElapsed;
            bool ok;
            if (_programmer is RT809HSDKProgrammer rt809hWriter)
            {
                var result = await rt809hWriter.EraseWriteVerifyAsync(
                    chip,
                    startAddress,
                    _buffer,
                    skipBlankPages,
                    progress,
                    progress,
                    progress,
                    elapsed =>
                    {
                        eraseElapsed = elapsed;
                        AppendLog($"Script stage: erase completed in {FormatDuration(eraseElapsed)}");
                    },
                    () => AppendLog("Script stage: write started"),
                    elapsed =>
                    {
                        writeElapsed = elapsed;
                        AppendLog($"Script stage: write completed: {FormatBytes(_buffer.Length)} in {FormatDuration(writeElapsed)} ({FormatSpeed(_buffer.Length, writeElapsed)})");
                    },
                    () => AppendLog("Script stage: verify started"));
                eraseElapsed = result.EraseElapsed;
                writeElapsed = result.WriteElapsed;
                finalVerifyElapsed = result.VerifyElapsed;
                ok = result.Verified;
            }
            else
            {
                var eraseWriteVerifyWatch = Stopwatch.StartNew();
                await _programmer.EraseAsync(chip, progress);
                eraseWriteVerifyWatch.Stop();
                eraseElapsed = eraseWriteVerifyWatch.Elapsed;
                AppendLog($"Script stage: erase completed in {FormatDuration(eraseElapsed)}");
                await UnprotectIfRequestedAsync(chip, progress);
                AppendLog("Script stage: write started");
                eraseWriteVerifyWatch.Restart();
                await _programmer.WriteAsync(chip, startAddress, _buffer, progress, skipBlankPages);
                eraseWriteVerifyWatch.Stop();
                writeElapsed = eraseWriteVerifyWatch.Elapsed;
                AppendLog($"Script stage: write completed: {FormatBytes(_buffer.Length)} in {FormatDuration(writeElapsed)} ({FormatSpeed(_buffer.Length, writeElapsed)})");
                AppendLog("Script stage: verify started");
                eraseWriteVerifyWatch.Restart();
                ok = await _programmer.VerifyAsync(chip, startAddress, _buffer, progress);
                eraseWriteVerifyWatch.Stop();
                finalVerifyElapsed = eraseWriteVerifyWatch.Elapsed;
            }

            AppendLog(ok
                ? $"Script stage: verify completed OK: {FormatBytes(_buffer.Length)} in {FormatDuration(finalVerifyElapsed)} ({FormatSpeed(_buffer.Length, finalVerifyElapsed)})"
                : $"Script stage: verify failed: {FormatBytes(_buffer.Length)} in {FormatDuration(finalVerifyElapsed)} ({FormatSpeed(_buffer.Length, finalVerifyElapsed)})");
            AppendLog(ok ? "Script completed: verify OK" : "Script completed: verify failed");
        });
        if (saveAfterScript)
        {
            SaveCurrentBufferWithDialog();
        }
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

    private void NewWindow_Click(object sender, RoutedEventArgs e)
    {
        OpenNewWindow();
    }

    private void NewWindowCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        OpenNewWindow();
    }

    private static void OpenNewWindow()
    {
        var window = new MainWindow();
        window.Show();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(ProjectUrl)
        {
            UseShellExecute = true
        });
    }

    private void Setting_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            _settings = dialog.Settings;
            ThemeService.Apply(_settings.ThemeName);
            ApplyThemeToMemoryEditors();
        }
    }

    private void ApplyThemeToMemoryEditors()
    {
        foreach (var state in _memoryTabs.Values)
        {
            state.Editor.Background = (Brush)FindResource("SurfaceBackgroundBrush");
            state.Editor.Foreground = (Brush)FindResource("TextBrush");
            state.Editor.InvalidateVisual();
        }
    }

    private static string AppVersion =>
        UpdateService.FormatVersion(UpdateService.CurrentVersion);

    private Task RunOperationAsync(string name, Func<IProgress<int>, Task> operation) =>
        RunOperationAsync(name, null, operation);

    private Task RunOperationAsync(string name, Func<IProgress<int>, Task> operation, bool logLifecycle) =>
        RunOperationAsync(name, null, operation, logLifecycle);

    private async Task RunDialogOperationAsync(
        string name,
        int? byteCount,
        Func<IProgress<int>, Task> operation,
        bool logCompletion = true,
        Action<string>? logger = null)
    {
        var writeLog = logger ?? AppendLog;
        if (_isBusy)
        {
            throw new InvalidOperationException("Another operation is already running.");
        }

        _isBusy = true;
        OperationStatusText.Text = name;
        OperationProgress.Value = 0;
        var progress = new Progress<int>(value => OperationProgress.Value = Math.Clamp(value, 0, 100));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await operation(progress);
            stopwatch.Stop();
            OperationProgress.Value = 100;
            OperationStatusText.Text = "Ready";
            if (logCompletion)
            {
                writeLog(byteCount is > 0
                    ? $"{name} completed: {FormatBytes(byteCount.Value)} in {FormatDuration(stopwatch.Elapsed)} ({FormatSpeed(byteCount.Value, stopwatch.Elapsed)})"
                    : $"{name} completed in {FormatDuration(stopwatch.Elapsed)}");
            }

            PlayOperationSound(name, success: true);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            OperationStatusText.Text = "Cancelled";
            writeLog($"{name} cancelled after {FormatDuration(stopwatch.Elapsed)}");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            OperationStatusText.Text = "Error";
            writeLog($"ERROR after {FormatDuration(stopwatch.Elapsed)}: {FirstLogLine(ex.Message)}");
            PlayOperationSound(name, success: false);
            throw;
        }
        finally
        {
            _isBusy = false;
        }
    }

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
            if (ex is not LoggedOperationException)
            {
                AppendLog($"ERROR after {FormatDuration(stopwatch.Elapsed)}: {FirstLogLine(ex.Message)}");
            }

            PlayOperationSound(name, success: false);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void PlayOperationSound(string operationName, bool success)
    {
        if (!_settings.SoundEnabled)
        {
            return;
        }

        if (success && operationName.Equals("Read ID", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (success && File.Exists(SuccessSoundPath))
            {
                using var player = new SoundPlayer(SuccessSoundPath);
                player.Play();
                return;
            }

            (success ? SystemSounds.Asterisk : SystemSounds.Hand).Play();
        }
        catch
        {
            // Best-effort only; sound failures must never affect programmer operations.
        }
    }

    private ChipProfile CurrentChip() => ChipCombo.SelectedItem as ChipProfile ?? _chips[0];

    private bool ConfirmVoltageAdapterIfNeeded(ChipProfile chip, string operationName)
    {
        if (_programmer is T48SDKProgrammer or RT809HSDKProgrammer)
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
            throw new LoggedOperationException();
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
            : $"Detected ID: {idText}. Multiple compatible IC profiles found");

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

        var selected = dialog.ShowDialog() == true ? dialog.SelectedCandidate : null;
        if (dialog.CatalogChanged)
        {
            ReloadIcCatalog();
        }

        if (selected is not null)
        {
            ApplyChip(selected.Profile);
            AppendLog($"Selected IC: {selected.Device}, {selected.Size}, {selected.Volts}, page {selected.Page}, {selected.Manuf}");
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

    private void AppendLogLines(string message)
    {
        foreach (var line in message.Split([Environment.NewLine], StringSplitOptions.None))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                AppendLog(line);
            }
        }
    }

    private static string FormatBytes(int bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }

        return bytes >= 1024 ? $"{bytes / 1024.0:0.##} KB" : $"{bytes} B";
    }

    private static string FirstLogLine(string message) =>
        message.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? message;

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}.{elapsed.Milliseconds / 100}s";
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}.{elapsed.Milliseconds / 100}s";
        }

        return $"{elapsed.TotalSeconds:0.0}s";
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


