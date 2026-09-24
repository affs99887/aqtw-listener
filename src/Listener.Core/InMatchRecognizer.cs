using System.Diagnostics;
using System.Numerics;

namespace Listener.Core;

// In-match pickup matcher. Replay of five full raids showed why the previous
// window-relative log-mel patch collapsed inside a match: the references carry
// almost no energy below ~600 Hz, while the game mix always has rumble, footsteps
// and room tone there. That low band became the loudest part of every patch, the
// -20 dB floor then clamped the item's own signature, and any ambience filled the
// template's silent frames. This matcher therefore
//   1. anchors the patch on the detected onset instead of sliding blindly,
//   2. subtracts the median spectrum of the ~300 ms before the onset (per band),
//   3. compares only mel bands ~0.9–11 kHz, where the item sounds live and where
//      a 24/44.1/48 kHz capture still agrees,
//   4. searches the onset estimate asymmetrically (-32 ms .. +128 ms), because a
//      click or footstep shortly before the pickup makes the scanner fire early.
// Scores stay cosine similarities in [-1, 1]; thresholds live in library.json.
public static class InMatchFeatures
{
    public const string Version = "inmatch-v1";
    public const int SampleRate = 48000, FrameSize = 1024, Hop = 256, MelBands = 64;
    public const int Band0 = 16, Band1 = 56, WindowFrames = 32, Lead = 2, SlideBefore = 6, SlideAfter = 24;
    public const int BackgroundFrames = 56, BackgroundGap = 3, MinBackgroundFrames = 8;
    public const double FloorDb = 20, OverSubtract = 1.5, SpectralFloor = .05, OnsetRelativeDb = 25, MinHz = 40, MaxHz = 16000;
    public static int Dim => (Band1 - Band0) * WindowFrames;
    private static readonly double[] Window = Enumerable.Range(0, FrameSize).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FrameSize)).ToArray();
    private static readonly (int First, double[] Weights)[] Bank = BuildBank();

    private static (int, double[])[] BuildBank()
    {
        static double Mel(double hz) => 2595 * Math.Log10(1 + hz / 700);
        static double Hz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);
        var edges = Enumerable.Range(0, MelBands + 2).Select(i => Hz(Mel(MinHz) + (Mel(MaxHz) - Mel(MinHz)) * i / (MelBands + 1))).ToArray();
        var binHz = (double)SampleRate / FrameSize;
        var bank = new (int, double[])[MelBands];
        for (var b = 0; b < MelBands; b++)
        {
            double left = edges[b], center = edges[b + 1], right = edges[b + 2];
            var first = (int)Math.Ceiling(left / binHz); var last = Math.Min(FrameSize / 2, (int)Math.Floor(right / binHz));
            var weights = new double[Math.Max(0, last - first + 1)];
            for (var k = first; k <= last; k++)
            {
                var f = k * binHz;
                var w = f <= center ? (f - left) / (center - left) : (right - f) / (right - center);
                weights[k - first] = Math.Max(0, w) / (right - left);
            }
            bank[b] = (first, weights);
        }
        return bank;
    }

    // Frames × MelBands of mel power (unit-area triangles over the one-sided power spectrum).
    public static double[][] MelPower(float[] pcm)
    {
        if (pcm.Length < FrameSize) return [];
        var count = 1 + (pcm.Length - FrameSize) / Hop;
        var frames = new double[count][];
        var spectrum = new Complex[FrameSize];
        var power = new double[FrameSize / 2 + 1];
        for (var f = 0; f < count; f++)
        {
            var start = f * Hop; double mean = 0;
            for (var i = 0; i < FrameSize; i++) mean += pcm[start + i];
            mean /= FrameSize;
            for (var i = 0; i < FrameSize; i++) spectrum[i] = new Complex((pcm[start + i] - mean) * Window[i], 0);
            AudioFeatures.Fft(spectrum);
            for (var k = 0; k <= FrameSize / 2; k++)
            {
                var v = spectrum[k].Real * spectrum[k].Real + spectrum[k].Imaginary * spectrum[k].Imaginary;
                power[k] = k == 0 || k == FrameSize / 2 ? v : 2 * v;
            }
            var mel = new double[MelBands];
            for (var b = 0; b < MelBands; b++)
            {
                var (first, weights) = Bank[b]; double e = 0;
                for (var j = 0; j < weights.Length; j++) e += weights[j] * power[first + j];
                mel[b] = e;
            }
            frames[f] = mel;
        }
        return frames;
    }

    // Energy above Band0 per frame in dB; the scanner and the template anchor both judge this.
    public static double[] HighBandDb(double[][] mel) => mel.Select(frame =>
    {
        double sum = 0;
        for (var b = Band0; b < MelBands; b++) sum += frame[b];
        return 10 * Math.Log10(sum + 1e-12);
    }).ToArray();

    // First frame within OnsetRelativeDb of the loudest high-band frame: the attack of the reference.
    public static int OnsetFrame(double[] highBandDb)
    {
        var peak = highBandDb.Max();
        for (var f = 0; f < highBandDb.Length; f++) if (highBandDb[f] > peak - OnsetRelativeDb) return f;
        return 0;
    }

    // Frame the sound starts on. Bundled references begin on the attack, so a clip
    // whose first frame already stands well above its own floor anchors there. A
    // recording with room tone first anchors on the earliest sharp high-band rise
    // that clears the floor; without one it falls back to OnsetFrame.
    public static int AnchorFrame(double[] highBandDb)
    {
        if (highBandDb.Length == 0) return 0;
        var sorted = highBandDb.OrderBy(v => v).ToArray();
        var floor = sorted[sorted.Length / 5];
        var peak = sorted[^1];
        if (highBandDb[0] >= floor + 15 && highBandDb[0] > peak - OnsetRelativeDb) return 0;
        for (var f = 1; f < highBandDb.Length; f++)
            if (highBandDb[f] - highBandDb[f - 1] >= 6 && highBandDb[f] >= floor + 12 && highBandDb[f] > peak - OnsetRelativeDb) return f;
        return OnsetFrame(highBandDb);
    }

    // Window-relative floor, mean removal and L2 normalisation over bands [Band0, Band1) of WindowFrames frames.
    public static float[]? Patch(double[][] logMel, int start)
    {
        if (start < 0 || start + WindowFrames > logMel.Length) return null;
        var v = new double[Dim]; var peak = double.NegativeInfinity; var n = 0;
        for (var f = start; f < start + WindowFrames; f++)
            for (var b = Band0; b < Band1; b++) { v[n++] = logMel[f][b]; peak = Math.Max(peak, logMel[f][b]); }
        double mean = 0;
        for (var i = 0; i < v.Length; i++) { v[i] = Math.Max(v[i], peak - FloorDb); mean += v[i]; }
        mean /= v.Length; double norm = 0;
        for (var i = 0; i < v.Length; i++) { v[i] -= mean; norm += v[i] * v[i]; }
        norm = Math.Sqrt(norm);
        var result = new float[Dim];
        if (norm > 1e-20) for (var i = 0; i < v.Length; i++) result[i] = (float)(v[i] / norm);
        return result;
    }

    public static double[][] LogMel(double[][] mel) => mel.Select(frame => frame.Select(e => 10 * Math.Log10(Math.Max(e, 1e-12))).ToArray()).ToArray();

    // Log-mel of the query after removing the median spectrum of the frames before the onset.
    // Silence before a clean reference leaves the spectrum untouched.
    public static double[][] Subtracted(double[][] mel, int onsetFrame)
    {
        var high = Math.Max(0, onsetFrame - BackgroundGap); var low = Math.Max(0, high - BackgroundFrames);
        if (high - low < MinBackgroundFrames) return LogMel(mel);
        var background = new double[MelBands]; var column = new double[high - low];
        for (var b = 0; b < MelBands; b++)
        {
            for (var f = low; f < high; f++) column[f - low] = mel[f][b];
            Array.Sort(column);
            background[b] = column.Length % 2 == 1 ? column[column.Length / 2] : (column[column.Length / 2 - 1] + column[column.Length / 2]) / 2;
        }
        return mel.Select(frame => frame.Select((e, b) => 10 * Math.Log10(Math.Max(Math.Max(e - OverSubtract * background[b], SpectralFloor * e), 1e-12))).ToArray()).ToArray();
    }

    // Template for a 48 kHz reference; null when the clip is too short for one window.
    // Bundled references start at the attack. A user recording carries room tone
    // first, so the anchor moves to the earliest sharp high-band rise and the room
    // tone before it is subtracted exactly as it is for live queries.
    public static float[]? Template(float[] pcm48)
    {
        var mel = MelPower(pcm48);
        if (mel.Length == 0) return null;
        var onset = AnchorFrame(HighBandDb(mel));
        var start = Math.Max(0, onset - Lead);
        var logMel = Subtracted(mel, onset);
        if (start + WindowFrames > logMel.Length)
        {
            var floor = logMel.Min(frame => frame.Min());
            logMel = logMel.Concat(Enumerable.Repeat(Enumerable.Repeat(floor, MelBands).ToArray(), start + WindowFrames - logMel.Length)).ToArray();
        }
        var patch = Patch(logMel, start);
        return patch is not null && patch.Any(v => v != 0) ? patch : null;
    }

    // Onset candidates for a clip supplied without an onset (CLI match, learning checks, tests):
    // the anchor plus the sharpest high-band rises, loudest first.
    public static int[] SelfOnsets(double[][] mel, int limit = 6)
    {
        if (mel.Length == 0) return [];
        var high = HighBandDb(mel); var peak = high.Max();
        var result = new List<int> { AnchorFrame(high) };
        foreach (var f in Enumerable.Range(1, high.Length - 1).Where(f => high[f] - high[f - 1] >= 6 && high[f] > peak - OnsetRelativeDb)
                     .OrderByDescending(f => high[f]))
        {
            if (result.Count >= limit) break;
            if (result.All(existing => Math.Abs(existing - f) > 4)) result.Add(f);
        }
        return result.ToArray();
    }

    public static double Cosine(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += (double)a[i] * b[i];
        return Math.Clamp(sum, -1, 1);
    }
}

