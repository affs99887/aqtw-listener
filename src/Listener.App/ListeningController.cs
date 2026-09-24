using System.Windows.Threading;

namespace Listener.App;

internal sealed record ListeningDiagnostics(bool Enabled, string TargetProcess, string ForegroundProcess,
    string State, bool Capturing, string CaptureSource, uint CapturedProcessId, string PreviewDeviceId, AudioCaptureHealth Audio,
    long TriggerCount, string LastTriggerSource, DateTimeOffset? LastTriggerAt,
    string LastIgnoredReason, string LastRecognition, string ManualTestStatus,
    bool AutomaticRecognition, long AutomaticScans, string AutomaticStatus, double AutomaticWindowRms);
internal enum AssistantMode { Listening, Interaction, Learning }
internal sealed record RecognitionActivity(long OperationId, bool Busy, string Message,
    RecognitionStatus Status = RecognitionStatus.Listening);

internal sealed class ListeningController : IAsyncDisposable
{
    private readonly Settings settings;
    private IRecognizer recognizer;
    private readonly IPlaybackCapture capture;
    private readonly Func<(string Name, uint Id)> foregroundProcess;
    private readonly Func<double> clock;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer poll;
    private readonly OperationEpoch epoch = new();
    private readonly AutomaticAudioScanner scanner = new();
    private readonly MouseEdgeLog mouse = new();
    private CancellationTokenSource? operation;
    private Task? recognitionTask;
    private long automaticScans;
    private string automaticStatus = "等待声音";
    private bool transitioning;
    private bool resumeCooling;
    private long modeEpoch;
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
    public SessionAudioStore Audio { get; } = new();
    public SoundLibrary Library { get; private set; } = new();
    public string LibraryRoot { get; private set; } = "";
    public string CaptureSession { get; } = Guid.NewGuid().ToString("N");
    public AssistantMode Mode { get; private set; }
    public RecognitionActivity CurrentActivity { get; private set; } = new(0, false, "等待声音");
    public event Action<RecognitionActivity>? Activity;
    public event Action? SnapshotAdded;
    public event Action<string>? State;
    public event Action<RecognitionResult?>? Result;
    private static bool IsGame((string Name, uint Id) process) => process.Id != 0 &&
        string.Equals(process.Name, Settings.GameProcessName, StringComparison.OrdinalIgnoreCase);
    public bool Foreground => IsGame(foregroundProcess());
    public ListeningDiagnostics Diagnostics() => new(Enabled, Settings.GameProcessName, foregroundProcess().Name,
        lastState, capture.Running, capture.DeviceName, capture.TargetProcessId, settings.DeviceId, capture.Health(), triggerCount,
        lastTriggerSource, lastTriggerAt, lastIgnoredReason, lastRecognition, ManualTestStatus,
        settings.AutomaticRecognition, automaticScans,
        !settings.AutomaticRecognition ? "已关闭（仅鼠标触发）" : !Enabled ? "等待开启监听" :
            !capture.Running ? "等待切回游戏" : automaticStatus, scanner.LastWindowRms);
    public ListeningController(Settings settings, SoundLibrary library, Dispatcher dispatcher, string? root = null)
        : this(settings, RecognizerFactory.Create(library, root), new LoopbackAudio(), dispatcher,
            NativeInput.ForegroundProcessIdentity, () => NativeInput.Now)
    { Library = library; LibraryRoot = root ?? Path.Combine(AppContext.BaseDirectory, "library"); }

