using System.Diagnostics;
using System.Globalization;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Listener.App;

internal sealed class MainWindow : Window
{
    private readonly Settings settings;
    private readonly SoundLibrary library;
    private readonly string libraryRoot;
    private readonly CandidatePanel preview;
    private readonly OverlayWindow overlay;
    private readonly ListeningController controller;
    private NativeInput? input;
    private System.Windows.Forms.NotifyIcon? tray;
    private readonly TextBlock status = Theme.Label("监听已关闭", 13, Theme.Accent);
    private readonly ComboBox devices = new(), processes = new();
    private readonly TextBox hotkey = new(), x = new(), y = new(), width = new(), height = new();
    private readonly Slider opacity = new() { Minimum = .35, Maximum = 1, TickFrequency = .05 };
    private readonly CheckBox names = new() { Content = "缩略图下显示物品名称", Foreground = Theme.Text, Margin = new Thickness(0, 10, 0, 6) };
    private readonly CheckBox acceleration = new() { Content = "硬件加速（高分辨率卡顿时启用，重启生效）", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 4, 0, 8) };
    private bool closing;
    public MainWindow(Settings settings, SoundLibrary library)
    {
        this.settings = settings; this.library = library; libraryRoot = Path.Combine(AppContext.BaseDirectory, "library");
        Title = "行商听音助手"; Width = 1120; Height = 830; MinWidth = 920; MinHeight = 650;
        Background = Theme.Background; Foreground = Theme.Text; FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        preview = new(libraryRoot, settings); overlay = new(libraryRoot, settings);
        controller = new(settings, library, Dispatcher);
        controller.State += s =>
        {
            status.Text = s; preview.SetState(s); overlay.Panel.SetState(s);
            if (controller.Enabled && controller.Foreground) overlay.Show(); else overlay.Hide();
        };
        controller.Result += r => { preview.ShowResult(r); overlay.Panel.ShowResult(r); };
        Content = Build();
        Loaded += OnLoaded;
        Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true; input?.Dispose(); tray?.Dispose();
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
        title.Children.Add(Theme.Label("听见线索，看清所有可能。", 13, Theme.Muted)); top.Children.Add(title);
        var grid = new Grid(); root.Children.Add(grid);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(480) });
        var left = new StackPanel(); grid.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var stateBox = new StackPanel(); stateBox.Children.Add(Theme.Label("监听控制", 18)); stateBox.Children.Add(status);
        stateBox.Children.Add(Theme.Label("仅在目标游戏前台采音。切出暂停，切回恢复。", 12, Theme.Muted));
        stateBox.Children.Add(Theme.Button("开启 / 关闭监听", async (_, _) => await controller.Toggle(), true));
        left.Children.Add(Theme.Box(stateBox));
        var config = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
        config.Children.Add(Theme.Label("采音与快捷键", 18));
        Field(config, "游戏进程 · 从正在运行的窗口中选择", processes); processes.IsEditable = true;
        Field(config, "播放设备 · 系统回环，不使用麦克风", devices);
        config.Children.Add(Theme.Button("刷新窗口和耳机", (_, _) => RefreshChoices()));
        Field(config, "启停快捷键", hotkey); hotkey.Text = settings.Hotkey;
        config.Children.Add(Theme.Label("浮窗外观", 18));
        var geometry = new UniformGridCompat(4);
        foreach (var (label, box, v) in new[] { ("左", x, settings.Left), ("上", y, settings.Top), ("宽", width, settings.Width), ("高", height, settings.Height) })
        { var column = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; box.Text = v.ToString(CultureInfo.InvariantCulture); Field(column, label, box); geometry.Children.Add(column); }
        config.Children.Add(geometry); Field(config, "透明度", opacity); opacity.Value = settings.Opacity;
        names.IsChecked = settings.ShowNames; config.Children.Add(names);
        acceleration.IsChecked = settings.HardwareAcceleration; config.Children.Add(acceleration);
        config.Children.Add(Theme.Button("保存设置", SaveSettings, true)); left.Children.Add(config);
        var info = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
        var covered = library.Groups.Where(g => g.Action == "pickup" && g.Templates.Count > 0).SelectMany(g => g.ItemIds).Distinct().Count();
        info.Children.Add(Theme.Label("音效库", 18)); info.Children.Add(Theme.Label($"{covered} 件候选已接入  /  {library.Items.Count} 件目录\n{library.Version}", 12, Theme.Muted));
        info.Children.Add(Theme.Label("社区同音组资料，尚未完成独立准确率验收。", 12, Theme.Gold));
        var buttons = new WrapPanel(); buttons.Children.Add(Theme.Button("试听文件识别", Offline));
        buttons.Children.Add(Theme.Button("查看覆盖清单", (_, _) => OpenDocument("COVERAGE.md")));
        buttons.Children.Add(Theme.Button("物品缩略图鉴", (_, _) => ShowCatalog()));
        buttons.Children.Add(Theme.Button("测试报告", (_, _) => OpenDocument("TEST-REPORT.md"))); info.Children.Add(buttons); left.Children.Add(info);
        var right = new DockPanel(); Grid.SetColumn(right, 2); grid.Children.Add(right);
        var previewHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(previewHeader, Dock.Top); right.Children.Add(previewHeader);
        var demo = Theme.Button("布局演示", (_, _) => Demo()); DockPanel.SetDock(demo, Dock.Right); previewHeader.Children.Add(demo);
        previewHeader.Children.Add(Theme.Label("浮窗预览", 16)); right.Children.Add(preview);
        return root;
    }
    private static void Field(Panel panel, string label, Control control)
    {
        panel.Children.Add(Theme.Label(label, 12, Theme.Muted)); control.Margin = new Thickness(0, 0, 0, 12);
        control.FontSize = 13; control.Padding = new Thickness(8, 6, 8, 6); panel.Children.Add(control);
    }
    private void RefreshChoices()
    {
        try
        {
            devices.ItemsSource = LoopbackAudio.Devices(); devices.SelectedValuePath = "Id"; devices.SelectedValue = settings.DeviceId;
            if (devices.SelectedItem is null) devices.SelectedIndex = 0;
            var options = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) using (p)
            { try { if (p.MainWindowHandle != IntPtr.Zero && p.Id != Environment.ProcessId) options.Add(p.ProcessName); } catch (InvalidOperationException) { } }
            processes.ItemsSource = options; processes.Text = settings.ProcessName;
        }
        catch (Exception ex) { status.Text = "刷新失败 · " + ex.Message; }
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshChoices();
        try
        {
            input = new(this); input.Toggle += async () => await controller.Toggle(); input.MouseDown += controller.Click;
            input.ForegroundChanged += controller.ForegroundChanged;
            input.SetHotkey(settings.Hotkey);
        }
        catch (Exception ex) { status.Text = ex.Message; }
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
        menu.Items.Add("开启 / 关闭监听", null, (_, _) => Dispatcher.InvokeAsync(async () => await controller.Toggle()));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        tray = new System.Windows.Forms.NotifyIcon { Text = "行商听音助手", Icon = System.Drawing.SystemIcons.Information, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        if (Environment.GetCommandLineArgs().Contains("--ui-smoke"))
            _ = ScreenshotSmoke();
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
            var nw = Number(width, 300, 900); var nh = Number(height, 340, 1200);
            input?.SetHotkey(hotkey.Text.Trim());
            await controller.Disable(); settings.ProcessName = Path.GetFileNameWithoutExtension(processes.Text.Trim());
            settings.DeviceId = (devices.SelectedItem as OutputDevice)?.Id ?? ""; settings.Hotkey = hotkey.Text.Trim();
            settings.Left = nx; settings.Top = ny; settings.Width = nw; settings.Height = nh; settings.Opacity = opacity.Value;
            settings.ShowNames = names.IsChecked == true; settings.HardwareAcceleration = acceleration.IsChecked == true;
            settings.Save(); overlay.Apply(settings); Demo(); status.Text = "设置已保存";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "设置未保存", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    public void Demo()
    {
        var group = library.Groups.Single(g => g.Id == "361affd2");
        var result = new RecognitionResult(0, RecognitionStatus.Matched, true,
            group.ItemIds.Select(id => new Candidate(library.Items.Single(i => i.Id == id), .91, group.Id)).ToArray(), 0, "");
        preview.ShowResult(result, true);
        if (Environment.GetCommandLineArgs().Contains("--ui-smoke")) overlay.Panel.ShowResult(result, true);
    }
    private void ShowCatalog()
    {
        var catalog = new CandidatePanel(libraryRoot, new Settings { ShowNames = true });
        catalog.SetState("物品图鉴 · 全目录，不是识别结果");
        catalog.ShowResult(new(0, RecognitionStatus.Matched, true, library.Items.Select(i => new Candidate(i, 0, "catalog")).ToArray(), 0, ""), true);
        new Window { Owner = this, Title = "物品图鉴 · 51 件社区资料", Width = 880, Height = 760,
            Background = Theme.Background, Content = catalog, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }
    private async void Offline(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "选择 WAV 音频进行离线识别", Filter = "WAV 音频|*.wav" };
        if (picker.ShowDialog(this) != true) return;
        await controller.Disable();
        try
        {
            var result = await Task.Run(() => { var audio = WaveAudio.Read(picker.FileName); using var r = RecognizerFactory.Create(library); return r.Recognize(audio.Samples, audio.SampleRate); });
            preview.SetState("离线文件 · " + Path.GetFileName(picker.FileName)); preview.ShowResult(result);
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
        Demo(); await Task.Delay(400);
        var bmp = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "ui-smoke.png"))) encoder.Save(stream);
        var foregroundBefore = NativeInput.GetForegroundWindow();
        overlay.Show(); await Task.Delay(100);
        var styles = NativeInput.OverlayStyles(overlay);
        JsonFile.Write(Path.Combine(AppContext.BaseDirectory, "ui-smoke.json"), new
        {
            images = library.Items.Count(i => i.Thumbnail is not null && File.Exists(Path.Combine(libraryRoot, i.Thumbnail))),
            clickThrough = (styles & 0x20) != 0, noActivate = (styles & 0x08000000) != 0,
            layered = (styles & 0x80000) != 0, topmost = overlay.Topmost,
            foregroundPreserved = foregroundBefore == NativeInput.GetForegroundWindow(),
            note = "仅桌面浮窗属性与渲染检查，未在游戏中测试输入或独占全屏覆盖"
        });
        var floating = new RenderTargetBitmap((int)overlay.Width, (int)overlay.Height, 96, 96, PixelFormats.Pbgra32); floating.Render(overlay);
        var floatingEncoder = new PngBitmapEncoder(); floatingEncoder.Frames.Add(BitmapFrame.Create(floating));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "overlay-smoke.png"))) floatingEncoder.Save(stream);
        Close();
    }
    private async Task PerformanceSmoke()
    {
        Demo(); await Task.Delay(1000);
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
