using System.Diagnostics;
using System.Security.Cryptography;

namespace Listener.Core;

public sealed class EvaluationManifest
{
    public string Kind { get; set; } = "independent";
    public List<EvaluationCase> Cases { get; set; } = [];
}
public sealed record EvaluationCase(string File, string RecordingId, string? ExpectedItemId,
    bool IsNonGoldOrNoise, string Split = "test");
public sealed record CaseResult(string File, string? ExpectedItemId, string[] Candidates, string Status,
    bool Hit, double Milliseconds);

public static class Evaluation
{
    public static object Run(SoundLibrary library, string manifestPath, string? libraryRoot = null)
    {
        var manifest = JsonFile.Read<EvaluationManifest>(manifestPath);
        if (manifest.Cases.Count == 0) throw new InvalidDataException("评估集为空，不能生成准确率。");
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var recordings = library.Groups.SelectMany(g => g.Templates).Select(t => t.RecordingId).Concat(library.CalibrationRecordingIds).ToHashSet();
        var hashes = library.Groups.SelectMany(g => g.Templates).Select(t => t.AudioSha256).Concat(library.CalibrationAudioHashes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var recognizer = RecognizerFactory.Create(library, libraryRoot);
        var rows = new List<CaseResult>();
        foreach (var c in manifest.Cases)
        {
            if (string.IsNullOrWhiteSpace(c.RecordingId) || c.Split != "test") throw new InvalidDataException("验收只接受有录制编号的 test 集。");
            if (c.ExpectedItemId is not null && !library.Items.Any(i => i.Id == c.ExpectedItemId)) throw new InvalidDataException("测试物品未在目录中定义。");
            if (c.IsNonGoldOrNoise != (c.ExpectedItemId is null || !library.Items.Single(i => i.Id == c.ExpectedItemId).IsGold))
                throw new InvalidDataException("非大金标记与物品目录不一致。");
            var path = LibraryBuilder.ResolveFile(root, c.File);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            if (manifest.Kind == "independent" && (recordings.Contains(c.RecordingId) || hashes.Contains(hash) || !seen.Add(hash)))
                throw new InvalidDataException($"数据泄漏或重复样本：{c.File}。独立测试不能使用参考录音及其衍生版本。");
            var clip = WaveAudio.Read(path); var result = recognizer.Recognize(clip.Samples, clip.SampleRate);
            rows.Add(new(c.File, c.ExpectedItemId, result.Candidates.Select(x => x.Item.Id).ToArray(), result.Status.ToString(),
                c.ExpectedItemId is not null && result.Candidates.Any(x => x.Item.Id == c.ExpectedItemId), result.ElapsedMilliseconds));
        }
        var positives = rows.Where(r => r.ExpectedItemId is not null).ToArray();
        var noise = rows.Where(r => r.ExpectedItemId is null).ToArray();
        var unique = rows.Where(r => r.Candidates.Length == 1).ToArray();
        static double? Ratio(int a, int b) => b == 0 ? null : (double)a / b;
        var hitRate = Ratio(positives.Count(r => r.Hit), positives.Length);
        var precision = Ratio(unique.Count(r => r.Hit), unique.Length);
        var falsePositive = Ratio(noise.Count(r => r.Candidates.Length > 0), noise.Length);
        var eligible = manifest.Kind == "independent" && rows.Count >= 200 && manifest.Cases.Count(c => c.IsNonGoldOrNoise) >= 100 && noise.Length > 0;
        return new
        {
            kind = manifest.Kind, eligibleForAcceptance = eligible,
            acceptancePassed = eligible && hitRate >= .95 && precision >= .95 && falsePositive <= .05,
            note = "仅离线音频匹配；不代表点击到浮窗的端到端延迟，也不代表真实对局准确率。来源编号必须反映原始录制，转载和变速版本仍属同一来源。",
            total = rows.Count, candidateHitRate = hitRate, uniquePrecision = precision,
            uniqueCoverage = Ratio(unique.Length, rows.Count), noiseFalsePositiveRate = falsePositive,
            candidateCountDistribution = rows.GroupBy(r => r.Candidates.Length).ToDictionary(g => g.Key, g => g.Count()),
            computeP95Milliseconds = rows.Select(r => r.Milliseconds).Order().ElementAt((int)Math.Ceiling(rows.Count * .95) - 1),
            rows
        };
    }

    public static SoundLibrary Calibrate(SoundLibrary library, string manifestPath, string? libraryRoot = null)
    {
        var manifest = JsonFile.Read<EvaluationManifest>(manifestPath);
        if (manifest.Cases.Count == 0 || manifest.Cases.Any(c => c.Split != "validation"))
            throw new InvalidDataException("校准必须使用独立的 validation 集，不能使用 test 集。");
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var recordings = library.Groups.SelectMany(g => g.Templates).Select(t => t.RecordingId).ToHashSet();
        var hashes = library.Groups.SelectMany(g => g.Templates).Select(t => t.AudioSha256).ToHashSet();
        using var recognizer = RecognizerFactory.Create(library, libraryRoot);
        var validationHashes = new HashSet<string>();
        var observations = manifest.Cases.Select(c =>
        {
            var path = LibraryBuilder.ResolveFile(root, c.File);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            if (string.IsNullOrWhiteSpace(c.RecordingId) || recordings.Contains(c.RecordingId)
                || hashes.Contains(hash) || !validationHashes.Add(hash))
                throw new InvalidDataException("校准数据与参考库重叠。");
            var audio = WaveAudio.Read(path);
            return (Case: c, Scores: recognizer.ScoreAudio(audio.Samples, audio.SampleRate));
        }).ToArray();
        foreach (var group in library.Groups.Where(g => g.Action == "pickup"))
        {
            var positive = observations.Where(o => o.Case.ExpectedItemId is not null && group.ItemIds.Contains(o.Case.ExpectedItemId)).ToArray();
            var negative = observations.Except(positive).ToArray();
            if (positive.Length < 5 || negative.Length < 10) throw new InvalidDataException($"{group.Name} 校准至少需要 5 个正例、10 个负例。");
            double? selected = null;
            for (var threshold = .5; threshold <= 1; threshold += .005)
            {
                var tp = positive.Count(o => o.Scores.Single(s => s.Group.Id == group.Id).Score >= threshold);
                var fp = negative.Count(o => o.Scores.Single(s => s.Group.Id == group.Id).Score >= threshold);
                if ((double)tp / positive.Length >= .95 && (double)fp / negative.Length <= .05) { selected = threshold; break; }
            }
            if (selected is null) throw new InvalidDataException($"{group.Name} 当前样本无法同时达到召回与误报目标，需补充样本或合并同音组。");
            group.Threshold = selected.Value;
        }
        library.CalibrationRecordingIds = manifest.Cases.Select(c => c.RecordingId).Distinct().ToArray();
        library.CalibrationAudioHashes = validationHashes.ToArray();
        library.ValidationStatus = "validation-calibrated"; return library;
    }
}
