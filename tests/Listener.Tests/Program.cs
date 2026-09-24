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
Test("松开鼠标由事件或按键检测各报告一次，不重复", () =>
{
    var mouse = new MouseButtonTracker();
    Check(mouse.HookDown(1) && mouse.HookUp(1.3), "hook release lost");
    Check(mouse.Sample(false, 1.5) == MouseEdge.None, "the poll repeated a hook release");
    Check(mouse.Sample(true, 2) == MouseEdge.Press && mouse.Sample(true, 2.1) == MouseEdge.None, "remote press lost or repeated");
    Check(mouse.Sample(false, 2.4) == MouseEdge.Release && mouse.Sample(false, 2.5) == MouseEdge.None, "remote release lost or repeated");
    Check(!mouse.HookUp(2.6), "a release without a press was reported");
});
Test("按下后的声音归为拿起声，松开后的归为放下声，远离鼠标操作的声音未知", () =>
{
    var log = new MouseEdgeLog();
    Check(log.Classify(10) == SoundPhase.Unknown, "no input produced a phase");
    log.Press(10); log.Release(10.9); log.Press(11.2);
    Check(log.Classify(10.08) == SoundPhase.Pickup, "the sound after a press was not a pickup");
    Check(log.Classify(10.97) == SoundPhase.Putdown, "the sound after a release was not a putdown");
    Check(log.Classify(11.18) == SoundPhase.Pickup, "an onset placed just before the press timestamp lost its press");
    Check(log.Classify(10.6) == SoundPhase.Unknown && log.Classify(12) == SoundPhase.Unknown, "a sound far from any edge was attributed to it");
});
Test("声音归到的按键边沿带时间戳，同一次按下内的多段声音共用它", () =>
{
    var log = new MouseEdgeLog();
    log.Press(10); log.Release(10.9);
    Check(log.Latest(10.08) is { At: 10, Press: true } && log.Latest(10.3) is { At: 10, Press: true }, "two sounds inside one hold did not share the press");
    Check(log.Latest(10.97) is { At: 10.9, Press: false }, "the sound after the release lost its edge");
    Check(log.Latest(12) is null, "a sound far from any edge was given one");
    Check(log.Recent().Select(e => (e.At, e.Press)).SequenceEqual([(10, true), (10.9, false)]), "recent edges are not in order");
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
Test("松开鼠标后的放下声再强也不替换拿起结果，按下后的拿起声不被当成放下声扣留", () =>
{
    RecognitionResult Result(long id, string item, double score) => new(id, RecognitionStatus.Matched, true,
        [new(new(item, item, true), score, "g-" + item)], 1, "match");
    var history = new RecognitionHistory();
    history.Remember(Result(1, "pickup", .9), "自动", true, historyAt, audioSeconds: 10, phase: SoundPhase.Pickup);
    var pickup = history.Latest!;
    // Past the timing window and clearly stronger: only the mouse says it is a putdown.
    history.Remember(Result(2, "lamp", .99), "自动", true, historyAt.AddSeconds(2.5), audioSeconds: 12.5, phase: SoundPhase.Putdown);
    Check(history.Latest?.Id == pickup.Id && history.Entries[0] is { FollowUp: true, Phase: SoundPhase.Putdown }, "a putdown replaced the pickup");
    history.Remember(Result(3, "lamp", .99), "自动", true, historyAt.AddSeconds(2.7), audioSeconds: 12.5, phase: SoundPhase.Putdown);
    Check(history.Latest?.Id == pickup.Id && history.Entries.Count == 2, "an overlapping scan of the putdown was promoted");
    // 0.6 s after a pickup and weaker, which timing alone holds as a putdown twin: a new press says pickup.
    history.Remember(Result(4, "second", .8), "自动", true, historyAt.AddSeconds(3.1), audioSeconds: 13.1, phase: SoundPhase.Pickup);
    history.Remember(Result(5, "third", .75), "自动", true, historyAt.AddSeconds(3.7), audioSeconds: 13.7, phase: SoundPhase.Pickup);
    Check(history.Latest?.Result.BestMatch?.Item.Id == "third", "a pickup after a new press was held back");
    var timed = new RecognitionHistory();
    timed.Remember(Result(1, "pickup", .9), "自动", true, historyAt, audioSeconds: 10);
    timed.Remember(Result(2, "second", .8), "自动", true, historyAt.AddSeconds(.6), audioSeconds: 10.6);
    Check(timed.Latest?.Result.BestMatch?.Item.Id == "pickup" && timed.Entries[0].Phase == SoundPhase.Unknown, "the timing fallback changed");
});
var baseRoot = Path.Combine(AppContext.BaseDirectory, "library");
SoundLibrary BaseLibrary() => JsonFile.Read<SoundLibrary>(Path.Combine(baseRoot, "library.json"));
float[] BaseReference(string group) => WaveAudio.Read(Path.Combine(baseRoot, "audio", group + ".wav")).Samples;
string[] Names(RecognitionResult result) => result.Candidates.Select(c => c.Item.Name).Order().ToArray();
// peer-047 and peer-051 were labelled 红外线理疗灯 / 天线 pickups, but SoundRadar's raw 琥珀天心-放下 and
// 目标定位-放下 captures score 0.996 / 0.968 against them: they are those classes' putdown sounds.
// Putting an item down is never recognised, so they are putdown-sound groups that veto a window.
Test("基础库：琥珀天心类、定位组的拿起声给出整类候选，放下/转移声判为放下声且不给候选", () =>
{
    var library = BaseLibrary();
    using var recognizer = new InMatchRecognizer(library, baseRoot);
    foreach (var (pickupGroup, putdownGroup, members, item, relabelled) in new[]
             { ("peer-024", "peer-047", 13, "胶囊电视", "红外线理疗灯"), ("peer-000", "peer-051", 9, "激光指示模块", "天线") })
    {
        Check(library.Groups.Single(g => g.Id == putdownGroup).Action == "putdown", putdownGroup + " is not a putdown-sound group");
        var pickup = recognizer.AnalyzeAt(Raid(BaseReference(pickupGroup), .5, clickGain: .05), 48000, .5);
        var putdown = recognizer.AnalyzeAt(Raid(BaseReference(putdownGroup), .5, clickGain: .05), 48000, .5);
        Check(pickup.Result is { Status: RecognitionStatus.Matched, BestMatch.Score: >= .9 } && pickup.Putdown is null,
            $"{item}: pickup unmatched or taken for a putdown: {pickup.Result.BestMatch?.Score:F3}");
        Check(pickup.Result.CandidateCount == members && Names(pickup.Result).Contains(item) && Names(pickup.Result).Contains(relabelled) &&
            pickup.Result.Candidates.Select(c => c.GroupId).Distinct().Count() == members - 1, $"{item}: pickup did not return the whole class per item");
        Check(putdown.Putdown is { } veto && veto.GroupId == putdownGroup && veto.Score >= .9 && putdown.Result.Status == RecognitionStatus.Unknown &&
            putdown.Result.CandidateCount == 0, $"{relabelled}: putdown sound was not vetoed: {putdown.Result.Status} {putdown.Putdown?.GroupId}");
        Check(!putdown.Scores.Any(s => s.GroupId != putdownGroup && s.Score >= .8), $"{relabelled}: putdown sound resembles a pickup reference");
    }
});
Test("基础库「听样本」只放原始录音：不是处理过的识别参考，未归一化，匹配器把每段识别为所关联的音效组", () =>
{
    var library = BaseLibrary();
    var playback = PlaybackAudio.Load(baseRoot);
    using var recognizer = new InMatchRecognizer(library, baseRoot);
    var groups = library.Groups.Select(g => g.Id).ToHashSet();
    var processed = ReferenceAudio.Load(baseRoot).Samples.Select(s => s.Sha256.ToUpperInvariant()).ToHashSet();
    Check(playback.Clips.Count > 0 && playback.Clips.Select(c => c.Id).Distinct().Count() == playback.Clips.Count, "playback manifest empty or duplicated");
    foreach (var clip in playback.Clips)
    {
        var whole = WaveAudio.Read(PlaybackAudio.VerifiedPath(baseRoot, clip));
        var window = PlaybackAudio.Read(baseRoot, clip);
        var duration = whole.Samples.Length / (double)whole.SampleRate;
        Check(!processed.Contains(clip.Sha256.ToUpperInvariant()), $"{clip.Id} reuses a processed matching reference");
        Check(clip.StartSeconds >= 0 && (clip.EndSeconds ?? duration) <= duration + 1e-6 && window.Samples.Length >= whole.SampleRate * .3,
            $"{clip.Id}: play window outside the recording or too short");
        Check(window.Samples.Max(v => Math.Abs(v)) < .95, $"{clip.Id}: normalised to full scale");
        Check(clip.GroupIds.Length > 0 && clip.GroupIds.All(groups.Contains), $"{clip.Id}: unknown groups");
        var analysis = recognizer.Analyze(whole.Samples, whole.SampleRate);
        var matched = clip.Action == "putdown" ? (analysis.Putdown is { } veto ? [veto.GroupId] : new HashSet<string>())
            : analysis.Result.Candidates.Select(c => c.GroupId).ToHashSet();
        Check(matched.SetEquals(clip.GroupIds), $"{clip.Id} ({clip.Label}) is recognised as {string.Join(",", matched)}, not {string.Join(",", clip.GroupIds)}");
    }
    Check(new[] { "peer-047", "peer-051" }.All(group => playback.Clips.Any(c => c.Action == "putdown" && c.GroupIds.Contains(group))),
        "a relabelled putdown sound has no putdown recording to audition");
});
Test("基础库：拖动胶囊电视后放下、单独快速转移都判为放下声，只有拿起声进入结果", () =>
{
    const int rate = 48000;
    var random = new Random(5);
    var samples = Enumerable.Range(0, rate * 6).Select(i => (float)(.004 * (random.NextDouble() * 2 - 1) + .02 * Math.Sin(2 * Math.PI * 110 * i / rate))).ToArray();
    foreach (var (at, clip) in new[] { (1.0, BaseReference("peer-024")), (1.6, BaseReference("peer-047")), (4.0, BaseReference("peer-047")) })
        for (var i = 0; i < clip.Length; i++) samples[(int)(at * rate) + i] += .3f * clip[i];
    using var recognizer = new InMatchRecognizer(BaseLibrary(), baseRoot);
    var scanner = new AutomaticAudioScanner(); scanner.Reset(0); var ring = new AudioTimeline(rate); var history = new RecognitionHistory();
    var events = new List<(double Onset, PickupAnalysis Pickup)>(); var operation = 0L;
    for (var first = 0; first < samples.Length; first += rate / 10)
    {
        ring.Append(samples.AsSpan(first, rate / 10).ToArray(), first / (double)rate);
        var now = (first + rate / 10) / (double)rate;
        if (scanner.TryTakeWindow(ring, now, true, false) is not { } window) continue;
        var pickup = PickupRecognition.Analyze(recognizer, window, ++operation);
        events.Add((window.OnsetSeconds, pickup));
        // The controller drops putdown sounds before they reach the history.
        if (!pickup.IsPutdown) history.Remember(pickup.Analysis.Result, "自动", true, historyAt.AddSeconds(now), audioSeconds: window.OnsetSeconds);
    }
    Check(events.Count == 3, "expected pickup, putdown and transfer events: " + string.Join(",", events.Select(s => s.Onset.ToString("F2"))));
    Check(!events[0].Pickup.IsPutdown && Names(events[0].Pickup.Analysis.Result).Contains("胶囊电视") && events[0].Pickup.Analysis.Result.CandidateCount == 13,
        "the pickup did not show the whole class");
    Check(events.Skip(1).All(e => e.Pickup.IsPutdown && e.Pickup.Analysis.Putdown!.GroupId == "peer-047" && e.Pickup.Analysis.Result.CandidateCount == 0),
        "a putdown or transfer sound was not judged a putdown");
    Check(history.Entries.Count == 1 && history.Latest?.Result.CandidateCount == 13, "a putdown sound reached the history");
});
Test("拿起声领先库内其他类 0.20 以上时，0.80 以上即可自动接受；差距小的仍需 0.86", () =>
{
    var library = Library();
    var stub = new ScriptedRecognizer(library);
    var window = new AudioScanWindow(new float[24000], 24000, 1.0, .5);
    stub.Next = (score: .84, separation: .27);
    Check(PickupRecognition.Analyze(stub, window).Analysis.Result.Status == RecognitionStatus.Matched, "a clean 0.84 match was rejected");
    stub.Next = (score: .84, separation: .10);
    Check(PickupRecognition.Analyze(stub, window).Analysis.Result.Status == RecognitionStatus.Unknown, "an ambiguous 0.84 match was accepted");
    stub.Next = (score: .79, separation: .40);
    Check(PickupRecognition.Analyze(stub, window).Analysis.Result.Status == RecognitionStatus.Unknown, "a 0.79 match was accepted");
    stub.Next = (score: .86, separation: 0);
    Check(PickupRecognition.Analyze(stub, window).Analysis.Result.Status == RecognitionStatus.Matched, "a 0.86 match was rejected");
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

// Returns one matched candidate with a scripted score and lead over the rest of the catalogue.
sealed class ScriptedRecognizer(SoundLibrary library) : IRecognizer
{
    public (double score, double separation) Next { get; set; }
    public RecognitionResult Recognize(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        => Analyze(samples, sampleRate, operationId, final, cancellation).Result;
    public RecognitionAnalysis Analyze(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default)
    {
        var group = library.Groups[0];
        var candidate = new Candidate(library.Items.First(i => i.Id == group.ItemIds[0]), Next.score, group.Id);
        return new(new(operationId, RecognitionStatus.Matched, final, [candidate], 0, "scripted"), [new(group.Id, Next.score)], null, Next.separation);
    }
    public RecognitionAnalysis AnalyzeAt(float[] samples, int sampleRate, double onsetSeconds, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        => Analyze(samples, sampleRate, operationId, final, cancellation);
    public (SoundGroup Group, double Score)[] ScoreAudio(float[] samples, int sampleRate, CancellationToken cancellation = default) => [(library.Groups[0], Next.score)];
    public void Dispose() { }
}
