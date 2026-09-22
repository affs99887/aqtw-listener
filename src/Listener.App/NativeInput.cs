using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Listener.App;

internal sealed class NativeInput : IDisposable
{
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    private delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    private delegate void WinEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
    private readonly Hook callback;
    private readonly HwndSource source;
    private readonly IntPtr mouseHook;
    private readonly IntPtr foregroundHook;
    private readonly WinEvent foregroundCallback;
    private readonly MouseButtonTracker buttons = new();
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer mousePoll;
    private bool disposed;
    private string registeredHotkey = "";
    private string registeredInteractionHotkey = "";
    private bool pagingRequested;
    public string PagingStatus { get; private set; } = "";
    public long HookPresses { get; private set; }
    public long PollTriggers { get; private set; }
    public string HookStatus { get; }
    public event Action? Toggle;
    public event Action? InteractionToggle;
    public event Action<double, string>? MouseDown;
    public event Action? ForegroundChanged;
    public event Action<int>? CandidatePage;
    public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    public NativeInput(Window window)
    {
        dispatcher = window.Dispatcher;
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        source.AddHook(WindowProc);
        callback = MouseProc;
        mouseHook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        HookStatus = mouseHook == IntPtr.Zero
            ? "鼠标事件注册失败，使用按键检测：" + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message
            : "鼠标事件 + 按键检测";
        foregroundCallback = (_, _, _, _, _, _, _) =>
        {
            try { if (!disposed) _ = dispatcher.BeginInvoke(new Action(() => { if (!disposed) ForegroundChanged?.Invoke(); })); }
            catch { } // The dispatcher can be shutting down as Windows delivers a callback.
        };
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
        mousePoll = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Input, (_, _) => PollMouse(), dispatcher);
    }
    public void SetHotkey(string text)
    {
        if (string.Equals(text, registeredHotkey, StringComparison.OrdinalIgnoreCase)) return;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = 0x4000;
        foreach (var part in parts.SkipLast(1)) modifiers |= part.ToLowerInvariant() switch
        { "ctrl" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u, _ => throw new ArgumentException("快捷键示例：Ctrl+Alt+L") };
        if (parts.Length < 2 || !Enum.TryParse<Key>(parts[^1], true, out var key) || key == Key.None)
            throw new ArgumentException("请输入包含组合键的快捷键，例如 Ctrl+Alt+L。");
        // Trial registration preserves the previous shortcut if the replacement conflicts.
        if (!RegisterHotKey(source.Handle, 72, modifiers, (uint)KeyInterop.VirtualKeyFromKey(key)))
            throw new InvalidOperationException("快捷键已被其他程序占用，请更换组合键。");
        UnregisterHotKey(source.Handle, 71); UnregisterHotKey(source.Handle, 72);
        if (!RegisterHotKey(source.Handle, 71, modifiers, (uint)KeyInterop.VirtualKeyFromKey(key)))
            throw new InvalidOperationException("快捷键注册失败，请重新设置。");
        registeredHotkey = text;
    }
    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x312 && wp.ToInt32() == 71) { Toggle?.Invoke(); handled = true; }
        else if (msg == 0x312 && wp.ToInt32() == 75) { InteractionToggle?.Invoke(); handled = true; }
        else if (msg == 0x312 && wp.ToInt32() is 73 or 74)
        { CandidatePage?.Invoke(wp.ToInt32() == 73 ? -1 : 1); handled = true; }
        return IntPtr.Zero;
    }
    public void SetInteractionHotkey(string text)
    {
        if (text == registeredInteractionHotkey) return;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = 0x4000;
        foreach (var part in parts.SkipLast(1)) modifiers |= part.ToLowerInvariant() switch
        { "ctrl" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u, _ => throw new ArgumentException("快捷键格式无效") };
        if (parts.Length < 2 || !Enum.TryParse<Key>(parts[^1], true, out var key) || key == Key.None) throw new ArgumentException("请输入有效的组合键");
        if (!RegisterHotKey(source.Handle, 76, modifiers, (uint)KeyInterop.VirtualKeyFromKey(key))) throw new InvalidOperationException("操作快捷键被占用，可从主窗口或托盘打开浮窗操作");
        UnregisterHotKey(source.Handle, 75); UnregisterHotKey(source.Handle, 76);
        if (!RegisterHotKey(source.Handle, 75, modifiers, (uint)KeyInterop.VirtualKeyFromKey(key))) throw new InvalidOperationException("操作快捷键注册失败");
        registeredInteractionHotkey = text;
    }
    public void SetPagingEnabled(bool enabled)
    {
        if (pagingRequested == enabled) return;
        pagingRequested = enabled;
        UnregisterHotKey(source.Handle, 73); UnregisterHotKey(source.Handle, 74); PagingStatus = "";
        if (!enabled) return;
        var previous = RegisterHotKey(source.Handle, 73, 0x4003, 0x21);
        var next = RegisterHotKey(source.Handle, 74, 0x4003, 0x22);
        if (!previous || !next)
        {
            UnregisterHotKey(source.Handle, 73); UnregisterHotKey(source.Handle, 74);
            PagingStatus = "翻页快捷键被占用，可在主窗口或识别历史翻页";
        }
    }
    private IntPtr MouseProc(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0 && !disposed)
            {
                var now = Now;
                if (message.ToInt32() == 0x202) buttons.HookUp(now);
                else if (message.ToInt32() == 0x201)
                {
                    HookPresses++;
                    if (buttons.HookDown(now)) QueueClick(now, "鼠标事件");
                }
            }
        }
        catch { } // Never let exceptions cross the native callback boundary.
        return CallNextHookEx(mouseHook, code, message, data);
    }
    private void PollMouse()
    {
        if (disposed) return;
        // GetAsyncKeyState uses physical buttons; the hook uses logical buttons.
        var key = GetSystemMetrics(23) != 0 ? 0x02 : 0x01;
        var now = Now;
        if (buttons.Poll((GetAsyncKeyState(key) & 0x8000) != 0, now))
        {
            PollTriggers++;
            QueueClick(now, "按键检测（远程兼容）");
        }
    }
    private void QueueClick(double now, string origin)
    {
        // Foreground lookup, cancellation and visual work run after the hook returns.
        _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        { if (!disposed) MouseDown?.Invoke(now, origin); }));
    }
    public static string ForegroundProcess()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return "";
        GetWindowThreadProcessId(window, out var pid);
        if (pid == 0) return "";
        try { using var process = Process.GetProcessById((int)pid); return process.ProcessName; }
        catch (ArgumentException) { return ""; }
        catch (InvalidOperationException) { return ""; }
        catch (System.ComponentModel.Win32Exception) { return ""; }
    }
    public static void MakeOverlay(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr(style | 0x20 | 0x80 | 0x08000000));
    }
    public static void SetOverlayInteractive(Window window, bool interactive)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        const long flags = 0x20 | 0x08000000;
        SetWindowLongPtr(handle, -20, new IntPtr(interactive ? style & ~flags : style | flags));
    }
    public static bool ForegroundBelongsToApplication()
    { GetWindowThreadProcessId(GetForegroundWindow(), out var pid); return pid == Environment.ProcessId; }
    public static Rect? ForegroundGameViewport()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || !string.Equals(ForegroundProcess(), Settings.GameProcessName, StringComparison.OrdinalIgnoreCase)
            || !GetClientRect(handle, out var client) || client.Right <= 0 || client.Bottom <= 0) return null;
        var origin = new NativePoint();
        if (!ClientToScreen(handle, ref origin)) return null;
        var viewport = new Rect(origin.X, origin.Y, client.Right, client.Bottom);
        var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        viewport.Intersect(new Rect(work.X, work.Y, work.Width, work.Height));
        return viewport.IsEmpty || viewport.Width < 300 || viewport.Height < 200 ? null : viewport;
    }
    internal static Rect WindowPixels(Window window)
    {
        return GetWindowRect(new WindowInteropHelper(window).Handle, out var r)
            ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : Rect.Empty;
    }
    public static void PositionOverlay(Window window, double x, double y)
    {
        var actual = WindowPixels(window);
        var left = (int)Math.Round(x); var top = (int)Math.Round(y);
        if (actual.Left == left && actual.Top == top) return;
        // Physical desktop coordinates avoid mixing monitor origins with WPF's per-monitor DPI.
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, left, top, 0, 0,
            0x0001 | 0x0004 | 0x0010); // NOSIZE | NOZORDER | NOACTIVATE
    }
    public static long OverlayStyles(Window window) => GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; mousePoll.Stop();
        UnregisterHotKey(source.Handle, 71); UnregisterHotKey(source.Handle, 72);
        UnregisterHotKey(source.Handle, 73); UnregisterHotKey(source.Handle, 74);
        UnregisterHotKey(source.Handle, 75); UnregisterHotKey(source.Handle, 76);
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        source.RemoveHook(WindowProc);
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
    }
}
