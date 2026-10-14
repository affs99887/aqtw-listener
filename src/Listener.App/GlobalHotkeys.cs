using System.ComponentModel;
using System.Windows.Input;

namespace Listener.App;

internal readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey)
{
    public static HotkeyGesture Parse(string text)
    {
        var parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        foreach (var part in parts.SkipLast(1)) modifiers |= part.ToLowerInvariant() switch
        {
            "ctrl" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u,
            _ => throw new ArgumentException("快捷键格式无效，例如 Ctrl+Alt+L。")
        };
        if (parts.Length < 2 || !Enum.TryParse<Key>(parts[^1], true, out var key) || !Enum.IsDefined(key))
            throw new ArgumentException("请输入包含组合键的快捷键，例如 Ctrl+Alt+L。");
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0 || vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5)
            throw new ArgumentException("组合键需要一个普通按键，例如 Ctrl+Alt+L。");
        if (key == Key.F12) throw new ArgumentException("F12 为系统调试保留键，请使用其他按键。");
        return new(modifiers | 0x4000, vk); // MOD_NOREPEAT
    }
}

internal sealed record HotkeyStatus(string Name, string Shortcut, bool Registered, string Error,
    int Attempts, int Failures, long Triggers, string LastFailure);

// Registration and retry run on the window's dispatcher thread. Keeping the successful
// registration avoids the old trial-unregister-register gap in which it could be lost.
internal sealed class GlobalHotkeys(Func<int, HotkeyGesture, int> register, Action<int> unregister) : IDisposable
{
    private sealed class Binding(string name)
    {
        public readonly string Name = name;
        public string Text = "", Error = "", LastFailure = "";
        public HotkeyGesture? Gesture;
        public int? Id;
        public int Attempts, Failures;
        public long Triggers;
    }
    private readonly Binding[] bindings = [new("启停"), new("浮窗操作")];
    private int nextId = 71;
    private bool disposed, initialized;
    public bool Ready => bindings.All(b => b.Id.HasValue);
    public IReadOnlyList<HotkeyStatus> Status => bindings.Select(b => new HotkeyStatus(
        b.Name, b.Text, b.Id.HasValue, b.Error, b.Attempts, b.Failures, b.Triggers, b.LastFailure)).ToArray();
    public string StatusText => string.Join("\n", bindings.Select(b =>
        $"{b.Name} {b.Text}：{(b.Id.HasValue ? "可用" : b.Error + (b.Gesture.HasValue ? "，自动重试中" : ""))} · 已触发 {b.Triggers} 次"));

    public void Initialize(string toggle, string interaction)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (initialized) return;
        initialized = true;
        var texts = new[] { toggle, interaction };
        for (var i = 0; i < bindings.Length; i++)
        {
            var b = bindings[i]; b.Text = texts[i];
            try
            {
                var gesture = HotkeyGesture.Parse(b.Text);
                if (i == 1 && gesture == bindings[0].Gesture)
                    throw new ArgumentException("启停与浮窗操作不能使用相同的快捷键。");
                b.Gesture = gesture;
            }
            catch (ArgumentException ex) { b.Error = b.LastFailure = ex.Message; }
        }
        Retry(); // One failed shortcut must not prevent the other from being registered.
    }

    public void Retry()
    {
        if (disposed) return;
        foreach (var b in bindings)
        {
            if (b.Id.HasValue || b.Gesture is not { } gesture) continue;
            var id = AllocateId();
            var error = Attempt(b, id, gesture, b.Text);
            if (error.Length == 0) { b.Id = id; b.Error = ""; }
            else b.Error = error;
        }
    }

    // Save both shortcuts together: invalid or occupied replacements keep the entire
    // previous pair active. Reusing registrations also permits exchanging the two keys.
    public void Apply(string toggle, string interaction)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var texts = new[] { toggle.Trim(), interaction.Trim() };
        var gestures = texts.Select(HotkeyGesture.Parse).ToArray();
        if (gestures[0] == gestures[1]) throw new ArgumentException("启停与浮窗操作不能使用相同的快捷键。");
        var ids = new int[2]; var acquired = new List<int>();
        try
        {
            for (var i = 0; i < bindings.Length; i++)
            {
                var existing = bindings.FirstOrDefault(b => b.Gesture == gestures[i] && b.Id.HasValue);
                if (existing is not null) { ids[i] = existing.Id!.Value; continue; }
                var id = AllocateId();
                var error = Attempt(bindings[i], id, gestures[i], texts[i]);
                if (error.Length != 0) throw new InvalidOperationException($"{bindings[i].Name}快捷键 {texts[i]}：{error}");
                ids[i] = id; acquired.Add(id);
            }
        }
        catch
        {
            foreach (var id in acquired) unregister(id);
            throw;
        }
        foreach (var b in bindings)
            if (b.Id is { } old && !ids.Contains(old)) unregister(old);
        for (var i = 0; i < bindings.Length; i++)
        {
            var b = bindings[i]; b.Text = texts[i]; b.Gesture = gestures[i]; b.Id = ids[i]; b.Error = "";
        }
    }

    public int? TakeMessage(int id)
    {
        if (disposed) return null;
        for (var i = 0; i < bindings.Length; i++)
            if (bindings[i].Id == id) { bindings[i].Triggers++; return i; }
        return null;
    }

    private string Attempt(Binding binding, int id, HotkeyGesture gesture, string text)
    {
        binding.Attempts++;
        var error = register(id, gesture);
        if (error == 0) return "";
        binding.Failures++;
        var message = error == 1409 ? "已被其他程序占用（1409）"
            : $"注册失败（{error}）：{new Win32Exception(error).Message}";
        binding.LastFailure = $"{text}：{message}";
        return message;
    }
    private int AllocateId()
    {
        // Do not immediately reuse a retired ID: its WM_HOTKEY may still be queued.
        int id;
        do { id = nextId++; if (nextId > 0xBFFF) nextId = 71; }
        while (bindings.Any(b => b.Id == id));
        return id;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var b in bindings)
            if (b.Id is { } id) { unregister(id); b.Id = null; }
    }
}
