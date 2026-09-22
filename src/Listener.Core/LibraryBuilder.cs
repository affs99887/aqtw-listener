using System.Security.Cryptography;

namespace Listener.Core;

public sealed class ImportManifest
{
    public SoundLibrary Library { get; set; } = new();
    public List<ImportSample> Samples { get; set; } = [];
}
public sealed record ImportSample(string GroupId, string File, string SourceUrl, string RecordingId,
    double StartSeconds = 0, double? EndSeconds = null);

public static class LibraryBuilder
{
    public static SoundLibrary Build(string manifestPath)
    {
        var manifest = JsonFile.Read<ImportManifest>(manifestPath);
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        foreach (var entry in manifest.Samples)
        {
            if (string.IsNullOrWhiteSpace(entry.RecordingId)) throw new InvalidDataException("每段音频必须有原始录制编号。");
            var group = manifest.Library.Groups.Single(g => g.Id == entry.GroupId);
            var path = ResolveFile(root, entry.File);
            var clip = WaveAudio.Read(path);
            var end = entry.EndSeconds ?? (double)clip.Samples.Length / clip.SampleRate;
            if (entry.StartSeconds < 0 || end <= entry.StartSeconds || end > (double)clip.Samples.Length / clip.SampleRate + 0.001 || end - entry.StartSeconds > 5)
                throw new InvalidDataException($"样本时间范围无效或超过 5 秒：{entry.File}");
            var samples = clip.Samples.Skip((int)(entry.StartSeconds * clip.SampleRate)).Take((int)((end - entry.StartSeconds) * clip.SampleRate)).ToArray();
            var features = AudioFeatures.Extract(samples, clip.SampleRate);
            if (features.Length < 3) throw new InvalidDataException($"样本过短或静音：{entry.File}");
            group.Templates.Add(new SoundTemplate
            {
                Id = group.Id + "-" + (group.Templates.Count + 1), SourceUrl = entry.SourceUrl, RecordingId = entry.RecordingId,
                AudioSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StartSeconds = entry.StartSeconds, EndSeconds = end, Features = features
            });
        }
        manifest.Library.Validate(); return manifest.Library;
    }
    public static string ResolveFile(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("数据文件必须位于清单目录内。");
        return full;
    }
}
