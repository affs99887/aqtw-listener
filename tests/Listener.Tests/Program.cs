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
Test("音量变化保持频谱匹配", () =>
{
    var quieter = AudioFeatures.Extract(audio.Select(v => v * .05f).ToArray(), 24000);
    Check(AudioFeatures.Similarity(features, quieter) > .98, "gain invariance");
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
var report = new List<object>(); var failed = 0;
foreach (var (name, run) in tests)
{
    var watch = Stopwatch.StartNew();
    try { run(); report.Add(new { name, passed = true, ms = watch.Elapsed.TotalMilliseconds }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; report.Add(new { name, passed = false, error = ex.Message }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
if (args.Length > 0) JsonFile.Write(args[0], new { total = tests.Count, passed = tests.Count - failed, failed, tests = report });
return failed == 0 ? 0 : 1;
