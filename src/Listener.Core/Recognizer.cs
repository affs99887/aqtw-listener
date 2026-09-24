using System.Diagnostics;

namespace Listener.Core;

public interface IRecognizer : IDisposable
{
    RecognitionResult Recognize(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default);
    RecognitionAnalysis Analyze(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        => new(Recognize(samples, sampleRate, operationId, final, cancellation), []);
    // Analyse a clip whose sound event starts onsetSeconds into the clip. Engines that
    // do not anchor on the onset simply analyse the whole clip.
    RecognitionAnalysis AnalyzeAt(float[] samples, int sampleRate, double onsetSeconds, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        => Analyze(samples, sampleRate, operationId, final, cancellation);
    (SoundGroup Group, double Score)[] ScoreAudio(float[] samples, int sampleRate, CancellationToken cancellation = default);
}

public sealed class Recognizer : IRecognizer
{
    private readonly SoundLibrary library;
    public Recognizer(SoundLibrary library)
    {
        library.Validate(); this.library = library;
    }

    public RecognitionResult Recognize(float[] samples, int sampleRate, long operationId = 0,
        bool final = true, CancellationToken cancellation = default)
        => Analyze(samples, sampleRate, operationId, final, cancellation).Result;
    public RecognitionAnalysis Analyze(float[] samples, int sampleRate, long operationId = 0,
        bool final = true, CancellationToken cancellation = default)
    {
        var clock = Stopwatch.StartNew();
        GroupScore[] ranked = [];
        RecognitionAnalysis Empty(RecognitionStatus s, string message) => new(new(operationId, s, final, [], clock.Elapsed.TotalMilliseconds, message), ranked);
        if (library.Groups.All(g => g.Action != "pickup" || g.Templates.Count == 0))
            return Empty(RecognitionStatus.LibraryEmpty, "音效库没有可识别的拾起样本，请导入音效包。");
        if (samples.Length < sampleRate * 0.03 || AudioFeatures.Rms(samples) < 0.00008)
            return Empty(RecognitionStatus.NoSound, "没有听到有效声音");
        if (samples.Count(s => Math.Abs(s) > 0.995) > samples.Length * 0.05)
            return Empty(RecognitionStatus.Interference, "声音削波严重，请降低音量后重试");
        cancellation.ThrowIfCancellationRequested();
        var query = AudioFeatures.Extract(samples, sampleRate);
        if (query.Length < 3) return Empty(RecognitionStatus.NoSound, "声音过短，请重新拖动货物");
        var scores = Score(query, cancellation);
        ranked = scores.OrderByDescending(s => s.Score).Select(s => new GroupScore(s.Group.Id, s.Score)).ToArray();
        var candidates = CandidateSelection.Select(library, scores);
        if (candidates.Length == 0) return Empty(RecognitionStatus.Unknown, "识别失败 · 未匹配到已收录音效");
        return new(new RecognitionResult(operationId, RecognitionStatus.Matched, final, candidates, clock.Elapsed.TotalMilliseconds,
            final ? "识别完成" : "初步候选 · 正在继续听"), ranked);
    }

    public (SoundGroup Group, double Score)[] ScoreAudio(float[] samples, int rate, CancellationToken token = default) => Score(AudioFeatures.Extract(samples, rate), token);
    public void Dispose() { }

    public (SoundGroup Group, double Score)[] Score(float[][] features, CancellationToken token = default) =>
        library.Groups.Where(g => g.Action == "pickup" && g.Templates.Count > 0).Select(g =>
        {
            token.ThrowIfCancellationRequested();
            var score = g.Templates.Max(t => AudioFeatures.Similarity(t.Features, features, token));
            return (g, score);
        }).ToArray();
}
