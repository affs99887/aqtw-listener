using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Listener.App;
using Listener.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var tests = new (string Name, Action Run)[]
        {
            ("旧配置默认开启声音识别，显式关闭仍有效", () =>
            {
                var old = JsonSerializer.Deserialize<Settings>("{\"processName\":\"wrong\"}", JsonFile.Options)!;
                Check(old.AutomaticRecognition && Settings.GameProcessName == "UAGame", "old settings disabled fallback");
                var disabled = JsonSerializer.Deserialize<Settings>("{\"automaticRecognition\":false}", JsonFile.Options)!;
                Check(!disabled.AutomaticRecognition, "explicit opt-out ignored");
            }),
            ("控制器在零鼠标触发时自动调用识别并发布结果", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                var d = f.Controller.Diagnostics();
                Check(d.TriggerCount == 0 && d.AutomaticScans == 1 && f.Recognizer.Calls == 1, "audio never reached recognizer");
                Check(f.Results.Last()?.Status == RecognitionStatus.Matched, "audio result not published");
            }),
            ("零鼠标触发的音频能通过实际 SoundRadar 工作进程返回判定", () =>
            {
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(AppContext.BaseDirectory, "library", "library.json"));
                var now = 10.0; var capture = new FakeCapture();
                var controller = new ListeningController(new Settings(), RecognizerFactory.Create(library), capture,
                    Dispatcher.CurrentDispatcher, () => "UAGame", () => now, startPolling: false);
                RecognitionResult? result = null; controller.Result += r => result = r;
                try
                {
                    controller.Toggle().GetAwaiter().GetResult(); capture.Timeline.Append(Fixture.Sound(), now);
                    now += .65; controller.Reconcile().GetAwaiter().GetResult();
                    var pending = controller.PendingRecognition; PumpUntil(() => pending.IsCompleted); pending.GetAwaiter().GetResult();
                    Check(controller.Diagnostics().TriggerCount == 0 && controller.Diagnostics().AutomaticScans == 1, "real engine trigger missing");
                    Check(result?.Status is RecognitionStatus.Matched or RecognitionStatus.Unknown, "real engine did not return a sound verdict");
                }
                finally { controller.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }),
            ("非游戏前台不采音、不进行声音识别", () =>
            {
                using var f = new Fixture(); f.Foreground = "notepad";
                f.Controller.Toggle().GetAwaiter().GetResult(); f.Capture.Timeline.Append(Fixture.Sound(), 10); f.Tick(11);
                Check(!f.Capture.Running && f.Recognizer.Calls == 0, "background audio scanned");
            }),
            ("识别引擎忙碌时不排队重复扫描", () =>
            {
                using var f = new Fixture(); f.Recognizer.Release.Reset(); f.StartSound();
                PumpUntil(() => f.Recognizer.Entered.IsSet);
                for (var i = 1; i <= 10; i++) f.Tick(10.65 + i * .1);
                Check(f.Recognizer.Calls == 1 && f.Controller.Diagnostics().AutomaticScans == 1, "unbounded scans queued");
                f.Recognizer.Release.Set(); f.Finish();
            }),
            ("切出游戏后丢弃已经在计算的声音结果", () =>
            {
                using var f = new Fixture(); f.Recognizer.Release.Reset(); f.StartSound();
                PumpUntil(() => f.Recognizer.Entered.IsSet);
                f.Foreground = "notepad"; f.Controller.ForegroundChanged();
                f.Recognizer.Release.Set(); f.Finish();
                Check(!f.Capture.Running && f.Results.All(r => r?.Status != RecognitionStatus.Matched), "stale result appeared after pause");
            }),
            ("关闭声音自动识别后丢弃旧结果且不再扫描", () =>
            {
                using var f = new Fixture(); f.Recognizer.Release.Reset(); f.StartSound();
                PumpUntil(() => f.Recognizer.Entered.IsSet);
                f.Controller.SetAutomaticRecognition(false); f.Recognizer.Release.Set(); f.Finish();
                f.Tick(11.5);
                Check(f.Recognizer.Calls == 1 && f.Results.All(r => r?.Status != RecognitionStatus.Matched), "disabled scan leaked result");
            }),
            ("静音不会清除候选，切出并恢复也保留已接受结果", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                f.Capture.Timeline.Clear(); f.Tick(60);
                Check(f.Results.Last()?.CandidateCount == 1, "retained candidate expired");
                Check(f.Recognizer.Calls == 1, "silence sent to recognizer");
                f.Foreground = "notepad"; f.Controller.ForegroundChanged();
                Check(!f.Capture.Running && f.Results.Last()?.CandidateCount == 1, "pause cleared accepted result");
                f.Foreground = "UAGame"; f.Tick(61);
                Check(f.Capture.Running && f.Results.Last()?.Message.Contains("已保留") == true, "resume lost retained label");
            }),
            ("未匹配与错误只更新诊断，不覆盖成功结果或写入历史", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                foreach (var status in new[] { RecognitionStatus.Unknown, RecognitionStatus.NoSound, RecognitionStatus.Error })
                {
                    f.Recognizer.Status = status; f.Capture.Timeline.Append(Fixture.Sound(), f.Now); f.Tick(f.Now + .65); f.Finish();
                    Check(f.Results.Last()?.CandidateCount == 1 && f.Controller.History.Entries.Count == 1, "miss replaced accepted result");
                }
            }),
            ("新有效候选替换浮窗，原候选仍可从历史查看", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                f.Recognizer.ItemId = "next"; f.Capture.Timeline.Append(Fixture.Sound(), f.Now); f.Tick(f.Now + .65); f.Finish();
                Check(f.Results.Last()?.BestMatch?.Item.Id == "next", "next match not displayed");
                Check(f.Controller.History.Entries.Count == 2 && f.Controller.History.Entries[1].Result.BestMatch?.Item.Id == "test", "previous match missing");
            }),
            ("手动清除取消正在计算的结果，但不删除历史", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                f.Recognizer.Entered.Reset(); f.Recognizer.Release.Reset();
                f.Capture.Timeline.Append(Fixture.Sound(), f.Now); f.Tick(f.Now + .65);
                PumpUntil(() => f.Recognizer.Entered.IsSet);
                f.Controller.ClearCurrentResult(); f.Recognizer.Release.Set(); f.Finish();
                Check(f.Results.Last() is null && f.Controller.History.Latest is null, "late completion revived cleared result");
                Check(f.Controller.History.Entries.Count == 1, "clear current erased history");
            }),
            ("点击分析和失败不清旧候选，同次点击两阶段只留一条历史", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                f.Recognizer.Status = RecognitionStatus.Unknown;
                f.Controller.Click(f.Now - 2, "test click"); f.Finish();
                Check(f.Results.Last()?.CandidateCount == 1 && f.Controller.History.Entries.Count == 1, "click miss cleared result");
                f.Recognizer.Status = RecognitionStatus.Matched; f.Recognizer.ItemId = "click";
                f.Controller.Click(f.Now - 2, "test click"); f.Finish();
                Check(f.Controller.History.Entries.Count == 2 && f.Controller.History.Entries[0].Result.IsFinal, "click stages duplicated history");
            }),
            ("少量候选按内容收缩，多量扩展，减少后能再次收缩", () =>
            {
                var (panel, library) = CreatePanel();
                panel.ShowResult(CatalogResult(library, 1)); Layout(panel, 470, 1000);
                var small = panel.DesiredSize.Height;
                panel.ShowResult(CatalogResult(library, 13)); Layout(panel, 470, 1000);
                Check(panel.DesiredSize.Height > small + 100, "height is fixed instead of content-driven");
                panel.ShowResult(CatalogResult(library, 1)); Layout(panel, 470, 1000);
                Check(Math.Abs(panel.DesiredSize.Height - small) < 1 && panel.PageCount == 1, "height stayed expanded");
            }),
            ("不同窗口宽度和大图下分页可访问全部候选，不裁掉或重复物品", () =>
            {
                foreach (var (width, height, size) in new[] { (300, 620, 120), (470, 680, 88), (900, 760, 120) })
                {
                    var (panel, library) = CreatePanel(size);
                    panel.ShowResult(CatalogResult(library, 51)); Layout(panel, width, height);
                    var ids = new List<string>();
                    for (var page = 0; page < panel.PageCount; page++)
                    {
                        Check(panel.PageFits, "one page clips an entire card");
                        ids.AddRange(panel.VisibleIds); panel.MovePage(1); Layout(panel, width, height);
                    }
                    Check(ids.Count == 51 && ids.Distinct().Count() == 51, "pagination lost or duplicated candidates");
                    Check(!Descendants<System.Windows.Controls.ScrollViewer>(panel).Any(), "candidate view still scrolls");
                }
            }),
            ("重复匹配保留正在看的页，新候选重置到第一页", () =>
            {
                var (panel, library) = CreatePanel(); var result = CatalogResult(library, 51);
                panel.ShowResult(result); Layout(panel, 470, 680); panel.MovePage(1); Layout(panel, 470, 680);
                var index = panel.PageIndex;
                panel.ShowResult(result with { OperationId = 99 }); Layout(panel, 470, 680);
                Check(index > 0 && panel.PageIndex == index, "repeated match reset page");
                panel.ShowResult(CatalogResult(library, 2)); Layout(panel, 470, 680);
                Check(panel.PageIndex == 0 && panel.PageCount == 1, "new result kept an invalid page");
            })
        };
        var report = new List<object>(); var failed = 0;
        foreach (var (name, run) in tests)
        {
            try { run(); report.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; report.Add(new { name, passed = false, error = ex.Message }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        if (args.Length > 0) JsonFile.Write(args[0], new { total = tests.Length, passed = tests.Length - failed, failed, tests = report });
        return failed == 0 ? 0 : 1;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static (CandidatePanel Panel, SoundLibrary Library) CreatePanel(double thumbnail = 88)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "library");
        var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
        return (new CandidatePanel(root, new Settings { ThumbnailSize = thumbnail, ShowNames = true }), library);
    }
    private static RecognitionResult CatalogResult(SoundLibrary library, int count) => new(1, RecognitionStatus.Matched, true,
        library.Items.Take(count).Select(i => new Candidate(i, .9, "test")).ToArray(), 1, "test");
    private static void Layout(CandidatePanel panel, double width, double height)
    {
        panel.Measure(new System.Windows.Size(width, height));
        panel.Arrange(new System.Windows.Rect(0, 0, width, panel.DesiredSize.Height));
        panel.UpdateLayout();
    }
    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void PumpUntil(Func<bool> done)
    {
        if (done()) return;
        var frame = new DispatcherFrame(); var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background,
            (_, _) => { if (done() || watch.Elapsed.TotalSeconds > 5) frame.Continue = false; }, Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Check(done(), "dispatcher operation timed out");
    }
    private sealed class Fixture : IDisposable
    {
        public double Now = 10;
        public string Foreground = "UAGame";
        public readonly FakeCapture Capture = new();
        public readonly FakeRecognizer Recognizer = new();
        public readonly ListeningController Controller;
        public readonly List<RecognitionResult?> Results = [];
        public Fixture()
        {
            Controller = new(new Settings(), Recognizer, Capture, Dispatcher.CurrentDispatcher, () => Foreground, () => Now, startPolling: false);
            Controller.Result += Results.Add;
        }
        public static float[] Sound() => Enumerable.Range(0, 12000).Select(i => (float)(.1 * Math.Sin(i * .2))).ToArray();
        public void StartSound()
        {
            Controller.Toggle().GetAwaiter().GetResult(); Capture.Timeline.Append(Sound(), Now); Tick(Now + .65);
        }
        public void Tick(double now) { Now = now; Controller.Reconcile().GetAwaiter().GetResult(); }
        public void Finish() { var pending = Controller.PendingRecognition; PumpUntil(() => pending.IsCompleted); pending.GetAwaiter().GetResult(); }
        public void Dispose()
        {
            Recognizer.Release.Set(); Controller.Disable().GetAwaiter().GetResult(); Finish();
            Controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Recognizer.Release.Dispose(); Recognizer.Entered.Dispose();
        }
    }
    private sealed class FakeCapture : IPlaybackCapture
    {
        public AudioTimeline Timeline { get; } = new();
        public string DeviceId => "test-device";
        public string DeviceName => "test";
        public bool Running { get; private set; }
        public event Action<string>? Failed { add { } remove { } }
        public AudioCaptureHealth Health() => new(1, 0, .1, 0);
        public string Resolve(string requested) => DeviceId;
        public void Start(string requested) => Running = true;
        public Task Stop() { Running = false; Timeline.Clear(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Running = false; return ValueTask.CompletedTask; }
    }
    private sealed class FakeRecognizer : IRecognizer
    {
        public int Calls;
        public RecognitionStatus Status = RecognitionStatus.Matched;
        public string ItemId = "test";
        public readonly ManualResetEventSlim Entered = new(false), Release = new(true);
        public RecognitionResult Recognize(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        {
            Interlocked.Increment(ref Calls); Entered.Set();
            // Deliberately finish a canceled request, as the persistent engine can do.
            if (!Release.Wait(TimeSpan.FromSeconds(4))) throw new TimeoutException("test recognizer blocked");
            return new(operationId, Status, final,
                Status == RecognitionStatus.Matched ? [new(new(ItemId, ItemId, false, null), .99, "test")] : [], 1, Status.ToString());
        }
        public (SoundGroup Group, double Score)[] ScoreAudio(float[] samples, int sampleRate, CancellationToken cancellation = default) => [];
        public void Dispose() { }
    }
}
