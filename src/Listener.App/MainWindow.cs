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
    private readonly Grid settingsPage = new();
    private readonly Dictionary<string, (Button Button, UIElement Content)> sections = new();
    private readonly Slider opacity = new() { Minimum = .35, Maximum = 1, TickFrequency = .05 };
    private readonly CheckBox acceleration = new() { Content = "硬件加速（高分辨率卡顿时启用，重启生效）", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 4, 0, 8) };
    private readonly CheckBox automatic = new() { Content = "声音自动识别（推荐 UU 远程使用）", Foreground = Theme.Text, Margin = new Thickness(0, 6, 0, 8) };
    private bool closing;
    public MainWindow(Settings settings, SoundLibrary library)
    {
        this.settings = settings;
        personal = new(Path.Combine(AppContext.BaseDirectory, "library"), Path.Combine(AppContext.BaseDirectory, "local-data", "personal"));
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
        workspace = new(controller, personal, overlay, settings, ActivateLibrary, () => EnterInteraction());
        workspace.LayoutChanged += overlay.RefreshPlacement;
        overlay.ExitRequested += async () => await ExitInteraction();
        overlay.HistoryRequested += ShowHistory;
        overlay.PlacementChanged += SavePlacement;
        overlay.AppearanceChanged += () =>
        {
            fontScale.SelectedIndex = settings.FontScale < 1 ? 0 : settings.FontScale > 1 ? 2 : 1;
            SavePlacement();
        };
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
            status.Text = s; RefreshListeningPresentation();
            var failed = s.StartsWith("采音失败", StringComparison.Ordinal) || s.StartsWith("无法采音", StringComparison.Ordinal);
            overlay.SetHeaderStatus(s, failed ? StatusTone.Failed
                : controller.Mode != AssistantMode.Listening ? StatusTone.Paused
                : !controller.Enabled ? StatusTone.Off
                : controller.Diagnostics().Capturing ? StatusTone.Listening : StatusTone.Paused);
            RefreshOverlayVisibility();
        };
        controller.Result += workspace.UpdateListeningResult;
        controller.Activity += workspace.UpdateListeningActivity;
        Content = Build();
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
        if (controller.Mode == AssistantMode.Listening)
        {
            overlay.SetListeningView();
            overlay.SetInteractive(true, activate: false);
        }
        if (controller.Mode == AssistantMode.Interaction ||
            controller.Foreground && (controller.Enabled || controller.Mode == AssistantMode.Learning)) overlay.Show();
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
            if (snapshot is not null || !wasLearning)
            {
                var latest = controller.History.Latest?.SnapshotId;
                workspace.Select(snapshot ?? (latest.HasValue ? controller.Audio.Get(latest.Value) : null));
            }
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
            await controller.SetMode(AssistantMode.Listening); workspace.RefreshAppearance(); RefreshOverlayVisibility();
        }
        finally { changingMode = false; }
    }
    private void SavePlacement()
    {
        x.Text = settings.Left.ToString(CultureInfo.InvariantCulture); y.Text = settings.Top.ToString(CultureInfo.InvariantCulture);
        positionMode.SelectedIndex = settings.EffectivePositionMode == OverlayPositionMode.GameTopCenter ? 0 : 1;
        try { if (!Environment.GetCommandLineArgs().Contains("--ui-smoke")) settings.Save(); }
        catch (Exception ex) { status.Text = "偏好未保存 · " + ex.Message; }
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
        var pickup = library.Groups.Where(g => g.Action == "pickup" && g.Templates.Count > 0).ToArray();
        var covered = pickup.SelectMany(g => g.ItemIds).Distinct().Count();
        var references = pickup.Sum(g => g.Templates.Count);
        var engine = library.PrimaryEngine == RecognizerFactory.Engine ? "局内匹配器" : library.PrimaryEngine;
        libraryInfo.Text = $"{covered} 件物品已接入 / {library.Items.Count} 件目录 · {references} 条参考\n{engine} · {library.Version}";
        dashboardLibraryInfo.Text = $"{library.Items.Count} 件物品 · {references} 条参考";
        dashboardEngineInfo.Text = $"{engine} · {library.Version}";
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
        var captureConfig = new StackPanel();
        captureConfig.Children.Add(Theme.Label("采音与快捷键", 18));
        captureConfig.Children.Add(Theme.Label("识别只采集 UAGame 进程声音，不收录 Discord／QQ／UU 等独立软件的播放声。", 11, Theme.Muted));
        Field(captureConfig, "试听播放设备 · 仅用于回放和试听原声", devices);
        captureConfig.Children.Add(Theme.Label("游戏采音不依赖此设备；试听听不到时再检查这里的播放路由。", 11, Theme.Muted));
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
        placement.Children.Add(Theme.Label("顶部居中跟随游戏窗口。候选优先向两侧展开，再压缩图片与间距；全部候选连续展示。", 11, Theme.Muted));
        placement.Children.Add(Theme.Button("保存设置", SaveSettings, true));
        var config = new StackPanel();
        config.Children.Add(Theme.Label("浮窗外观", 18));
        thumbnailSize.Value = settings.ThumbnailSize; Field(config, "候选图片大小 · 小 ← → 大", thumbnailSize);
        fontScale.ItemsSource = new[] { "小 · 90%", "中 · 100%", "大 · 115%" }; fontScale.SelectedIndex = settings.FontScale < 1 ? 0 : settings.FontScale > 1 ? 2 : 1;
        Field(config, "字号（独立于图片尺寸）", fontScale);
        Field(config, "透明度", opacity); opacity.Value = settings.Opacity;
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
            section.Button.BorderBrush = label == name ? Theme.Accent : Brushes.Transparent;
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
        if (Environment.GetCommandLineArgs().Contains("--ui-smoke")) { _ = RunScreenshotSmoke(); return; }
        overlay.SetWorkspace(workspace);
        try
        {
            input = new(this); input.Toggle += async () => await controller.Toggle(); input.MouseDown += controller.Click; input.MouseUp += controller.Release;
            input.ForegroundChanged += controller.ForegroundChanged;
            input.InteractionToggle += async () => { if (controller.Mode == AssistantMode.Interaction) await ExitInteraction(); else await EnterInteraction(); };
            input.SetHotkey(settings.Hotkey); input.SetInteractionHotkey(settings.InteractionHotkey);
        }
        catch (Exception ex) { inputError = ex.Message; status.Text = ex.Message; }
        UpdateDiagnostics();
        var menu = BrandAssets.TrayMenu();
        menu.Items.Add("打开设置", null, (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
        menu.Items.Add("开启 / 关闭监听", null, (_, _) => Dispatcher.InvokeAsync(async () => await controller.Toggle()));
        menu.Items.Add("打开监听浮窗", null, (_, _) => Dispatcher.InvokeAsync(async () => await EnterInteraction()));
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
            settings.HardwareAcceleration = acceleration.IsChecked == true;
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
        var d = controller.Diagnostics();
        var audio = !d.Capturing ? "未采集（仅游戏前台采集）" :
            d.Audio.LastPacketAgeSeconds is null or > 2 ? "暂未收到游戏进程音频包，请在游戏内播放声音" :
            d.Audio.LastSoundAgeSeconds is < 2 ? "已收到声音" : "已收到音频包，当前静音";
        var inputState = input is null ? "输入未就绪" : $"鼠标事件 {input.HookPresses} / 备用触发 {input.PollTriggers}";
        diagnostics.Text = $"前台：{(d.ForegroundProcess.Length == 0 ? "无法读取" : d.ForegroundProcess)} · 目标：{d.TargetProcess}\n" +
            $"{inputState} · 已接受 {d.TriggerCount} 次\n音频：{audio}\n声音分析 {d.AutomaticScans} 次 · {d.AutomaticStatus}\n最近识别：{d.LastRecognition}";
        if (inputError.Length != 0) diagnostics.Text += "\n输入提示：" + inputError;
        else if (input?.HookStatus.StartsWith("鼠标事件注册失败", StringComparison.Ordinal) == true) diagnostics.Text += "\n" + input.HookStatus;
        if (d.ManualTestStatus.Length != 0) diagnostics.Text += "\n" + d.ManualTestStatus;
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
        var result = new RecognitionResult(0, RecognitionStatus.Matched, true,
            PreviewScenarios.Thirteen(library, .91), 0, "");
        overlay.Panel.ShowResult(result, true);
    }
    private void ShowCatalog()
    {
        var catalog = new CandidatePanel(libraryRoot, new Settings { ShowNames = true });
        var pending = library.Items.Count(item => !library.Groups.Any(group => group.ItemIds.Contains(item.Id) && group.Templates.Count > 0));
        catalog.SetState($"物品图鉴 · {pending} 件待补拾取音效");
        catalog.ShowResult(new(0, RecognitionStatus.Matched, true, library.Items.Select(i => new Candidate(i, 0, "catalog")).ToArray(), 0, ""), true);
        var window = new Window { Owner = this, Title = $"物品图鉴 · {library.Items.Count} 件目录物品", Width = Math.Min(880, SystemParameters.WorkArea.Width),
            Height = Math.Min(760, SystemParameters.WorkArea.Height * .9), Background = Theme.Background,
            Content = new ScrollViewer { Content = catalog, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        BrandAssets.StyleWindow(window); window.Show();
    }
    private void ShowHistory()
    {
        if (historyWindow is not null) { historyWindow.Activate(); return; }
        historyWindow = new(controller.History, libraryRoot, settings, async entry =>
        {
            var snapshot = controller.Audio.Get(entry.SnapshotId);
            if (snapshot is null) { MessageBox.Show(this, "此记录的声音片段已过期，文字候选仍保留。", "监听浮窗"); return; }
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
    private async Task RunScreenshotSmoke()
    {
        try { await ScreenshotSmoke(); }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-error.txt"), ex.ToString());
            JsonFile.Write(Path.Combine(AppContext.BaseDirectory, "ui-smoke.json"), new { passed = false, error = ex.ToString() });
            Close();
        }
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
        var preserved = foregroundBefore == NativeInput.GetForegroundWindow();
        var largeHeight = overlay.ActualHeight;
        var largeBounds = NativeInput.WindowPixels(overlay);
        var viewport = overlay.PlacementViewport;
        var largeWidth = overlay.ActualWidth;
        var commonGroupFits = !overlay.Panel.NeedsScroll;
        var commonCards = Descendants<Border>(overlay.Panel).Where(card => card.Tag is Candidate).ToArray();
        var bottomCardTop = commonCards.Max(card => card.TransformToAncestor(overlay.Panel).TransformBounds(new Rect(card.RenderSize)).Top);
        var lastCardRight = commonCards.Where(card => Math.Abs(card.TransformToAncestor(overlay.Panel).TransformBounds(new Rect(card.RenderSize)).Top - bottomCardTop) < 2)
            .Max(card => card.TransformToAncestor(overlay.Panel).TransformBounds(new Rect(card.RenderSize)).Right);
        var bottomRowRightGap = overlay.Panel.ActualWidth - lastCardRight;
        RenderSmoke(overlay, "overlay-smoke.png");
        var largest = library.Groups.OrderByDescending(g => g.ItemIds.Length).First();
        overlay.Panel.ShowResult(new(998, RecognitionStatus.Matched, true, largest.ItemIds.Select(id =>
            new Candidate(library.Items.Single(i => i.Id == id), .9, largest.Id)).ToArray(), 0, "布局演示 · 非识别结果"), true);
        await Task.Delay(120); overlay.UpdateLayout(); var largestGroupFits = !overlay.Panel.NeedsScroll;
        RenderSmoke(overlay, "overlay-largest-group-smoke.png");
        var one = new RecognitionResult(999, RecognitionStatus.Matched, true,
            [new(library.Items[0], .9, "smoke")], 1, "布局测试 · 模拟结果");
        var indexedCandidates = library.Groups
            .SelectMany(group => group.ItemIds.Select(id => new Candidate(
                library.Items.Single(item => item.Id == id), .9, group.Id)))
            .GroupBy(candidate => candidate.Item.Id)
            .Select(group => group.First()).ToArray();
        overlay.Panel.ShowResult(one, true); await Task.Delay(100);
        var smallHeight = overlay.ActualHeight;
        var smallBounds = NativeInput.WindowPixels(overlay);
        var centered = Math.Abs((smallBounds.Left + smallBounds.Width / 2) - (viewport.Left + viewport.Width / 2)) <= 2;
        var topAnchored = Math.Abs(smallBounds.Top - largeBounds.Top) <= 1;
        var smallWidth = overlay.ActualWidth;
        RenderSmoke(overlay, "overlay-small-smoke.png");
        overlay.Panel.ShowResult(one with { Candidates = indexedCandidates }, true);
        await Task.Delay(100);
        var overflowHeight = overlay.ActualHeight;
        var staysInGame = NativeInput.WindowPixels(overlay).Bottom <= viewport.Bottom + 1;
        var overflowWidth = overlay.ActualWidth;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        overlay.UpdateLayout();
        var paginationPresent = Descendants<Button>(overlay.Panel).Any(b => Equals(b.Content, "上一页") || Equals(b.Content, "下一页"));
        var visibleIds = overlay.Panel.RenderedIds;
        RenderSmoke(overlay, "overlay-full-smoke.png");
        var allCardsFit = Descendants<Border>(overlay.Panel).Where(card => card.Tag is Candidate).All(card =>
        {
            var rect = card.TransformToAncestor(overlay).TransformBounds(new Rect(card.RenderSize));
            return rect.Right <= overlay.ActualWidth + 1 && rect.Bottom <= overlay.ActualHeight + 1;
        });
        var allCandidatesReachable = visibleIds.Count == indexedCandidates.Length &&
            visibleIds.Distinct().Count() == indexedCandidates.Length && allCardsFit;
        var maximumHeight = overlay.MaxHeight;
        var overflowNeedsScroll = overlay.Panel.NeedsScroll;
        overlay.Panel.ShowResult(one, true); await Task.Delay(100);
        var shrunkAgain = Math.Abs(overlay.ActualHeight - smallHeight) < 1;
        var smokeHistory = new RecognitionHistory();
        smokeHistory.Remember(one, "布局测试（模拟结果）", false, DateTimeOffset.Now);
        smokeHistory.Remember(one with { OperationId = 1000, Candidates = PreviewScenarios.Thirteen(library, .9) }, "布局测试（模拟结果）", false, DateTimeOffset.Now);
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
        var historyIds = fullHistoryView.VisibleRecordIds;
        RenderSmoke(fullHistoryView, "history-full-smoke.png");
        var allHistoryReachable = historyIds.Count == 100 && historyIds.Distinct().Count() == 100;
        fullHistoryView.Close();
        var followHistory = new RecognitionHistory();
        followHistory.Remember(one with { OperationId = 2001, Candidates = PreviewScenarios.Thirteen(library, .96) }, "声音自动识别", true,
            DateTimeOffset.Now, audioSeconds: 100);
        followHistory.Remember(one with { OperationId = 2002 }, "声音自动识别", true, DateTimeOffset.Now.AddSeconds(.5), audioSeconds: 100.5);
        var followView = new HistoryWindow(followHistory, libraryRoot, settings) { Owner = this };
        followView.Show(); await Task.Delay(150);
        var followUpMarked = followHistory.Entries[0].FollowUp && followHistory.Latest?.Id == followHistory.Entries[1].Id &&
            Descendants<TextBlock>(followView).Any(label => label.Text == "疑似放下声");
        RenderSmoke(followView, "history-followup-smoke.png"); followView.Close();
        var sampleMenu = new ContextMenu();
        foreach (var (number, label) in new[] { (1, "目标定位拿起录音"), (2, "目标定位放下录音 1"), (3, "目标定位放下录音 2") })
            sampleMenu.Items.Add(new MenuItem { Header = $"原声 {number} · {label}", IsCheckable = true, IsChecked = number == 1 });
        var titleMenu = new ContextMenu();
        foreach (var header in new[] { "恢复顶部居中", "识别历史" }) titleMenu.Items.Add(new MenuItem { Header = header });
        RenderSmoke(sampleMenu, "menu-samples-smoke.png"); RenderSmoke(titleMenu, "menu-title-smoke.png");
        var menusStyled = new[] { sampleMenu, titleMenu }.All(menu =>
            Descendants<Border>(menu).Count(border => border.Name == "Surface") == menu.Items.Count);
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
        bool StopEnabled() => Descendants<Button>(workspace).Single(b => Equals(b.Content, "■ 停止")).IsEnabled;
        await CheckWorkspace("sound");
        var stopIdle = !StopEnabled();
        ClickWorkspace("▶ 回放"); var replayStarted = workspace.IsPlaying;
        var capturePausedDuringReplay = !controller.Diagnostics().Capturing;
        var stopWhilePlaying = StopEnabled();
        ClickWorkspace("■ 停止"); var replayStopped = !workspace.IsPlaying;
        var referenceButton = Descendants<Button>(workspace).First(b => b.Tag is Candidate && b.IsEnabled);
        referenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var referenceStarted = workspace.IsPlaying;
        ClickWorkspace("■ 停止"); var referenceStopped = !workspace.IsPlaying;
        var stopFollowsPlayback = stopIdle && stopWhilePlaying && !StopEnabled();
        foreach (var (label, name) in new[] { ("候选详情", "candidates"), ("个人库", "personal"), ("补库 / 学习", "learn"), ("录入新物品", "new-item") })
        { ClickWorkspace(label); await CheckWorkspace(name); }
        var nameField = Descendants<TextBox>(workspace).Single(t => t.IsVisible); nameField.Text = "字号保留测试";
        overlay.SetFontScale(.9); await Task.Delay(80); var smallEditorFont = nameField.FontSize;
        overlay.SetFontScale(1.15); await Task.Delay(80);
        var editorFontPreservesText = ReferenceEquals(nameField, Descendants<TextBox>(workspace).Single(t => t.IsVisible)) &&
            nameField.Text == "字号保留测试" && nameField.FontSize > smallEditorFont &&
            Descendants<FrameworkElement>(workspace).All(e => e.LayoutTransform.Value.IsIdentity && e.RenderTransform.Value.IsIdentity);
        overlay.SetFontScale(1); await Task.Delay(80);
        var pinButton = Descendants<Button>(overlay).Single(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("已固定"));
        var pinVisual = pinButton.Content;
        overlay.ToggleLock(); var unlocked = !settings.PositionLocked;
        var samePin = ReferenceEquals(pinVisual, pinButton.Content); overlay.ToggleLock();
        workspace.Select(snapshot);
        var demoNames = new[] { "热成像模块", "金豹雕像", "古董茶壶", "激光指示模块", "金狮雕像", "花瓶" };
        var demoCandidates = demoNames.Select(name => library.Items.Single(i => i.Name == name))
            .Select(item => new Candidate(item, .9, library.Groups.First(g => g.ItemIds.Contains(item.Id)).Id)).ToArray();
        var demoResult = new RecognitionResult(1001, RecognitionStatus.Matched, true, demoCandidates, 0, "布局演示 · 非识别结果");
        var demoSnapshot = controller.Audio.Add(sound, new(demoResult, analysis.Scores), "六件布局演示", library, libraryRoot);
        var fontChecks = new Dictionary<string, object>();
        var comparisonFits = true;
        foreach (var (scenario, clip) in new[] { ("two", snapshot), ("six", demoSnapshot) })
        foreach (var (label, scale) in new[] { ("small", .9), ("medium", 1.0), ("large", 1.15) })
        {
            workspace.Select(clip);
            overlay.SetFontScale(scale); await Task.Delay(150); overlay.UpdateLayout();
            var panel = Descendants<CandidatePanel>(workspace).First();
            var sampleButtonsFit = Descendants<Button>(panel).Where(b => b.Tag is Candidate).All(b =>
            {
                var rect = b.TransformToAncestor(panel).TransformBounds(new Rect(b.RenderSize));
                return rect.Top >= 0 && rect.Bottom <= panel.ActualHeight + 1 && rect.Right <= panel.ActualWidth + 1;
            });
            var toolbarButtonsFit = Descendants<Button>(panel).Where(b => b.Tag is not Candidate && b.IsVisible).All(b =>
            {
                var rect = b.TransformToAncestor(panel).TransformBounds(new Rect(b.RenderSize));
                return rect.Top >= 0 && rect.Bottom <= panel.ActualHeight + 1 && rect.Right <= panel.ActualWidth + 1;
            });
            var namesOneLine = Descendants<Border>(panel).Where(card => card.Tag is Candidate).All(card =>
            {
                var name = Descendants<TextBlock>(card).First(label => label.Text == ((Candidate)card.Tag).Item.Name);
                var line = new TextBlock { Text = name.Text, FontSize = name.FontSize, FontWeight = name.FontWeight, FontFamily = name.FontFamily };
                line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return name.ActualHeight <= line.DesiredSize.Height + 1;
            });
            comparisonFits &= !panel.NeedsScroll && sampleButtonsFit && toolbarButtonsFit && namesOneLine &&
                (scenario != "six" || panel.ThumbnailSize >= 104);
            fontChecks[scenario + "-" + label] = new { scale = settings.FontScale, overlay.ActualWidth, overlay.ActualHeight,
                noWindowScale = overlay.LayoutTransform.Value.IsIdentity,
                candidates = panel.RenderedIds.Count, panel.NeedsScroll, panel.ThumbnailSize, panel.ContentHeight, sampleButtonsFit, toolbarButtonsFit, namesOneLine };
            RenderSmoke(overlay, $"comparison-{scenario}-{label}-smoke.png");
        }
        overlay.SetFontScale(1); await Task.Delay(100); overlay.UpdateLayout();
        var thirteenResult = new RecognitionResult(1003, RecognitionStatus.Matched, true,
            PreviewScenarios.Thirteen(library), 0, "十三件布局检查");
        var thirteenSnapshot = controller.Audio.Add(sound, new(thirteenResult, analysis.Scores),
            "十三件布局检查", library, libraryRoot);
        workspace.Select(thirteenSnapshot); await Task.Delay(120); overlay.UpdateLayout();
        var thirteenPanel = Descendants<CandidatePanel>(workspace).First();
        var thirteenCards = Descendants<Border>(thirteenPanel).Where(card => card.Tag is Candidate).ToArray();
        var thirteenHeadings = Descendants<TextBlock>(thirteenPanel)
            .Where(label => new[] { "2 格", "3 格", "4 格", "6 格" }.Contains(label.Text)).ToArray();
        // Every card carries its own 差异率, fully inside the card; the summary row carries none.
        var thirteenNamesFit = thirteenCards.Length == 13 && thirteenCards.All(card =>
        {
            var name = Descendants<TextBlock>(card).FirstOrDefault(label => label.Text == ((Candidate)card.Tag).Item.Name);
            var difference = Descendants<TextBlock>(card).Where(label => label.Text.StartsWith("差异率", StringComparison.Ordinal)).ToArray();
            if (name is null || difference.Length != 1) return false;
            var bounds = card.TransformToAncestor(overlay).TransformBounds(new Rect(card.RenderSize));
            var differenceBounds = difference[0].TransformToAncestor(card).TransformBounds(new Rect(difference[0].RenderSize));
            return name.TextTrimming == TextTrimming.None && name.FontSize >= 13 &&
                name.DesiredSize.Height <= name.ActualHeight + 1 &&
                difference[0].Text == "差异率 1.0%" && differenceBounds.Bottom <= card.ActualHeight + 1 &&
                bounds.Right <= overlay.ActualWidth + 1 && bounds.Bottom <= overlay.ActualHeight + 1;
        }) && !Descendants<TextBlock>(thirteenPanel).Any(label => label.Text.Contains("匹配", StringComparison.Ordinal));
        var listeningThirteen = overlay.ActualWidth <= 930 && !thirteenPanel.NeedsScroll && thirteenNamesFit &&
            thirteenHeadings.Length == 4 &&
            Descendants<TextBlock>(thirteenPanel).Count(label => System.Windows.Automation.AutomationProperties.GetName(label)
                .StartsWith("大红概率 ", StringComparison.Ordinal)) == 4 &&
            !Descendants<TextBlock>(thirteenPanel).Any(label => label.Text is "同音候选" or "疑似");
        var thirteenMissingLabels = thirteenCards.Where(card =>
            !Descendants<TextBlock>(card).Any(label => label.Text == ((Candidate)card.Tag).Item.Name))
            .Select(card => ((Candidate)card.Tag).Item.Name).ToArray();
        var listeningThirteenDetails = new { overlay.ActualWidth, overlay.ActualHeight,
            thirteenPanel.ThumbnailSize, groups = thirteenHeadings.Select(label => label.Text).ToArray(),
            cards = thirteenCards.Length, thirteenNamesFit, thirteenMissingLabels, thirteenPanel.NeedsScroll };
        RenderSmoke(overlay, "listening-thirteen-smoke.png");
        var unevenCandidates = new[] { (Cells: 2, Count: 3), (Cells: 4, Count: 9), (Cells: 6, Count: 3) }
            .SelectMany(group => library.Items.Where(item => item.Cells == group.Cells).Take(group.Count))
            .Select(item => new Candidate(item, .96,
                library.Groups.First(group => group.ItemIds.Contains(item.Id)).Id)).ToArray();
        var unevenResult = new RecognitionResult(1004, RecognitionStatus.Matched, true,
            unevenCandidates, 0, "十五件紧凑布局检查");
        var unevenSnapshot = controller.Audio.Add(sound, new(unevenResult, analysis.Scores),
            "十五件紧凑布局检查", library, libraryRoot);
        workspace.Select(unevenSnapshot); await Task.Delay(120); overlay.UpdateLayout();
        var unevenPanel = Descendants<CandidatePanel>(workspace).First();
        var unevenCards = Descendants<Border>(unevenPanel).Where(card => card.Tag is Candidate).ToArray();
        var unevenRows = unevenCards.Select(card => Math.Round(card.TransformToAncestor(overlay)
            .TransformBounds(new Rect(card.RenderSize)).Top)).Distinct().Count();
        var unevenBands = new[] { 2, 4, 6 }.Select(cells => unevenCards
            .Where(card => ((Candidate)card.Tag).Item.Cells == cells)
            .Select(card => card.TransformToAncestor(overlay).TransformBounds(new Rect(card.RenderSize)))
            .ToArray()).ToArray();
        var groupedFifteen = unevenCards.Length == 15 && unevenRows == 5 && !unevenPanel.NeedsScroll &&
            unevenBands[0].Max(bounds => bounds.Bottom) <= unevenBands[1].Min(bounds => bounds.Top) + 1 &&
            unevenBands[1].Max(bounds => bounds.Bottom) <= unevenBands[2].Min(bounds => bounds.Top) + 1 &&
            unevenPanel.ThumbnailSize >= 96 && overlay.ActualWidth <= 930 && overlay.ActualHeight <= 760;
        var groupedFifteenDetails = new { overlay.ActualWidth, overlay.ActualHeight,
            unevenPanel.ThumbnailSize, unevenPanel.ContentHeight, rows = unevenRows,
            cards = unevenCards.Length, unevenPanel.NeedsScroll };
        RenderSmoke(overlay, "listening-fifteen-grouped-smoke.png");
        workspace.Select(demoSnapshot); await Task.Delay(100); overlay.UpdateLayout();
        var nearControlRemoved = !Descendants<CheckBox>(workspace).Any(b => Equals(b.Content, "展开相近音效"));
        var allItemCandidates = indexedCandidates;
        var allItemResult = new RecognitionResult(1002, RecognitionStatus.Matched, true,
            allItemCandidates, 0, "全候选布局检查");
        var allItemSnapshot = controller.Audio.Add(sound, new(allItemResult, analysis.Scores),
            "全候选布局检查", library, libraryRoot);
        workspace.Select(allItemSnapshot); await Task.Delay(120); overlay.UpdateLayout();
        var allItemPanel = Descendants<CandidatePanel>(workspace).First();
        var allItemCards = Descendants<Border>(allItemPanel).Where(card => card.Tag is Candidate).ToArray();
        var allItemCardBounds = allItemCards.Select(card => card.TransformToAncestor(overlay)
            .TransformBounds(new Rect(card.RenderSize))).ToArray();
        var allItemCardsFit = allItemCardBounds.All(rect => rect.Right <= overlay.ActualWidth + 1 &&
            rect.Bottom <= overlay.ActualHeight + 1);
        var listeningOnePage = !allItemPanel.NeedsScroll && allItemPanel.ThumbnailSize >= 80 &&
            !Descendants<ScrollViewer>(allItemPanel).Any() && allItemCards.Length == indexedCandidates.Length && allItemCardsFit;
        var listeningOnePageDetails = new { overlay.ActualWidth, overlay.ActualHeight,
            allItemPanel.NeedsScroll, allItemPanel.ThumbnailSize, allItemPanel.ContentHeight,
            cards = allItemCards.Length, allItemCardsFit,
            maxRight = allItemCardBounds.Max(rect => rect.Right), maxBottom = allItemCardBounds.Max(rect => rect.Bottom) };
        RenderSmoke(overlay, "listening-all-smoke.png");
        workspace.Select(demoSnapshot); await Task.Delay(80); overlay.UpdateLayout();
        var listeningSurface = Descendants<CandidatePanel>(workspace).FirstOrDefault(panel => panel.IsVisible);
        var listeningDesignVisible = listeningSurface is not null && listeningSurface.RenderedIds.Count == demoCandidates.Length &&
            Descendants<Button>(listeningSurface).Count(button => button.Tag is Candidate) == demoCandidates.Length &&
            overlay.ActualWidth >= 700 && overlay.ActualWidth <= 800 && !listeningSurface.NeedsScroll &&
            listeningSurface.ThumbnailSize >= 104;
        RenderSmoke(overlay, "listening-design-smoke.png");
        await ExitInteraction();
        // The live view as it looks in a raid: a pickup heard a moment ago, listening on.
        var liveResult = new RecognitionResult(1005, RecognitionStatus.Matched, true,
            PreviewScenarios.Thirteen(library), 0, "十三件布局检查");
        controller.History.Remember(liveResult, "布局模拟", true, DateTimeOffset.Now,
            thirteenSnapshot.Id, library.Version, libraryRoot);
        workspace.UpdateListeningResult(liveResult);
        workspace.UpdateListeningActivity(new(1003, false, "监听中 · 继续听音"));
        overlay.SetHeaderStatus("监听中 · 继续听音", StatusTone.Listening);
        overlay.Show(); await Task.Delay(100); overlay.UpdateLayout();
        var livePanel = Descendants<CandidatePanel>(workspace).First();
        var liveCards = Descendants<Border>(livePanel).Where(card => card.Tag is Candidate).ToArray();
        var liveLabels = Descendants<TextBlock>(livePanel).ToArray();
        var liveStyles = NativeInput.OverlayStyles(overlay);
        // 听样本 is enabled exactly for items with an unprocessed recording.
        var recorded = PlaybackAudio.Load(libraryRoot).Clips.SelectMany(clip => clip.GroupIds).ToHashSet();
        bool Recorded(Candidate candidate) => library.Groups.Any(group => group.ItemIds.Contains(candidate.Item.Id) && recorded.Contains(group.Id));
        var liveThirteen = ReferenceEquals(Descendants<CandidatePanel>(overlay).FirstOrDefault(panel => panel.IsVisible), livePanel) &&
            overlay.Interactive && !livePanel.NeedsScroll && liveCards.Length == liveResult.CandidateCount &&
            liveResult.Candidates.All(candidate => liveLabels.Any(label => label.Text == candidate.Item.Name)) &&
            liveLabels.Count(label => label.Text == "差异率 1.0%") == liveResult.CandidateCount &&
            !liveLabels.Any(label => label.Text.Contains("匹配", StringComparison.Ordinal)) &&
            Descendants<Button>(livePanel).Count(button => button.Tag is Candidate) == liveResult.CandidateCount &&
            Descendants<Button>(livePanel).Where(button => button.Tag is Candidate).All(button => button.IsEnabled == Recorded((Candidate)button.Tag)) &&
            liveResult.Candidates.Any(Recorded) && (liveStyles & 0x20) == 0 && (liveStyles & 0x08000000) != 0;
        var liveFresh = liveLabels.Any(label => label.Text == "刚刚" && label.IsVisible) &&
            Descendants<CandidateLayout>(livePanel).Single().Opacity == 1;
        RenderSmoke(overlay, "listening-arrival-smoke.png");
        await Task.Delay(1100); overlay.UpdateLayout();
        RenderSmoke(overlay, "listening-live-thirteen-smoke.png");
        // Ten seconds later with nothing new recognised, the same result fades.
        var staleResult = liveResult with { OperationId = 1006 };
        controller.History.Remember(staleResult, "布局模拟", true, DateTimeOffset.Now.AddSeconds(-14),
            thirteenSnapshot.Id, library.Version, libraryRoot);
        workspace.UpdateListeningResult(staleResult);
        overlay.SetHeaderStatus("监听中 · 继续听音", StatusTone.Listening);
        await Task.Delay(100); overlay.UpdateLayout();
        var stalePanel = Descendants<CandidatePanel>(workspace).First();
        var liveStale = Descendants<TextBlock>(stalePanel).Any(label => label.Text.EndsWith("秒前", StringComparison.Ordinal) && label.IsVisible) &&
            Descendants<CandidateLayout>(stalePanel).Single().Opacity < 1;
        RenderSmoke(overlay, "listening-stale-smoke.png");
        controller.History.Remember(liveResult with { OperationId = 1007 }, "布局模拟", true, DateTimeOffset.Now,
            thirteenSnapshot.Id, library.Version, libraryRoot);
        workspace.UpdateListeningResult(liveResult); await Task.Delay(100); overlay.UpdateLayout();
        livePanel = Descendants<CandidatePanel>(workspace).First();
        if (Environment.GetCommandLineArgs().Contains("--ui-hold-live")) return;
        var liveReplay = Descendants<Button>(livePanel).First(button => Equals(button.Content, "▶ 回放"));
        liveReplay.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Task.Delay(300);
        var liveReplayPausesSampling = controller.Mode == AssistantMode.Interaction && workspace.IsPlaying;
        await ExitInteraction();
        controller.History.ClearCurrent(); workspace.UpdateListeningResult(null);
        overlay.Show(); await Task.Delay(100); overlay.UpdateLayout();
        var waitingSurface = Descendants<CandidatePanel>(overlay).FirstOrDefault(panel => panel.IsVisible);
        var waitingViewVisible = ReferenceEquals(waitingSurface, Descendants<CandidatePanel>(workspace).FirstOrDefault()) && overlay.Interactive &&
            overlay.ActualWidth <= 900;
        RenderSmoke(overlay, "listening-waiting-smoke.png");
        var restoredStyles = NativeInput.OverlayStyles(overlay);
        var adaptivePassed = largeWidth >= smallWidth && overflowHeight <= maximumHeight + 1 &&
            !paginationPresent && allCandidatesReachable && allHistoryReachable && settingsFit.Values.All(fit => fit) &&
            shrunkAgain && historyOnScreen && centered && topAnchored && staysInGame && !overflowNeedsScroll;
        var passed = adaptivePassed && comparisonFits && listeningDesignVisible && waitingViewVisible && liveThirteen && liveReplayPausesSampling && listeningThirteen && groupedFifteen && listeningOnePage && commonGroupFits && bottomRowRightGap <= 32 && largestGroupFits && editorFontPreservesText && operationFit.Values.All(fit => fit) && samePin && unlocked &&
            nearControlRemoved && replayStarted && replayStopped && referenceStarted && referenceStopped && capturePausedDuringReplay &&
            stopFollowsPlayback && liveFresh && liveStale && followUpMarked && menusStyled &&
            (liveStyles & (0x20 | 0x08000000)) == 0x08000000 && preserved &&
            (interactiveStyles & (0x20 | 0x08000000)) == 0 && (restoredStyles & (0x20 | 0x08000000)) == 0x08000000;
        JsonFile.Write(Path.Combine(AppContext.BaseDirectory, "ui-smoke.json"), new
        {
            passed,
            images = library.Items.Count(i => i.Thumbnail is not null && File.Exists(Path.Combine(libraryRoot, i.Thumbnail))),
            clickThrough = (liveStyles & 0x20) != 0, noActivate = (liveStyles & 0x08000000) != 0,
            layered = (liveStyles & 0x80000) != 0, topmost = overlay.Topmost,
            foregroundPreserved = preserved,
            operation = new { operationFit, unlocked, samePin, fontChecks, comparisonFits, listeningDesignVisible, waitingViewVisible, liveThirteen, liveReplayPausesSampling, listeningThirteen, listeningThirteenDetails, groupedFifteen, groupedFifteenDetails, listeningOnePage, listeningOnePageDetails, nearControlRemoved, editorFontPreservesText,
                replayStarted, replayStopped, referenceStarted, referenceStopped, capturePausedDuringReplay,
                stopFollowsPlayback, liveFresh, liveStale, followUpMarked, menusStyled,
                enabledInteraction = (interactiveStyles & (0x20 | 0x08000000)) == 0,
                restoredListeningControls = (restoredStyles & (0x20 | 0x08000000)) == 0x08000000,
                recognitionRemainedOff = !controller.Enabled },
            placement = new { mode = settings.EffectivePositionMode.ToString(), viewport, smallBounds, largeBounds,
                centered, topAnchored, staysInGame },
            layout = new { thumbnailSize = settings.ThumbnailSize, smallHeight, smallWidth, largeHeight, largeWidth,
                overflowHeight, overflowWidth, maximumHeight, allCandidatesReachable, allCardsFit, shrunkAgain, overflowNeedsScroll, paginationPresent,
                commonGroupFits, bottomRowRightGap, largestGroupFits,
                historySmallHeight, historyLargeHeight, historyOnScreen,
                allHistoryReachable, settingsFit,
                adaptivePassed },
            inputMode = input?.HookStatus, inputError, diagnostics = controller.Diagnostics(),
            note = "仅桌面浮窗属性与渲染检查，未在游戏中测试输入或独占全屏覆盖"
        });
        if (Environment.GetCommandLineArgs().Contains("--ui-hold")) { await EnterInteraction(demoSnapshot); return; }
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
    private static void RenderSmoke(FrameworkElement element, string name)
    {
        if (element is not Window)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
        }
        element.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(element); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, name)); png.Save(stream);
    }
    private async Task PerformanceSmoke()
    {
        PrepareOverlaySmoke(); overlay.Show(); await Task.Delay(1000);
        using var own = Process.GetCurrentProcess();
        var before = own.TotalProcessorTime.TotalMilliseconds;
        var clock = Stopwatch.StartNew(); await Task.Delay(5000);
        own.Refresh();
        var cpu = own.TotalProcessorTime.TotalMilliseconds - before;
        JsonFile.Write(Path.Combine(AppContext.BaseDirectory, "performance-smoke.json"), new
        {
            mode = "静态浮窗预览，监听关闭；非游戏运行性能", seconds = clock.Elapsed.TotalSeconds,
            rendering = System.Windows.Media.RenderOptions.ProcessRenderMode.ToString(),
            managedHeapMiB = GC.GetTotalMemory(false) / 1048576.0,
            cpuPercentOfOneCore = cpu / clock.Elapsed.TotalMilliseconds * 100,
            workingSetMiB = own.WorkingSet64 / 1048576.0, privateBytesMiB = own.PrivateMemorySize64 / 1048576.0
        });
        Close();
    }
    private sealed class UniformGridCompat : System.Windows.Controls.Primitives.UniformGrid
    { public UniformGridCompat(int columns) { Columns = columns; } }
}
