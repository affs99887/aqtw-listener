namespace Listener.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var smoke = args.Contains("--ui-smoke");
        using var singleton = new Mutex(true, "AqtwListener-Desktop-v1" + (smoke ? $"-ui-smoke-{Environment.ProcessId}" : ""), out var first);
        if (!first) { MessageBox.Show("听音助手已在运行，请从托盘打开。"); return 0; }
        try
        {
            var settings = Settings.Load();
            // The static overlay does not benefit from a permanent GPU rendering context.
            // Measured locally: software rendering reduced total working set from 253 to 167 MiB.
            System.Windows.Media.RenderOptions.ProcessRenderMode = settings.HardwareAcceleration
                ? System.Windows.Interop.RenderMode.Default : System.Windows.Interop.RenderMode.SoftwareOnly;
            if (args.Contains("--audio-smoke"))
            {
                AudioSmoke().GetAwaiter().GetResult(); return 0;
            }
            var library = JsonFile.Read<SoundLibrary>(Path.Combine(AppContext.BaseDirectory, "library", "library.json"));
            library.Validate(); var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
            return app.Run(new MainWindow(settings, library));
        }
        catch (Exception ex)
        {
            var message = "启动失败：" + ex.Message;
            if (args.Any(x => x.EndsWith("-smoke"))) File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-error.txt"), ex.ToString());
            else MessageBox.Show(message, "行商听音助手");
            return 1;
        }
    }
    private static async Task AudioSmoke()
    {
        await using var capture = new LoopbackAudio(); var devices = LoopbackAudio.Devices();
        capture.Start(""); await Task.Delay(1000);
        var frames = capture.Timeline.Slice(NativeInput.Now - .5, NativeInput.Now);
        JsonFile.Write(Path.Combine(AppContext.BaseDirectory, "audio-smoke.json"), new
        { devices, capture.DeviceName, capture.DeviceId, capture.Packets, health = capture.Health(), capture.Timeline.SampleRate, frames = frames.Length, rms = AudioFeatures.Rms(frames) });
    }
}
