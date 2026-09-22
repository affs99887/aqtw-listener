using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Listener.App;

internal sealed class NativeInput : IDisposable
{
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
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
    private readonly ClickLatch latch = new();
    private string registeredHotkey = "";
    public event Action? Toggle;
    public event Action<double>? MouseDown;
    public event Action? ForegroundChanged;
    public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    public NativeInput(Window window)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        source.AddHook(WindowProc);
        callback = MouseProc;
        mouseHook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (mouseHook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "鼠标监听注册失败");
        foregroundCallback = (_, _, _, _, _, _, _) => { try { ForegroundChanged?.Invoke(); } catch { } };
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
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
        return IntPtr.Zero;
    }
    private IntPtr MouseProc(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            if (message.ToInt32() == 0x202) latch.Up();
            else if (message.ToInt32() == 0x201 && latch.Down())
            {
                // Never swallow input or let exceptions cross the native callback boundary.
                try { MouseDown?.Invoke(Now); } catch { }
            }
        }
        return CallNextHookEx(mouseHook, code, message, data);
    }
    public static string ForegroundProcess()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        try { using var process = Process.GetProcessById((int)pid); return process.ProcessName; }
        catch (ArgumentException) { return ""; }
        catch (InvalidOperationException) { return ""; }
    }
    public static void MakeOverlay(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr(style | 0x20 | 0x80 | 0x08000000));
    }
    public static long OverlayStyles(Window window) => GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64();
    public void Dispose()
    {
        UnregisterHotKey(source.Handle, 71); UnregisterHotKey(source.Handle, 72);
        UnhookWindowsHookEx(mouseHook); source.RemoveHook(WindowProc);
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
    }
}
