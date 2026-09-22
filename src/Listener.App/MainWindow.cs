using System.Diagnostics;
using System.Globalization;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Listener.App;

internal sealed class MainWindow : Window
{
    private readonly Settings settings;
    private readonly SoundLibrary library;
    private readonly string libraryRoot;
    private readonly OverlayWindow overlay;
    private readonly ListeningController controller;
    private NativeInput? input;
    private System.Windows.Forms.NotifyIcon? tray;
    private readonly TextBlock status = Theme.Label("监听已关闭", 13, Theme.Accent);
    private readonly TextBlock diagnostics = Theme.Label("正在检查输入和采音…", 11, Theme.Muted);
    private readonly DispatcherTimer diagnosticsTimer;
    private string inputError = "";
    private readonly ComboBox devices = new();
    private HistoryWindow? historyWindow;
    private Button? historyButton;
    private readonly Slider thumbnailSize = new() { Minimum = 60, Maximum = 120, TickFrequency = 4, IsSnapToTickEnabled = true };
    private readonly TextBox hotkey = new(), x = new(), y = new(), width = new();
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
        this.settings = settings; this.library = library; libraryRoot = Path.Combine(AppContext.BaseDirectory, "library");
        Title = "行商听音助手"; Width = 640; MinWidth = 540;
        WindowPlacement.UseContentHeight(this);
        Background = Theme.Background; Foreground = Theme.Text; FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        overlay = new(libraryRoot, settings);
        controller = new(settings, library, Dispatcher);
        controller.State += s =>
        {
            status.Text = s; overlay.Panel.SetState(s);
            if (controller.Enabled && controller.Foreground) overlay.Show(); else overlay.Hide();
        };
        controller.Result += r => overlay.Panel.ShowResult(r, listening: controller.Enabled);
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
            await controller.DisposeAsync(); overlay.Close(); _ = Dispatcher.BeginInvoke(Close);
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
    }
    private UIElement Build()
    {
        var root = new DockPanel { Margin = new Thickness(28) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 24) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Theme.Button("最小化到托盘", (_, _) => { WindowState = WindowState.Minimized; }));
        DockPanel.SetDock(actions, Dock.Right); top.Children.Add(actions);
        var title = new StackPanel(); title.Children.Add(Theme.Label("行商听音助手", 28));
        title.Children.Add(Theme.Label("听见线索，看清所有可能。 · 历史记录版", 13, Theme.Muted)); top.Children.Add(title);
        var leftColumn = new DockPanel(); root.Children.Add(leftColumn);
        var navigation = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(navigation, Dock.Top); leftColumn.Children.Add(navigation); leftColumn.Children.Add(settingsPage);
        var stateBox = new StackPanel(); stateBox.Children.Add(Theme.Label("监听控制", 18)); stateBox.Children.Add(status);
        stateBox.Children.Add(Theme.Label("暗区突围专用 · 切出游戏暂停，切回后恢复。", 12, Theme.Muted));
        automatic.IsChecked = settings.AutomaticRecognition;
        automatic.Click += (_, _) =>
        {
            controller.SetAutomaticRecognition(automatic.IsChecked == true);
            try { settings.Save(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "自动识别设置未保存"); }
            UpdateDiagnostics();
        };
        stateBox.Children.Add(automatic);
        stateBox.Children.Add(Theme.Button("开启 / 关闭监听", async (_, _) => await controller.Toggle(), true));
        var diagnosticActions = new WrapPanel();
        diagnosticActions.Children.Add(Theme.Button("3 秒后试识别", async (_, _) => await controller.TestAfterCountdown()));
        diagnosticActions.Children.Add(Theme.Button("导出诊断", (_, _) => ExportDiagnostics()));
        stateBox.Children.Add(diagnosticActions);
        var resultActions = new WrapPanel();
        historyButton = Theme.Button("识别历史（0）", (_, _) => ShowHistory());
        resultActions.Children.Add(historyButton);
        resultActions.Children.Add(Theme.Button("清除当前结果", (_, _) => controller.ClearCurrentResult()));
        stateBox.Children.Add(resultActions);
        var candidateNavigation = new DockPanel { Height = 32 };
        previousCandidatePage = Theme.Button("候选上一页", (_, _) => overlay.Panel.MovePage(-1));
        nextCandidatePage = Theme.Button("候选下一页", (_, _) => overlay.Panel.MovePage(1));
        foreach (var button in new[] { nextCandidatePage, previousCandidatePage })
        { button.Padding = new Thickness(8, 3, 8, 3); DockPanel.SetDock(button, Dock.Right); candidateNavigation.Children.Add(button); }
        candidateNavigation.Children.Add(candidatePageLabel); stateBox.Children.Add(candidateNavigation);
        UpdateCandidateNavigation();
        stateBox.Children.Add(Theme.Label("匹配结果持续保留，直到新的匹配或手动清除。", 11, Theme.Muted));
        var captureConfig = new StackPanel();
        captureConfig.Children.Add(Theme.Label("采音与快捷键", 18));
        Field(captureConfig, "播放设备 · 系统回环，不使用麦克风", devices);
        captureConfig.Children.Add(Theme.Label("UU 远程时也要选择游戏实际输出的设备；听不到声音时，检查 UU 虚拟声卡。", 11, Theme.Muted));
        captureConfig.Children.Add(Theme.Button("刷新播放设备", (_, _) => RefreshDevices()));
        Field(captureConfig, "启停快捷键", hotkey); hotkey.Text = settings.Hotkey;
        captureConfig.Children.Add(Theme.Button("保存设置", SaveSettings, true));
        var config = new StackPanel();
        config.Children.Add(Theme.Label("浮窗外观", 18));
        var geometry = new UniformGridCompat(3);
        foreach (var (label, box, v) in new[] { ("左", x, settings.Left), ("上", y, settings.Top), ("宽", width, settings.Width) })
        { var column = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; box.Text = v.ToString(CultureInfo.InvariantCulture); Field(column, label, box); geometry.Children.Add(column); }
        config.Children.Add(geometry);
        config.Children.Add(Theme.Label("高度随候选数量自动伸缩；达到屏幕上限后分页，使用 Ctrl+Alt+PgUp / PgDn 翻页。", 11, Theme.Muted));
        thumbnailSize.Value = settings.ThumbnailSize; Field(config, "候选图片大小 · 小 ← → 大", thumbnailSize);
        Field(config, "透明度", opacity); opacity.Value = settings.Opacity;
        names.IsChecked = settings.ShowNames; config.Children.Add(names);
        acceleration.IsChecked = settings.HardwareAcceleration; config.Children.Add(acceleration);
        config.Children.Add(Theme.Button("保存设置", SaveSettings, true));
        var info = new StackPanel();
        var covered = library.Groups.Where(g => g.Action == "pickup" && g.Templates.Count > 0).SelectMany(g => g.ItemIds).Distinct().Count();
        info.Children.Add(Theme.Label("音效库", 18)); info.Children.Add(Theme.Label($"{covered} 件候选已接入  /  {library.Items.Count} 件目录\n{library.Version}", 12, Theme.Muted));
        info.Children.Add(Theme.Label("社区同音组资料，尚未完成独立准确率验收。", 12, Theme.Gold));
        var buttons = new WrapPanel(); buttons.Children.Add(Theme.Button("试听文件识别", Offline));
        buttons.Children.Add(Theme.Button("查看覆盖清单", (_, _) => OpenDocument("COVERAGE.md")));
        buttons.Children.Add(Theme.Button("物品缩略图鉴", (_, _) => ShowCatalog()));
        buttons.Children.Add(Theme.Button("测试报告", (_, _) => OpenDocument("TEST-REPORT.md"))); info.Children.Add(buttons);
        var diagnosticPage = new StackPanel(); diagnosticPage.Children.Add(Theme.Label("运行诊断", 18)); diagnosticPage.Children.Add(diagnostics);
        diagnosticPage.Children.Add(Theme.Button("导出诊断", (_, _) => ExportDiagnostics()));
        foreach (var (label, content) in new[] { ("监听", stateBox), ("采音", captureConfig), ("浮窗", config), ("音效库", info), ("诊断", diagnosticPage) })
        {
            var button = Theme.Button(label, (_, _) => ShowSettingsSection(label));
            sections[label] = (button, Theme.Box(content)); navigation.Children.Add(button);
        }
        ShowSettingsSection("监听");
        return root;
    }
    private void ShowSettingsSection(string name)
    {
        settingsPage.Children.Clear(); settingsPage.Children.Add(sections[name].Content);
        foreach (var (label, section) in sections)
        {
            section.Button.Background = label == name ? Theme.Accent : Theme.Brush("#263347");
            section.Button.Foreground = label == name ? Theme.Background : Theme.Text;
        }
        SizeToContent = SizeToContent.Height;
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
            input.CandidatePage += delta => { if (controller.Enabled && controller.Foreground) overlay.Panel.MovePage(delta); };
            input.SetHotkey(settings.Hotkey);
        }
        catch (Exception ex) { inputError = ex.Message; status.Text = ex.Message; }
        UpdateDiagnostics();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
        menu.Items.Add("开启 / 关闭监听", null, (_, _) => Dispatcher.InvokeAsync(async () => await controller.Toggle()));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        tray = new System.Windows.Forms.NotifyIcon { Text = "行商听音助手", Icon = System.Drawing.SystemIcons.Information, ContextMenuStrip = menu, Visible = true };
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
            input?.SetHotkey(hotkey.Text.Trim());
            var resume = controller.Enabled;
            await controller.Disable();
            settings.DeviceId = (devices.SelectedItem as OutputDevice)?.Id ?? ""; settings.Hotkey = hotkey.Text.Trim();
            settings.Left = nx; settings.Top = ny; settings.Width = nw; settings.Opacity = opacity.Value;
            settings.ThumbnailSize = thumbnailSize.Value;
            settings.ShowNames = names.IsChecked == true; settings.HardwareAcceleration = acceleration.IsChecked == true;
            settings.Save(); overlay.Apply(settings);
            if (resume) await controller.Toggle();
            else status.Text = "设置已保存 · 监听未开启，请点击开启或按快捷键";
            UpdateDiagnostics();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "设置未保存", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void UpdateDiagnostics()
    {
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
        var window = new Window { Owner = this, Title = "物品图鉴 · 51 件社区资料", Width = Math.Min(880, SystemParameters.WorkArea.Width),
            Background = Theme.Background, Content = catalog, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        WindowPlacement.UseContentHeight(window); window.Show();
    }
    private void ShowHistory()
    {
        if (historyWindow is not null) { historyWindow.Activate(); return; }
        historyWindow = new(controller.History, libraryRoot, settings) { Owner = this };
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
            WindowPlacement.UseContentHeight(window); window.Show();
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
        var largePages = overlay.Panel.PageCount;
        RenderSmoke(overlay, "overlay-smoke.png");
        var one = new RecognitionResult(999, RecognitionStatus.Matched, true,
            [new(library.Items[0], .9, "smoke")], 1, "布局测试 · 模拟结果");
        overlay.Panel.ShowResult(one, true); await Task.Delay(100);
        var smallHeight = overlay.ActualHeight;
        var smallPages = overlay.Panel.PageCount;
        RenderSmoke(overlay, "overlay-small-smoke.png");
        overlay.Panel.ShowResult(one with { Candidates = library.Items.Select(i => new Candidate(i, .9, "smoke")).ToArray() }, true);
        await Task.Delay(100);
        var overflowHeight = overlay.ActualHeight;
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
        JsonFile.Write(Path.Combine(AppContext.BaseDirectory, "ui-smoke.json"), new
        {
            images = library.Items.Count(i => i.Thumbnail is not null && File.Exists(Path.Combine(libraryRoot, i.Thumbnail))),
            clickThrough = (styles & 0x20) != 0, noActivate = (styles & 0x08000000) != 0,
            layered = (styles & 0x80000) != 0, topmost = overlay.Topmost,
            foregroundPreserved = preserved,
            layout = new { thumbnailSize = settings.ThumbnailSize, smallHeight, smallPages, largeHeight, largePages,
                overflowHeight, overflowPages, maximumHeight, allCandidatesReachable, shrunkAgain, overlayHasScrollViewer, overlayPageHint,
                historySmallHeight, historyLargeHeight, historyOnScreen,
                allHistoryReachable, historyPages, settingsFit,
                adaptivePassed = largeHeight > smallHeight && smallPages == 1 && overflowHeight <= maximumHeight + 1 &&
                    overflowPages > 1 && overlayPageHint && allCandidatesReachable && allHistoryReachable && settingsFit.Values.All(fit => fit) &&
                    shrunkAgain && !overlayHasScrollViewer && historyOnScreen && historyLargeHeight > historySmallHeight },
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
