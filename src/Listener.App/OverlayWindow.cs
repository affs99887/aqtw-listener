using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Listener.App;

internal sealed class OverlayWindow : Window
{
    private Settings settings;
    private readonly DispatcherTimer placementTimer;
    private readonly ContentControl content = new();
    private readonly DockPanel chrome = new() { Height = 32, Background = Theme.Panel };
    private readonly Button lockButton;
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
    public OverlayWindow(string root, Settings settings)
    {
        this.settings = settings;
        Title = "行商听音 · 浮窗"; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false;
        Topmost = true; ResizeMode = ResizeMode.NoResize;
        Panel = new(root, settings, interactive: false, compact: true); content.Content = Panel;
        var body = new DockPanel(); Content = body;
        DockPanel.SetDock(chrome, Dock.Top); body.Children.Add(chrome); body.Children.Add(content);
        var close = SmallButton("返回监听", () => ExitRequested?.Invoke()); DockPanel.SetDock(close, Dock.Right); chrome.Children.Add(close);
        var reset = SmallButton("居中", RestoreTopCenter); DockPanel.SetDock(reset, Dock.Right); chrome.Children.Add(reset);
        lockButton = SmallButton("已锁定", ToggleLock); DockPanel.SetDock(lockButton, Dock.Right); chrome.Children.Add(lockButton);
        var history = SmallButton("历史", () => HistoryRequested?.Invoke()); DockPanel.SetDock(history, Dock.Right); chrome.Children.Add(history);
        var handle = new Border { Background = Brushes.Transparent, Padding = new Thickness(8, 4, 0, 0),
            Child = Theme.Label("行商听音 · 操作", 12), Cursor = Cursors.SizeAll };
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
    public void SetWorkspace(UIElement view) { workspace = view; workspaceActive = true; content.Content = view; RefreshPlacement(); }
    public void SetInteractive(bool enabled)
    {
        Interactive = enabled; chrome.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        NativeInput.SetOverlayInteractive(this, enabled);
        if (enabled) { Show(); Activate(); }
        else { workspaceActive = false; content.Content = Panel; }
        RefreshPlacement();
    }
    public void ToggleLock()
    { settings.PositionLocked = !settings.PositionLocked; UpdateLock(); PlacementChanged?.Invoke(); }
    public void Unlock() { settings.PositionLocked = false; UpdateLock(); PlacementChanged?.Invoke(); }
    private void UpdateLock() => lockButton.Content = settings.PositionLocked ? "已锁定" : "可拖动";
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
        settings = value; Width = Math.Clamp(settings.Width, 300, 900); SizeToContent = SizeToContent.Height;
        Opacity = Math.Clamp(settings.Opacity, .35, 1); Panel.RefreshAppearance(); UpdateLock();
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
                var bounds = OverlayPlacement.TopCenter(PlacementViewport, dpi.DpiScaleX, dpi.DpiScaleY, settings.Width);
                Width = bounds.Width; MaxHeight = bounds.MaximumHeight;
                NativeInput.PositionOverlay(this, bounds.LeftPixels, bounds.TopPixels);
            }
            else
            {
                var monitor = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.MonitorName)
                    ?? System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
                var work = monitor.WorkingArea;
                Width = Math.Min(Math.Clamp(settings.Width, 300, 900), work.Width / dpi.DpiScaleX);
                var left = settings.MonitorName is null ? settings.Left * dpi.DpiScaleX : work.Left + settings.MonitorOffsetX * dpi.DpiScaleX;
                var top = settings.MonitorName is null ? settings.Top * dpi.DpiScaleY : work.Top + settings.MonitorOffsetY * dpi.DpiScaleY;
                left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - Width * dpi.DpiScaleX));
                top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - 160 * dpi.DpiScaleY));
                MaxHeight = Math.Max(1, (work.Bottom - top) / dpi.DpiScaleY);
                NativeInput.PositionOverlay(this, left, top);
            }
            var cramped = MaxHeight < 175 || Width < 280;
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
                    else body.Children.Add(Theme.Label(workspaceActive ? "Ctrl+Alt+C 暂停采样并操作" : "空间较小 · Ctrl+Alt+C 查看历史详情", 9, Theme.Muted));
                    tiny.Content = body; tiny.Background = Theme.Background; tinyInteractive = Interactive;
                    content.Content = tiny; chrome.Visibility = Visibility.Collapsed;
                }
            }
            else if (ReferenceEquals(content.Content, tiny))
            { content.Content = workspaceActive ? workspace : Panel; chrome.Visibility = Interactive ? Visibility.Visible : Visibility.Collapsed; }
        }
        finally { positioning = false; }
    }
}
