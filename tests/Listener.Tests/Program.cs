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
// The matcher ignores bands below ~0.9 kHz, where Tone(800) lives, so recognition
// fixtures use a pickup-like click. Every Library() variant shares these references.
var click = Reference(4, 6000);
var libraryRoot = ReferenceRoot(Library(), click);
InMatchRecognizer Engine(SoundLibrary library) => new(library, libraryRoot);
Test("共享音效候选按物品去重，4 件中 1 件大红为 25%", () =>
{
    using var recognizer = Engine(Library()); var r = recognizer.Recognize(click, 48000);
    Check(r.CandidateCount == 4 && r.GoldCount == 1 && r.GoldCandidateRatio == .25, "multiple templates changed denominator");
});
Test("空候选不显示 0%", () =>
{
    using var recognizer = Engine(Library()); var r = recognizer.Recognize(new float[48000], 48000);
    Check(r.Status == RecognitionStatus.NoSound && r.GoldCandidateRatio is null && r.HighestValue is null, "silence verdict");
});
Test("未匹配的高价物品不进入最高价值结果", () =>
{
    var l = Library(); l.Items.Add(new("irrelevant", "unmatched", true, 99999));
    using var recognizer = Engine(l);
    Check(recognizer.Recognize(click, 48000).HighestValue?.Item.Id == "red", "unmatched price leaked");
});
Test("未知参考价不会伪造最高价值", () =>
{
    var l = Library(); l.Items = l.Items.Select(i => i with { ReferenceValue = null }).ToList();
    using var recognizer = Engine(l);
    Check(recognizer.Recognize(click, 48000).HighestValue is null, "unknown price ranked");
});
Test("放下音效不能作为拾起模板", () =>
{
    var l = Library(); l.Groups[0].Action = "putdown";
    using var recognizer = Engine(l);
    Check(recognizer.Recognize(click, 48000).Status == RecognitionStatus.LibraryEmpty, "putdown recognized");
});
Test("已停用的识别引擎给出明确错误", () =>
{
    var l = Library(); l.PrimaryEngine = "soundradar";
    Throws<InvalidDataException>(() => RecognizerFactory.Create(l, libraryRoot));
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
Test("音量变化不改变匹配分数", () =>
{
    using var recognizer = Engine(Library());
    var loud = recognizer.ScoreAudio(click, 48000).Single().Score;
    var quiet = recognizer.ScoreAudio(click.Select(v => v * .05f).ToArray(), 48000).Single().Score;
    Check(loud > .98 && Math.Abs(loud - quiet) < .01, $"gain changed the score: {loud:F3} vs {quiet:F3}");
});
Test("没有任何鼠标事件也能从声音窗口识别候选", () =>
{
    var ring = new AudioTimeline(24000);
    ring.Append(WaveAudio.Resample(click, 48000, 24000), 10);
    var scanner = new AutomaticAudioScanner(); scanner.Reset(9.5);
    var window = scanner.TryTakeWindow(ring, 10.65, active: true, busy: false);
    Check(window is not null, "audio-only trigger lost");
    using var recognizer = Engine(Library());
    var result = PickupRecognition.Analyze(recognizer, window!).Analysis.Result;
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
Test("取消信号中断识别", () =>
{
    using var c = new CancellationTokenSource(); c.Cancel();
    using var recognizer = Engine(Library());
    Throws<OperationCanceledException>(() => recognizer.Recognize(click, 48000, cancellation: c.Token));
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
    Throws<InvalidDataException>(() => Evaluation.Run(l, manifest, libraryRoot));
    l.CalibrationRecordingIds = ["validation-source"];
    JsonFile.Write(manifest, new EvaluationManifest { Cases = [new("a.wav", "validation-source", "red", false)] });
    Throws<InvalidDataException>(() => Evaluation.Run(l, manifest, libraryRoot));
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
// A decaying tone-plus-noise click, like the catalogue's 60–300 ms item sounds.
float[] Burst(int seed, double hz, double ms = 80, int rate = 48000, double gain = 1)
{
    var random = new Random(seed); var n = (int)(rate * ms / 1000);
    return Enumerable.Range(0, n).Select(i => (float)(gain * (.6 * Math.Sin(2 * Math.PI * hz * i / rate) + .4 * (random.NextDouble() * 2 - 1)) * Math.Exp(-2.5 * i / n))).ToArray();
}
float[] Reference(int seed, double hz) { var clip = new float[21600]; Burst(seed, hz).CopyTo(clip, 0); return clip; }
// A raid: loud low-frequency rumble, a little hiss, and a quiet item click at `at` seconds.
float[] Raid(float[] click, double at, double seconds = 1.2, int rate = 48000, double clickGain = .02, int seed = 9)
{
    var random = new Random(seed);
    var samples = Enumerable.Range(0, (int)(rate * seconds)).Select(i => (float)(.05 * Math.Sin(2 * Math.PI * 90 * i / rate)
        + .03 * Math.Sin(2 * Math.PI * 140 * i / rate) + .002 * (random.NextDouble() * 2 - 1))).ToArray();
    for (var i = 0; i < click.Length; i++) samples[(int)(at * rate) + i] += (float)(clickGain * click[i]);
    return samples;
}
// One reference WAV per template of every group, at slightly different gains.
string ReferenceRoot(SoundLibrary library, float[] clip)
{
    var root = Path.Combine(Path.GetTempPath(), "aqtw-core-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "audio"));
    var references = new ReferenceAudio();
    foreach (var group in library.Groups)
        for (var index = 0; index < group.Templates.Count; index++)
        {
            var file = $"audio/{group.Id}-{index}.wav";
            WaveAudio.Write(Path.Combine(root, file), clip.Select(v => v * (1 - .1f * index)).ToArray(), 48000);
            references.Samples.Add(new(group.Id, group.Templates[index].Id, file, ReferenceAudio.Hash(Path.Combine(root, file)), group.Templates[index].RecordingId));
        }
    JsonFile.Write(Path.Combine(root, "references.json"), references);
    return root;
}
(SoundLibrary Library, string Root) InMatchLibrary()
{
    var root = Path.Combine(Path.GetTempPath(), "aqtw-inmatch-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "audio"));
    var library = new SoundLibrary { PrimaryEngine = "inmatch", Items = [new("a", "a", true), new("b", "b", false), new("c", "c", false)] };
    var references = new ReferenceAudio();
    // "c" is learned from a raid recording: room tone first, then the click.
    foreach (var (id, clip) in new[] { ("a", Reference(1, 6000)), ("b", Reference(2, 3500)), ("c", Raid(Burst(3, 5000), .3, seconds: .9, seed: 11, clickGain: .03)) })
    {
        var file = $"audio/{id}.wav"; WaveAudio.Write(Path.Combine(root, file), clip, 48000);
        library.Groups.Add(new() { Id = id, Name = id, ItemIds = [id], Threshold = .82,
            Templates = [new() { Id = id + "-1", RecordingId = id, Features = AudioFeatures.Extract(clip, 48000) }] });
        references.Samples.Add(new(id, id + "-1", file, ReferenceAudio.Hash(Path.Combine(root, file)), id));
    }
    JsonFile.Write(Path.Combine(root, "references.json"), references);
    return (library, root);
}
Test("局内匹配器：低频轰鸣中的微弱拿起声仍能匹配，且不误认其他音效", () =>
{
    var (library, root) = InMatchLibrary();
    using var recognizer = new InMatchRecognizer(library, root);
    var raid = Raid(Burst(1, 6000), .5);
    Check(AudioFeatures.Rms(raid) > AudioFeatures.Rms(Burst(1, 6000, gain: .02)) * 3, "fixture rumble is not louder than the click");
    var scores = recognizer.ScoreAudio(raid, 48000, .5).ToDictionary(s => s.Group.Id, s => s.Score);
    Check(scores["a"] >= .85 && scores["b"] < .6 && scores["c"] < .6, $"in-match scores a={scores["a"]:F3} b={scores["b"]:F3} c={scores["c"]:F3}");
    var result = recognizer.AnalyzeAt(raid, 48000, .5).Result;
    Check(result.Status == RecognitionStatus.Matched && result.Candidates.Single().Item.Id == "a", "raid pickup not recognised");
    // A template learned inside a raid still matches the same click over a different background.
    var learned = recognizer.ScoreAudio(Raid(Burst(3, 5000), .5), 48000, .5).ToDictionary(s => s.Group.Id, s => s.Score);
    Check(learned["c"] >= .85 && learned["a"] < .6, $"raid-learned template scores c={learned["c"]:F3} a={learned["a"]:F3}");
    var clean = recognizer.Recognize(Reference(1, 6000), 48000);
    Check(clean.Status == RecognitionStatus.Matched && clean.BestMatch is { Score: >= .95, Item.Id: "a" }, "clean self-match failed");
    Check(recognizer.Recognize(Raid(new float[1], .5), 48000).Status == RecognitionStatus.Unknown, "rumble alone matched");
    Check(InMatchFeatures.Template(new float[9000]) is null && InMatchFeatures.Template(new float[48000]) is null, "silence produced a template");
    Directory.Delete(root, true);
});
Test("局内匹配器：起点估计偏早或偏晚仍能对齐拿起声", () =>
{
    var (library, root) = InMatchLibrary();
    using var recognizer = new InMatchRecognizer(library, root);
    var raid = Raid(Burst(1, 6000), .5);
    for (var i = 0; i < 960; i++) raid[(int)(.41 * 48000) + i] += (float)(.01 * Math.Sin(2 * Math.PI * 2000 * i / 48000.0));
    foreach (var estimate in new[] { .42, .47, .53 })
        Check(recognizer.AnalyzeAt(raid, 48000, estimate).Result is { Status: RecognitionStatus.Matched, BestMatch.Item.Id: "a" },
            $"onset estimate {estimate} lost the pickup");
    Directory.Delete(root, true);
});
Test("扫描器按高频能量检测起点，低频轰鸣不掩盖也不触发", () =>
{
    const int rate = 48000;
    var samples = Raid(Burst(1, 6000), 1.5, seconds: 3);
    var scanner = new AutomaticAudioScanner(); scanner.Reset(0); var ring = new AudioTimeline(rate);
    var windows = new List<AudioScanWindow>();
    for (var first = 0; first < samples.Length; first += 4800)
    {
        ring.Append(samples.AsSpan(first, 4800).ToArray(), first / (double)rate);
        var found = scanner.TryTakeWindow(ring, (first + 4800) / (double)rate, true, false);
        if (found is not null) windows.Add(found);
    }
    Check(windows.Count == 1 && windows[0].OnsetSeconds is >= 1.46 and <= 1.54, "quiet click over rumble was missed or rumble triggered: " +
        string.Join(",", windows.Select(w => w.OnsetSeconds)));
    var focus = windows[0].Focus();
    Check(focus.StartSeconds <= windows[0].OnsetSeconds - .44 && focus.EndSeconds >= windows[0].OnsetSeconds + .35, "focus lacks room tone or tail");
});
Test("扫描器在更响的前置声音之后仍能检测紧随的拿起声", () =>
{
    const int rate = 48000;
    var samples = Raid(Burst(1, 6000), 1.5, seconds: 3);
    var earlier = Burst(4, 2500, 40, gain: .06);
    for (var i = 0; i < earlier.Length; i++) samples[(int)(1.2 * rate) + i] += earlier[i];
    var scanner = new AutomaticAudioScanner(); scanner.Reset(0); var ring = new AudioTimeline(rate);
    var onsets = new List<double>();
    for (var first = 0; first < samples.Length; first += 4800)
    {
        ring.Append(samples.AsSpan(first, 4800).ToArray(), first / (double)rate);
        if (scanner.TryTakeWindow(ring, (first + 4800) / (double)rate, true, false) is { } found) onsets.Add(found.OnsetSeconds);
    }
    Check(onsets.Any(o => o is >= 1.18 and <= 1.24) && onsets.Any(o => o is >= 1.46 and <= 1.54),
        "a louder click 0.3 s earlier masked the pickup: " + string.Join(",", onsets));
});
Test("拿起后一秒内的另一音效按放下声保留，不替换当前结果", () =>
{
    var history = new RecognitionHistory();
    history.Remember(HistoryMatch(1), "自动", true, historyAt, audioSeconds: 10);
    var pickup = history.Latest!;
    history.Remember(HistoryMatch(2, "twin"), "自动", true, historyAt.AddSeconds(.6), audioSeconds: 10.6);
    Check(history.Latest?.Id == pickup.Id && history.Entries.Count == 2 && history.Entries[0].FollowUp, "putdown twin replaced the pickup");
    history.Remember(HistoryMatch(3, "twin"), "自动", true, historyAt.AddSeconds(.8), audioSeconds: 10.8);
    Check(history.Latest?.Id == pickup.Id && history.Entries.Count == 2, "repeated twin scan was promoted");
    var stronger = new RecognitionResult(4, RecognitionStatus.Matched, true, [new(new("strong", "strong", true), .97, "group")], 1, "match");
    history.Remember(stronger, "自动", true, historyAt.AddSeconds(.9), audioSeconds: 10.9);
    Check(history.Latest?.Result.BestMatch?.Item.Id == "strong", "clearly stronger follow-up was held back");
    history.Remember(HistoryMatch(5, "next"), "自动", true, historyAt.AddSeconds(3), audioSeconds: 13);
    Check(history.Latest?.Result.BestMatch?.Item.Id == "next" && !history.Latest.FollowUp, "later pickup was held back");
    history.Remember(HistoryMatch(6, "click"), "点击", false, historyAt.AddSeconds(3.4), audioSeconds: 13.4);
    Check(history.Latest?.Result.BestMatch?.Item.Id == "click", "manual click was held back");
});
Test("放下声与拿起声候选相同时，再干净也不替换拿起结果的逐件分数", () =>
{
    RecognitionResult Result(long id, params (string Item, string Group, double Score)[] candidates) => new(id, RecognitionStatus.Matched, true,
        candidates.Select(c => new Candidate(new(c.Item, c.Item, true), c.Score, c.Group)).ToArray(), 1, "match");
    var history = new RecognitionHistory();
    history.Remember(Result(1, ("a", "ga", .90), ("b", "gb", .89)), "自动", true, historyAt, audioSeconds: 10);
    var pickup = history.Latest!;
    history.Remember(Result(2, ("a", "place", .97), ("b", "place", .97)), "自动", true, historyAt.AddSeconds(.6), audioSeconds: 10.6);
    Check(history.Latest?.Id == pickup.Id && history.Entries[0].FollowUp, "same-item putdown replaced the pickup's scores");
    history.Remember(Result(3, ("c", "gc", .97)), "自动", true, historyAt.AddSeconds(.9), audioSeconds: 10.9);
    Check(history.Latest?.Result.BestMatch?.Item.Id == "c", "clearly stronger different item was held back");
});
var baseRoot = Path.Combine(AppContext.BaseDirectory, "library");
SoundLibrary BaseLibrary() => JsonFile.Read<SoundLibrary>(Path.Combine(baseRoot, "library.json"));
float[] BaseReference(string group) => WaveAudio.Read(Path.Combine(baseRoot, "audio", group + ".wav")).Samples;
string[] Names(RecognitionResult result) => result.Candidates.Select(c => c.Item.Name).Order().ToArray();
// peer-047 and peer-051 were labelled 红外线理疗灯 / 天线 pickups, but SoundRadar's raw 琥珀天心-放下 and
// 目标定位-放下 captures score 0.996 / 0.968 against them: they are those classes' putdown sounds.
Test("基础库：琥珀天心类、定位组的拿起声与放下/转移声给出同一组候选，不再只报红外线理疗灯或天线", () =>
{
    using var recognizer = new InMatchRecognizer(BaseLibrary(), baseRoot);
    foreach (var (pickupGroup, putdownGroup, members, item, relabelled) in new[]
             { ("peer-024", "peer-047", 13, "胶囊电视", "红外线理疗灯"), ("peer-000", "peer-051", 9, "激光指示模块", "天线") })
    {
        var pickup = recognizer.AnalyzeAt(Raid(BaseReference(pickupGroup), .5, clickGain: .05), 48000, .5).Result;
        var putdown = recognizer.AnalyzeAt(Raid(BaseReference(putdownGroup), .5, clickGain: .05), 48000, .5).Result;
        Check(pickup is { Status: RecognitionStatus.Matched, BestMatch.Score: >= .9 } && putdown is { Status: RecognitionStatus.Matched, BestMatch.Score: >= .9 },
            $"{item}: class sounds unmatched: {pickup.BestMatch?.Score:F3} / {putdown.BestMatch?.Score:F3}");
        Check(Names(pickup).SequenceEqual(Names(putdown)), $"pickup and putdown name different items: {string.Join("、", Names(pickup))} | {string.Join("、", Names(putdown))}");
        Check(putdown.CandidateCount == members && Names(putdown).Contains(item) && Names(putdown).Contains(relabelled),
            $"{relabelled}: putdown did not return the whole class");
        Check(pickup.Candidates.Select(c => c.GroupId).Distinct().Count() == members - 1, $"{item}: pickup lost the per-item references");
    }
});
Test("基础库：拖动胶囊电视后放下不改候选，单独快速转移也给出同类候选", () =>
{
    const int rate = 48000;
    var random = new Random(5);
    var samples = Enumerable.Range(0, rate * 6).Select(i => (float)(.004 * (random.NextDouble() * 2 - 1) + .02 * Math.Sin(2 * Math.PI * 110 * i / rate))).ToArray();
    foreach (var (at, clip) in new[] { (1.0, BaseReference("peer-024")), (1.6, BaseReference("peer-047")), (4.0, BaseReference("peer-047")) })
        for (var i = 0; i < clip.Length; i++) samples[(int)(at * rate) + i] += .3f * clip[i];
    using var recognizer = new InMatchRecognizer(BaseLibrary(), baseRoot);
    var scanner = new AutomaticAudioScanner(); scanner.Reset(0); var ring = new AudioTimeline(rate); var history = new RecognitionHistory();
    var shown = new List<(double Onset, RecognitionEntry Latest)>(); var operation = 0L;
    for (var first = 0; first < samples.Length; first += rate / 10)
    {
        ring.Append(samples.AsSpan(first, rate / 10).ToArray(), first / (double)rate);
        var now = (first + rate / 10) / (double)rate;
        if (scanner.TryTakeWindow(ring, now, true, false) is not { } window) continue;
        var result = PickupRecognition.Analyze(recognizer, window, ++operation).Analysis.Result;
        history.Remember(result, "自动", true, historyAt.AddSeconds(now), audioSeconds: window.OnsetSeconds);
        shown.Add((window.OnsetSeconds, history.Latest!));
    }
    Check(shown.Count == 3, "expected pickup, putdown and transfer events: " + string.Join(",", shown.Select(s => s.Onset.ToString("F2"))));
    Check(shown.All(s => Names(s.Latest.Result).Contains("胶囊电视") && s.Latest.Result.CandidateCount == 13), "a class sound showed other candidates");
    Check(ReferenceEquals(shown[1].Latest, shown[0].Latest) && history.Entries.Any(e => e.FollowUp), "the putdown replaced the pickup result");
    Check(shown[2].Latest.Result.Candidates.All(c => c.GroupId == "peer-047"), "a lone transfer did not use the putdown/transfer sound");
});
var report = new List<object>(); var failed = 0;
foreach (var (name, run) in tests)
{
    var watch = Stopwatch.StartNew();
    try { run(); report.Add(new { name, passed = true, ms = watch.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; report.Add(new { name, passed = false, error = ex.Message }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
if (args.Length > 0) JsonFile.Write(args[0], new { total = tests.Count, passed = tests.Count - failed, failed, tests = report });
Directory.Delete(libraryRoot, true);
return failed == 0 ? 0 : 1;
