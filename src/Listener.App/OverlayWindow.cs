using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Automation;

namespace Listener.App;

internal sealed class OverlayWindow : Window
{
    private Settings settings;
    private readonly DispatcherTimer placementTimer;
    private readonly ContentControl content = new();
    private readonly DockPanel chrome = new() { Height = 48, Background = Theme.Panel };
    private readonly Button lockButton;
    private readonly System.Windows.Shapes.Path pin = new()
    {
        Data = Geometry.Parse("M 7,2 L 17,2 L 17,4 L 15,5 L 15,11 L 18,14 L 18,16 L 13,16 L 12,23 L 11,16 L 6,16 L 6,14 L 9,11 L 9,5 L 7,4 Z"),
        Width = 15, Height = 20, Stretch = Stretch.Uniform
    };
    private readonly List<(Button Button, double Scale)> fontButtons = [];
    private readonly TextBlock title = Theme.Label("行商听音助手", 13);
    private readonly TextBlock headerStatus = Theme.Label("监听中 · 等待声音", 10, Theme.Muted);
    private readonly System.Windows.Shapes.Ellipse statusDot = new() { Width = 8, Height = 8, Fill = Theme.Faint,
        Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private UIElement? workspace;
    private readonly ContentControl tiny = new();
    private readonly TextBlock tinySummary = Theme.Label("", 10);
    private bool tinyInteractive, workspaceActive;
    private Rect? lastGameViewport;
    private bool sourceReady, positioning, dragging;
    public bool Interactive { get; private set; }
    public CandidatePanel Panel { get; }
    internal Rect PlacementViewport { get; private set; }
    public event Action? ExitRequested;
    public event Action? PlacementChanged;
    public event Action? HistoryRequested;
    public event Action? AppearanceChanged;
    public OverlayWindow(string root, Settings settings)
    {
        this.settings = settings;
        Title = "行商听音助手 · 监听浮窗"; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false;
        Topmost = true; ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Microsoft YaHei UI"); UseLayoutRounding = true; SnapsToDevicePixels = true;
        Panel = new(root, settings, interactive: false, compact: true); content.Content = Panel;
        Panel.LayoutChanged += RefreshPlacement;
        var body = new DockPanel(); Content = new Border { Child = body, Background = Theme.Background,
            BorderBrush = Theme.Line, BorderThickness = new Thickness(1) };
        DockPanel.SetDock(chrome, Dock.Top); body.Children.Add(chrome); body.Children.Add(content);
        var close = IconButton(Theme.Glyph("\uE8BB", 14), "返回监听（Esc）", () => ExitRequested?.Invoke());
        close.Width = 48; close.Height = 46; close.BorderBrush = Theme.Line;
        close.BorderThickness = new Thickness(1, 0, 0, 0); close.Margin = new Thickness(0);
        DockPanel.SetDock(close, Dock.Right); chrome.Children.Add(close);
        lockButton = IconButton(pin, "固定浮窗", ToggleLock); DockPanel.SetDock(lockButton, Dock.Right); chrome.Children.Add(lockButton);
        lockButton.Width = 48; lockButton.Height = 46; lockButton.Margin = new Thickness(0);
        lockButton.BorderBrush = Theme.Line; lockButton.BorderThickness = new Thickness(1, 0, 0, 0);
        var fonts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0) };
        foreach (var (label, scale) in new[] { ("小", .9), ("中", 1.0), ("大", 1.15) })
        {
            var button = SmallButton(label, () => SetFontScale(scale)); button.Width = 34; button.Height = 28;
            button.Padding = new Thickness(0); button.Margin = new Thickness(1); button.FontSize = 11;
            button.ToolTip = label + "字号"; AutomationProperties.SetName(button, label + "字号");
            fontButtons.Add((button, scale)); fonts.Children.Add(button);
        }
        DockPanel.SetDock(fonts, Dock.Right); chrome.Children.Add(fonts);
        var sizeLabel = Theme.Label("字号", 11, Theme.Muted); sizeLabel.Margin = new Thickness(0, 0, 6, 0);
        sizeLabel.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(sizeLabel, Dock.Right); chrome.Children.Add(sizeLabel);
        title.Margin = new Thickness(0); title.FontWeight = FontWeights.Bold; title.VerticalAlignment = VerticalAlignment.Center;
        title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var mark = new Image { Source = BrandAssets.Mark, Width = 28, Height = 28, Stretch = Stretch.Uniform,
            Margin = new Thickness(10, 0, 7, 0) };
        var divider = new Border { Width = 1, Height = 18, Background = Theme.Line,
            Margin = new Thickness(12, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        headerStatus.Margin = new Thickness(0, 0, 10, 0); headerStatus.VerticalAlignment = VerticalAlignment.Center;
        headerStatus.TextWrapping = TextWrapping.NoWrap; headerStatus.TextTrimming = TextTrimming.CharacterEllipsis;
        // A dock, not a stack: the status must receive a finite width to trim.
        var heading = new DockPanel { LastChildFill = true };
        foreach (var part in new FrameworkElement[] { mark, title, divider, statusDot })
        { DockPanel.SetDock(part, Dock.Left); heading.Children.Add(part); }
        heading.Children.Add(headerStatus);
        var handle = new Border { Background = Brushes.Transparent, Child = heading };
        handle.ToolTip = "图钉变暗后可拖动标题；右键恢复顶部居中";
        var menu = new ContextMenu(); var center = new MenuItem { Header = "恢复顶部居中" };
        center.Click += (_, _) => RestoreTopCenter(); menu.Items.Add(center);
        var history = new MenuItem { Header = "识别历史" }; history.Click += (_, _) => HistoryRequested?.Invoke();
        menu.Items.Add(history); handle.ContextMenu = menu;
        handle.MouseLeftButtonDown += (_, e) => { if (Interactive && !settings.PositionLocked) { e.Handled = true; DragFromTitle(); } };
        chrome.Children.Add(handle); chrome.Visibility = Visibility.Collapsed;
        placementTimer = new(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        placementTimer.Tick += (_, _) => RefreshPlacement();
        SourceInitialized += (_, _) => { sourceReady = true; NativeInput.MakeOverlay(this); RefreshPlacement(); };
        DpiChanged += (_, _) => { if (!dragging) RefreshPlacement(); };
        SizeChanged += (_, _) => RefreshPlacement();
        IsVisibleChanged += (_, _) => { if (IsVisible) { RefreshPlacement(); placementTimer.Start(); } else placementTimer.Stop(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !dragging) { e.Handled = true; ExitRequested?.Invoke(); } };
        Deactivated += async (_, _) =>
        {
            await Task.Delay(100);
            if (Interactive && !dragging && !NativeInput.ForegroundBelongsToApplication()) ExitRequested?.Invoke();
        };
        Closed += (_, _) => placementTimer.Stop(); Apply(settings);
    }
    internal static Button SmallButton(string label, Action action)
    {
        var button = Theme.Button(label, (_, _) => action());
        button.Padding = new Thickness(7, 3, 7, 3); button.Margin = new Thickness(2); button.FontSize = 11; return button;
    }
    private static Button IconButton(UIElement icon, string label, Action action)
    {
        var button = SmallButton("", action); button.Content = icon; button.Width = 30; button.Height = 30;
        button.Padding = new Thickness(6); button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0);
        button.ToolTip = label; AutomationProperties.SetName(button, label); return button;
    }
    internal void SetFontScale(double scale)
    {
        settings.FontScale = scale; UpdateAppearance(); Panel.RefreshAppearance();
        if (workspace is OverlayWorkspace view) view.RefreshAppearance();
        RefreshPlacement(); AppearanceChanged?.Invoke();
    }
    private void UpdateAppearance()
    {
        var scale = double.IsFinite(settings.FontScale) ? Math.Clamp(settings.FontScale, .9, 1.15) : 1;
        title.FontSize = 15 * scale; headerStatus.FontSize = 10 * scale; chrome.Height = 48 * scale;
        foreach (var (button, size) in fontButtons)
        {
            var active = Math.Abs(size - scale) < .01;
            button.FontSize = 11 * scale; button.Background = active ? Theme.Selected : Theme.Raised;
            button.BorderBrush = active ? Theme.Accent : Theme.Line;
            AutomationProperties.SetHelpText(button, active ? "当前字号" : "切换字号");
        }
    }
    public void ReturnToListening() => ExitRequested?.Invoke();
    public void SetHeaderStatus(string text, StatusTone tone)
    {
        headerStatus.Text = text; headerStatus.ToolTip = text;
        HeaderTone = tone; statusDot.Fill = Theme.Tone(tone);
    }
    internal StatusTone HeaderTone { get; private set; }
    public void SetWorkspace(UIElement view)
    { workspace = view; workspaceActive = true; content.Content = view; chrome.Visibility = Visibility.Visible; RefreshPlacement(); }
    public void SetListeningView()
    {
        // The sound workspace is the only runtime overlay surface. Listening
        // changes the input state, not the visual layout or candidate source.
        workspaceActive = workspace is not null;
        content.Content = workspace ?? Panel;
        chrome.Visibility = workspaceActive ? Visibility.Visible : Visibility.Collapsed;
        RefreshPlacement();
    }
    public void SetInteractive(bool enabled, bool activate = true)
    {
        Interactive = enabled; chrome.Visibility = enabled || workspaceActive ? Visibility.Visible : Visibility.Collapsed;
        if (enabled && !activate) NativeInput.SetOverlayListeningControls(this);
        else NativeInput.SetOverlayInteractive(this, enabled);
        if (enabled && activate) { Show(); Activate(); }
        RefreshPlacement();
    }
    public void ToggleLock()
    { settings.PositionLocked = !settings.PositionLocked; UpdateLock(); PlacementChanged?.Invoke(); }
    public void Unlock() { settings.PositionLocked = false; UpdateLock(); PlacementChanged?.Invoke(); }
    private void UpdateLock()
    {
        pin.Fill = settings.PositionLocked ? Theme.Text : Theme.Muted;
        pin.Opacity = settings.PositionLocked ? 1 : .45;
        var description = settings.PositionLocked ? "已固定，点击解锁位置" : "已解锁，可拖动标题；点击固定";
        lockButton.ToolTip = description; AutomationProperties.SetName(lockButton, description);
    }
    public void RestoreTopCenter()
    {
        settings.PositionMode = OverlayPositionMode.GameTopCenter; settings.PositionLocked = true;
        UpdateLock(); RefreshPlacement(); PlacementChanged?.Invoke();
    }
    private void DragFromTitle()
    {
        var origin = NativeInput.WindowPixels(this);
        dragging = true; SizeToContent = SizeToContent.Manual; Height = ActualHeight;
        try
        {
            DragMove();
            if (!IsActive) { NativeInput.PositionOverlay(this, origin.Left, origin.Top); return; }
            var moved = NativeInput.WindowPixels(this);
            if (Math.Abs(moved.Left - origin.Left) < 1 && Math.Abs(moved.Top - origin.Top) < 1) return;
            var monitor = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            var dpi = VisualTreeHelper.GetDpi(this);
            settings.PositionMode = OverlayPositionMode.Manual; settings.MonitorName = monitor.DeviceName;
            settings.MonitorOffsetX = Math.Clamp((moved.Left - monitor.WorkingArea.Left) / dpi.DpiScaleX, 0,
                Math.Max(0, monitor.WorkingArea.Width / dpi.DpiScaleX - ActualWidth));
            settings.MonitorOffsetY = Math.Clamp((moved.Top - monitor.WorkingArea.Top) / dpi.DpiScaleY, 0,
                Math.Max(0, monitor.WorkingArea.Height / dpi.DpiScaleY - 160));
            settings.Left = Left; settings.Top = Top;
            PlacementChanged?.Invoke();
        }
        catch (InvalidOperationException) { NativeInput.PositionOverlay(this, origin.Left, origin.Top); }
        finally { dragging = false; SizeToContent = SizeToContent.Height; Height = double.NaN; RefreshPlacement(); }
    }
    public void Apply(Settings value)
    {
        settings = value; Width = Math.Clamp(settings.Width, 280, 1600); SizeToContent = SizeToContent.Height;
        Opacity = Math.Clamp(settings.Opacity, .35, 1); Panel.RefreshAppearance(); UpdateLock(); UpdateAppearance();
        if (workspace is OverlayWorkspace view) view.RefreshAppearance();
        if (!sourceReady) { Left = settings.Left; Top = settings.Top; }
        RefreshPlacement();
    }
    internal void RefreshPlacement()
    {
        if (!sourceReady || positioning || dragging) return;
        positioning = true;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            if (settings.EffectivePositionMode == OverlayPositionMode.GameTopCenter)
            {
                var game = NativeInput.ForegroundGameViewport(); if (game.HasValue) lastGameViewport = game;
                var monitor = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
                if (lastGameViewport is { } previous && !System.Windows.Forms.Screen.AllScreens.Any(s =>
                    new Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height).IntersectsWith(previous))) lastGameViewport = null;
                PlacementViewport = lastGameViewport ?? new Rect(monitor.Left, monitor.Top, monitor.Width, monitor.Height);
                if (workspaceActive)
                {
                    var maximumWidth = Math.Min(1600, PlacementViewport.Width * .94 / dpi.DpiScaleX);
                    var compactHeight = Math.Min(520, PlacementViewport.Height * .52 / dpi.DpiScaleY);
                    var candidateCount = workspace is OverlayWorkspace view ? view.CandidateCount : 0;
                    MaxHeight = candidateCount > 6 || candidateCount > 0 &&
                        (compactHeight < 430 || settings.FontScale > 1.05)
                        ? Math.Min(1000, Math.Max(1, PlacementViewport.Height / dpi.DpiScaleY - 24))
                        : compactHeight;
                    var preferred = PreferredWidth(maximumWidth, MaxHeight);
                    Width = Math.Min(maximumWidth, workspace is OverlayWorkspace sound && sound.CandidateCount > 0
                        ? preferred : Math.Max(settings.Width, preferred));
                    NativeInput.PositionOverlay(this, PlacementViewport.Left + (PlacementViewport.Width - Width * dpi.DpiScaleX) / 2,
                        PlacementViewport.Top + 16 * dpi.DpiScaleY);
                }
                else
                {
                    var limit = OverlayPlacement.TopCenter(PlacementViewport, dpi.DpiScaleX, dpi.DpiScaleY, double.MaxValue);
                    var desired = PreferredWidth(limit.Width, limit.MaximumHeight);
                    var bounds = OverlayPlacement.TopCenter(PlacementViewport, dpi.DpiScaleX, dpi.DpiScaleY, Math.Max(settings.Width, desired));
                    Width = bounds.Width; MaxHeight = bounds.MaximumHeight;
                    NativeInput.PositionOverlay(this, bounds.LeftPixels, bounds.TopPixels);
                }
            }
            else
            {
                var monitor = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.MonitorName)
                    ?? System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
                var work = monitor.WorkingArea;
                var maximumWidth = Math.Min(1600, work.Width * .94 / dpi.DpiScaleX);
                var left = settings.MonitorName is null ? settings.Left * dpi.DpiScaleX : work.Left + settings.MonitorOffsetX * dpi.DpiScaleX;
                var top = settings.MonitorName is null ? settings.Top * dpi.DpiScaleY : work.Top + settings.MonitorOffsetY * dpi.DpiScaleY;
                top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - 160 * dpi.DpiScaleY));
                var candidateCount = workspaceActive && workspace is OverlayWorkspace view ? view.CandidateCount : Panel.CandidateCount;
                var availableHeight = Math.Max(1, (work.Bottom - top - 8 * dpi.DpiScaleY) / dpi.DpiScaleY);
                MaxHeight = candidateCount > 6 || candidateCount > 0 && availableHeight < 500
                    ? Math.Min(1000, availableHeight) : Math.Min(520, availableHeight);
                var preferred = PreferredWidth(maximumWidth, MaxHeight);
                Width = Math.Min(maximumWidth, workspaceActive && workspace is OverlayWorkspace sound && sound.CandidateCount > 0
                    ? preferred : Math.Max(settings.Width, preferred));
                left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - Width * dpi.DpiScaleX));
                NativeInput.PositionOverlay(this, left, top);
            }
            var cramped = MaxHeight < 90 || Width < 220;
            if (cramped)
            {
                tinySummary.Text = workspaceActive && workspace is OverlayWorkspace view ? view.StatusText : Panel.Summary;
                if (!ReferenceEquals(content.Content, tiny) || tinyInteractive != Interactive)
                {
                    if (tiny.Content is StackPanel old) old.Children.Clear();
                    var body = new StackPanel { Margin = new Thickness(6) };
                    tinySummary.TextWrapping = TextWrapping.NoWrap; tinySummary.TextTrimming = TextTrimming.CharacterEllipsis;
                    body.Children.Add(tinySummary);
                    if (Interactive)
                        body.Children.Add(new WrapPanel { Children = { SmallButton("历史详情", () => HistoryRequested?.Invoke()), SmallButton("返回监听", () => ExitRequested?.Invoke()) } });
                    else body.Children.Add(Theme.Label(workspaceActive ? $"{settings.InteractionHotkey} 暂停采样并操作" : $"空间较小 · {settings.InteractionHotkey} 查看历史详情", 9, Theme.Muted));
                    tiny.Content = body; tiny.Background = Theme.Background; tinyInteractive = Interactive;
                    content.Content = tiny; chrome.Visibility = Visibility.Collapsed;
                }
            }
            else if (ReferenceEquals(content.Content, tiny))
            { content.Content = workspaceActive ? workspace : Panel; chrome.Visibility = Interactive || workspaceActive ? Visibility.Visible : Visibility.Collapsed; }
        }
        finally { positioning = false; }
    }
    private double PreferredWidth(double maximumWidth, double maximumHeight) => workspaceActive && workspace is OverlayWorkspace view
        ? view.GetPreferredWidth(maximumWidth, Math.Max(1, maximumHeight - (Interactive ? chrome.Height : 0)))
        : Panel.GetPreferredWidth(maximumWidth, maximumHeight);
}
