namespace Listener.Core;

// Adds a reference recording to one or more sound groups of a library folder: the
// game plays a few distinct recordings per sound class, so a class needs every
// variant it uses as its own template. One file may serve several groups (a class
// shares one sound); each group receives a template entry and a manifest sample,
// and the catalogue's manifest hash is rebound.
public static class LibraryReferences
{
    public sealed record Added(string File, string Sha256, string[] TemplateIds);

    public static Added Add(string root, string wavPath, string recordingId, string sourceUrl, IReadOnlyList<string> groupIds, string? name = null)
    {
        if (groupIds.Count == 0) throw new ArgumentException("至少指定一个音效组。", nameof(groupIds));
        if (string.IsNullOrWhiteSpace(recordingId)) throw new ArgumentException("每段音频必须有原始录制编号。", nameof(recordingId));
        var libraryPath = Path.Combine(root, "library.json");
        var library = JsonFile.Read<SoundLibrary>(libraryPath);
        var references = ReferenceAudio.Load(root);
        var clip = WaveAudio.Read(wavPath);
        var duration = clip.Samples.Length / (double)clip.SampleRate;
        if (duration is < .19 or > 5) throw new InvalidDataException($"参考应为 0.19–5 秒：{wavPath}");
        if (!InMatchRecognizer.CanIndex(clip)) throw new InvalidDataException("未能生成声纹；音频可能过短或无有效频谱。");
        var features = AudioFeatures.Extract(clip.Samples, clip.SampleRate);
        if (features.Length < 3) throw new InvalidDataException("样本过短或静音。");
        var relative = "audio/" + (name ?? Path.GetFileNameWithoutExtension(wavPath)) + ".wav";
        var destination = LibraryBuilder.ResolveFile(root, relative);
        if (!string.Equals(Path.GetFullPath(wavPath), destination, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(destination)) throw new InvalidDataException("同名参考文件已存在：" + relative);
            File.Copy(wavPath, destination);
        }
        var sha = ReferenceAudio.Hash(destination);
        var ids = new List<string>();
        foreach (var groupId in groupIds)
        {
            var group = library.Groups.FirstOrDefault(g => g.Id == groupId) ?? throw new InvalidDataException("音效组不存在：" + groupId);
            if (references.Samples.Any(s => s.GroupId == groupId && s.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"音效组 {groupId} 已包含这段参考。");
            var templateId = $"{groupId}-{group.Templates.Count + 1}";
            group.Templates.Add(new SoundTemplate
            {
                Id = templateId, SourceUrl = sourceUrl, RecordingId = recordingId, AudioSha256 = sha,
                StartSeconds = 0, EndSeconds = duration, Features = features
            });
            references.Samples.Add(new(groupId, templateId, relative, sha, recordingId));
            ids.Add(templateId);
        }
        library.Validate();
        JsonFile.Write(Path.Combine(root, "references.json"), references);
        library.EngineIndexSha256 = ReferenceAudio.Hash(Path.Combine(root, "references.json"));
        JsonFile.Write(libraryPath, library);
        return new(relative, sha, ids.ToArray());
    }
}
