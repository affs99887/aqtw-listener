using System.Diagnostics;
using System.Globalization;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Listener.App;

internal sealed partial class MainWindow : Window
{
    private readonly Settings settings;
    private SoundLibrary library;
    private string libraryRoot;
    private readonly OverlayWindow overlay;
    private readonly ListeningController controller;
    private readonly PersonalLibraryStore personal;
    private readonly OverlayWorkspace workspace;
    private bool changingMode, leavingInteraction;
    private nint returnWindow;
    private NativeInput? input;
    private System.Windows.Forms.NotifyIcon? tray;
    private readonly TextBlock status = Theme.Label("监听已关闭", 13, Theme.Accent);
    private readonly TextBlock diagnostics = Theme.Label("正在检查输入和采音…", 11, Theme.Muted);
    private readonly TextBlock libraryInfo = Theme.Label("", 12, Theme.Muted);
    private readonly DispatcherTimer diagnosticsTimer;
    private string inputError = "";
    private readonly ComboBox devices = new();
    private HistoryWindow? historyWindow;
    private Button? historyButton;
    private readonly Slider thumbnailSize = new() { Minimum = 60, Maximum = 120, TickFrequency = 4, IsSnapToTickEnabled = true };
    private readonly TextBox hotkey = new(), x = new(), y = new(), width = new();
    private readonly ComboBox positionMode = new();
    private readonly ComboBox fontScale = new();
    private readonly TextBox interactionHotkey = new();
    private readonly TextBlock candidatePageLabel = Theme.Label("", 11, Theme.Muted);
    private Button? previousCandidatePage, nextCandidatePage;
    private readonly Grid settingsPage = new();
    private readonly Dictionary<string, (Button Button, UIElement Content)> sections = new();
    private readonly Slider opacity = new() { Minimum = .35, Maximum = 1, TickFrequency = .05 };
    private readonly CheckBox names = new() { Content = "缩略图下显示物品名称", Foreground = Theme.Text, Margin = new Thickness(0, 10, 0, 6) };
    private readonly CheckBox acceleration = new() { Content = "硬件加速（高分辨率卡顿时启用，重启生效）", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 4, 0, 8) };
    private readonly CheckBox automatic = new() { Content = "声音自动识别（推荐 UU 远程使用）", Foreground = Theme.Text, Margin = new Thickness(0, 6, 0, 8) };
    private bool closing;
    public MainWindow(Settings settings, SoundLibrary library)
    {
        this.settings = settings;
        personal = new(Path.Combine(AppContext.BaseDirectory, "library"), Path.Combine(AppContext.BaseDirectory, "local-data", "personal"),
            Path.Combine(AppContext.BaseDirectory, "engine", "Listener.Engine.exe"));
        var active = personal.ResolveActive(); this.library = active.Library; libraryRoot = active.Root;
        Title = "行商听音助手"; Width = Math.Min(900, SystemParameters.WorkArea.Width); MinWidth = Math.Min(760, Width);
        Height = Math.Min(720, SystemParameters.WorkArea.Height); MinHeight = Math.Min(560, Height);
        MaxHeight = SystemParameters.WorkArea.Height;
        Icon = BrandAssets.Mark;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        Background = Theme.Background; Foreground = Theme.Text; FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        overlay = new(libraryRoot, settings);
        controller = new(settings, this.library, Dispatcher, libraryRoot);
        workspace = new(controller, personal, overlay, settings, ActivateLibrary);
        overlay.ExitRequested += async () => await ExitInteraction();
        overlay.HistoryRequested += ShowHistory;
        overlay.PlacementChanged += SavePlacement;
        workspace.CaptureStarted += () =>
        {
            overlay.SetInteractive(false); overlay.SetWorkspace(workspace); overlay.Show();
            if (returnWindow != 0) NativeInput.SetForegroundWindow(returnWindow);
        };
        workspace.CaptureFinished += () =>
        {
            if (closing || leavingInteraction) return;
            overlay.SetWorkspace(workspace); overlay.SetInteractive(true);
        };
        controller.State += s =>
        {
            status.Text = s; overlay.Panel.SetState(s); RefreshListeningPresentation();
            RefreshOverlayVisibility();
        };
        controller.Result += r =>
        {
            overlay.Panel.SetLibraryRoot(controller.History.Latest?.LibraryRoot ?? libraryRoot);
            overlay.Panel.ShowResult(r, listening: controller.Enabled);
        };
        controller.Activity += overlay.Panel.SetActivity;
        Content = Build();
        overlay.Panel.PagesChanged += UpdateCandidateNavigation;
        controller.History.Changed += () => historyButton!.Content = $"识别历史（{controller.History.Entries.Count}）";
        diagnosticsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => UpdateDiagnostics(), Dispatcher);
        Loaded += OnLoaded;
        Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true; diagnosticsTimer.Stop(); input?.Dispose(); tray?.Dispose();
            workspace.Dispose(); await controller.DisposeAsync(); historyWindow?.Close(); overlay.Close(); _ = Dispatcher.BeginInvoke(Close);
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
    }
    private void RefreshOverlayVisibility()
    {
        if (closing) return;
        if (controller.Mode == AssistantMode.Interaction || controller.Foreground && (controller.Enabled || controller.Mode == AssistantMode.Learning)) overlay.Show();
        else overlay.Hide();
    }
    private async Task EnterInteraction(AnalysisSnapshot? snapshot = null, bool learn = false, bool move = false)
    {
        if (changingMode || closing) return;
        changingMode = true;
        try
        {
            leavingInteraction = false;
            if (controller.Foreground) returnWindow = NativeInput.GetForegroundWindow();
            var wasLearning = workspace.Learning;
            workspace.PauseLearning(); await controller.SetMode(AssistantMode.Interaction);
            overlay.SetWorkspace(workspace); overlay.SetInteractive(true);
            if (snapshot is not null || !wasLearning) workspace.Select(snapshot);
            if (learn) workspace.OpenLearning();
            if (move) overlay.Unlock();
        }
        catch (Exception ex) { status.Text = "浮窗操作失败 · " + ex.Message; }
        finally { changingMode = false; }
    }
    private async Task ExitInteraction()
    {
        if (changingMode || closing || controller.Mode == AssistantMode.Listening) return;
        changingMode = true; leavingInteraction = true;
        try
        {
            workspace.Stop(); overlay.SetInteractive(false);
            if (NativeInput.ForegroundBelongsToApplication() && returnWindow != 0) NativeInput.SetForegroundWindow(returnWindow);
            await controller.SetMode(AssistantMode.Listening); RefreshOverlayVisibility();
        }
        finally { changingMode = false; }
    }
    private void SavePlacement()
    {
        x.Text = settings.Left.ToString(CultureInfo.InvariantCulture); y.Text = settings.Top.ToString(CultureInfo.InvariantCulture);
        positionMode.SelectedIndex = settings.EffectivePositionMode == OverlayPositionMode.GameTopCenter ? 0 : 1;
        try { settings.Save(); } catch (Exception ex) { status.Text = "位置未保存 · " + ex.Message; }
    }
    private async Task ActivateLibrary(LibraryVersion version, bool rollback, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var old = new LibraryVersion(controller.Library, controller.LibraryRoot);
        await controller.InstallLibrary(version.Library, version.Root);
        try { token.ThrowIfCancellationRequested(); if (rollback) personal.Rollback(); else personal.Activate(version); }
        catch { await controller.InstallLibrary(old.Library, old.Root); throw; }
        library = version.Library; libraryRoot = version.Root;
        UpdateLibraryInfo();
        status.Text = "音效库已更新 · " + library.Version;
    }
    private void UpdateLibraryInfo()
    {
        var covered = library.Groups.Where(g => g.Action == "pickup" && g.Templates.Count > 0).SelectMany(g => g.ItemIds).Distinct().Count();
        libraryInfo.Text = $"{covered} 件候选已接入 / {library.Items.Count} 件目录\n{library.Version}";
        dashboardLibraryInfo.Text = $"{covered} 件候选";
    }
    private UIElement Build()
    {
        var root = BuildShell(out var navigation);
        var stateBox = BuildListeningPage();
        automatic.IsChecked = settings.AutomaticRecognition;
        automatic.Click += (_, _) =>
        {
            controller.SetAutomaticRecognition(automatic.IsChecked == true);
            try { settings.Save(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "自动识别设置未保存"); }
            UpdateDiagnostics();
        };
        UpdateCandidateNavigation();
        var captureConfig = new StackPanel();
        captureConfig.Children.Add(Theme.Label("采音与快捷键", 18));
        Field(captureConfig, "播放设备 · 系统回环，不使用麦克风", devices);
        captureConfig.Children.Add(Theme.Label("UU 远程时也要选择游戏实际输出的设备；听不到声音时，检查 UU 虚拟声卡。", 11, Theme.Muted));
        captureConfig.Children.Add(Theme.Button("刷新播放设备", (_, _) => RefreshDevices()));
        Field(captureConfig, "启停快捷键", hotkey); hotkey.Text = settings.Hotkey;
        Field(captureConfig, "浮窗操作快捷键", interactionHotkey); interactionHotkey.Text = settings.InteractionHotkey;
        captureConfig.Children.Add(Theme.Button("保存设置", SaveSettings, true));
        var placement = new StackPanel();
        placement.Children.Add(Theme.Label("浮窗位置与移动", 18));
        var positionRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var positionLabel = Theme.Label("位置", 13); positionLabel.Width = 50; positionLabel.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(positionLabel, Dock.Left); positionRow.Children.Add(positionLabel);
        positionMode.ItemsSource = new[] { "游戏顶部居中（推荐）", "自定义坐标" };
        positionMode.SelectedIndex = settings.EffectivePositionMode == OverlayPositionMode.GameTopCenter ? 0 : 1;
        positionMode.Padding = new Thickness(8, 4, 8, 4);
        positionMode.SelectionChanged += (_, _) => x.IsEnabled = y.IsEnabled = positionMode.SelectedIndex == 1;
        x.IsEnabled = y.IsEnabled = positionMode.SelectedIndex == 1;
        positionRow.Children.Add(positionMode); placement.Children.Add(positionRow);
        var geometry = new UniformGridCompat(3);
        foreach (var (label, box, v) in new[] { ("左", x, settings.Left), ("上", y, settings.Top), ("宽", width, settings.Width) })
        { var column = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; box.Text = v.ToString(CultureInfo.InvariantCulture); Field(column, label, box); geometry.Children.Add(column); }
        placement.Children.Add(geometry);
        placement.Children.Add(new WrapPanel { Children = { Theme.Button("移动浮窗", async (_, _) => await EnterInteraction(move: true)), Theme.Button("恢复顶部居中", (_, _) => overlay.RestoreTopCenter()) } });
        placement.Children.Add(Theme.Label("顶部居中跟随游戏窗口，在上方区域自适应高度；候选多时分页，Ctrl+Alt+PgUp / PgDn 翻页。", 11, Theme.Muted));
        placement.Children.Add(Theme.Button("保存设置", SaveSettings, true));
        var config = new StackPanel();
        config.Children.Add(Theme.Label("浮窗外观", 18));
        thumbnailSize.Value = settings.ThumbnailSize; Field(config, "候选图片大小 · 小 ← → 大", thumbnailSize);
        fontScale.ItemsSource = new[] { "小 · 90%", "中 · 100%", "大 · 115%" }; fontScale.SelectedIndex = settings.FontScale < 1 ? 0 : settings.FontScale > 1 ? 2 : 1;
        Field(config, "字号（独立于图片尺寸）", fontScale);
        Field(config, "透明度", opacity); opacity.Value = settings.Opacity;
        names.IsChecked = settings.ShowNames; config.Children.Add(names);
        acceleration.IsChecked = settings.HardwareAcceleration; config.Children.Add(acceleration);
        config.Children.Add(Theme.Button("保存设置", SaveSettings, true));
        var info = new StackPanel();
        UpdateLibraryInfo(); info.Children.Add(Theme.Label("音效库", 18)); info.Children.Add(libraryInfo);
        info.Children.Add(Theme.Label("社区同音组资料，尚未完成独立准确率验收。", 12, Theme.Gold));
        var buttons = new WrapPanel(); buttons.Children.Add(Theme.Button("试听文件识别", Offline));
        buttons.Children.Add(Theme.Button("补库 / 录入 / 学习", async (_, _) => await EnterInteraction(learn: true)));
        buttons.Children.Add(Theme.Button("查看覆盖清单", (_, _) => OpenDocument("COVERAGE.md")));
        buttons.Children.Add(Theme.Button("物品缩略图鉴", (_, _) => ShowCatalog()));
        buttons.Children.Add(Theme.Button("测试报告", (_, _) => OpenDocument("TEST-REPORT.md"))); info.Children.Add(buttons);
        var diagnosticPage = new StackPanel(); diagnosticPage.Children.Add(Theme.Label("运行诊断", 18)); diagnosticPage.Children.Add(diagnostics);
        diagnosticPage.Children.Add(Theme.Button("导出诊断", (_, _) => ExportDiagnostics()));
        foreach (var (label, content) in new[] { ("监听", stateBox), ("采音", captureConfig), ("位置", placement), ("浮窗", config), ("音效库", info), ("诊断", diagnosticPage) })
        {
            var button = NavigationButton(label);
            sections[label] = (button, label == "监听" ? content : Theme.Box(content, 22)); navigation.Children.Add(button);
        }
        ShowSettingsSection("监听");
        return root;
    }
    private void ShowSettingsSection(string name)
    {
        settingsPage.Children.Clear(); settingsPage.Children.Add(sections[name].Content);
        foreach (var (label, section) in sections)
        {
            section.Button.Background = label == name ? Theme.Selected : Brushes.Transparent;
            section.Button.Foreground = label == name ? Theme.Accent : Theme.Muted;
            section.Button.BorderBrush = label == name ? Theme.Brush("#63746B") : Brushes.Transparent;
        }
        pageHeading.Text = SectionTitle(name);
        pageDescription.Text = SectionDescription(name);
    }
    private static void Field(Panel panel, string label, Control control)
    {
        panel.Children.Add(Theme.Label(label, 12, Theme.Muted)); control.Margin = new Thickness(0, 0, 0, 12);
        control.FontSize = 13; control.Padding = new Thickness(8, 6, 8, 6); panel.Children.Add(control);
    }
    private void RefreshDevices()
    {
        try
        {
            var outputs = LoopbackAudio.Devices();
            if (!outputs.Any(d => d.Id == settings.DeviceId)) outputs.Add(new(settings.DeviceId, "已保存的播放设备当前不可用，请重新选择"));
            devices.ItemsSource = outputs; devices.SelectedValuePath = "Id"; devices.SelectedValue = settings.DeviceId;
        }
        catch (Exception ex) { status.Text = "刷新失败 · " + ex.Message; }
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshDevices();
        // UI checks may run beside the user's listener without installing input hooks or hotkeys.
        if (Environment.GetCommandLineArgs().Contains("--ui-smoke")) { _ = ScreenshotSmoke(); return; }
        try
        {
            input = new(this); input.Toggle += async () => await controller.Toggle(); input.MouseDown += controller.Click;
            input.ForegroundChanged += controller.ForegroundChanged;
            input.InteractionToggle += async () => { if (controller.Mode == AssistantMode.Interaction) await ExitInteraction(); else await EnterInteraction(); };
            input.CandidatePage += delta => { if (controller.Enabled && controller.Foreground) overlay.Panel.MovePage(delta); };
            input.SetHotkey(settings.Hotkey); input.SetInteractionHotkey(settings.InteractionHotkey);
        }
        catch (Exception ex) { inputError = ex.Message; status.Text = ex.Message; }
        UpdateDiagnostics();
        var menu = BrandAssets.TrayMenu();
        menu.Items.Add("打开设置", null, (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
        menu.Items.Add("开启 / 关闭监听", null, (_, _) => Dispatcher.InvokeAsync(async () => await controller.Toggle()));
        menu.Items.Add("浮窗声音对比", null, (_, _) => Dispatcher.InvokeAsync(async () => await EnterInteraction()));
        menu.Items.Add("移动浮窗", null, (_, _) => Dispatcher.InvokeAsync(async () => await EnterInteraction(move: true)));
        menu.Items.Add("恢复顶部居中", null, (_, _) => Dispatcher.Invoke(overlay.RestoreTopCenter));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        tray = new System.Windows.Forms.NotifyIcon { Text = "行商听音助手 · 监听已关闭", Icon = BrandAssets.TrayIcon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        if (Environment.GetCommandLineArgs().Contains("--performance-smoke"))
            _ = PerformanceSmoke();
    }
    private async void SaveSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            double Number(TextBox box, double min, double max)
            {
                if (!double.TryParse(box.Text, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v) || v < min || v > max)
                    throw new ArgumentException($"请输入 {min} 到 {max} 之间的数值。"); return v;
            }
            var nx = Number(x, -10000, 10000); var ny = Number(y, -10000, 10000);
            var nw = Number(width, 300, 900);
            input?.SetHotkey(hotkey.Text.Trim()); input?.SetInteractionHotkey(interactionHotkey.Text.Trim());
            var resume = controller.Enabled;
            await controller.Disable();
            settings.DeviceId = (devices.SelectedItem as OutputDevice)?.Id ?? ""; settings.Hotkey = hotkey.Text.Trim();
            if (settings.Left != nx || settings.Top != ny) settings.MonitorName = null;
            settings.Left = nx; settings.Top = ny; settings.Width = nw; settings.Opacity = opacity.Value; settings.InteractionHotkey = interactionHotkey.Text.Trim();
            settings.FontScale = fontScale.SelectedIndex == 0 ? .9 : fontScale.SelectedIndex == 2 ? 1.15 : 1;
            settings.PositionMode = positionMode.SelectedIndex == 0 ? OverlayPositionMode.GameTopCenter : OverlayPositionMode.Manual;
            settings.ThumbnailSize = thumbnailSize.Value;
            settings.ShowNames = names.IsChecked == true; settings.HardwareAcceleration = acceleration.IsChecked == true;
            settings.Save(); overlay.Apply(settings); RefreshListeningPresentation();
            if (resume) await controller.Toggle();
            else status.Text = "设置已保存 · 监听未开启，请点击开启或按快捷键";
            UpdateDiagnostics();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "设置未保存", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void UpdateDiagnostics()
    {
        RefreshListeningPresentation();
        UpdateCandidateNavigation();
        var d = controller.Diagnostics();
        var audio = !d.Capturing ? "未采集（仅游戏前台采集）" :
            d.Audio.LastPacketAgeSeconds is null or > 2 ? "暂未收到音频包，请检查播放设备或播放声音" :
            d.Audio.LastSoundAgeSeconds is < 2 ? "已收到声音" : "已收到音频包，当前静音";
        var inputState = input is null ? "输入未就绪" : $"鼠标事件 {input.HookPresses} / 备用触发 {input.PollTriggers}";
        diagnostics.Text = $"前台：{(d.ForegroundProcess.Length == 0 ? "无法读取" : d.ForegroundProcess)} · 目标：{d.TargetProcess}\n" +
            $"{inputState} · 已接受 {d.TriggerCount} 次\n音频：{audio}\n声音分析 {d.AutomaticScans} 次 · {d.AutomaticStatus}\n最近识别：{d.LastRecognition}";
        if (inputError.Length != 0) diagnostics.Text += "\n输入提示：" + inputError;
        else if (input?.HookStatus.StartsWith("鼠标事件注册失败", StringComparison.Ordinal) == true) diagnostics.Text += "\n" + input.HookStatus;
        if (d.ManualTestStatus.Length != 0) diagnostics.Text += "\n" + d.ManualTestStatus;
        if (!string.IsNullOrEmpty(input?.PagingStatus)) diagnostics.Text += "\n" + input.PagingStatus;
        var compact = $"鼠标触发 {d.TriggerCount} 次 · {audio}\n" +
            (d.AutomaticRecognition ? $"声音分析 {d.AutomaticScans} 次 · {d.AutomaticStatus}" : "声音自动识别已关闭");
        if (d.ManualTestStatus.Length != 0) compact += "\n" + d.ManualTestStatus;
        overlay.Panel.SetDiagnostics(compact);
    }
    private void UpdateCandidateNavigation()
    {
        var multiple = overlay.Panel.PageCount > 1;
        candidatePageLabel.Text = multiple ? $"候选第 {overlay.Panel.PageIndex + 1} / {overlay.Panel.PageCount} 页" : "";
        if (previousCandidatePage is not null && nextCandidatePage is not null)
        {
            previousCandidatePage.Visibility = nextCandidatePage.Visibility = multiple ? Visibility.Visible : Visibility.Collapsed;
            previousCandidatePage.IsEnabled = overlay.Panel.PageIndex > 0;
            nextCandidatePage.IsEnabled = overlay.Panel.PageIndex + 1 < overlay.Panel.PageCount;
        }
        input?.SetPagingEnabled(multiple && controller.Enabled && controller.Foreground);
    }
    private void ExportDiagnostics()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "local-data", $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            JsonFile.Write(path, new
            {
                createdAt = DateTimeOffset.Now, listening = controller.Diagnostics(),
                input = new { mode = input?.HookStatus, hookPresses = input?.HookPresses, fallbackTriggers = input?.PollTriggers, error = inputError },
                note = "仅状态与计数，不包含录音。切回助手时暂停采音，最近识别结果和触发计数仍保留。"
            });
            MessageBox.Show(this, "诊断已保存，可将此文件提供给开发者：\n" + path, "导出诊断");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败"); }
    }
    private void PrepareOverlaySmoke()
    {
        var group = library.Groups.Single(g => g.Id == "361affd2");
        var result = new RecognitionResult(0, RecognitionStatus.Matched, true,
            group.ItemIds.Select(id => new Candidate(library.Items.Single(i => i.Id == id), .91, group.Id)).ToArray(), 0, "");
        overlay.Panel.ShowResult(result, true);
    }
    private void ShowCatalog()
    {
        var catalog = new CandidatePanel(libraryRoot, new Settings { ShowNames = true });
        catalog.SetState("物品图鉴 · 全目录，不是识别结果");
        catalog.ShowResult(new(0, RecognitionStatus.Matched, true, library.Items.Select(i => new Candidate(i, 0, "catalog")).ToArray(), 0, ""), true);
        var window = new Window { Owner = this, Title = $"物品图鉴 · {library.Items.Count} 件目录物品", Width = Math.Min(880, SystemParameters.WorkArea.Width),
            Background = Theme.Background, Content = catalog, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        BrandAssets.StyleWindow(window); WindowPlacement.UseContentHeight(window); window.Show();
    }
    private void ShowHistory()
    {
        if (historyWindow is not null) { historyWindow.Activate(); return; }
        historyWindow = new(controller.History, libraryRoot, settings, async entry =>
        {
            var snapshot = controller.Audio.Get(entry.SnapshotId);
            if (snapshot is null) { MessageBox.Show(this, "此记录的声音片段已过期，文字候选仍保留。", "声音对比"); return; }
            await EnterInteraction(snapshot);
        }) { Owner = this };
        historyWindow.Closed += (_, _) => historyWindow = null;
        historyWindow.Show();
    }
    private async void Offline(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "选择 WAV 音频进行离线识别", Filter = "WAV 音频|*.wav" };
        if (picker.ShowDialog(this) != true) return;
        await controller.Disable();
        try
        {
            var result = await Task.Run(() => { var audio = WaveAudio.Read(picker.FileName); using var r = RecognizerFactory.Create(library); return r.Recognize(audio.Samples, audio.SampleRate); });
            var results = new CandidatePanel(libraryRoot, settings);
            results.SetState("离线文件 · " + Path.GetFileName(picker.FileName)); results.ShowResult(result);
            var window = new Window { Owner = this, Title = "音频文件识别结果", Width = Math.Min(540, SystemParameters.WorkArea.Width),
                Background = Theme.Background, Content = results, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            BrandAssets.StyleWindow(window); WindowPlacement.UseContentHeight(window); window.Show();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "音频读取失败"); }
    }
    private void OpenDocument(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", file);
        if (File.Exists(path))
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception)
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe")) { UseShellExecute = false };
                start.ArgumentList.Add(path); Process.Start(start);
            }
        }
        else MessageBox.Show(this, "请查看源码目录 docs/" + file);
    }
    private async Task ScreenshotSmoke()
    {
        PrepareOverlaySmoke(); await Task.Delay(400);
        var bmp = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "ui-smoke.png"))) encoder.Save(stream);
        var originalMaximum = MaxHeight; MaxHeight = Math.Min(originalMaximum, 672);
        var settingsFit = new Dictionary<string, bool>();
        foreach (var name in sections.Keys)
        {
            ShowSettingsSection(name); await Task.Delay(100);
            settingsFit[name] = Descendants<Button>(sections[name].Content).Where(b => b.IsVisible)
                .All(b => b.TransformToAncestor(this).TransformBounds(new Rect(b.RenderSize)).Bottom <= ActualHeight + 1);
            if (name == "浮窗") RenderSmoke(this, "settings-smoke.png");
        }
        MaxHeight = originalMaximum; ShowSettingsSection("监听");
        var foregroundBefore = NativeInput.GetForegroundWindow();
        overlay.Show(); await Task.Delay(200);
        var styles = NativeInput.OverlayStyles(overlay);
        var preserved = foregroundBefore == NativeInput.GetForegroundWindow();
        var largeHeight = overlay.ActualHeight;
        var largeBounds = NativeInput.WindowPixels(overlay);
        var viewport = overlay.PlacementViewport;
        var largePages = overlay.Panel.PageCount;
        RenderSmoke(overlay, "overlay-smoke.png");
        var one = new RecognitionResult(999, RecognitionStatus.Matched, true,
            [new(library.Items[0], .9, "smoke")], 1, "布局测试 · 模拟结果");
        overlay.Panel.ShowResult(one, true); await Task.Delay(100);
        var smallHeight = overlay.ActualHeight;
        var smallBounds = NativeInput.WindowPixels(overlay);
        var centered = Math.Abs((smallBounds.Left + smallBounds.Width / 2) - (viewport.Left + viewport.Width / 2)) <= 2;
        var topAnchored = Math.Abs(smallBounds.Top - largeBounds.Top) <= 1;
        var smallPages = overlay.Panel.PageCount;
        RenderSmoke(overlay, "overlay-small-smoke.png");
        overlay.Panel.ShowResult(one with { Candidates = library.Items.Select(i => new Candidate(i, .9, "smoke")).ToArray() }, true);
        await Task.Delay(100);
        var overflowHeight = overlay.ActualHeight;
        var staysAboveCenter = NativeInput.WindowPixels(overlay).Bottom <= viewport.Top + viewport.Height / 3 + 2;
        var overflowPages = overlay.Panel.PageCount;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        overlay.UpdateLayout();
        var overlayPageHint = Descendants<TextBlock>(overlay.Panel).Any(t => t.Text.Contains("PgUp"));
        var visibleIds = new List<string>();
        for (var page = 0; page < overflowPages; page++)
        {
            visibleIds.AddRange(overlay.Panel.VisibleIds);
            if (page == 0) RenderSmoke(overlay, "overlay-paged-smoke.png");
            overlay.Panel.MovePage(1); await Task.Delay(50);
        }
        var allCandidatesReachable = visibleIds.Count == library.Items.Count && visibleIds.Distinct().Count() == library.Items.Count;
        var maximumHeight = overlay.MaxHeight;
        var overlayHasScrollViewer = Descendants<ScrollViewer>(overlay.Panel).Any();
        overlay.Panel.ShowResult(one, true); await Task.Delay(100);
        var shrunkAgain = Math.Abs(overlay.ActualHeight - smallHeight) < 1;
        var smokeHistory = new RecognitionHistory();
        smokeHistory.Remember(one, "布局测试（模拟结果）", false, DateTimeOffset.Now);
        var group = library.Groups.Single(g => g.Id == "e9f62d39");
        smokeHistory.Remember(one with { OperationId = 1000, Candidates = group.ItemIds.Select(id =>
            new Candidate(library.Items.Single(i => i.Id == id), .9, group.Id)).ToArray() }, "布局测试（模拟结果）", false, DateTimeOffset.Now);
        var historyView = new HistoryWindow(smokeHistory, libraryRoot, settings) { Owner = this };
        historyView.Show(); await Task.Delay(150); RenderSmoke(historyView, "history-smoke.png");
        var historyLargeHeight = historyView.ActualHeight;
        var historyOnScreen = historyView.Top >= WindowPlacement.WorkArea(historyView).Top - 1 &&
            historyView.Top + historyView.ActualHeight <= WindowPlacement.WorkArea(historyView).Bottom + 1;
        historyView.Close();
        var smallHistory = new RecognitionHistory(); smallHistory.Remember(one, "模拟结果", false, DateTimeOffset.Now);
        var smallHistoryView = new HistoryWindow(smallHistory, libraryRoot, settings) { Owner = this };
        smallHistoryView.Show(); await Task.Delay(150); RenderSmoke(smallHistoryView, "history-small-smoke.png");
        var historySmallHeight = smallHistoryView.ActualHeight; smallHistoryView.Close();
        var fullHistory = new RecognitionHistory();
        for (var i = 1; i <= 100; i++) fullHistory.Remember(one with { OperationId = i }, "模拟记录", false, DateTimeOffset.Now.AddSeconds(i));
        var fullHistoryView = new HistoryWindow(fullHistory, libraryRoot, settings) { Owner = this };
        fullHistoryView.Show(); await Task.Delay(100);
        var historyIds = new List<long>(); var historyPages = fullHistoryView.RecordPageCount;
        RenderSmoke(fullHistoryView, "history-paged-smoke.png");
        for (var i = 0; i < historyPages; i++)
        { historyIds.AddRange(fullHistoryView.VisibleRecordIds); fullHistoryView.ChangeRecordPage(1); await Task.Delay(10); }
        var allHistoryReachable = historyIds.Count == 100 && historyIds.Distinct().Count() == 100;
        fullHistoryView.Close();
        var reference = ReferenceAudio.Load(libraryRoot).Samples[0];
        var sound = WaveAudio.Read(ReferenceAudio.VerifiedPath(libraryRoot, reference));
        using var smokeRecognizer = RecognizerFactory.Create(library, libraryRoot);
        var analysis = await Task.Run(() => smokeRecognizer.Analyze(sound.Samples, sound.SampleRate));
        var snapshot = controller.Audio.Add(sound, analysis, "参考录音 · 界面检查", library, libraryRoot);
        await EnterInteraction(snapshot); await Task.Delay(100);
        var interactiveStyles = NativeInput.OverlayStyles(overlay);
        var operationFit = new Dictionary<string, bool>();
        async Task CheckWorkspace(string name)
        {
            await Task.Delay(100); overlay.UpdateLayout();
            operationFit[name] = Descendants<Button>(workspace).Where(b => b.IsVisible).All(b =>
            {
                var rect = b.TransformToAncestor(overlay).TransformBounds(new Rect(b.RenderSize));
                return rect.Bottom <= overlay.ActualHeight + 1 && rect.Right <= overlay.ActualWidth + 1;
            });
            RenderSmoke(overlay, "operation-" + name + "-smoke.png");
        }
        void ClickWorkspace(string label) => Descendants<Button>(workspace).Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await CheckWorkspace("sound");
        foreach (var (label, name) in new[] { ("完整候选", "candidates"), ("个人库", "personal"), ("补库 / 学习", "learn"), ("录入新物品", "new-item") })
        { ClickWorkspace(label); await CheckWorkspace(name); }
        overlay.ToggleLock(); var unlocked = !settings.PositionLocked; overlay.ToggleLock();
        await ExitInteraction();
        var restoredStyles = NativeInput.OverlayStyles(overlay);
        JsonFile.Write(Path.Combine(AppContext.BaseDirectory, "ui-smoke.json"), new
        {
            images = library.Items.Count(i => i.Thumbnail is not null && File.Exists(Path.Combine(libraryRoot, i.Thumbnail))),
            clickThrough = (styles & 0x20) != 0, noActivate = (styles & 0x08000000) != 0,
            layered = (styles & 0x80000) != 0, topmost = overlay.Topmost,
            foregroundPreserved = preserved,
            operation = new { operationFit, unlocked, enabledInteraction = (interactiveStyles & (0x20 | 0x08000000)) == 0,
                restoredPassThrough = (restoredStyles & (0x20 | 0x08000000)) == (0x20 | 0x08000000),
                recognitionRemainedOff = !controller.Enabled },
            placement = new { mode = settings.EffectivePositionMode.ToString(), viewport, smallBounds, largeBounds,
                centered, topAnchored, staysAboveCenter },
            layout = new { thumbnailSize = settings.ThumbnailSize, smallHeight, smallPages, largeHeight, largePages,
                overflowHeight, overflowPages, maximumHeight, allCandidatesReachable, shrunkAgain, overlayHasScrollViewer, overlayPageHint,
                historySmallHeight, historyLargeHeight, historyOnScreen,
                allHistoryReachable, historyPages, settingsFit,
                adaptivePassed = largeHeight > smallHeight && smallPages == 1 && overflowHeight <= maximumHeight + 1 &&
                    overflowPages > 1 && overlayPageHint && allCandidatesReachable && allHistoryReachable && settingsFit.Values.All(fit => fit) &&
                    shrunkAgain && !overlayHasScrollViewer && historyOnScreen && historyLargeHeight > historySmallHeight &&
                    centered && topAnchored && staysAboveCenter },
            inputMode = input?.HookStatus, inputError, diagnostics = controller.Diagnostics(),
            note = "仅桌面浮窗属性与渲染检查，未在游戏中测试输入或独占全屏覆盖"
        });
        Close();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void RenderSmoke(Window window, string name)
    {
        window.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, name)); png.Save(stream);
    }
    private async Task PerformanceSmoke()
    {
        PrepareOverlaySmoke(); overlay.Show(); await Task.Delay(1000);
        using var own = Process.GetCurrentProcess();
        var workers = Process.GetProcessesByName("Listener.Engine");
        var before = own.TotalProcessorTime.TotalMilliseconds + workers.Sum(p => p.TotalProcessorTime.TotalMilliseconds);
        var clock = Stopwatch.StartNew(); await Task.Delay(5000);
        own.Refresh(); foreach (var p in workers) p.Refresh();
        var cpu = own.TotalProcessorTime.TotalMilliseconds + workers.Sum(p => p.TotalProcessorTime.TotalMilliseconds) - before;
        JsonFile.Write(Path.Combine(AppContext.BaseDirectory, "performance-smoke.json"), new
        {
            mode = "静态浮窗预览，监听关闭；非游戏运行性能", seconds = clock.Elapsed.TotalSeconds,
            rendering = System.Windows.Media.RenderOptions.ProcessRenderMode.ToString(),
            managedHeapMiB = GC.GetTotalMemory(false) / 1048576.0,
            processCount = workers.Length + 1, cpuPercentOfOneCore = cpu / clock.Elapsed.TotalMilliseconds * 100,
            totalWorkingSetMiB = (own.WorkingSet64 + workers.Sum(p => p.WorkingSet64)) / 1048576.0,
            totalPrivateBytesMiB = (own.PrivateMemorySize64 + workers.Sum(p => p.PrivateMemorySize64)) / 1048576.0,
            appWorkingSetMiB = own.WorkingSet64 / 1048576.0, engineWorkingSetMiB = workers.Sum(p => p.WorkingSet64) / 1048576.0
        });
        foreach (var p in workers) p.Dispose(); Close();
    }
    private sealed class UniformGridCompat : System.Windows.Controls.Primitives.UniformGrid
    { public UniformGridCompat(int columns) { Columns = columns; } }
}
