using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Listener.App;

internal static class HotkeyTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("快捷键首次冲突不阻断另一组，释放占用后无需重启恢复", StartupConflict);
        yield return ("快捷键配置无效不阻断另一组，错误明确且不反复重试", InvalidStartup);
        yield return ("两组快捷键保存失败均保留原设置，释放临时注册", FailedChange);
        yield return ("快捷键等价写法与交换不重复注册，重复组合被拒绝", EquivalentAndSwap);
        yield return ("快捷键替换无释放重注册间隙，旧消息及退出后消息被忽略", Lifecycle);
        yield return ("快捷键保留实际系统错误及恢复前失败记录", ErrorDiagnostics);
        yield return ("Windows真实快捷键注册冲突、释放恢复、窗口消息及清理", NativeRecovery);
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new Exception("invalid shortcut change was accepted");
    }
    private sealed class Registry
    {
        public readonly Dictionary<int, HotkeyGesture> Active = [];
        public readonly HashSet<HotkeyGesture> Blocked = [];
        public readonly List<int> Released = [];
        public int Error;
        public int Register(int id, HotkeyGesture gesture)
        {
            if (Error != 0) return Error;
            if (Blocked.Contains(gesture) || Active.ContainsValue(gesture)) return 1409;
            Check(!Active.ContainsKey(id), "registration identifier was reused while active");
            Active.Add(id, gesture); return 0;
        }
        public void Unregister(int id) { Released.Add(id); Check(Active.Remove(id), "unregistered an unowned identifier"); }
        public int Id(string text) => Active.Single(p => p.Value == HotkeyGesture.Parse(text)).Key;
        public GlobalHotkeys Create() => new(Register, Unregister);
    }
    private static void StartupConflict()
    {
        var registry = new Registry(); var blocked = HotkeyGesture.Parse("Ctrl+Alt+L"); registry.Blocked.Add(blocked);
        using var keys = registry.Create(); keys.Initialize("Ctrl+Alt+L", "Ctrl+Alt+C");
        Check(!keys.Status[0].Registered && keys.Status[1].Registered, "first conflict prevented the second registration");
        Check(keys.TakeMessage(registry.Id("Ctrl+Alt+C")) == 1, "second shortcut did not work during conflict");
        keys.Retry(); registry.Blocked.Clear(); keys.Retry();
        Check(keys.Ready && keys.TakeMessage(registry.Id("Ctrl+Alt+L")) == 0, "shortcut did not recover without reinitialization");
        Check(keys.Status[0] is { Attempts: 3, Failures: 2, Triggers: 1, Error: "" } &&
            keys.Status[0].LastFailure.Contains("1409"), "recovered shortcut lost failure evidence");
        Check(keys.Status[1] is { Attempts: 1, Triggers: 1 }, "healthy shortcut was re-registered");
        keys.Initialize("Ctrl+Alt+L", "Ctrl+Alt+C");
        keys.Retry(); Check(registry.Released.Count == 0 && keys.Status[0].Attempts == 3, "successful registration was discarded");
    }
    private static void InvalidStartup()
    {
        foreach (var invalid in new[] { "", "Ctrl++L", "L", "Ctrl+LeftCtrl", "Ctrl+F12", "Ctrl+NotAKey" })
        {
            var registry = new Registry(); using var keys = registry.Create(); keys.Initialize(invalid, "Ctrl+Alt+C"); keys.Retry();
            Check(keys.Status[0] is { Registered: false, Attempts: 0 } && keys.Status[0].Error.Length > 0,
                "invalid setting was retried or had no error: " + invalid);
            Check(keys.Status[1].Registered, "invalid first setting disabled the valid shortcut");
        }
        var duplicateRegistry = new Registry(); using var duplicate = duplicateRegistry.Create();
        duplicate.Initialize("Ctrl+Alt+L", "alt+CTRL+l");
        Check(duplicate.Status[0].Registered && !duplicate.Status[1].Registered && duplicate.Status[1].Error.Contains("相同"),
            "duplicate shortcut was not explained");
    }
    private static void FailedChange()
    {
        var registry = new Registry(); using var keys = registry.Create(); keys.Initialize("Ctrl+Alt+L", "Ctrl+Alt+C");
        var toggle = registry.Id("Ctrl+Alt+L"); var interaction = registry.Id("Ctrl+Alt+C");
        registry.Blocked.Add(HotkeyGesture.Parse("Ctrl+Alt+K"));
        Reject(() => keys.Apply("Ctrl+Alt+J", "Ctrl+Alt+K"));
        Check(registry.Active.Count == 2 && keys.TakeMessage(toggle) == 0 && keys.TakeMessage(interaction) == 1,
            "failed save changed one shortcut or leaked a temporary registration");
        Check(keys.Status[0].Shortcut == "Ctrl+Alt+L" && keys.Status[1].Shortcut == "Ctrl+Alt+C", "failed save changed saved text");
        Reject(() => keys.Apply("Ctrl+Alt+J", "invalid"));
        registry.Blocked.Clear(); keys.Retry();
        Check(registry.Active.Count == 2 && registry.Active.ContainsKey(toggle) && registry.Active.ContainsKey(interaction),
            "retry installed a rejected setting");
    }
    private static void EquivalentAndSwap()
    {
        var registry = new Registry(); using var keys = registry.Create(); keys.Initialize("Ctrl+Alt+L", "Ctrl+Alt+C");
        var l = registry.Id("Ctrl+Alt+L"); var c = registry.Id("Ctrl+Alt+C");
        keys.Apply("alt + CTRL + l", "ctrl+alt+c");
        Check(keys.Status.All(s => s.Attempts == 1) && registry.Released.Count == 0, "equivalent shortcut caused a self-conflict");
        keys.Apply("Ctrl+Alt+C", "Ctrl+Alt+L");
        Check(keys.TakeMessage(c) == 0 && keys.TakeMessage(l) == 1 && registry.Released.Count == 0, "shortcut exchange lost bindings");
        Reject(() => keys.Apply("Ctrl+Alt+J", "Alt+Ctrl+J"));
        Check(keys.TakeMessage(c) == 0 && keys.TakeMessage(l) == 1, "duplicate save changed active bindings");
    }
    private static void Lifecycle()
    {
        var registry = new Registry(); var keys = registry.Create(); keys.Initialize("Ctrl+Alt+L", "Ctrl+Alt+C");
        var old = registry.Id("Ctrl+Alt+L"); keys.Apply("Ctrl+Alt+J", "Ctrl+Alt+C");
        var current = registry.Id("Ctrl+Alt+J");
        Check(registry.Released.SequenceEqual([old]), "replacement released its new registration");
        Check(keys.TakeMessage(old) is null && keys.TakeMessage(current) == 0, "queued old shortcut still invoked the action");
        keys.Dispose(); keys.Retry(); keys.Dispose();
        Check(registry.Active.Count == 0 && keys.TakeMessage(current) is null && registry.Released.Count == 3,
            "disposed shortcuts remained active or were registered again");
    }
    private static void ErrorDiagnostics()
    {
        var registry = new Registry { Error = 5 }; using var keys = registry.Create(); keys.Initialize("Ctrl+Alt+L", "Ctrl+Alt+C");
        Check(keys.Status.All(s => s.Error.Contains("（5）") && !s.Error.Contains("占用")), "every native error was mislabeled as a conflict");
        registry.Error = 0; keys.Retry();
        Check(keys.Ready && keys.Status.All(s => s.Error == "" && s.LastFailure.Contains("（5）") && s.Failures == 1),
            "error did not clear on recovery or failure evidence was lost");
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr window, int message, IntPtr wp, IntPtr lp);

    // A separate thread owns the conflicting shortcut; it never injects keyboard input.
    private sealed class Reservation : IDisposable
    {
        private readonly ManualResetEventSlim ready = new(false), release = new(false);
        private readonly Thread thread;
        public int Error { get; private set; }
        public Reservation(HotkeyGesture gesture)
        {
            thread = new Thread(() =>
            {
                var registered = RegisterHotKey(IntPtr.Zero, 1, gesture.Modifiers, gesture.VirtualKey);
                Error = registered ? 0 : Marshal.GetLastWin32Error(); ready.Set();
                release.Wait();
                if (registered) UnregisterHotKey(IntPtr.Zero, 1);
            }) { IsBackground = true };
            thread.Start(); Check(ready.Wait(TimeSpan.FromSeconds(5)), "native reservation timed out");
        }
        public void Dispose() { release.Set(); Check(thread.Join(5000), "native reservation did not stop"); ready.Dispose(); release.Dispose(); }
    }
    private static void NativeRecovery()
    {
        const string first = "Ctrl+Alt+Shift+F23", second = "Ctrl+Alt+Shift+F24";
        var gesture = HotkeyGesture.Parse(first);
        using var source = new HwndSource(new HwndSourceParameters("Listener hotkey regression")
            { ParentWindow = new IntPtr(-3), WindowStyle = 0 }); // Hidden message-only window.
        var ids = new Dictionary<HotkeyGesture, int>();
        using var keys = new GlobalHotkeys((id, shortcut) =>
        {
            if (!RegisterHotKey(source.Handle, id, shortcut.Modifiers, shortcut.VirtualKey)) return Marshal.GetLastWin32Error();
            ids[shortcut] = id; return 0;
        }, id => Check(UnregisterHotKey(source.Handle, id), "native unregister failed"));
        using (var blocker = new Reservation(gesture))
        {
            Check(blocker.Error == 0, "test shortcut unavailable: " + new Win32Exception(blocker.Error).Message);
            keys.Initialize(first, second);
            Check(!keys.Status[0].Registered && keys.Status[0].Error.Contains("1409") && keys.Status[1].Registered,
                "native startup conflict was not isolated");
        }
        keys.Retry(); Check(keys.Ready, "native retry did not recover after release");
        using (var probe = new Reservation(gesture)) Check(probe.Error == 1409, "recovered shortcut was not held by the window");
        var received = new List<int>();
        source.AddHook((IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (message == 0x312 && keys.TakeMessage(wp.ToInt32()) is { } action) { received.Add(action); handled = true; }
            return IntPtr.Zero;
        });
        foreach (var shortcut in new[] { gesture, HotkeyGesture.Parse(second) })
            Check(PostMessage(source.Handle, 0x312, ids[shortcut], (IntPtr)((shortcut.VirtualKey << 16) | (shortcut.Modifiers & 0xF))),
                "posting a test hotkey message failed");
        var frame = new DispatcherFrame(); var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) => { if (received.Count == 2 || watch.Elapsed.TotalSeconds > 3) frame.Continue = false; }, Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Check(received.SequenceEqual([0, 1]) && keys.Status.All(s => s.Triggers == 1), "window hotkey messages did not reach both actions");
        keys.Dispose();
        using var released = new Reservation(gesture); Check(released.Error == 0, "disposed native shortcut remained reserved");
    }
}