public sealed class InMatchRecognizer : IRecognizer
{
    private readonly SoundLibrary library;
    private readonly (SoundGroup Group, float[][] Templates)[] groups;
    public InMatchRecognizer(SoundLibrary library, string root)
    {
        library.Validate(); this.library = library;
        var references = ReferenceAudio.Load(root);
        var manifest = Path.Combine(root, "references.json");
        if (!string.IsNullOrWhiteSpace(library.EngineIndexSha256) && (!File.Exists(manifest) ||
            !ReferenceAudio.Hash(manifest).Equals(library.EngineIndexSha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("参考音频清单与物品目录不属于同一音效包，请整体替换 library 文件夹。");
        // Putdown-sound groups are scored too: a window that matches one is an item
        // being put down or transferred, which is never reported as a pickup.
        var list = new List<(SoundGroup, float[][])>();
        foreach (var group in library.Groups.Where(g => g.Action is "pickup" or "putdown" && g.Templates.Count > 0))
        {
            var templates = new List<float[]>();
            foreach (var sample in references.Samples.Where(s => s.GroupId == group.Id))
            {
                var clip = WaveAudio.Read(ReferenceAudio.VerifiedPath(root, sample));
                var template = InMatchFeatures.Template(WaveAudio.Resample(clip.Samples, clip.SampleRate, InMatchFeatures.SampleRate));
                if (template is not null) templates.Add(template);
            }
            if (templates.Count == 0) throw new InvalidDataException($"音效组 {group.Name} 缺少可用的参考音频，请重新导入完整音效库。");
            list.Add((group, templates.ToArray()));
        }
        groups = list.ToArray();
    }
    public static bool CanIndex(AudioClip clip) =>
        InMatchFeatures.Template(WaveAudio.Resample(clip.Samples, clip.SampleRate, InMatchFeatures.SampleRate)) is not null;

    public (SoundGroup Group, double Score)[] ScoreAudio(float[] samples, int sampleRate, CancellationToken cancellation = default)
        => ScoreAudio(samples, sampleRate, null, cancellation);
    public (SoundGroup Group, double Score)[] ScoreAudio(float[] samples, int sampleRate, double? onsetSeconds, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (samples.Length > sampleRate * 5) throw new ArgumentException("请将单次音频裁剪至 5 秒以内。");
        var pcm = WaveAudio.Resample(samples, sampleRate, InMatchFeatures.SampleRate);
        var mel = InMatchFeatures.MelPower(pcm);
        var best = new double[groups.Length];
        Array.Fill(best, -1);
        if (mel.Length < InMatchFeatures.WindowFrames) return groups.Select((g, i) => (g.Group, best[i])).ToArray();
        var onsets = onsetSeconds is { } seconds
            ? [Math.Clamp((int)(seconds * InMatchFeatures.SampleRate / InMatchFeatures.Hop), 0, mel.Length - 1)]
            : InMatchFeatures.SelfOnsets(mel);
        foreach (var onset in onsets)
        {
            cancellation.ThrowIfCancellationRequested();
            var logMel = InMatchFeatures.Subtracted(mel, onset);
            for (var k = onset - InMatchFeatures.Lead - InMatchFeatures.SlideBefore; k <= onset - InMatchFeatures.Lead + InMatchFeatures.SlideAfter; k++)
            {
                var patch = InMatchFeatures.Patch(logMel, k);
                if (patch is null) continue;
                for (var g = 0; g < groups.Length; g++)
                    foreach (var template in groups[g].Templates)
                        best[g] = Math.Max(best[g], InMatchFeatures.Cosine(patch, template));
            }
        }
        return groups.Select((g, i) => (g.Group, best[i])).ToArray();
    }
    public RecognitionResult Recognize(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        => Run(samples, sampleRate, null, operationId, final, cancellation).Result;
    public RecognitionAnalysis Analyze(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        => Run(samples, sampleRate, null, operationId, final, cancellation);
    public RecognitionAnalysis AnalyzeAt(float[] samples, int sampleRate, double onsetSeconds, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        => Run(samples, sampleRate, onsetSeconds, operationId, final, cancellation);
    private RecognitionAnalysis Run(float[] samples, int sampleRate, double? onsetSeconds, long operationId, bool final, CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        GroupScore[] ranked = [];
        RecognitionAnalysis Empty(RecognitionStatus s, string m, GroupScore? putdown = null) =>
            new(new(operationId, s, final, [], watch.Elapsed.TotalMilliseconds, m), ranked, putdown);
        if (groups.All(g => g.Group.Action != "pickup")) return Empty(RecognitionStatus.LibraryEmpty, "音效库没有拾起样本");
        if (samples.Length < sampleRate * .03 || AudioFeatures.Rms(samples) < .00008) return Empty(RecognitionStatus.NoSound, "没有听到有效声音");
        if (samples.Count(v => Math.Abs(v) > .995) > samples.Length * .05) return Empty(RecognitionStatus.Interference, "声音削波严重，请降低音量");
        var scores = ScoreAudio(samples, sampleRate, onsetSeconds, cancellation);
        ranked = scores.OrderByDescending(s => s.Score).Select(s => new GroupScore(s.Group.Id, s.Score)).ToArray();
        if (Putdown(scores) is { } putdown) return Empty(RecognitionStatus.Unknown, "放下声 · 不识别", putdown);
        var candidates = CandidateSelection.Select(library, scores);
        if (candidates.Length == 0) return Empty(RecognitionStatus.Unknown, "识别失败 · 未匹配到已收录音效");
        var chosen = candidates.Select(c => c.GroupId).ToHashSet();
        var outside = scores.Where(s => s.Group.Action == "pickup" && !chosen.Contains(s.Group.Id)).Select(s => s.Score).DefaultIfEmpty(-1).Max();
        return new(new(operationId, RecognitionStatus.Matched, final, candidates, watch.Elapsed.TotalMilliseconds, final ? "识别完成" : "初步候选 · 正在继续听"),
            ranked, null, candidates.Max(c => c.Score) - outside);
    }
    // The sound is a putdown when a putdown-sound group clears its own threshold and
    // no pickup group scores higher: the two kinds of sound do not resemble each other
    // (SoundRadar's raw 琥珀天心 pickup scores 0.45 against that class's putdown).
    public static GroupScore? Putdown(IEnumerable<(SoundGroup Group, double Score)> scores)
    {
        var list = scores.ToArray();
        var best = list.Where(s => s.Group.Action == "putdown" && s.Score >= s.Group.Threshold)
            .OrderByDescending(s => s.Score).FirstOrDefault();
        if (best.Group is null) return null;
        var pickup = list.Where(s => s.Group.Action == "pickup").Select(s => s.Score).DefaultIfEmpty(-1).Max();
        return best.Score > pickup ? new GroupScore(best.Group.Id, best.Score) : null;
    }
    public void Dispose() { }
}
