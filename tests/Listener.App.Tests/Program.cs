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
            ("监听浮窗显示加载动画、醒目失败和两种识别标签", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var panel = new CandidatePanel(root, new Settings(), interactive: false, compact: true);
                var items = library.Items.Take(2).ToArray();
                panel.ShowResult(new(1, RecognitionStatus.Matched, true,
                    [new(items[0], .95, "exact", RecognitionTag.Exact),
                     new(items[1], .83, "suspected", RecognitionTag.Suspected)], 1, "test"));
                panel.SetActivity(new(1, true, "识别中…", RecognitionStatus.Analyzing));
                Layout(panel, 780, 500);
                var progress = Descendants<System.Windows.Controls.ProgressBar>(panel).Single();
                Check(progress.IsIndeterminate && progress.Visibility == System.Windows.Visibility.Visible,
                    "loading animation missing");
                var labels = Descendants<System.Windows.Controls.TextBlock>(panel).Select(label => label.Text).ToArray();
                Check(labels.Contains("精确识别") && labels.Contains("疑似"), "recognition tags missing");
                panel.SetActivity(new(2, false, "未匹配到已收录音效", RecognitionStatus.Unknown));
                Layout(panel, 780, 500);
                Check(progress.Visibility == System.Windows.Visibility.Collapsed &&
                    Descendants<System.Windows.Controls.TextBlock>(panel).Any(label => label.Text.StartsWith("识别失败")),
                    "failure did not replace the loading status");
                var design = new CandidatePanel(root, new Settings(), interactive: true, compact: true);
                design.ShowResult(new(3, RecognitionStatus.Matched, true,
                    PreviewScenarios.Thirteen(library, .9),
                    1, "test"));
                Layout(design, 900, 720);
                Check(!Descendants<System.Windows.Controls.TextBlock>(design).Any(label =>
                        label.Text is "同音候选" or "疑似") && ShareLabels(design).Length == 4,
                    "the shared-sound badge remains or size groups lack red-item percentages");
            }),
            ("基础图鉴只包含已关联音效的物品", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                library.Validate();
                var unlinked = library.Items.Where(item => !library.Groups.Any(group => group.ItemIds.Contains(item.Id))).ToArray();
                Check(library.Items.Count == 62 && unlinked.Length == 0,
                    "unverified items entered the runtime catalog");
                var panel = new CandidatePanel(root, new Settings { ShowNames = true });
                var result = new RecognitionResult(1, RecognitionStatus.Matched, true,
                    library.Items.Select(item => new Candidate(item, 0, "catalog")).ToArray(), 0, "");
                panel.ShowResult(result, demo: true); Layout(panel, 820, 760);
                CheckCandidateCoverage(panel, result);
            }),
            ("控制器在零鼠标触发时自动调用识别并发布结果", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                var d = f.Controller.Diagnostics();
                Check(d.TriggerCount == 0 && d.AutomaticScans == 1 && f.Recognizer.Calls is >= 1 and <= 2,
                    "audio never reached recognizer");
                Check(f.Results.Last()?.Status == RecognitionStatus.Matched, "audio result not published");
            }),
            ("零鼠标触发的音频能通过实际局内匹配器返回判定", () =>
            {
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(AppContext.BaseDirectory, "library", "library.json"));
                var now = 10.0; var capture = new FakeCapture();
                var controller = new ListeningController(new Settings(), RecognizerFactory.Create(library), capture,
                    Dispatcher.CurrentDispatcher, () => ("UAGame", 4242u), () => now, startPolling: false);
                RecognitionResult? result = null; controller.Result += r => result = r;
                try
                {
                    controller.Toggle().GetAwaiter().GetResult(); capture.Timeline.Append(Fixture.Sound(), now);
                    now += .65; controller.Reconcile().GetAwaiter().GetResult();
                    var pending = controller.PendingRecognition; PumpUntil(() => pending.IsCompleted); pending.GetAwaiter().GetResult();
                    Check(controller.Diagnostics().TriggerCount == 0 && controller.Diagnostics().AutomaticScans == 1, "real matcher trigger missing");
                    Check(result?.Status is RecognitionStatus.Matched or RecognitionStatus.Unknown, "real matcher did not return a sound verdict");
                }
                finally { controller.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }),
            ("自动声音事件门能接住全部已收录参考片段", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var references = ReferenceAudio.Load(root).Samples;
                Check(references.Count == 62, "reference archive is incomplete");
                foreach (var reference in references)
                {
                    var clip = WaveAudio.Read(ReferenceAudio.VerifiedPath(root, reference));
                    var ring = new AudioTimeline();
                    ring.Append(WaveAudio.Resample(clip.Samples, clip.SampleRate, ring.SampleRate), 10);
                    var scanner = new AutomaticAudioScanner(); scanner.Reset(9.5);
                    var detected = new[] { 10.65, 10.85, 11.05 }
                        .Any(now => scanner.TryTakeWindow(ring, now, true, false) is not null);
                    Check(detected, "reference event was missed: " + reference.File);
                }
            }),
            ("局内持续底噪下每条参考的拿起声仍可触发", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                foreach (var reference in ReferenceAudio.Load(root).Samples)
                {
                    var clip = WaveAudio.Read(ReferenceAudio.VerifiedPath(root, reference));
                    var rate = 24000; var signal = WaveAudio.Resample(clip.Samples, clip.SampleRate, rate);
                    var noise = new Random(45); var level = AudioFeatures.Rms(signal) * .15;
                    var samples = Enumerable.Range(0, rate * 3).Select(_ => (float)(level * (noise.NextDouble() * 2 - 1))).ToArray();
                    for (var i = 0; i < signal.Length; i++) samples[rate + i] += signal[i];
                    var scanner = new AutomaticAudioScanner(); scanner.Reset(0); var ring = new AudioTimeline(rate);
                    AudioScanWindow? found = null;
                    for (var first = 0; first < samples.Length; first += 2400)
                    {
                        ring.Append(samples.AsSpan(first, 2400).ToArray(), first / (double)rate);
                        var window = scanner.TryTakeWindow(ring, (first + 2400) / (double)rate, true, false);
                        if (window is { OnsetSeconds: >= .9 and < 1.5 }) found = window;
                    }
                    Check(found is not null, "ambient sound blocked " + reference.File);
                }
            }),
            ("自动监听期间的连续鼠标点击不取消声音识别", () =>
            {
                using var f = new Fixture(); f.Controller.Toggle().GetAwaiter().GetResult();
                f.Capture.Timeline.Append(Fixture.Sound(), 10);
                for (var i = 0; i < 6; i++) { f.Controller.Click(10 + i * .1, "鼠标事件"); f.Tick(10 + i * .1); }
                f.Tick(10.65); f.Finish();
                Check(f.Controller.Diagnostics().AutomaticScans == 1 && f.Results.Last()?.Status == RecognitionStatus.Matched,
                    "input kept postponing the automatic scan");
                Check(f.Recognizer.Calls == 1 && f.Results.Last()!.IsFinal, "pickup waited for a later action");
            }),
            ("按下鼠标后的拿起声发布结果，松开鼠标后的放下声只记历史不替换", () =>
            {
                using var f = new Fixture(); f.Controller.Toggle().GetAwaiter().GetResult();
                f.Controller.Click(10, "鼠标事件"); f.Capture.Timeline.Append(Fixture.Sound(), 10.05); f.Tick(10.7); f.Finish();
                Check(f.Controller.History.Latest?.Result.BestMatch?.Item.Id == "test", "the pickup after a press was not published");
                // Held for 1.5 s, past the timing window: only the release marks the next sound as a putdown.
                f.Recognizer.ItemId = "drop";
                f.Controller.Release(11.5, "鼠标事件"); f.Capture.Timeline.Append(Fixture.Sound(), 11.55); f.Tick(12.2); f.Finish();
                Check(f.Controller.History.Latest?.Result.BestMatch?.Item.Id == "test" &&
                    f.Controller.History.Entries[0] is { FollowUp: true, Phase: SoundPhase.Putdown } &&
                    f.Results.Last()?.BestMatch?.Item.Id == "test" && f.Controller.CurrentActivity.Message.Contains("放下声"),
                    "the putdown after a release replaced the pickup");
                f.Recognizer.ItemId = "next";
                f.Controller.Click(13, "鼠标事件"); f.Capture.Timeline.Append(Fixture.Sound(), 13.05); f.Tick(13.7); f.Finish();
                Check(f.Controller.History.Latest?.Result.BestMatch?.Item.Id == "next" && f.Controller.History.Latest.Phase == SoundPhase.Pickup,
                    "the next pickup was held back");
            }),
            ("后续未匹配声音保留已发布的拿起候选与录音", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                var before = f.Controller.History.Latest!;
                f.Recognizer.Status = RecognitionStatus.Unknown;
                f.Capture.Timeline.Append(Fixture.Sound(), f.Now + .3); f.Tick(f.Now + .95); f.Finish();
                var after = f.Controller.History.Latest!;
                Check(after.SnapshotId == before.SnapshotId &&
                    after.Result.Candidates.SequenceEqual(before.Result.Candidates) && f.Controller.History.Entries.Count == 1,
                    "unmatched follow-up replaced the pickup evidence");
                f.Controller.ClearCurrentResult();
                f.Capture.Timeline.Append(Fixture.Sound(), f.Now + .3); f.Tick(f.Now + .95); f.Finish();
                Check(f.Controller.History.Latest is null, "unmatched sound created a result");
            }),
            ("拿起窗口经扫描器与匹配器可确认全部 62 条参考", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                using var recognizer = RecognizerFactory.Create(library);
                var failures = new Dictionary<string, string>();
                foreach (var reference in ReferenceAudio.Load(root).Samples)
                {
                    var clip = WaveAudio.Read(ReferenceAudio.VerifiedPath(root, reference));
                    var ring = new AudioTimeline();
                    ring.Append(WaveAudio.Resample(clip.Samples, clip.SampleRate, ring.SampleRate), 10);
                    var scanner = new AutomaticAudioScanner(); scanner.Reset(9.5);
                    var window = new[] { 10.65, 10.85, 11.05 }.Select(now => scanner.TryTakeWindow(ring, now, true, false))
                        .FirstOrDefault(found => found is not null)!;
                    var analysis = PickupRecognition.Analyze(recognizer, window).Analysis;
                    var confirmed = analysis.Result;
                    if (confirmed.Status != RecognitionStatus.Matched ||
                        !confirmed.Candidates.Any(candidate => candidate.GroupId == reference.GroupId))
                        failures.Add(reference.GroupId, reference.File + " status=" + confirmed.Status + " candidates=" +
                            string.Join(",", confirmed.Candidates.Select(c => c.GroupId)) + " scores=" +
                            string.Join(",", analysis.Scores.Take(3).Select(s => $"{s.GroupId}:{s.Score:F5}")));
                }
                // The in-match matcher anchors on the onset and ignores the bands a
                // 24 kHz timeline cannot carry, so the three jewelry references the
                // previous engine rejected now confirm as well.
                Check(failures.Count == 0, "scanner + matcher missed references: " + string.Join("; ", failures.Values));
            }),
            ("完整拿起参考均能匹配并试听，同波形关联的候选全部保留", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var references = ReferenceAudio.Load(root).Samples;
                using var recognizer = RecognizerFactory.Create(library);
                Check(!Directory.Exists(Path.Combine(root, "putdown")), "obsolete auxiliary samples were bundled");
                foreach (var reference in references)
                {
                    var clip = WaveAudio.Read(ReferenceAudio.VerifiedPath(root, reference));
                    var result = recognizer.Recognize(clip.Samples, clip.SampleRate);
                    var sameAudioGroups = references.Where(r => r.Sha256 == reference.Sha256)
                        .Select(r => r.GroupId).Append(reference.GroupId).ToHashSet();
                    Check(result.Status == RecognitionStatus.Matched &&
                        sameAudioGroups.IsSubsetOf(result.Candidates.Select(c => c.GroupId).ToHashSet()),
                        "a self-match or same-waveform candidate is missing: " + reference.File);
                    foreach (var item in result.Candidates.Select(c => c.Item))
                        Check(item.GridVerified && item.Cells > 0 && item.Thumbnail is not null &&
                            File.Exists(Path.Combine(root, item.Thumbnail)), "candidate metadata incomplete: " + item.Name);
                }
            }),
            ("非游戏前台不采音、不进行声音识别", () =>
            {
                using var f = new Fixture(); f.Foreground = "notepad";
                f.Controller.Toggle().GetAwaiter().GetResult(); f.Capture.Timeline.Append(Fixture.Sound(), 10); f.Tick(11);
                Check(!f.Capture.Running && f.Recognizer.Calls == 0, "background audio scanned");
            }),
            ("采音仅绑定前台游戏进程，进程变化后重新绑定", () =>
            {
                using var f = new Fixture();
                f.Controller.Toggle().GetAwaiter().GetResult();
                Check(f.Capture.Running && f.Capture.StartedIds.SequenceEqual([4242u]),
                    "capture did not bind to the game PID");
                f.ForegroundProcessId = 5252; f.Tick(10.5);
                Check(f.Capture.Running && f.Capture.TargetProcessId == 5252 &&
                    f.Capture.StartedIds.SequenceEqual([4242u, 5252u]), "game restart did not rebind capture");
                f.ForegroundProcessId = 0; f.Tick(10.6);
                Check(!f.Capture.Running && f.Recognizer.Calls == 0,
                    "capture continued without a verified game PID");
            }),
            ("游戏进程采音失败后停止监听，不分析混合声音", () =>
            {
                using var f = new Fixture(); f.Capture.FailStart = true;
                f.Controller.Toggle().GetAwaiter().GetResult();
                Check(!f.Controller.Enabled && !f.Capture.Running &&
                    f.Capture.StartedIds.SequenceEqual([4242u]) && f.Recognizer.Calls == 0 &&
                    f.Controller.Diagnostics().State.Contains("无法采音", StringComparison.Ordinal),
                    "failed process capture did not stop recognition");
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
                Check(f.Controller.Diagnostics().AutomaticScans == 1 && f.Recognizer.Calls is >= 1 and <= 2 &&
                    f.Results.All(r => r?.Status != RecognitionStatus.Matched), "disabled scan leaked result");
            }),
            ("静音不会清除候选，切出并恢复也保留已接受结果", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                var initialCalls = f.Recognizer.Calls;
                f.Capture.Timeline.Clear(); f.Tick(60);
                Check(f.Results.Last()?.CandidateCount == 1, "retained candidate expired");
                Check(initialCalls is >= 1 and <= 2 && f.Recognizer.Calls == initialCalls, "silence sent to recognizer");
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
                // A distinct sound well after the pickup (beyond the putdown window) is a new item.
                f.Recognizer.ItemId = "next"; f.Capture.Timeline.Append(Fixture.Sound(), f.Now + .6); f.Tick(f.Now + 1.25); f.Finish();
                Check(f.Results.Last()?.BestMatch?.Item.Id == "next", "next match not displayed");
                Check(f.Controller.History.Entries.Count == 2 && f.Controller.History.Entries[1].Result.BestMatch?.Item.Id == "test", "previous match missing");
            }),
            ("拿起后一秒内的另一音效按放下声保留在历史，不替换浮窗", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                var pickup = f.Controller.History.Latest!;
                // The putdown sound 0.6 s after the pickup resembles another catalogue item.
                f.Recognizer.ItemId = "twin"; f.Capture.Timeline.Append(Fixture.Sound(), 10.6); f.Tick(11.3); f.Finish();
                Check(f.Results.Last()?.BestMatch?.Item.Id == "test" && f.Controller.History.Latest?.Id == pickup.Id, "putdown twin replaced the pickup on the overlay");
                Check(f.Controller.History.Entries.Count == 2 && f.Controller.History.Entries[0].FollowUp &&
                    f.Controller.History.Entries[0].Result.BestMatch?.Item.Id == "twin", "follow-up sound was not kept for review");
                // The next item, dragged later, still replaces the result.
                f.Recognizer.ItemId = "later"; f.Capture.Timeline.Append(Fixture.Sound(), 12); f.Tick(12.65); f.Finish();
                Check(f.Results.Last()?.BestMatch?.Item.Id == "later" && !f.Controller.History.Latest!.FollowUp, "later pickup was held back");
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
                f.Controller.SetAutomaticRecognition(false);
                f.Recognizer.Status = RecognitionStatus.Unknown;
                f.Controller.Click(f.Now - 2, "test click"); f.Finish();
                Check(f.Results.Last()?.CandidateCount == 1 && f.Controller.History.Entries.Count == 1, "click miss cleared result");
                f.Recognizer.Status = RecognitionStatus.Matched; f.Recognizer.ItemId = "click";
                f.Controller.Click(f.Now - 2, "test click"); f.Finish();
                Check(f.Controller.History.Entries.Count == 2 && f.Controller.History.Entries[0].Result.IsFinal, "click stages duplicated history");
            }),
            ("加载状态延迟显示、完成结束，错误保留候选并标明旧结果", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                Check(!f.Controller.CurrentActivity.Busy, "quick completion kept spinner");
                f.Recognizer.Release.Reset(); f.Recognizer.Entered.Reset(); f.Recognizer.Status = RecognitionStatus.Unknown;
                f.Capture.Timeline.Append(Fixture.Sound(), f.Now); f.Tick(f.Now + .65);
                PumpUntil(() => f.Controller.CurrentActivity.Busy);
                Check(f.Results.Last()?.CandidateCount == 1, "loading blanked candidates");
                f.Recognizer.Release.Set(); f.Finish();
                Check(!f.Controller.CurrentActivity.Busy && f.Controller.CurrentActivity.Message.Contains("上次匹配"), "miss did not finish activity");
            }),
            ("操作期间不采音或识别，退出冷却完整300毫秒并保留监听开关", () =>
            {
                using var f = new Fixture(); f.StartSound(); f.Finish();
                var initialCalls = f.Recognizer.Calls;
                var enter = f.Controller.SetMode(AssistantMode.Interaction); PumpUntil(() => enter.IsCompleted); enter.GetAwaiter().GetResult();
                Check(!f.Capture.Running && f.Controller.Enabled, "interaction changed user listening toggle");
                f.Capture.Timeline.Append(Fixture.Sound(), f.Now); f.Controller.Click(f.Now, "should-ignore"); f.Tick(f.Now + .8);
                Check(initialCalls is >= 1 and <= 2 && f.Recognizer.Calls == initialCalls, "preview contaminated recognition");
                var watch = Stopwatch.StartNew(); var exit = f.Controller.SetMode(AssistantMode.Listening);
                f.Tick(f.Now + .01); Check(!f.Capture.Running, "poll bypassed cooldown");
                PumpUntil(() => exit.IsCompleted); exit.GetAwaiter().GetResult();
                Check(watch.ElapsedMilliseconds >= 290 && f.Capture.Running && f.Recognizer.Calls == initialCalls,
                    "cooldown or buffer clear failed");
                f.Controller.Disable().GetAwaiter().GetResult(); f.Controller.SetMode(AssistantMode.Interaction).GetAwaiter().GetResult();
                exit = f.Controller.SetMode(AssistantMode.Listening); PumpUntil(() => exit.IsCompleted);
                Check(!f.Capture.Running && !f.Controller.Enabled, "exit restarted disabled listener");
            }),
            ("零鼠标引导学习：切出暂停续录，三参考一留出，普通识别不调用", () =>
            {
                using var f = new Fixture();
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var store = new PersonalLibraryStore(root, Path.Combine(Path.GetTempPath(), "aqtw-guided-tests", Guid.NewGuid().ToString("N")));
                var draft = new LearningDraft { Item = new("user-guide", "test", true), GroupId = "user-guide", IsNewItem = true };
                var tick = 0; var paused = false; var completed = false;
                var learning = new GuidedLearning(f.Controller, store, (milliseconds, token) =>
                {
                    token.ThrowIfCancellationRequested(); tick++;
                    if (tick > 400) throw new Exception("learning never progressed");
                    if (tick == 5) { f.Foreground = "notepad"; f.Controller.ForegroundChanged(); }
                    if (tick == 6) { f.Foreground = "UAGame"; f.Controller.ForegroundChanged(); }
                    var count = milliseconds * 24;
                    f.Capture.Timeline.Append(Enumerable.Range(0, count).Select(i => (float)(.1 * Math.Sin((i + tick * 97) * .2))).ToArray(), f.Now);
                    f.Now += milliseconds / 1000.0; return Task.CompletedTask;
                });
                learning.Progress += message => paused |= message.Contains("暂停"); learning.Completed += _ => completed = true;
                var run = learning.Start(draft); PumpUntil(() => run.IsCompleted); run.GetAwaiter().GetResult();
                Check(paused && completed && !learning.Running && draft.Samples.Count(s => !s.CheckOnly) == 3 && draft.Samples.Count(s => s.CheckOnly) == 1, "rounds/pause failed: " + draft.Status);
                Check(f.Recognizer.Calls == 0 && f.Controller.Diagnostics().TriggerCount == 0 && f.Controller.Mode == AssistantMode.Interaction && !f.Capture.Running, "learning leaked into normal recognition");
            }),
            ("候选优先横向扩展，减少后宽高再次收缩", () =>
            {
                var (panel, library) = CreatePanel();
                panel.ShowResult(CatalogResult(library, 1));
                var smallWidth = panel.GetPreferredWidth(1300, 360); Layout(panel, smallWidth, 360);
                var smallHeight = panel.DesiredSize.Height;
                panel.ShowResult(CatalogResult(library, 13));
                var wide = panel.GetPreferredWidth(1300, 360); Layout(panel, wide, 360);
                Check(wide > smallWidth && wide <= 1300, "more candidates did not use available horizontal space");
                panel.ShowResult(CatalogResult(library, 1));
                var restoredWidth = panel.GetPreferredWidth(1300, 360); Layout(panel, restoredWidth, 360);
                Check(Math.Abs(restoredWidth - smallWidth) < 1 && Math.Abs(panel.DesiredSize.Height - smallHeight) < 1, "small result kept expanded geometry");
            }),
            ("不同窗口宽度下全部候选保留在同一布局，宽裕时无需滚动", () =>
            {
                foreach (var (width, height, size, count) in new[] { (300, 620, 120, 51), (470, 680, 88, 51), (780, 460, 120, 6) })
                {
                    var (panel, library) = CreatePanel(size);
                    var result = CatalogResult(library, count);
                    panel.ShowResult(result); Layout(panel, width, height);
                    CheckCandidateCoverage(panel, result);
                    Check(panel.DesiredSize.Height <= height + 1, "candidate panel exceeds available height");
                    if (count == 6) Check(!panel.NeedsScroll, "moderate result scrolls despite generous available space");
                }
            }),
            ("旧默认坐标升级到顶部居中，自定义位置和明确选择保持", () =>
            {
                var defaults = JsonSerializer.Deserialize<Settings>("{\"left\":32,\"top\":110}", JsonFile.Options)!;
                var moved = JsonSerializer.Deserialize<Settings>("{\"left\":850,\"top\":24}", JsonFile.Options)!;
                Check(defaults.EffectivePositionMode == OverlayPositionMode.GameTopCenter, "old default not migrated");
                Check(moved.EffectivePositionMode == OverlayPositionMode.Manual, "custom position overwritten");
                defaults.PositionMode = OverlayPositionMode.Manual;
                var saved = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(defaults, JsonFile.Options), JsonFile.Options)!;
                Check(saved.EffectivePositionMode == OverlayPositionMode.Manual, "explicit manual position lost after reload");
            }),
            ("顶部居中按游戏区域和DPI计算，覆盖窗口化与负坐标显示器", () =>
            {
                foreach (var viewport in new[] { new System.Windows.Rect(0, 0, 1920, 1080), new System.Windows.Rect(240, 80, 1280, 720),
                    new System.Windows.Rect(-2560, -1440, 2560, 1440) })
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                foreach (var width in new[] { 470.0, 780.0 })
                {
                    var b = OverlayPlacement.TopCenter(viewport, scale, scale, width);
                    Check(Math.Abs(b.LeftPixels + b.Width * scale / 2 - (viewport.Left + viewport.Width / 2)) < .01, "not centered inside game");
                    Check(b.TopPixels > viewport.Top && b.TopPixels < viewport.Top + viewport.Height * .05, "not near game top");
                    Check(b.TopPixels + b.MaximumHeight * scale <= viewport.Bottom - 8 * scale + .01,
                        "one-page layout extends beyond game viewport");
                    Check(b.Width * scale <= viewport.Width * .94 + .01, "one-page layout extends beyond game width");
                }
            }),
            ("顶部候选在多分辨率和DPI下不丢失，字号不改写缩略图偏好", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                foreach (var (pixelWidth, pixelHeight, scale) in new[] {
                    (1280, 720, 1.0), (1920, 1080, 1.5), (2560, 1440, 2.0) })
                foreach (var font in new[] { .9, 1.0, 1.15 })
                {
                    var settings = new Settings { ThumbnailSize = 120, ShowNames = true, FontScale = font };
                    var panel = new CandidatePanel(root, settings, interactive: false, compact: true);
                    foreach (var count in new[] { 1, 6, 13, 51 })
                    {
                        var result = CatalogResult(library, count); panel.ShowResult(result); panel.RefreshAppearance();
                        var budget = OverlayPlacement.TopCenter(new System.Windows.Rect(0, 0, pixelWidth, pixelHeight), scale, scale, pixelWidth / scale);
                        var preferred = panel.GetPreferredWidth(budget.Width, budget.MaximumHeight);
                        var bounds = OverlayPlacement.TopCenter(new System.Windows.Rect(0, 0, pixelWidth, pixelHeight), scale, scale, preferred);
                        Layout(panel, bounds.Width, bounds.MaximumHeight);
                        CheckCandidateCoverage(panel, result);
                        Check(!panel.NeedsScroll && panel.ContentHeight <= bounds.MaximumHeight + 1,
                            "DPI one-page layout exceeds game height: " + panel.LayoutInfo);
                        Check(settings.ThumbnailSize == 120 && panel.LayoutTransform.Value.IsIdentity, "font or adaptive layout changed image preference or scaled whole UI");
                    }
                }
            }),
            ("对比卡片逐件提供参考试听按钮，极端空间仍保留全部候选", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var previousNameSize = 0d;
                foreach (var font in new[] { .9, 1.0, 1.15 })
                {
                    var settings = new Settings { FontScale = font, ThumbnailSize = 120, ShowNames = true };
                    var panel = new CandidatePanel(root, settings, interactive: true, compact: true, minimal: true);
                    string? played = null; panel.SetAudioPreview(candidate => played = candidate.Item.Id, _ => true);
                    var result = CatalogResult(library, 51); panel.ShowResult(result); panel.RefreshAppearance(); Layout(panel, 470, 175);
                    CheckCandidateCoverage(panel, result);
                    var buttons = Descendants<System.Windows.Controls.Button>(panel).Where(b => b.Tag is Candidate).ToArray();
                    Check(buttons.Length == 51 && buttons.All(b => System.Windows.Automation.AutomationProperties.GetName(b).Contains(((Candidate)b.Tag).Item.Name)), "candidate preview buttons missing or unnamed");
                    var button = buttons.Last(); button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Check(button.IsEnabled && played == ((Candidate)button.Tag).Item.Id, "reference button did not replay its own candidate");
                    var name = Descendants<System.Windows.Controls.TextBlock>(panel).First(text => text.Text == library.Items[0].Name);
                    Check(name.FontSize > previousNameSize, "small/medium/large selection did not enlarge readable text"); previousNameSize = name.FontSize;
                }
            }),
            ("监听浮窗按宽度排列候选分组并铺满末行", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                foreach (var font in new[] { 1.0, 1.15 })
                {
                    var panel = new CandidatePanel(root, new Settings { FontScale = font, ThumbnailSize = 120, ShowNames = true }, interactive: true, compact: true);
                    var names = new[] { "热成像模块", "金豹雕像", "古董茶壶", "激光指示模块", "金狮雕像", "花瓶" };
                    var candidates = names.Select(name => library.Items.Single(item => item.Name == name))
                        .Select(item => new Candidate(item, .9, library.Groups.First(group => group.ItemIds.Contains(item.Id)).Id)).ToArray();
                    var result = new RecognitionResult(1, RecognitionStatus.Matched, true, candidates, 1, "design");
                    panel.ShowResult(result); Layout(panel, 780, 460);
                    CheckCandidateCoverage(panel, result);
                    Layout(panel, 780, 520);
                    Check(!panel.NeedsScroll && panel.ThumbnailSize >= 104,
                        "six candidates have unreadably small pictures: " + panel.LayoutInfo);
                    var sections = Descendants<System.Windows.Controls.Border>(panel)
                        .Where(border => border.Child is System.Windows.Controls.Grid grid &&
                            grid.ColumnDefinitions.Count == 2 &&
                            grid.Children.OfType<System.Windows.Controls.Border>().Any(child =>
                                ReferenceEquals(child.Background, Theme.Raised))).ToArray();
                    Check(!panel.NeedsScroll && sections.Length == 3,
                        "wide listening layout should fit: " + font + " " + panel.LayoutInfo);
                    Layout(panel, 520, 620); CheckCandidateCoverage(panel, result);
                    var narrowSections = Descendants<System.Windows.Controls.Border>(panel)
                        .Where(border => border.Child is System.Windows.Controls.Grid grid &&
                            grid.ColumnDefinitions.Count == 2 &&
                            grid.Children.OfType<System.Windows.Controls.Border>().Any(child =>
                                ReferenceEquals(child.Background, Theme.Raised))).ToArray();
                    var sectionBounds = narrowSections.Select(section => section.TransformToAncestor(panel)
                        .TransformBounds(new System.Windows.Rect(section.RenderSize))).ToArray();
                    Check(!panel.NeedsScroll && !Descendants<System.Windows.Controls.ScrollViewer>(panel).Any() &&
                        sectionBounds.Length == 3 &&
                        sectionBounds.All(bounds => bounds.Right <= panel.ActualWidth + 1),
                        "narrow listening layout clips a group: " + font + " " + panel.LayoutInfo);
                    Check(sectionBounds[2].Top > sectionBounds[0].Top + 10 &&
                        panel.ActualWidth - sectionBounds[2].Right <= 2,
                        "last listening row does not use the available width");
                    if (font == 1.0)
                    {
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(520,
                            (int)Math.Ceiling(panel.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(panel);
                        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "listening-narrow-smoke.png"));
                        png.Save(stream);
                    }
                }
            }),
            ("新目录多候选在对应空间预算内一页展示，保留图片，共享分数只在汇总行显示一次", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                // The new catalog spans six grid sizes. The whole catalog is a
                // desktop-size stress fixture, not a typical recognition result.
                foreach (var (width, viewportHeight, count) in new[] { (1203d, 720d, 24), (1362d, 813d, 51), (1600d, 1032d, 62) })
                foreach (var scale in new[] { .9, 1.0, 1.15 })
                {
                    var result = CatalogResult(library, count);
                    var panel = new CandidatePanel(root, new Settings { FontScale = scale, ShowNames = true },
                        interactive: true, compact: true);
                    // Measure text with the overlay's font; the default font has shorter lines.
                    System.Windows.Documents.TextElement.SetFontFamily(panel, new System.Windows.Media.FontFamily("Microsoft YaHei UI"));
                    panel.SetAudioPreview(_ => { }, _ => true); panel.ShowResult(result);
                    var height = Math.Min(1000, viewportHeight - 24) - 48 * scale - 26;
                    Layout(panel, width, height); CheckCandidateCoverage(panel, result);
                    var cards = Descendants<System.Windows.Controls.Border>(panel)
                        .Where(border => border.Tag is Candidate).ToArray();
                    Check(!panel.NeedsScroll && panel.ThumbnailSize >= 80 &&
                        !Descendants<System.Windows.Controls.ScrollViewer>(panel).Any(),
                        $"{width}×{viewportHeight}, 字号{scale}: " + panel.LayoutInfo);
                    Check(cards.All(card =>
                    {
                        var bounds = card.TransformToAncestor(panel).TransformBounds(new System.Windows.Rect(card.RenderSize));
                        return bounds.Right <= panel.ActualWidth + 1 && bounds.Bottom <= panel.ActualHeight + 1;
                    }), $"{width}×{viewportHeight}, 字号{scale}: a candidate lies outside the listening surface");
                    Check(cards.All(card => ShowsDifference(card, "差异率 10.0%")) && NoSummaryScore(panel),
                        $"{width}×{viewportHeight}, 字号{scale}: a card lacks its own 差异率 or a shared score remains");
                    var clipped = cards.Where(card => !ContentInside(card)).Select(card => ((Candidate)card.Tag).Item.Name).ToArray();
                    Check(clipped.Length == 0, $"{width}×{viewportHeight}, 字号{scale}: card text or button is clipped: " + string.Join("、", clipped));
                }
            }),
            ("十三候选以较窄宽度向下排布且完整显示名称", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var result = new RecognitionResult(1, RecognitionStatus.Matched, true,
                    PreviewScenarios.Thirteen(library), 1, "test");
                Check(result.Candidates.Count == 13, "the crowded reference group changed");
                foreach (var font in new[] { .9, 1.0, 1.15 })
                {
                    var panel = new CandidatePanel(root, new Settings { FontScale = font, ShowNames = true },
                        interactive: true, compact: true);
                    System.Windows.Documents.TextElement.SetFontFamily(panel, new System.Windows.Media.FontFamily("Microsoft YaHei UI"));
                    panel.SetAudioPreview(_ => { }, _ => true); panel.ShowResult(result);
                    var width = panel.GetPreferredWidth(1600, 720);
                    var height = 720 - 24 - 48 * font - 40;
                    Layout(panel, width, height);
                    Check(width <= 900 && !panel.NeedsScroll && panel.ThumbnailSize >= 80,
                        "thirteen candidates are too wide or tall: " + panel.LayoutInfo);
                    var cards = Descendants<System.Windows.Controls.Border>(panel)
                        .Where(border => border.Tag is Candidate).ToArray();
                    var groupHeadings = Descendants<System.Windows.Controls.TextBlock>(panel)
                        .Where(label => new[] { "2 格", "3 格", "4 格", "6 格" }.Contains(label.Text)).ToArray();
                    var probabilityLabels = ShareLabels(panel);
                    Check(cards.Length == 13 && groupHeadings.Length == 4 &&
                        groupHeadings.Select(label => label.Text).SequenceEqual(new[] {
                            "2 格", "3 格", "4 格", "6 格" }) &&
                        probabilityLabels.Length == 4 && probabilityLabels.All(label =>
                            label.TextTrimming == System.Windows.TextTrimming.None &&
                            label.DesiredSize.Height <= label.ActualHeight + 1 &&
                            label.TransformToAncestor(panel).TransformBounds(new System.Windows.Rect(label.RenderSize)).Bottom
                                <= panel.ActualHeight + 1),
                        "the narrow layout does not show clear size sections");
                    Check(!Descendants<System.Windows.Controls.TextBlock>(panel).Any(label =>
                            label.Text is "同音候选" or "疑似"),
                        "unrequested or uncertain badge remains on a high sound match");
                    Check(NoSummaryScore(panel), "a shared score remains in the summary row");
                    foreach (var card in cards)
                    {
                        var candidate = (Candidate)card.Tag;
                        var name = Descendants<System.Windows.Controls.TextBlock>(card)
                            .Single(label => label.Text == candidate.Item.Name);
                        var bounds = card.TransformToAncestor(panel).TransformBounds(new System.Windows.Rect(card.RenderSize));
                        Check(name.TextTrimming == System.Windows.TextTrimming.None && name.FontSize >= 13 * font &&
                            name.DesiredSize.Height <= name.ActualHeight + 1 && ShowsDifference(card, "差异率 1.0%") && ContentInside(card) &&
                            bounds.Right <= panel.ActualWidth + 1 && bounds.Bottom <= panel.ActualHeight + 1,
                            "narrow card clips its name or its own 差异率: " + candidate.Item.Name);
                    }
                }
            }),
            ("不均衡的十五候选保留独立格数分区，不留下半幅空白", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var candidates = new[] { (Cells: 2, Count: 3), (Cells: 4, Count: 9), (Cells: 6, Count: 3) }
                    .SelectMany(group => library.Items.Where(item => item.Cells == group.Cells).Take(group.Count))
                    .Select(item => new Candidate(item, .96, "layout"))
                    .ToArray();
                Check(candidates.Length == 15, "the uneven candidate fixture is incomplete");
                var result = new RecognitionResult(1, RecognitionStatus.Matched, true, candidates, 1, "test");
                foreach (var font in new[] { .9, 1.0, 1.15 })
                {
                    var panel = new CandidatePanel(root, new Settings { FontScale = font, ShowNames = true },
                        interactive: true, compact: true);
                    panel.SetAudioPreview(_ => { }, _ => true);
                    panel.ShowResult(result); Layout(panel, 900, 650);
                    CheckCandidateCoverage(panel, result);
                    var cards = Descendants<System.Windows.Controls.Border>(panel)
                        .Where(border => border.Tag is Candidate).ToArray();
                    var rows = cards.Select(card => Math.Round(card.TransformToAncestor(panel)
                        .TransformBounds(new System.Windows.Rect(card.RenderSize)).Top)).Distinct().Count();
                    var headings = Descendants<System.Windows.Controls.TextBlock>(panel)
                        .Where(label => new[] { "2 格", "4 格", "6 格" }.Contains(label.Text))
                        .Select(label => label.Text).ToArray();
                    var bandBounds = new[] { 2, 4, 6 }.Select(cells => cards
                        .Where(card => ((Candidate)card.Tag).Item.Cells == cells)
                        .Select(card => card.TransformToAncestor(panel)
                            .TransformBounds(new System.Windows.Rect(card.RenderSize))).ToArray()).ToArray();
                    var expectedRatios = new[] { 2, 4, 6 }.Select(cells =>
                    {
                        var sizeGroup = candidates.Where(candidate => candidate.Item.Cells == cells).ToArray();
                        return $"大红概率 {sizeGroup.Count(candidate => candidate.Item.IsGold) / (double)sizeGroup.Length:P0}";
                    }).ToArray();
                    var shownRatios = ShareLabels(panel).Select(ShareText).ToArray();
                    Check(rows == 5 && headings.SequenceEqual(new[] { "2 格", "4 格", "6 格" }) &&
                        shownRatios.SequenceEqual(expectedRatios) &&
                        bandBounds[0].Max(bounds => bounds.Bottom) <= bandBounds[1].Min(bounds => bounds.Top) + 1 &&
                        bandBounds[1].Max(bounds => bounds.Bottom) <= bandBounds[2].Min(bounds => bounds.Top) + 1 &&
                        !panel.NeedsScroll && panel.ThumbnailSize >= 96 && panel.ContentHeight <= 530 &&
                        !Descendants<System.Windows.Controls.Border>(panel).Any(border =>
                            border.Height == 5 && ReferenceEquals(border.Background, Theme.Gold)),
                        "uneven groups mix hierarchy, waste space or retain the ratio bar: " + panel.LayoutInfo);
                    Check(NoSummaryScore(panel), "a shared score remains in the summary row");
                    foreach (var card in cards)
                    {
                        var candidate = (Candidate)card.Tag;
                        var name = Descendants<System.Windows.Controls.TextBlock>(card)
                            .Single(label => label.Text == candidate.Item.Name);
                        Check(name.DesiredSize.Height <= name.ActualHeight + 1 &&
                            name.TextTrimming == System.Windows.TextTrimming.None && ShowsDifference(card, "差异率 4.0%"),
                            "packed candidate clips its name or its own 差异率: " + candidate.Item.Name);
                    }
                    if (font != 1.0) continue;
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(900,
                        (int)Math.Ceiling(panel.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(panel);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "listening-grouped-fifteen-smoke.png"));
                    png.Save(stream);
                }
            }),
            ("单行一至四件使用对应卡片布局且不拉长试听按钮", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var sizes = new[] { (Cells: 3, Count: 1), (Cells: 2, Count: 2),
                    (Cells: 4, Count: 3), (Cells: 6, Count: 4) };
                var candidates = sizes.SelectMany(group => library.Items
                    .Where(item => item.Cells == group.Cells).Take(group.Count))
                    .Select(item => new Candidate(item, .96, "row-design")).ToArray();
                Check(candidates.Length == 10, "the one-to-four fixture is incomplete");
                var result = new RecognitionResult(1, RecognitionStatus.Matched, true, candidates, 1, "test");
                foreach (var font in new[] { .9, 1.0, 1.15 })
                {
                    var panel = new CandidatePanel(root, new Settings { FontScale = font, ShowNames = true },
                        interactive: true, compact: true);
                    panel.SetAudioPreview(_ => { }, _ => true); panel.ShowResult(result);
                    Layout(panel, 900, 620); CheckCandidateCoverage(panel, result);
                    Check(!panel.NeedsScroll && panel.ThumbnailSize >= 80,
                        "one-to-four rows overflow or shrink images: " + panel.LayoutInfo);
                    var cards = Descendants<System.Windows.Controls.Border>(panel)
                        .Where(border => border.Tag is Candidate).ToArray();
                    var widths = sizes.Select(group => cards
                        .Where(card => ((Candidate)card.Tag).Item.Cells == group.Cells)
                        .Select(card => card.ActualWidth).Distinct().Single()).ToArray();
                    Check(widths[0] > widths[1] && widths[1] > widths[2] && widths[2] > widths[3],
                        "row density did not change card proportions");
                    var bands = sizes.OrderBy(group => group.Cells).Select(group => cards
                        .Where(card => ((Candidate)card.Tag).Item.Cells == group.Cells)
                        .Select(card => card.TransformToAncestor(panel)
                            .TransformBounds(new System.Windows.Rect(card.RenderSize))).ToArray()).ToArray();
                    Check(bands.Zip(bands.Skip(1)).All(pair =>
                            pair.First.Max(bounds => bounds.Bottom) <= pair.Second.Min(bounds => bounds.Top) + 1),
                        "size sections overlap or share a row");
                    foreach (var card in cards)
                    {
                        var candidate = (Candidate)card.Tag;
                        var name = Descendants<System.Windows.Controls.TextBlock>(card)
                            .Single(label => label.Text == candidate.Item.Name);
                        var play = Descendants<System.Windows.Controls.Button>(card)
                            .Single(button => button.Tag is Candidate);
                        var image = Descendants<System.Windows.Controls.Image>(card).Single();
                        var playBounds = play.TransformToAncestor(card)
                            .TransformBounds(new System.Windows.Rect(play.RenderSize));
                        Check(name.TextTrimming == System.Windows.TextTrimming.None &&
                            name.DesiredSize.Height <= name.ActualHeight + 1 && ShowsDifference(card, "差异率 4.0%") &&
                            image.Parent is System.Windows.Controls.Grid { ActualWidth: >= 80, ActualHeight: >= 80 } && play.ActualWidth > 60 &&
                            playBounds.Right <= card.ActualWidth + 1 && playBounds.Bottom <= card.ActualHeight + 1 &&
                            (candidate.Item.Cells is not (2 or 3) || play.ActualWidth <= 158),
                            "one-to-four card clips text, shrinks image or stretches its button: " + candidate.Item.Name);
                    }
                    if (font != 1.0) continue;
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(900,
                        (int)Math.Ceiling(panel.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(panel);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "listening-one-to-four-smoke.png"));
                    png.Save(stream);
                }
            }),
            ("候选布局在不同输入状态下保持名称、分数和分组一致", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var result = new RecognitionResult(1, RecognitionStatus.Matched, true,
                    PreviewScenarios.Thirteen(library, .9), 1, "test");
                var live = new CandidatePanel(root, new Settings { ShowNames = false }, interactive: false, compact: true);
                var operation = new CandidatePanel(root, new Settings { ShowNames = false }, interactive: true, compact: true);
                foreach (var panel in new[] { live, operation })
                {
                    panel.ShowResult(result); Layout(panel, 900, 712);
                    CheckCandidateCoverage(panel, result);
                    var text = Descendants<System.Windows.Controls.TextBlock>(panel).ToArray();
                    Check(!panel.NeedsScroll && ShareLabels(panel).Length == 4 &&
                        result.Candidates.All(candidate => text.Any(label => label.Text == candidate.Item.Name)) &&
                        text.Count(label => label.Text == "差异率 10.0%") == result.CandidateCount && NoSummaryScore(panel),
                        "live overlay lost the operation view's readable names, scores or grouping");
                }
                Check(!Descendants<System.Windows.Controls.Button>(live).Any(button => button.Tag is Candidate) &&
                    Descendants<System.Windows.Controls.Button>(operation).Count(button => button.Tag is Candidate) == result.CandidateCount,
                    "click-through listening view exposes controls that cannot be clicked");
            }),
            ("相近参考与重复物品不改变同格大红概率，参考也只显示一次", () =>
            {
                var panel = new CandidatePanel(Path.Combine(AppContext.BaseDirectory, "library"),
                    new Settings(), interactive: true, compact: true);
                var gold = new Candidate(new("gold", "收藏品", true), .95, "gold");
                var common = new Candidate(new("common", "普通物品", false), .9, "common");
                var nearby = new Candidate(new("nearby", "相近参考", true), .8, "nearby");
                panel.ShowResult(new(1, RecognitionStatus.Matched, true, [gold, common, gold], 1, "test"));
                panel.SetNearCandidates([gold, nearby, nearby]); Layout(panel, 780, 460);
                Check(panel.RenderedIds.Count == 3 && panel.RenderedIds.Distinct().Count() == 3, "references duplicated or replaced real candidates");
                Check(panel.Summary.Contains("2 件候选") &&
                    ShareLabels(panel).Count(label => ShareText(label) == "大红概率 50%") == 1,
                    "near or duplicate items changed the size group's red-item denominator");
                panel.SetNearCandidates([]); Layout(panel, 780, 460);
                Check(panel.RenderedIds.Count == 2 && !panel.RenderedIds.Contains("nearby"), "collapsing references removed candidates or left reference cards");
            }),
            ("重复匹配与新结果更新全部卡片，空结果移除旧候选", () =>
            {
                var (panel, library) = CreatePanel(); var result = CatalogResult(library, 51);
                panel.ShowResult(result); Layout(panel, 470, 680); CheckCandidateCoverage(panel, result);
                panel.ShowResult(result with { OperationId = 99 }); Layout(panel, 470, 680);
                CheckCandidateCoverage(panel, result);
                var changed = CatalogResult(library, 2); panel.ShowResult(changed); Layout(panel, 470, 680); CheckCandidateCoverage(panel, changed);
                panel.ShowResult(new(0, RecognitionStatus.Unknown, true, [], 0, "test")); Layout(panel, 470, 680);
                Check(panel.RenderedIds.Count == 0 && panel.VisibleIds.Count == 0, "empty result left stale candidates");
            }),
            ("每件候选显示自己的差异率，汇总行不再显示统一匹配度", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var items = library.Items.Where(item => item.Cells == 4).Take(3).ToArray();
                var result = new RecognitionResult(1, RecognitionStatus.Matched, true,
                    [new(items[0], .96, "a"), new(items[1], .955, "a"), new(items[2], .88, "b")], 1, "test");
                var panel = new CandidatePanel(root, new Settings { ShowNames = true }, interactive: true, compact: true);
                panel.SetAudioPreview(_ => { }, _ => true); panel.ShowResult(result); Layout(panel, 900, 620);
                System.Windows.Controls.Border Card(ItemDefinition item) =>
                    Descendants<System.Windows.Controls.Border>(panel).Single(card => (card.Tag as Candidate)?.Item.Id == item.Id);
                var lower = Descendants<System.Windows.Controls.TextBlock>(Card(items[2])).SingleOrDefault(label => label.Text == "差异率 12.0%");
                Check(ShowsDifference(Card(items[0]), "差异率 4.0%") && ShowsDifference(Card(items[1]), "差异率 4.5%") &&
                    ShowsDifference(Card(items[2]), "差异率 12.0%") && lower is not null && ReferenceEquals(lower.Foreground, Theme.Muted) &&
                    NoSummaryScore(panel),
                    "a card lacks its own 差异率 or the summary row still carries one score for all candidates");
                panel.ShowResult(new(2, RecognitionStatus.Matched, true, library.Items.Take(3).Select(item => new Candidate(item, 0, "catalog")).ToArray(), 0, ""), demo: true);
                Layout(panel, 900, 620);
                Check(!Descendants<System.Windows.Controls.TextBlock>(panel).Any(label => label.Text.Contains("差异率", StringComparison.Ordinal)),
                    "catalog pages show a 差异率 for items that were never heard");
            }),
            ("大红概率是分组内最大的数字，并按比例着色", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                foreach (var font in new[] { .9, 1.0, 1.15 })
                {
                    var panel = new CandidatePanel(root, new Settings { FontScale = font, ShowNames = true }, interactive: true, compact: true);
                    System.Windows.Documents.TextElement.SetFontFamily(panel, new System.Windows.Media.FontFamily("Microsoft YaHei UI"));
                    panel.SetAudioPreview(_ => { }, _ => true);
                    panel.ShowResult(new(1, RecognitionStatus.Matched, true, PreviewScenarios.Thirteen(library), 1, "test"));
                    Layout(panel, 900, 720 - 24 - 48 * font);
                    var shares = ShareLabels(panel).ToDictionary(ShareText, label => (System.Windows.Documents.Run)label.Inlines.FirstInline);
                    var largestName = Descendants<System.Windows.Controls.Border>(panel).Where(card => card.Tag is Candidate)
                        .SelectMany(card => Descendants<System.Windows.Controls.TextBlock>(card)).Max(label => label.FontSize);
                    // An unconstrained copy of the text must fit where the label was arranged.
                    bool Fits(System.Windows.Controls.TextBlock label)
                    {
                        var copy = new System.Windows.Controls.TextBlock { FontFamily = label.FontFamily };
                        foreach (var run in label.Inlines.OfType<System.Windows.Documents.Run>())
                            copy.Inlines.Add(new System.Windows.Documents.Run(run.Text) { FontSize = run.FontSize, FontWeight = run.FontWeight });
                        copy.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                        return copy.DesiredSize.Width <= label.ActualWidth + .5 && copy.DesiredSize.Height <= label.ActualHeight + 1;
                    }
                    Check(shares.Count == 4 &&
                        ReferenceEquals(shares["大红概率 0%"].Foreground, Theme.Faint) &&
                        ReferenceEquals(shares["大红概率 20%"].Foreground, Theme.Collectible) &&
                        ReferenceEquals(shares["大红概率 50%"].Foreground, Theme.Hot) &&
                        ReferenceEquals(shares["大红概率 100%"].Foreground, Theme.Hot) &&
                        shares.Values.All(run => run.FontSize > largestName) && ShareLabels(panel).All(Fits),
                        "red-item shares are not the most prominent, clip, or ignore their value at font " + font);
                }
            }),
            ("两件一行时试听按钮在名称下方，名称保持一行", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var candidates = new[] { "热成像模块", "金豹雕像", "古董茶壶", "激光指示模块", "金狮雕像", "花瓶" }
                    .Select(name => new Candidate(library.Items.Single(item => item.Name == name), .9, "g")).ToArray();
                foreach (var font in new[] { .9, 1.0, 1.15 })
                {
                    var panel = new CandidatePanel(root, new Settings { FontScale = font, ShowNames = true }, interactive: true, compact: true);
                    System.Windows.Documents.TextElement.SetFontFamily(panel, new System.Windows.Media.FontFamily("Microsoft YaHei UI"));
                    panel.SetAudioPreview(_ => { }, _ => true);
                    panel.ShowResult(new(1, RecognitionStatus.Matched, true, candidates, 1, "test"));
                    Layout(panel, panel.GetPreferredWidth(1600, 720), 720 - 24 - 48 * font);
                    foreach (var card in Descendants<System.Windows.Controls.Border>(panel).Where(card => card.Tag is Candidate))
                    {
                        var item = ((Candidate)card.Tag).Item;
                        var name = Descendants<System.Windows.Controls.TextBlock>(card).Single(label => label.Text == item.Name);
                        var play = Descendants<System.Windows.Controls.Button>(card).Single(button => button.Tag is Candidate);
                        var line = new System.Windows.Controls.TextBlock { Text = name.Text, FontSize = name.FontSize,
                            FontWeight = name.FontWeight, FontFamily = name.FontFamily };
                        line.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                        var nameBounds = name.TransformToAncestor(card).TransformBounds(new System.Windows.Rect(name.RenderSize));
                        var playBounds = play.TransformToAncestor(card).TransformBounds(new System.Windows.Rect(play.RenderSize));
                        Check(name.ActualHeight <= line.DesiredSize.Height + 1 && playBounds.Top >= nameBounds.Bottom &&
                            playBounds.Width <= 120 * font && playBounds.Bottom <= card.ActualHeight + 1,
                            $"paired card wraps its name or misplaces its button at font {font}: {item.Name}");
                    }
                }
            }),
            ("实时结果显示识别时间，超过 10 秒后候选变暗", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var panel = new CandidatePanel(root, new Settings { ShowNames = true }, interactive: true, compact: true);
                panel.ShowResult(new(1, RecognitionStatus.Matched, true, PreviewScenarios.Thirteen(library), 1, "test"), listening: true);
                panel.SetFreshness(DateTimeOffset.Now); Layout(panel, 900, 700);
                var cards = Descendants<CandidateLayout>(panel).Single();
                bool Shows(string text) => Descendants<System.Windows.Controls.TextBlock>(panel)
                    .Any(label => label.Text == text && label.Visibility == System.Windows.Visibility.Visible);
                Check(Shows("刚刚") && cards.Opacity == 1, "a fresh result is not marked as just heard");
                panel.SetFreshness(DateTimeOffset.Now.AddSeconds(-25)); Layout(panel, 900, 700);
                Check(Shows("25 秒前") && cards.Opacity < 1, "an old result did not fade");
                panel.SetFreshness(null); Layout(panel, 900, 700);
                Check(!Shows("25 秒前") && !Shows("刚刚") && cards.Opacity == 1, "a reviewed recording kept the live age");
            }),
            ("历史列表标出疑似放下声，并按格数汇总大红", () =>
            {
                var root = Path.Combine(AppContext.BaseDirectory, "library");
                var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
                var fourGold = library.Items.First(item => item.Cells == 4 && item.IsGold);
                var fourPlain = library.Items.First(item => item.Cells == 4 && !item.IsGold);
                var sixGold = library.Items.First(item => item.Cells == 6 && item.IsGold);
                var pickup = new RecognitionResult(1, RecognitionStatus.Matched, true,
                    [new(fourGold, .96, "a"), new(fourPlain, .96, "a"), new(sixGold, .96, "a")], 1, "pickup");
                var twin = new RecognitionResult(2, RecognitionStatus.Matched, true, [new(fourPlain, .9, "b")], 1, "putdown");
                Check(HistoryWindow.SizeSummary(pickup) == "4 格 大红 1/2 · 6 格 大红 1/1", "size summary changed: " + HistoryWindow.SizeSummary(pickup));
                var history = new RecognitionHistory();
                history.Remember(pickup, "声音自动识别", true, DateTimeOffset.Now, audioSeconds: 10);
                history.Remember(twin, "声音自动识别", true, DateTimeOffset.Now.AddSeconds(.4), audioSeconds: 10.4);
                var window = new HistoryWindow(history, root, new Settings());
                try
                {
                    var content = (System.Windows.FrameworkElement)window.Content;
                    content.Measure(new System.Windows.Size(1060, 700)); content.Arrange(new System.Windows.Rect(0, 0, 1060, 700)); content.UpdateLayout();
                    var rows = Descendants<System.Windows.Controls.ListBox>(content).Single().Items.Cast<System.Windows.Controls.ListBoxItem>().ToArray();
                    bool Marked(System.Windows.Controls.ListBoxItem row) => Descendants<System.Windows.Controls.TextBlock>((System.Windows.DependencyObject)row.Content)
                        .Any(label => label.Text == "疑似放下声");
                    Check(history.Entries[0].FollowUp && rows.Length == 2 && Marked(rows[0]) && rows[0].Opacity < 1 && !Marked(rows[1]),
                        "the putdown twin is not marked in the history list");
                    Check(Descendants<System.Windows.Controls.TextBlock>(content).Any(label => label.Text.StartsWith("疑似放下声：", StringComparison.Ordinal)),
                        "the selected follow-up lacks its explanation");
                }
                finally { window.Close(); }
            }),
            ("历史记录以单一列表展示最近100条，更新保留选中项", () =>
            {
                var (_, library) = CreatePanel(); var history = new RecognitionHistory();
                for (var i = 0; i < 105; i++) history.Remember(CatalogResult(library, 1) with { OperationId = i }, "test", false, DateTimeOffset.Now.AddSeconds(i * 3));
                var window = new HistoryWindow(history, Path.Combine(AppContext.BaseDirectory, "library"), new Settings());
                try
                {
                    Check(window.VisibleRecordIds.Count == 100 && window.VisibleRecordIds.SequenceEqual(history.Entries.Select(e => e.Id)), "history list omitted or reordered stored records");
                    var content = (System.Windows.FrameworkElement)window.Content;
                    content.Measure(new System.Windows.Size(1060, 700)); content.Arrange(new System.Windows.Rect(0, 0, 1060, 700)); content.UpdateLayout();
                    var list = Descendants<System.Windows.Controls.ListBox>(content).Single();
                    list.SelectedIndex = 25; var selected = ((RecognitionEntry)((System.Windows.Controls.ListBoxItem)list.SelectedItem).Tag).Id;
                    history.Remember(CatalogResult(library, 2) with { OperationId = 200 }, "test", false, DateTimeOffset.Now.AddMinutes(20));
                    Check(((RecognitionEntry)((System.Windows.Controls.ListBoxItem)list.SelectedItem).Tag).Id == selected, "new history entry reset selection");
                    list.SelectedIndex = 99; list.ScrollIntoView(list.SelectedItem); content.UpdateLayout();
                    Check(list.SelectedItem is not null && window.VisibleRecordIds.Count == 100, "last retained record cannot be selected");
                    history.ClearHistory(); Check(window.VisibleRecordIds.Count == 0, "clear history left visible rows");
                }
                finally { window.Close(); }
            })
        };
        var report = new List<object>(); var failed = 0;
        foreach (var (name, run) in tests.Concat(PersonalLibraryTests.Cases()))
        {
            try { run(); report.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; report.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex); }
        }
        if (args.Length > 0) JsonFile.Write(args[0], new { total = report.Count, passed = report.Count - failed, failed, tests = report });
        return failed == 0 ? 0 : 1;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void CheckCandidateCoverage(CandidatePanel panel, RecognitionResult result)
    {
        var expected = result.Candidates.Select(c => c.Item.Id).ToHashSet();
        Check(panel.RenderedIds.Count == expected.Count && panel.RenderedIds.Distinct().Count() == expected.Count && expected.SetEquals(panel.RenderedIds), "full candidate layout lost or duplicated items: " + panel.LayoutInfo);
        Check(expected.SetEquals(panel.VisibleIds), "candidate IDs differ from complete result");
        var cards = Descendants<System.Windows.Controls.Border>(panel).Where(border => border.Tag is Candidate)
            .Select(border => ((Candidate)border.Tag).Item.Id).ToArray();
        Check(cards.Length == expected.Count && expected.SetEquals(cards), "actual card tree does not contain the complete result");
    }
    // A card's own 差异率 label, wholly inside the card.
    private static bool ShowsDifference(System.Windows.Controls.Border card, string expected)
    {
        var labels = Descendants<System.Windows.Controls.TextBlock>(card).Where(label => label.Text.Contains("差异率", StringComparison.Ordinal)).ToArray();
        if (labels.Length != 1 || !labels[0].Text.Contains(expected, StringComparison.Ordinal)) return false;
        var bounds = labels[0].TransformToAncestor(card).TransformBounds(new System.Windows.Rect(labels[0].RenderSize));
        return bounds.Top >= -1 && bounds.Bottom <= card.ActualHeight + 1 && bounds.Right <= card.ActualWidth + 1;
    }
    // Nothing in a fixed-height card may be pushed outside it and clipped.
    private static bool ContentInside(System.Windows.Controls.Border card) =>
        Descendants<System.Windows.FrameworkElement>(card).Where(element => element is System.Windows.Controls.TextBlock or System.Windows.Controls.Button &&
                element.Visibility == System.Windows.Visibility.Visible)
            .All(element =>
            {
                var bounds = element.TransformToAncestor(card).TransformBounds(new System.Windows.Rect(element.RenderSize));
                return bounds.Top >= -1 && bounds.Bottom <= card.ActualHeight + 1 && bounds.Left >= -1 && bounds.Right <= card.ActualWidth + 1;
            });
    private static bool NoSummaryScore(System.Windows.DependencyObject panel) =>
        !Descendants<System.Windows.Controls.TextBlock>(panel).Any(label => label.Text.Contains("匹配", StringComparison.Ordinal));
    // Each size group's red-item share, found by its accessible name ("大红概率 50%").
    private static System.Windows.Controls.TextBlock[] ShareLabels(System.Windows.DependencyObject root) =>
        Descendants<System.Windows.Controls.TextBlock>(root).Where(label =>
            System.Windows.Automation.AutomationProperties.GetName(label).StartsWith("大红概率 ", StringComparison.Ordinal)).ToArray();
    private static string ShareText(System.Windows.Controls.TextBlock label) =>
        System.Windows.Automation.AutomationProperties.GetName(label);
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
        public uint ForegroundProcessId = 4242;
        public readonly FakeCapture Capture = new();
        public readonly FakeRecognizer Recognizer = new();
        public readonly ListeningController Controller;
        public readonly List<RecognitionResult?> Results = [];
        public Fixture()
        {
            Controller = new(new Settings(), Recognizer, Capture, Dispatcher.CurrentDispatcher,
                () => (Foreground, ForegroundProcessId), () => Now, startPolling: false);
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
        public string DeviceName => "test";
        public uint TargetProcessId { get; private set; }
        public List<uint> StartedIds { get; } = [];
        public bool FailStart;
        public bool Running { get; private set; }
        public event Action<string>? Failed { add { } remove { } }
        public AudioCaptureHealth Health() => new(1, 0, .1, 0);
        public Task Start(uint processId)
        {
            StartedIds.Add(processId);
            if (FailStart) throw new NotSupportedException("process loopback unavailable");
            TargetProcessId = processId; Running = true; return Task.CompletedTask;
        }
        public Task Stop() { Running = false; TargetProcessId = 0; Timeline.Clear(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Running = false; TargetProcessId = 0; return ValueTask.CompletedTask; }
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
