using System.Windows.Threading;

namespace Listener.App;

internal sealed record ListeningDiagnostics(bool Enabled, string TargetProcess, string ForegroundProcess,
    string State, bool Capturing, string DeviceName, string SelectedDeviceId, AudioCaptureHealth Audio,
    long TriggerCount, string LastTriggerSource, DateTimeOffset? LastTriggerAt,
    string LastIgnoredReason, string LastRecognition, string ManualTestStatus,
    bool AutomaticRecognition, long AutomaticScans, string AutomaticStatus, double AutomaticWindowRms);

internal sealed class ListeningController : IAsyncDisposable
{
    private readonly Settings settings;
    private readonly IRecognizer recognizer;
    private readonly IPlaybackCapture capture;
    private readonly Func<string> foregroundProcess;
    private readonly Func<double> clock;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer poll;
    private readonly OperationEpoch epoch = new();
    private readonly AutomaticAudioScanner scanner = new();
    private CancellationTokenSource? operation;
    private Task? recognitionTask;
    private long automaticScans;
    private string automaticStatus = "等待声音";
    private bool transitioning;
    private bool disposed;
    private double lastClick = double.NegativeInfinity;
    private string lastState = "";
    private string? captureError;
    private long triggerCount;
    private string lastTriggerSource = "尚未触发", lastIgnoredReason = "", lastRecognition = "尚未识别";
    private DateTimeOffset? lastTriggerAt;
    private CancellationTokenSource? countdown;
    public string ManualTestStatus { get; private set; } = "";
    public bool Enabled { get; private set; }
    public RecognitionHistory History { get; } = new();
    public event Action<string>? State;
    public event Action<RecognitionResult?>? Result;
    public bool Foreground => string.Equals(foregroundProcess(), Settings.GameProcessName, StringComparison.OrdinalIgnoreCase);
    public ListeningDiagnostics Diagnostics() => new(Enabled, Settings.GameProcessName, foregroundProcess(),
        lastState, capture.Running, capture.DeviceName, settings.DeviceId, capture.Health(), triggerCount,
        lastTriggerSource, lastTriggerAt, lastIgnoredReason, lastRecognition, ManualTestStatus,
        settings.AutomaticRecognition, automaticScans,
        !settings.AutomaticRecognition ? "已关闭（仅鼠标触发）" : !Enabled ? "等待开启监听" :
            !capture.Running ? "等待切回游戏" : automaticStatus, scanner.LastWindowRms);
    public ListeningController(Settings settings, SoundLibrary library, Dispatcher dispatcher)
        : this(settings, RecognizerFactory.Create(library), new LoopbackAudio(), dispatcher,
            NativeInput.ForegroundProcess, () => NativeInput.Now) { }

