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
Test("同音候选按物品去重，4 件中 1 件大金为 25%", () =>
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
Test("连续声音按节奏分析，识别忙碌时不堆积任务", () =>
{
    var ring = new AudioTimeline(24000); ring.Append(Tone(800, 96000), 10);
    var scanner = new AutomaticAudioScanner(); scanner.Reset(10);
    Check(scanner.TryTakeWindow(ring, 10.2, true, false) is null, "capture warmup skipped");
    Check(scanner.TryTakeWindow(ring, 10.6, true, false) is not null, "first window lost");
    Check(scanner.TryTakeWindow(ring, 10.7, true, false) is null, "scan interval ignored");
    Check(scanner.TryTakeWindow(ring, 11.3, true, true) is null, "work queued while busy");
    var next = scanner.TryTakeWindow(ring, 12.5, true, false);
    Check(next is not null && Math.Abs(next.EndSeconds - 12.42) < .001, "stale window replayed after busy work");
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
