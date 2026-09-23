using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Listener.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var smoke = args.Contains("--ui-smoke") || args.Contains("--audio-smoke") || args.Contains("--audio-smoke-self");
        using var singleton = new Mutex(true, "AqtwListener-Desktop-v1" + (smoke ? $"-ui-smoke-{Environment.ProcessId}" : ""), out var first);
        if (!first)
        {
            if (!ShowRunningWindow()) MessageBox.Show("听音助手已在运行，但无法找到它的窗口。请在任务管理器中结束 AqtwListener 后重试。");
            return 0;
        }
        try
        {
            var settings = smoke ? new Settings() : Settings.Load();
            // The static overlay does not benefit from a permanent GPU rendering context.
            // Measured locally: software rendering reduced total working set from 253 to 167 MiB.
            System.Windows.Media.RenderOptions.ProcessRenderMode = settings.HardwareAcceleration
                ? System.Windows.Interop.RenderMode.Default : System.Windows.Interop.RenderMode.SoftwareOnly;
            if (args.Contains("--audio-smoke") || args.Contains("--audio-smoke-self"))
            {
                AudioSmoke(args.Contains("--audio-smoke-self")).GetAwaiter().GetResult(); return 0;
            }
            var library = JsonFile.Read<SoundLibrary>(Path.Combine(AppContext.BaseDirectory, "library", "library.json"));
            library.Validate(); var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
            return app.Run(new MainWindow(settings, library));
        }
        catch (Exception ex)
        {
            var message = "启动失败：" + ex.Message;
            if (smoke || args.Contains("--performance-smoke")) File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-error.txt"), ex.ToString());
            else MessageBox.Show(message, "行商听音助手");
            return 1;
        }
    }
    private static bool ShowRunningWindow()
    {
        foreach (var process in Process.GetProcessesByName("AqtwListener"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    process.Refresh();
                    var window = process.MainWindowHandle;
                    if (window == IntPtr.Zero) continue;
                    ShowWindow(window, 9); // SW_RESTORE also shows a hidden main window.
                    NativeInput.SetForegroundWindow(window);
                    return true;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        return false;
    }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);

    private static async Task AudioSmoke(bool self)
    {
        await using var capture = new LoopbackAudio(); var devices = LoopbackAudio.Devices();
        var foreground = NativeInput.ForegroundProcessIdentity();
        uint gameId;
        if (self) gameId = (uint)Environment.ProcessId;
        else if (string.Equals(foreground.Name, Settings.GameProcessName, StringComparison.OrdinalIgnoreCase)) gameId = foreground.Id;
        else
        {
            var games = Process.GetProcessesByName(Settings.GameProcessName);
            try
            {
                if (games.Length != 1) throw new InvalidOperationException("音频检查需要恰好一个正在运行的 UAGame 进程。");
                gameId = (uint)games[0].Id;
            }
            finally { foreach (var game in games) game.Dispose(); }
        }
        await capture.Start(gameId); await Task.Delay(1000);
        var frames = capture.Timeline.Slice(NativeInput.Now - .5, NativeInput.Now);
        JsonFile.Write(Path.Combine(AppContext.BaseDirectory, self ? "audio-smoke-self.json" : "audio-smoke.json"), new
        { source = self ? "self" : "UAGame", devices, capture.DeviceName, capture.TargetProcessId,
            capture.Packets, health = capture.Health(), capture.Timeline.SampleRate, frames = frames.Length, rms = AudioFeatures.Rms(frames) });
    }
}