    internal ListeningController(Settings settings, IRecognizer recognizer, IPlaybackCapture capture,
        Dispatcher dispatcher, Func<(string Name, uint Id)> foregroundProcess, Func<double> clock, bool startPolling = true)
    {
        this.settings = settings; this.recognizer = recognizer; this.capture = capture; this.dispatcher = dispatcher;
        this.foregroundProcess = foregroundProcess; this.clock = clock;
        History.Changed += () => Audio.Retain(History.Entries.Select(e => e.SnapshotId).Append(History.Latest?.SnapshotId));
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
    private void SetActivity(long id, bool busy, string message, RecognitionStatus status = RecognitionStatus.Listening)
    { CurrentActivity = new(id, busy, message, status); Activity?.Invoke(CurrentActivity); }
    private async Task ShowAutomaticLoading(long id, CancellationToken token)
    {
        try { await Task.Delay(150, token); if (epoch.IsCurrent(id) && recognitionTask is { IsCompleted: false } &&
            !(CurrentActivity.OperationId == id && !CurrentActivity.Busy)) SetActivity(id, true, "识别中…", RecognitionStatus.Analyzing); }
        catch (OperationCanceledException) { }
    }
    public async Task SetMode(AssistantMode mode)
    {
        var generation = ++modeEpoch;
        resumeCooling = mode == AssistantMode.Listening;
        Mode = mode; CancelTest(); Invalidate();
        if (mode == AssistantMode.Listening) await Task.Delay(300);
        if (generation != modeEpoch || disposed) return;
        resumeCooling = false;
        while (transitioning) await Task.Delay(10);
        if (generation != modeEpoch || disposed) return;
        capture.Timeline.Clear(); await Reconcile();
    }
    public bool ReadyForLearning => Mode == AssistantMode.Learning && Foreground && capture.Running;
    public string LearningUnavailableReason => !Foreground ? "学习已暂停 · 切回游戏后重新倒计时" : captureError ?? "学习已暂停 · 正在准备播放设备";
    public double Now => clock();
    public AudioClip LearningSlice(double start, double end) => new(capture.Timeline.Slice(start, end), capture.Timeline.SampleRate);
    public async Task InstallLibrary(SoundLibrary library, string root)
    {
        var next = await Task.Run(() => RecognizerFactory.Create(library, root));
        Invalidate();
        try { await PendingRecognition; }
        catch (OperationCanceledException) { }
        catch { next.Dispose(); throw; }
        var old = recognizer; recognizer = next; Library = library; LibraryRoot = root;
        await Task.Run(old.Dispose);
        SetActivity(0, false, "音效库已更新 · " + library.Version);
    }
    public void SetAutomaticRecognition(bool enabled)
    {
        settings.AutomaticRecognition = enabled;
        Invalidate();
    }
    private void PublishState(string message) { if (message == lastState) return; lastState = message; State?.Invoke(message); }
    private RecognitionResult? RetainedResult => History.Latest is { } entry
        ? entry.Result with { Message = $"{entry.LastSeen:HH:mm:ss} · 已保留 · {entry.Source}" } : null;
    private void PublishResult(RecognitionResult result, string source, bool automatic = false, AnalysisSnapshot? snapshot = null, double? onset = null,
        SoundPhase phase = SoundPhase.Unknown)
    {
        if (History.Remember(result, source, automatic, DateTimeOffset.Now, snapshot?.Id, Library.Version, LibraryRoot, onset, phase)) Result?.Invoke(RetainedResult);
        else if (History.Latest is null) Result?.Invoke(result);
        var message = result.Status switch {
            RecognitionStatus.Analyzing => "识别中…",
            RecognitionStatus.Matched when automatic && phase == SoundPhase.Putdown => "松开鼠标后的放下声 · 未替换当前结果",
            RecognitionStatus.Matched => result.IsFinal ? "识别完成" : "初步匹配 · 继续识别…",
            RecognitionStatus.Unknown => "识别失败 · 未匹配到已收录音效", RecognitionStatus.NoSound => "识别失败 · 未采到有效声音",
            RecognitionStatus.Interference => "识别失败 · 声音干扰过强",
            RecognitionStatus.LibraryEmpty => "识别失败 · 音效库无可用样本",
            RecognitionStatus.Error => "识别失败 · " + result.Message,
            _ => result.Message };
        if (History.Latest is not null && result.Status != RecognitionStatus.Matched) message += " · 下方为上次匹配结果";
        SetActivity(result.OperationId, !result.IsFinal, message, result.Status);
    }
    private void PublishAnalysis(RecognitionAnalysis analysis, float[] samples, int rate, string source, bool automatic = false, double? start = null, double? end = null, double? onset = null,
        SoundPhase phase = SoundPhase.Unknown)
    {
        AnalysisSnapshot? snapshot = null;
        if (analysis.Result.Status is RecognitionStatus.Matched or RecognitionStatus.Unknown or RecognitionStatus.Interference)
            snapshot = Audio.Add(new(samples, rate), analysis, source, Library, LibraryRoot, CaptureSession, start, end);
        PublishResult(analysis.Result, source, automatic, snapshot, onset, phase);
        Audio.Retain(History.Entries.Select(e => e.SnapshotId).Append(History.Latest?.SnapshotId));
        if (snapshot is not null) SnapshotAdded?.Invoke();
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
        if (disposed || !Enabled && Mode == AssistantMode.Listening) return;
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
        SetActivity(0, false, Mode == AssistantMode.Interaction ? "操作模式 · 识别已暂停" : "等待声音");
    }
    internal async Task Reconcile()
    {
        if (transitioning || disposed) return;
        transitioning = true;
        try
        {
            var foreground = foregroundProcess();
            var active = !resumeCooling && (Enabled && Mode == AssistantMode.Listening || Mode == AssistantMode.Learning) && IsGame(foreground);
            if (!active)
            {
                if (capture.Running) { Invalidate(); await capture.Stop(); }
                PublishState(Mode == AssistantMode.Interaction ? "操作模式 · 识别已暂停" : Mode == AssistantMode.Learning ? "学习已暂停 · 请切回游戏" : Enabled ? $"已暂停 · 等待切回暗区突围，当前：{(foreground.Name.Length == 0 ? "无法读取" : foreground.Name)}" : captureError ?? "监听已关闭"); return;
            }
            if (capture.Running && capture.TargetProcessId != foreground.Id) { Invalidate(); await capture.Stop(); }
            if ((!Enabled && Mode != AssistantMode.Learning) || Mode == AssistantMode.Interaction || !IsGame(foreground)) return;
            if (!capture.Running) { Invalidate(); await capture.Start(foreground.Id); }
            // Async process activation may finish after focus or the game PID changes.
            var current = foregroundProcess();
            if (disposed || resumeCooling || !IsGame(current) || current.Id != foreground.Id ||
                !(Enabled && Mode == AssistantMode.Listening || Mode == AssistantMode.Learning))
            {
                Invalidate(); await capture.Stop();
                PublishState("已暂停 · 游戏进程已切换"); return;
            }
            PublishState((Mode == AssistantMode.Learning ? "学习采样 · " : "正在监听 · ") + capture.DeviceName);
            if (Mode == AssistantMode.Listening) PollAutomaticRecognition();
        }
        catch (Exception ex)
        {
            Enabled = false; CancelTest(); Invalidate(); captureError = "无法采音 · " + ex.Message;
            await capture.Stop(); PublishState(captureError);
        }
        finally { transitioning = false; }
    }
    public void Release(double releasedAt, string origin)
    {
        if (!disposed) mouse.Release(releasedAt);
    }
    public void Click(double clickedAt, string origin)
    {
        if (disposed) return;
        if (origin != "倒计时试识别") mouse.Press(clickedAt);
        lastIgnoredReason = Mode != AssistantMode.Listening ? "操作或学习期间暂停识别" : !Enabled ? "监听未开启" : !Foreground ? "当前前台不是暗区突围" :
            !capture.Running || transitioning ? "采音正在准备，请再次拖动" : "";
        if (lastIgnoredReason.Length != 0) return;
        triggerCount++; lastTriggerSource = origin; lastTriggerAt = DateTimeOffset.Now;
        // With automatic listening enabled, clicks are diagnostics only. Combat
        // and remote clicks must not repeatedly cancel or postpone sound scans.
        if (settings.AutomaticRecognition && origin != "倒计时试识别") return;
        scanner.Reset(clickedAt + .6);
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
                foreach (var (delay, duration) in new[] { (450, .4), (730, .65) })
                {
                    var remaining = clickedAt + delay / 1000.0 - clock();
                    if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining), token);
                    token.ThrowIfCancellationRequested();
                    var audio = timeline.Slice(start, clickedAt + duration);
                    var analysis = recognizer.Analyze(audio, timeline.SampleRate, id, true, token);
                    var result = analysis.Result with { ElapsedMilliseconds = (clock() - clickedAt) * 1000 };
                    if (result.Status == RecognitionStatus.NoSound)
                        result = result with { Message = "已收到触发，但没有听到游戏进程声音 · 请检查游戏音量和声音输出" };
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (epoch.IsCurrent(id) && !token.IsCancellationRequested && Enabled && Foreground)
                        {
                            lastRecognition = result.Message;
                            PublishAnalysis(analysis with { Result = result }, audio, timeline.SampleRate, origin, start: start, end: clickedAt + duration);
                        }
                    });
                    if (result.Status == RecognitionStatus.Matched) break;
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
        if (Mode != AssistantMode.Listening || !settings.AutomaticRecognition || !Enabled || !Foreground || !capture.Running) return;
        var now = clock();
        var window = scanner.TryTakeWindow(capture.Timeline, now, active: true,
            busy: recognitionTask is { IsCompleted: false });
        if (window is null)
        {
            if (recognitionTask is not { IsCompleted: false })
                automaticStatus = scanner.LastWindowRms < .00008 ? "等待游戏声音" : "已收到游戏声音 · 等待拿起声";
            return;
        }
        operation?.Dispose(); operation = new();
        var token = operation.Token; var id = epoch.Next();
        var phase = mouse.Classify(window.OnsetSeconds);
        automaticScans++; automaticStatus = "正在分析声音";
        _ = ShowAutomaticLoading(id, token);
        recognitionTask = Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var pickup = PickupRecognition.Analyze(recognizer, window, id, token);
                var analysis = pickup.Analysis;
                var result = analysis.Result;
                await dispatcher.InvokeAsync(() =>
                {
                    if (!epoch.IsCurrent(id) || token.IsCancellationRequested || !settings.AutomaticRecognition || !Enabled || !Foreground) return;
                    if (result.Status == RecognitionStatus.Matched && phase == SoundPhase.Putdown)
                    {
                        automaticStatus = "已听到放下声 · 继续听音";
                        lastRecognition = "声音自动识别 · 松开鼠标后的放下声，未替换当前结果";
                    }
                    else if (result.Status == RecognitionStatus.Matched)
                    {
                        automaticStatus = "已匹配 · 继续听音";
                        lastRecognition = "声音自动识别 · 最近匹配";
                    }
                    else
                    {
                        automaticStatus = result.Status == RecognitionStatus.Unknown ? "已分析，暂未匹配" : result.Message;
                        lastRecognition = "声音自动识别 · " + automaticStatus;
                    }
                    PublishAnalysis(analysis with { Result = result with { Message = lastRecognition } }, pickup.Window.Samples, window.SampleRate, "声音自动识别", automatic: true,
                        start: pickup.Window.StartSeconds, end: pickup.Window.EndSeconds, onset: window.OnsetSeconds, phase: phase);
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
        disposed = true; poll.Stop(); Enabled = false; CancelTest(); Invalidate(); await capture.DisposeAsync();
        try { await PendingRecognition; } catch (OperationCanceledException) { }
        finally { await Task.Run(recognizer.Dispose); }
    }
}
