using System.Windows.Threading;

namespace Listener.App;

internal sealed class ListeningController : IAsyncDisposable
{
    private readonly Settings settings;
    private readonly IRecognizer recognizer;
    private readonly LoopbackAudio capture = new();
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer poll;
    private readonly OperationEpoch epoch = new();
    private CancellationTokenSource? operation;
    private bool transitioning;
    private bool disposed;
    private double lastClick = double.NegativeInfinity;
    private string lastState = "";
    public bool Enabled { get; private set; }
    public event Action<string>? State;
    public event Action<RecognitionResult?>? Result;
    public bool Foreground => !string.IsNullOrWhiteSpace(settings.ProcessName)
        && string.Equals(NativeInput.ForegroundProcess(), settings.ProcessName, StringComparison.OrdinalIgnoreCase);
    public ListeningController(Settings settings, SoundLibrary library, Dispatcher dispatcher)
    {
        this.settings = settings; recognizer = RecognizerFactory.Create(library); this.dispatcher = dispatcher;
        capture.Failed += message => dispatcher.BeginInvoke(async () =>
        { if (!disposed) { Enabled = false; Invalidate(); await capture.Stop(); PublishState("采音失败 · " + message); } });
        poll = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            async (_, _) => await Reconcile(), dispatcher);
    }
    private void PublishState(string message) { if (message == lastState) return; lastState = message; State?.Invoke(message); }
    public async Task Toggle()
    {
        if (!Enabled && string.IsNullOrWhiteSpace(settings.ProcessName))
        { PublishState("请先在设置中选择游戏进程"); return; }
        Enabled = !Enabled; Invalidate(); await Reconcile();
    }
    public async Task Disable()
    {
        Enabled = false; Invalidate(); await Reconcile();
    }
    public void ForegroundChanged()
    {
        if (disposed || !Enabled) return;
        if (!Foreground) { Invalidate(); capture.Timeline.Clear(); }
        _ = Reconcile();
    }
    private void Invalidate()
    {
        epoch.Next(); operation?.Cancel(); operation?.Dispose(); operation = null;
        lastClick = double.NegativeInfinity; Result?.Invoke(null);
    }
    private async Task Reconcile()
    {
        if (transitioning || disposed) return;
        transitioning = true;
        try
        {
            var active = Enabled && Foreground;
            if (!active)
            {
                if (capture.Running) { Invalidate(); await capture.Stop(); }
                PublishState(Enabled ? "已暂停 · 等待切回游戏" : "监听已关闭"); return;
            }
            var selected = capture.Resolve(settings.DeviceId);
            if (capture.Running && capture.DeviceId != selected) { Invalidate(); await capture.Stop(); }
            if (!Enabled || !Foreground) return;
            if (!capture.Running) { Invalidate(); capture.Start(selected); }
            PublishState("正在监听 · " + capture.DeviceName);
        }
        catch (Exception ex) { Enabled = false; Invalidate(); await capture.Stop(); PublishState("无法采音 · " + ex.Message); }
        finally { transitioning = false; }
    }
    public void Click(double clickedAt)
    {
        if (!Enabled || !Foreground || !capture.Running || transitioning) return;
        operation?.Cancel(); operation?.Dispose(); operation = new();
        var token = operation.Token; var id = epoch.Next();
        // A rapid next click may not reuse pre-roll from the previous operation.
        var start = Math.Max(clickedAt - .06, lastClick + .03); lastClick = clickedAt;
        var timeline = capture.Timeline;
        // Return the low-level mouse hook immediately; visual-tree work must not delay game input.
        _ = dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            if (epoch.IsCurrent(id) && Enabled && Foreground)
                Result?.Invoke(new(id, RecognitionStatus.Analyzing, false, [], 0, "正在听这件货物…"));
        }));
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var (delay, final) in new[] { (450, false), (1080, true) })
                {
                    var remaining = clickedAt + delay / 1000.0 - NativeInput.Now;
                    if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining), token);
                    token.ThrowIfCancellationRequested();
                    var audio = timeline.Slice(start, clickedAt + (final ? 1.0 : .4));
                    var result = recognizer.Recognize(audio, timeline.SampleRate, id, final, token)
                        with { ElapsedMilliseconds = (NativeInput.Now - clickedAt) * 1000 };
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (epoch.IsCurrent(id) && !token.IsCancellationRequested && Enabled && Foreground)
                            Result?.Invoke(result with { ElapsedMilliseconds = (NativeInput.Now - clickedAt) * 1000 });
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await dispatcher.InvokeAsync(() =>
                { if (epoch.IsCurrent(id) && Enabled && Foreground) Result?.Invoke(new(id, RecognitionStatus.Error, true, [], 0, ex.Message)); });
            }
        }, token);
    }
    public async ValueTask DisposeAsync()
    {
        disposed = true; poll.Stop(); Enabled = false; Invalidate(); await capture.DisposeAsync(); recognizer.Dispose();
    }
}
