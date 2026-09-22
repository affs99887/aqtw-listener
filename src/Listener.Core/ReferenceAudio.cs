using System.Security.Cryptography;

namespace Listener.Core;

public sealed record ReferenceSample(string GroupId, string TemplateId, string File, string Sha256, string RecordingId);
public sealed class ReferenceAudio
{
    public List<ReferenceSample> Samples { get; set; } = [];
    public static ReferenceAudio Load(string root) => File.Exists(Path.Combine(root, "references.json"))
        ? JsonFile.Read<ReferenceAudio>(Path.Combine(root, "references.json")) : new();
    public static string VerifiedPath(string root, ReferenceSample sample)
    {
        var path = LibraryBuilder.ResolveFile(root, sample.File);
        if (!File.Exists(path) || !Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path))).Equals(sample.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("参考音频缺失或校验失败，请重新导入完整音效库。");
        return path;
    }
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path)));
}