    internal ListeningController(Settings settings, IRecognizer recognizer, IPlaybackCapture capture,
        Dispatcher dispatcher, Func<string> foregroundProcess, Func<double> clock, bool startPolling = true)
    {
        this.settings = settings; this.recognizer = recognizer; this.capture = capture; this.dispatcher = dispatcher;
        this.foregroundProcess = foregroundProcess; this.clock = clock;
        capture.Failed += message => dispatcher.BeginInvoke(async () =>
        {
            if (!disposed)
            {
                Enabled = false; CancelTest(); Invalidate(); captureError = "采音失败 · " + message;
                await capture.Stop(); PublishState(captureError);
            }
        });
        poll = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            async (_, _) => await Reconcile(), dispatcher);
        if (!startPolling) poll.Stop();
    }
    internal Task PendingRecognition => recognitionTask ?? Task.CompletedTask;
    public void SetAutomaticRecognition(bool enabled)
    {
        settings.AutomaticRecognition = enabled;
        Invalidate();
    }
    private void PublishState(string message) { if (message == lastState) return; lastState = message; State?.Invoke(message); }
    private RecognitionResult? RetainedResult => History.Latest is { } entry
        ? entry.Result with { Message = $"{entry.LastSeen:HH:mm:ss} · 已保留 · {entry.Source}" } : null;
    private void PublishResult(RecognitionResult result, string source, bool automatic = false)
    {
        if (History.Remember(result, source, automatic, DateTimeOffset.Now)) Result?.Invoke(RetainedResult);
        else if (History.Latest is null) Result?.Invoke(result);
    }
    public void ClearCurrentResult()
    {
        History.ClearCurrent(); capture.Timeline.Clear(); Invalidate();
        lastRecognition = "当前结果已清除，历史记录仍保留";
    }
    public async Task Toggle()
    {
        captureError = null;
        Enabled = !Enabled;
        if (!Enabled) CancelTest();
        Invalidate(); await Reconcile();
    }
    public async Task Disable()
    {
        Enabled = false; captureError = null; CancelTest(); Invalidate(); await Reconcile();
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
        scanner.Reset(clock());
        automaticStatus = "等待声音";
        if (lastRecognition == "正在听这件货物…") lastRecognition = "本次识别已取消（切出游戏或关闭监听）";
        lastClick = double.NegativeInfinity; Result?.Invoke(RetainedResult);
    }
    internal async Task Reconcile()
    {
        if (transitioning || disposed) return;
        transitioning = true;
        try
        {
            var active = Enabled && Foreground;
            if (!active)
            {
                if (capture.Running) { Invalidate(); await capture.Stop(); }
                var foreground = foregroundProcess();
                PublishState(Enabled ? $"已暂停 · 等待切回暗区突围，当前：{(foreground.Length == 0 ? "无法读取" : foreground)}" : captureError ?? "监听已关闭"); return;
            }
            var selected = capture.Resolve(settings.DeviceId);
            if (capture.Running && capture.DeviceId != selected) { Invalidate(); await capture.Stop(); }
            if (!Enabled || !Foreground) return;
            if (!capture.Running) { Invalidate(); capture.Start(selected); }
            PublishState("正在监听 · " + capture.DeviceName);
            PollAutomaticRecognition();
        }
        catch (Exception ex)
        {
            Enabled = false; CancelTest(); Invalidate(); captureError = "无法采音 · " + ex.Message;
            await capture.Stop(); PublishState(captureError);
        }
        finally { transitioning = false; }
    }
    public void Click(double clickedAt, string origin)
    {
        if (disposed) return;
        lastIgnoredReason = !Enabled ? "监听未开启" : !Foreground ? "当前前台不是暗区突围" :
            !capture.Running || transitioning ? "采音正在准备，请再次拖动" : "";
        if (lastIgnoredReason.Length != 0) return;
        scanner.Reset(clickedAt + 2.2); // Prioritize the two click-triggered recognition stages.
        triggerCount++; lastTriggerSource = origin; lastTriggerAt = DateTimeOffset.Now;
        lastRecognition = "正在听这件货物…";
        operation?.Cancel(); operation?.Dispose(); operation = new();
        var token = operation.Token; var id = epoch.Next();
        // A rapid next click may not reuse pre-roll from the previous operation.
        var start = Math.Max(clickedAt - .06, lastClick + .03); lastClick = clickedAt;
        var timeline = capture.Timeline;
        // Return the low-level mouse hook immediately; visual-tree work must not delay game input.
        _ = dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            if (epoch.IsCurrent(id) && Enabled && Foreground)
                PublishResult(new(id, RecognitionStatus.Analyzing, false, [], 0, "正在听这件货物…"), origin);
        }));
        recognitionTask = Task.Run(async () =>
        {
            try
            {
                foreach (var (delay, final) in new[] { (450, false), (1080, true) })
                {
                    var remaining = clickedAt + delay / 1000.0 - clock();
                    if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining), token);
                    token.ThrowIfCancellationRequested();
                    var audio = timeline.Slice(start, clickedAt + (final ? 1.0 : .4));
                    var result = recognizer.Recognize(audio, timeline.SampleRate, id, final, token)
                        with { ElapsedMilliseconds = (clock() - clickedAt) * 1000 };
                    if (result.Status == RecognitionStatus.NoSound)
                        result = result with { Message = "已收到触发，但没有听到声音 · 请检查游戏音量和播放设备（包括 UU 虚拟声卡）" };
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (epoch.IsCurrent(id) && !token.IsCancellationRequested && Enabled && Foreground)
                        {
                            lastRecognition = result.Message;
                            PublishResult(result with { ElapsedMilliseconds = (clock() - clickedAt) * 1000 }, origin);
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (epoch.IsCurrent(id) && Enabled && Foreground)
                    { lastRecognition = ex.Message; PublishResult(new(id, RecognitionStatus.Error, true, [], 0, ex.Message), origin); }
                });
            }
        }, token);
    }
    private void PollAutomaticRecognition()
    {
        if (!settings.AutomaticRecognition || !Enabled || !Foreground || !capture.Running) return;
        var now = clock();
        var window = scanner.TryTakeWindow(capture.Timeline, now, active: true,
            busy: recognitionTask is { IsCompleted: false });
        if (window is null)
        {
            if (recognitionTask is not { IsCompleted: false } && scanner.LastWindowRms < .00008)
                automaticStatus = "等待声音";
            return;
        }
        operation?.Dispose(); operation = new();
        var token = operation.Token; var id = epoch.Next();
        automaticScans++; automaticStatus = "正在分析声音";
        recognitionTask = Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var result = recognizer.Recognize(window.Samples, window.SampleRate, id, true, token);
                await dispatcher.InvokeAsync(() =>
                {
                    if (!epoch.IsCurrent(id) || token.IsCancellationRequested || !settings.AutomaticRecognition || !Enabled || !Foreground) return;
                    if (result.Status == RecognitionStatus.Matched)
                    {
                        automaticStatus = "已匹配 · 继续听音";
                        lastRecognition = "声音自动识别 · 最近匹配";
                        PublishResult(result with { Message = lastRecognition }, "声音自动识别", automatic: true);
                    }
                    else
                    {
                        automaticStatus = result.Status == RecognitionStatus.Unknown ? "已分析，暂未匹配" : result.Message;
                        lastRecognition = "声音自动识别 · " + automaticStatus;
                        PublishResult(result with { Message = lastRecognition }, "声音自动识别", automatic: true);
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (!epoch.IsCurrent(id) || token.IsCancellationRequested || !settings.AutomaticRecognition || !Enabled || !Foreground) return;
                    automaticStatus = "识别错误 · " + ex.Message; lastRecognition = automaticStatus;
                    PublishResult(new(id, RecognitionStatus.Error, true, [], 0, automaticStatus), "声音自动识别", automatic: true);
                });
            }
        }, token);
    }
    public async Task TestAfterCountdown()
    {
        CancelTest();
        using var test = new CancellationTokenSource(); countdown = test;
        try
        {
            if (!Enabled) await Toggle();
            for (var seconds = 3; seconds > 0; seconds--)
            {
                ManualTestStatus = $"{seconds} 秒后试识别 · 请切回游戏，倒计时结束时拖动货物";
                await Task.Delay(1000, test.Token);
            }
            await Reconcile();
            test.Token.ThrowIfCancellationRequested();
            if (!Enabled || !Foreground || !capture.Running)
            { ManualTestStatus = "试识别未启动 · 请确认已切回暗区突围，且采音没有报错"; return; }
            ManualTestStatus = "试识别已触发 · 接下来 1 秒内拖动货物";
            Click(clock(), "倒计时试识别");
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(countdown, test)) countdown = null; }
    }
    private void CancelTest()
    {
        countdown?.Cancel(); countdown = null; ManualTestStatus = "";
    }
    public async ValueTask DisposeAsync()
    {
        disposed = true; poll.Stop(); Enabled = false; CancelTest(); Invalidate(); await capture.DisposeAsync(); recognizer.Dispose();
    }
}
