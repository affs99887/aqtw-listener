using Listener.Core;
using System.Diagnostics;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
float[] Tone(double hz, int length = 12000, int rate = 24000) => Enumerable.Range(0, length).Select(i => (float)(.2 * Math.Sin(2 * Math.PI * hz * i / rate) * Math.Pow(Math.Sin(Math.PI * i / length), 2))).ToArray();
var audio = Tone(800); var features = AudioFeatures.Extract(audio, 24000);
SoundLibrary Library() => new()
{
    Items = [new("red", "red", true, 100), new("cheap", "cheap", false, 1), new("cheap2", "cheap2", false, 2), new("cheap3", "cheap3", false, 3)],
    Groups = [new() { Id = "shared", Name = "shared", ItemIds = ["red", "cheap", "cheap2", "cheap3"], Threshold = .85,
        Templates = [new() { Id = "a", Features = features, RecordingId = "ref" }, new() { Id = "b", Features = features, RecordingId = "ref" }] }]
};
Test("共享音效候选按物品去重，4 件中 1 件大红为 25%", () =>
{
    var r = new Recognizer(Library()).Recognize(audio, 24000);
    Check(r.CandidateCount == 4 && r.GoldCount == 1 && r.GoldCandidateRatio == .25, "multiple templates changed denominator");
});
Test("空候选不显示 0%", () =>
{
    var r = new Recognizer(Library()).Recognize(new float[24000], 24000);
    Check(r.Status == RecognitionStatus.NoSound && r.GoldCandidateRatio is null && r.HighestValue is null, "silence verdict");
});
Test("未匹配的高价物品不进入最高价值结果", () =>
{
    var l = Library(); l.Items.Add(new("irrelevant", "unmatched", true, 99999));
    Check(new Recognizer(l).Recognize(audio, 24000).HighestValue?.Item.Id == "red", "unmatched price leaked");
});
Test("未知参考价不会伪造最高价值", () =>
{
    var l = Library(); l.Items = l.Items.Select(i => i with { ReferenceValue = null }).ToList();
    Check(new Recognizer(l).Recognize(audio, 24000).HighestValue is null, "unknown price ranked");
});
Test("放下音效不能作为拾起模板", () =>
{
    var l = Library(); l.Groups[0].Action = "putdown";
    Check(new Recognizer(l).Recognize(audio, 24000).Status == RecognitionStatus.LibraryEmpty, "putdown recognized");
});
Test("静音时间洞补零，不能复用旧声音", () =>
{
    var ring = new AudioTimeline(100, 2); ring.Append([1, 2, 3], 1);
    Check(ring.Slice(1, 1.03).SequenceEqual(new float[] { 1, 2, 3 }), "timestamp placement");
    Check(ring.Slice(3, 3.03).All(x => x == 0), "old data repeated after wrap");
    ring.Clear(); Check(ring.Slice(1, 1.03).All(x => x == 0), "pause did not clear");
});
Test("长按/拖动只触发一次，松开不产生识别", () =>
{
    var latch = new ClickLatch(); Check(latch.Down(), "first click lost");
    for (var i = 0; i < 100; i++) Check(!latch.Down(), "hold retriggered");
    latch.Up(); Check(latch.Down(), "next click lost");
});
Test("连续点击和暂停使旧任务与排队结果失效", () =>
{
    var epoch = new OperationEpoch(); var first = epoch.Next(); var second = epoch.Next();
    Check(!epoch.IsCurrent(first) && epoch.IsCurrent(second), "stale completion");
    epoch.Next(); Check(!epoch.IsCurrent(second), "pause completion");
});
Test("远程按键检测在没有鼠标事件时触发，长按不重复", () =>
{
    var mouse = new MouseButtonTracker();
    Check(mouse.Poll(true, 1), "remote press lost");
    for (var i = 1; i <= 100; i++) Check(!mouse.Poll(true, 1 + i * .025), "drag retriggered");
    Check(!mouse.Poll(false, 4), "release triggered recognition");
    Check(mouse.Poll(true, 4.1), "next remote press lost");
});
Test("鼠标事件与按键检测合并去重，容忍 Windows 状态更新延迟", () =>
{
    var mouse = new MouseButtonTracker();
    Check(mouse.HookDown(1), "hook press lost");
    Check(!mouse.Poll(false, 1.02), "stale up triggered");
    Check(!mouse.Poll(true, 1.1), "same press duplicated");
    mouse.HookUp(1.2);
    Check(!mouse.Poll(true, 1.22), "stale down duplicated");
    Check(!mouse.Poll(false, 1.3), "release triggered");
    Check(mouse.Poll(true, 1.4), "fallback press lost");
    Check(!mouse.HookDown(1.41), "late hook duplicated fallback");
});
Test("漏掉松开事件后能恢复下一次拖动", () =>
{
    var mouse = new MouseButtonTracker();
    Check(mouse.HookDown(1), "first press lost");
    Check(!mouse.Poll(false, 1.2), "release triggered");
    Check(mouse.HookDown(1.3), "latch stuck after missed release");
});
Test("音量变化保持频谱匹配", () =>
{
    var quieter = AudioFeatures.Extract(audio.Select(v => v * .05f).ToArray(), 24000);
    Check(AudioFeatures.Similarity(features, quieter) > .98, "gain invariance");
});
Test("没有任何鼠标事件也能从声音窗口识别候选", () =>
{
    var ring = new AudioTimeline(24000);
    ring.Append(audio, 10);
    var scanner = new AutomaticAudioScanner(); scanner.Reset(9.5);
    var window = scanner.TryTakeWindow(ring, 10.65, active: true, busy: false);
    Check(window is not null, "audio-only trigger lost");
    using var recognizer = new Recognizer(Library());
    var result = recognizer.Recognize(window!.Samples, window.SampleRate);
    Check(result.Status == RecognitionStatus.Matched && result.CandidateCount == 4, "audio-only recognition failed");
});
Test("自动扫描只响应新的短声音事件，忙碌时不堆积任务", () =>
{
    var ring = new AudioTimeline(24000); ring.Append(Tone(800), 10);
    var scanner = new AutomaticAudioScanner(); scanner.Reset(10);
    Check(scanner.TryTakeWindow(ring, 10.2, true, false) is null, "capture warmup skipped");
    Check(scanner.TryTakeWindow(ring, 10.6, true, false) is not null, "first window lost");
    Check(scanner.TryTakeWindow(ring, 10.7, true, false) is null, "scan interval ignored");
    Check(scanner.TryTakeWindow(ring, 11.3, true, true) is null, "work queued while busy");
    Check(scanner.TryTakeWindow(ring, 11.4, true, false) is null, "same sound triggered twice");
    ring.Append(Tone(800), 12);
    var next = scanner.TryTakeWindow(ring, 12.65, true, false);
    Check(next is not null && Math.Abs(next.EndSeconds - 12.57) < .001, "new sound after busy work was lost");
});
Test("持续背景声和窗口重叠不应凭音量反复触发", () =>
{
    var ring = new AudioTimeline(24000);
    ring.Append(Enumerable.Repeat(.01f, 24000 * 3).ToArray(), 10);
    var scanner = new AutomaticAudioScanner(); scanner.Reset(10);
    for (var now = 10.6; now < 13; now += .6)
        Check(scanner.TryTakeWindow(ring, now, true, false) is null, "steady background was recognized");
});
Test("背景持续有声时拿起事件可在放下之前触发", () =>
{
    const int rate = 24000;
    var random = new Random(73);
    var samples = Enumerable.Range(0, rate * 3).Select(_ => (float)(.008 * (random.NextDouble() * 2 - 1))).ToArray();
    var pickup = Tone(900, (int)(rate * .16), rate);
    for (var i = 0; i < pickup.Length; i++) samples[rate + i] += pickup[i];
    var putdown = Tone(1600, (int)(rate * .18), rate);
    for (var i = 0; i < putdown.Length; i++) samples[rate * 2 + i] += putdown[i];
    var scanner = new AutomaticAudioScanner(); scanner.Reset(0); var ring = new AudioTimeline(rate);
    var windows = new List<AudioScanWindow>();
    for (var first = 0; first < samples.Length; first += 2400)
    {
        ring.Append(samples.AsSpan(first, 2400).ToArray(), first / (double)rate);
        var found = scanner.TryTakeWindow(ring, (first + 2400) / (double)rate, true, false);
        if (found is not null) windows.Add(found);
    }
    var eventWindow = windows.Single(window => window.OnsetSeconds is >= .95 and < 1.2);
    Check(eventWindow.EndSeconds < 1.7 && eventWindow.Focus().EndSeconds < 1.6,
        "pickup required release or focused audio included putdown");
    Check(windows.Count == 2, "background or overlapping windows retriggered the same events");
});
Test("高分单件可标精确，同音物品和边缘分数保持疑似", () =>
{
    var library = Library();
    library.Groups.Add(new SoundGroup { Id = "single", Name = "single", ItemIds = ["red"], Threshold = .75,
        Templates = [new SoundTemplate { Id = "single-reference", Features = features }] });
    var exact = CandidateSelection.Select(library, [(library.Groups[1], .94), (library.Groups[0], .7)]);
    Check(exact.Length == 1 && exact[0].Tag == RecognitionTag.Exact, "strong single item was not exact");
    var ambiguous = CandidateSelection.Select(library, [(library.Groups[0], .91), (library.Groups[1], .88)]);
    Check(ambiguous.Length == 4 && ambiguous.All(item => item.Tag == RecognitionTag.Suspected), "shared sound claimed exact item");
    Check(CandidateSelection.Select(library, [(library.Groups[0], .7)]).Length == 0, "below threshold claimed a match");
});
Test("自动识别需前后主候选一致，无法确认时保留疑似", () =>
{
    Candidate A(double score = .94, RecognitionTag tag = RecognitionTag.Exact) =>
        new(new("a", "a", false), score, "a", tag);
    Candidate B(double score = .9) => new(new("b", "b", false), score, "b");
    RecognitionAnalysis Analysis(RecognitionStatus status, Candidate[] candidates, params GroupScore[] scores) =>
        new(new(1, status, true, candidates, 1, "test"), scores);
    var full = Analysis(RecognitionStatus.Matched, [A(), B(.8)]);
    var stable = AutomaticRecognitionConsensus.Resolve(full, Analysis(RecognitionStatus.Matched, [A(.9, RecognitionTag.Suspected)]));
    Check(stable.Result.Status == RecognitionStatus.Matched &&
        stable.Result.BestMatch?.Tag == RecognitionTag.Suspected, "one pass made a single item exact");
    var switched = AutomaticRecognitionConsensus.Resolve(full, Analysis(RecognitionStatus.Matched, [B(.96), A(.8)]));
    Check(switched.Result.Status == RecognitionStatus.Unknown && switched.Result.CandidateCount == 0,
        "different leading sound replaced the result");
    var focusedOnly = Analysis(RecognitionStatus.Matched, [A(.9)]);
    var recovered = AutomaticRecognitionConsensus.Resolve(
        Analysis(RecognitionStatus.Unknown, [], new GroupScore("a", .72)), focusedOnly);
    Check(recovered.Result.Status == RecognitionStatus.Matched &&
        recovered.Result.BestMatch?.Tag == RecognitionTag.Suspected, "focused recovery should remain suspected");
    var unrelated = AutomaticRecognitionConsensus.Resolve(
        Analysis(RecognitionStatus.Unknown, [], new GroupScore("b", .76)), focusedOnly);
    Check(unrelated.Result.Status == RecognitionStatus.Unknown, "unrelated focused hit was accepted");
});
Test("关闭或切出游戏不自动扫描，恢复后静音不能复用旧声音", () =>
{
    var ring = new AudioTimeline(24000); ring.Append(audio, 10);
    var scanner = new AutomaticAudioScanner(); scanner.Reset(9.5);
    Check(scanner.TryTakeWindow(ring, 10.65, false, false) is null, "inactive capture recognized");
    ring.Clear(); scanner.Reset(11);
    Check(scanner.TryTakeWindow(ring, 11.5, true, false) is null, "old sound reused after pause");
    Check(scanner.LastWindowRms == 0, "silent window reported sound");
    ring.Append(audio, 12);
    Check(scanner.TryTakeWindow(ring, 12.65, true, false) is not null, "new sound after resume lost");
});
Test("取消信号中断 DTW", () =>
{
    using var c = new CancellationTokenSource(); c.Cancel(); Throws<OperationCanceledException>(() => AudioFeatures.Similarity(features, features, c.Token));
});
Test("重采样抑制超过目标奈奎斯特频率的成分", () =>
{
    var low = WaveAudio.Resample(Tone(1000, 48000, 48000), 48000, 24000);
    var high = WaveAudio.Resample(Tone(18000, 48000, 48000), 48000, 24000);
    Check(AudioFeatures.Rms(high) < AudioFeatures.Rms(low) * .05, "alias rejection");
});
Test("库格式与格数验证", () =>
{
    var l = Library(); l.Items[0] = l.Items[0] with { GridWidth = 0 }; Throws<InvalidDataException>(l.Validate);
    l = Library(); l.Groups[0].Threshold = double.NaN; Throws<InvalidDataException>(l.Validate);
});
Test("测试集拒绝参考来源和校准来源泄漏", () =>
{
    var temp = Path.Combine(Path.GetTempPath(), "aqtw-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
    var wav = Path.Combine(temp, "a.wav"); WaveAudio.Write(wav, audio, 24000);
    var manifest = Path.Combine(temp, "cases.json");
    var l = Library();
    JsonFile.Write(manifest, new EvaluationManifest { Cases = [new("a.wav", "ref", "red", false)] });
    Throws<InvalidDataException>(() => Evaluation.Run(l, manifest));
    l.CalibrationRecordingIds = ["validation-source"];
    JsonFile.Write(manifest, new EvaluationManifest { Cases = [new("a.wav", "validation-source", "red", false)] });
    Throws<InvalidDataException>(() => Evaluation.Run(l, manifest));
    File.Delete(wav); File.Delete(manifest); Directory.Delete(temp);
});
Test("校准不能复用 test 集", () =>
{
    var temp = Path.Combine(Path.GetTempPath(), "aqtw-manifest-" + Guid.NewGuid().ToString("N") + ".json");
    JsonFile.Write(temp, new EvaluationManifest { Cases = [new("missing.wav", "test", "red", false)] });
    Throws<InvalidDataException>(() => Evaluation.Calibrate(Library(), temp)); File.Delete(temp);
});
RecognitionResult HistoryMatch(long id, string item = "red", bool final = true) => new(id, RecognitionStatus.Matched, final,
    [new(new(item, item, true), .9, "group")], 1, "match");
var historyAt = DateTimeOffset.Parse("2026-09-22T12:00:00+08:00");
Test("辅助确认只接受近期同组的放下证据，保持拿起候选与录音", () =>
{
    var history = new RecognitionHistory(); var snapshot = Guid.NewGuid();
    Check(!history.ConfirmPutdown(new("group", .99), 1), "putdown fabricated a primary result");
    history.Remember(HistoryMatch(1), "auto", true, historyAt, snapshot);
    Check(!history.ConfirmPutdown(new("other", .99), 1) &&
        !history.ConfirmPutdown(new("group", .99), 9) && !history.ConfirmPutdown(new("group", .7), 1),
        "unrelated, stale or weak confirmation was accepted");
    Check(history.ConfirmPutdown(new("group", .96), 1), "matching putdown did not confirm");
    Check(history.Latest!.Result.PutdownConfirmed && history.Latest.SnapshotId == snapshot &&
        history.Latest.Result.Candidates.SequenceEqual(HistoryMatch(1).Candidates) && history.Latest.Matches == 1,
        "confirmation replaced the primary evidence or created another recognition");
});
Test("历史只接收有效匹配，保存候选快照", () =>
{
    var history = new RecognitionHistory();
    var candidates = HistoryMatch(1).Candidates.ToArray();
    history.Remember(HistoryMatch(1) with { Candidates = candidates }, "自动", true, historyAt);
    candidates[0] = new(new("changed", "changed", false), 0, "other");
    Check(history.Latest?.Result.BestMatch?.Item.Id == "red", "history references mutable candidates");
    Check(!history.Remember(new(2, RecognitionStatus.Unknown, true, [], 0, ""), "自动", true, historyAt), "non-match added");
    Check(!history.Remember(HistoryMatch(3) with { Candidates = [] }, "自动", true, historyAt), "empty match added");
    Check(history.Entries.Count == 1 && history.Latest?.Result.CandidateCount == 1, "non-match replaced latest");
});
Test("相邻自动扫描合并，不同候选和间隔后的匹配另记", () =>
{
    var history = new RecognitionHistory();
    history.Remember(HistoryMatch(1), "自动", true, historyAt);
    history.Remember(HistoryMatch(2), "自动", true, historyAt.AddSeconds(.6));
    Check(history.Entries.Count == 1 && history.Latest?.Matches == 2, "overlapping windows duplicated");
    history.Remember(HistoryMatch(3, "other"), "自动", true, historyAt.AddSeconds(1.2));
    history.Remember(HistoryMatch(4, "other"), "自动", true, historyAt.AddSeconds(4));
    Check(history.Entries.Count == 3, "distinct sound or later event merged");
});
Test("点击初步和最终匹配更新同条记录，独立点击另记", () =>
{
    var history = new RecognitionHistory();
    history.Remember(HistoryMatch(1, final: false), "点击", false, historyAt);
    history.Remember(HistoryMatch(1, "final"), "点击", false, historyAt.AddSeconds(.6));
    Check(history.Entries.Count == 1 && history.Latest?.Matches == 1 && history.Latest.Result.IsFinal, "two stages duplicated");
    history.Remember(HistoryMatch(2, "final"), "点击", false, historyAt.AddSeconds(1));
    Check(history.Entries.Count == 2, "independent clicks merged");
});
Test("历史限量100条，清除当前和清空历史相互独立", () =>
{
    var history = new RecognitionHistory();
    for (var i = 1; i <= 105; i++) history.Remember(HistoryMatch(i), "点击", false, historyAt.AddSeconds(i));
    Check(history.Entries.Count == 100 && history.Entries[^1].Result.OperationId == 6, "history unbounded or wrong eviction");
    history.ClearCurrent(); Check(history.Latest is null && history.Entries.Count == 100, "clearing latest erased history");
    history.Remember(HistoryMatch(106), "点击", false, historyAt.AddSeconds(106));
    history.ClearHistory(); Check(history.Entries.Count == 0 && history.Latest?.Result.OperationId == 106, "clearing history erased current");
});
var report = new List<object>(); var failed = 0;
foreach (var (name, run) in tests)
{
    var watch = Stopwatch.StartNew();
    try { run(); report.Add(new { name, passed = true, ms = watch.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; report.Add(new { name, passed = false, error = ex.Message }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
if (args.Length > 0) JsonFile.Write(args[0], new { total = tests.Count, passed = tests.Count - failed, failed, tests = report });
return failed == 0 ? 0 : 1;
