using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Listener.Core;

public sealed record PersonalItem(ItemDefinition Item, string GroupId, bool Enabled = true);
public sealed record PersonalSample(string Id, string ItemId, string GroupId, string File, string Sha256,
    string SourceFile, string OriginalSha256, string RecordingId, string SessionId, string Source,
    double StartSeconds, double EndSeconds, bool CheckOnly = false, bool Enabled = true,
    string? CaptureSession = null, double? CaptureStart = null, double? CaptureEnd = null);
public sealed class PersonalProfile
{
    public List<PersonalItem> Items { get; set; } = [];
    public List<PersonalSample> Samples { get; set; } = [];
}
public sealed class LearningDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required ItemDefinition Item { get; set; }
    public required string GroupId { get; set; }
    public bool IsNewItem { get; set; }
    public bool Guided { get; set; }
    public bool GroupConfirmed { get; set; }
    public List<PersonalSample> Samples { get; set; } = [];
    public string Status { get; set; } = "待采样";
    public override string ToString() => $"{Item.Name} · {Samples.Count} 段 · {Status}";
}
public sealed record LibraryVersion(SoundLibrary Library, string Root);
public sealed record LibraryBuildResult(LibraryVersion? Version, string Message, string[] Conflicts, string? ReportPath = null);
public sealed record PersonalLibraryState(string? Current = null, string? Previous = null);

public sealed class PersonalLibraryStore
{
    public string BaseRoot { get; }
    public string Root { get; }
    private readonly string engine;
    private readonly SemaphoreSlim buildGate = new(1);
    public string RecoveryNotice { get; private set; } = "";
    private string StatePath => Path.Combine(Root, "active.json");
    public PersonalLibraryStore(string baseRoot, string root, string engine)
    { BaseRoot = Path.GetFullPath(baseRoot); Root = Path.GetFullPath(root); this.engine = engine; }
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonFile.Options), JsonFile.Options)!;
    private string Local(string path) => LibraryBuilder.ResolveFile(Root, path);
    private PersonalLibraryState State()
    {
        try { return File.Exists(StatePath) ? JsonFile.Read<PersonalLibraryState>(StatePath) : new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { RecoveryNotice = "个人库指针损坏，已使用基础库"; return new(); }
    }
    public LibraryVersion ResolveActive()
    {
        var state = State();
        if (state.Current is null) return LoadVersion(BaseRoot);
        foreach (var relative in new[] { state.Current, state.Previous })
        {
            if (relative is null) continue;
            try
            {
                var version = LoadVersion(Local(relative));
                if (relative != state.Current) RecoveryNotice = "当前个人库损坏，已恢复上一版本";
                return version;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or JsonException or InvalidOperationException)
            { RecoveryNotice = "个人库加载失败，使用可用版本：" + ex.Message; }
        }
        return LoadVersion(BaseRoot);
    }
    private static LibraryVersion LoadVersion(string root)
    {
        var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json")); library.Validate();
        if (library.PrimaryEngine == "soundradar" && !ReferenceAudio.Hash(Path.Combine(root, "radar-index.bin"))
            .Equals(library.EngineIndexSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("目录与声纹索引不一致");
        if (library.PrimaryEngine == "inmatch" && !string.IsNullOrWhiteSpace(library.EngineIndexSha256) &&
            !ReferenceAudio.Hash(Path.Combine(root, "references.json")).Equals(library.EngineIndexSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("目录与参考音频清单不一致");
        foreach (var sample in ReferenceAudio.Load(root).Samples) ReferenceAudio.VerifiedPath(root, sample);
        return new(library, root);
    }
    public PersonalProfile Profile()
    {
        var path = Path.Combine(ResolveActive().Root, "personal.json");
        return File.Exists(path) ? JsonFile.Read<PersonalProfile>(path) : new();
    }
    public void SaveDraft(LearningDraft draft)
    { JsonFile.Write(Local($"drafts/{draft.Id}.json"), draft); }
    public IReadOnlyList<LearningDraft> Drafts()
    {
        var directory = Path.Combine(Root, "drafts");
        if (!Directory.Exists(directory)) return [];
        var result = new List<LearningDraft>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
            try { result.Add(JsonFile.Read<LearningDraft>(file)); } catch (Exception ex) when (ex is IOException or JsonException) { }
        return result;
    }
    public void DeleteDraft(LearningDraft draft) => File.Delete(Local($"drafts/{draft.Id}.json"));
    public static string? AudioIssue(AudioClip clip)
    {
        if (clip.SampleRate is < 8000 or > 192000 || clip.Samples.Length < clip.SampleRate * .19 || clip.Samples.Length > clip.SampleRate * 5)
            return "片段需为 0.19–5 秒";
        if (clip.Samples.Any(s => !float.IsFinite(s))) return "音频包含无效数据";
        if (AudioFeatures.Rms(clip.Samples) < .00008) return "没有足够清晰的声音，请增大游戏音量后补录";
        if (clip.Samples.Count(s => Math.Abs(s) > .995) > clip.Samples.Length * .05) return "声音削波严重，请降低音量后补录";
        if (AudioFeatures.Extract(clip.Samples, clip.SampleRate).Length < 3) return "没有有效音效特征，请重新采样";
        return null;
    }
    public PersonalSample AddSample(LearningDraft draft, AudioClip original, string source,
        double start = 0, double? end = null, bool checkOnly = false,
        string? captureSession = null, double? captureStart = null, double? captureEnd = null)
    {
        var finish = end ?? (double)original.Samples.Length / original.SampleRate;
        if (!double.IsFinite(start) || !double.IsFinite(finish) || start < 0 || finish <= start || finish > (double)original.Samples.Length / original.SampleRate + .0001)
            throw new InvalidDataException("裁剪时间超出音频范围");
        var samples = original.Samples.Skip((int)(start * original.SampleRate)).Take((int)((finish - start) * original.SampleRate)).ToArray();
        var issue = AudioIssue(new(samples, original.SampleRate));
        if (issue is not null) throw new InvalidDataException(issue);
        if (captureSession is not null && draft.Samples.Any(s => s.CaptureSession == captureSession &&
            s.CaptureStart < captureEnd && captureStart < s.CaptureEnd))
            throw new InvalidDataException("片段来自重叠的采音窗口，不能当作独立录音，请重新拖动录制");
        var id = Guid.NewGuid().ToString("N");
        var sourceFile = $"audio/{id}-source.wav"; var file = $"audio/{id}.wav";
        WaveAudio.Write(Local(sourceFile), original.Samples, original.SampleRate);
        WaveAudio.Write(Local(file), WaveAudio.Resample(samples, original.SampleRate, 48000), 48000);
        var hash = ReferenceAudio.Hash(Local(file)); var originalHash = ReferenceAudio.Hash(Local(sourceFile));
        if (draft.Samples.Any(s => s.Sha256 == hash || s.OriginalSha256 == originalHash))
        { File.Delete(Local(file)); File.Delete(Local(sourceFile)); throw new InvalidDataException("同一录音或重复片段不能作为新一轮样本，请重新拖动录制"); }
        var sample = new PersonalSample(id, draft.Item.Id, draft.GroupId, file, hash, sourceFile, originalHash,
            originalHash, draft.Id, source, start, finish, checkOnly, CaptureSession: captureSession, CaptureStart: captureStart, CaptureEnd: captureEnd);
        draft.Samples.Add(sample); draft.Status = "已保存，待检查"; SaveDraft(draft); return sample;
    }
    public AudioClip ReadSample(PersonalSample sample)
    {
        var path = Local(sample.File);
        if (ReferenceAudio.Hash(path) != sample.Sha256) throw new InvalidDataException("个人样本校验失败");
        return WaveAudio.Read(path);
    }
    public AudioClip ReadSource(PersonalSample sample)
    {
        var path = Local(sample.SourceFile);
        if (!ReferenceAudio.Hash(path).Equals(sample.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("原始录音校验失败");
        return WaveAudio.Read(path);
    }
    public void TrimSample(LearningDraft draft, PersonalSample sample, double start, double end, bool checkOnly)
    {
        var edited = Clone(draft); edited.Samples.RemoveAll(s => s.Id == sample.Id);
        var added = AddSample(edited, ReadSource(sample), sample.Source, start, end, checkOnly,
            sample.CaptureSession, sample.CaptureStart, sample.CaptureEnd);
        edited.Samples[edited.Samples.IndexOf(added)] = added with { SourceFile = sample.SourceFile, OriginalSha256 = sample.OriginalSha256, RecordingId = sample.RecordingId };
        SaveDraft(edited);
        draft.Samples = edited.Samples; draft.Status = edited.Status;
    }
    public void RemoveSample(LearningDraft draft, string id)
    { draft.Samples.RemoveAll(s => s.Id == id); SaveDraft(draft); }
    public string ImportImage(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg")) throw new InvalidDataException("图片支持 PNG 或 JPG");
        if (new FileInfo(path).Length > 10 * 1024 * 1024) throw new InvalidDataException("图片不能超过 10 MB");
        var relative = $"images/user-{Guid.NewGuid():N}{extension}";
        Directory.CreateDirectory(Path.GetDirectoryName(Local(relative))!); File.Copy(path, Local(relative)); return relative;
    }
    public async Task<LibraryBuildResult> BuildDraft(LearningDraft draft, CancellationToken token = default)
    {
        var references = draft.Samples.Where(s => s.Enabled && !s.CheckOnly).ToArray();
        var checks = draft.Samples.Where(s => s.Enabled && s.CheckOnly).ToArray();
        if (references.Length < (draft.Guided ? 3 : 1) || draft.Guided && checks.Length < 1)
            return new(null, draft.Guided ? "需要 3 段参考音频和 1 段单独试识别音频" : "至少需要一段参考音频", []);
        if (draft.Guided && references.Select(s => s.RecordingId).Distinct().Count() < 3)
            return new(null, "三轮参考必须来自三段不同录音", []);
        if (checks.Any(c => references.Any(r => r.RecordingId == c.RecordingId || r.Sha256 == c.Sha256)))
            return new(null, "试识别片段不能来自参考录音，请重新录制", []);
        var active = ResolveActive();
        if (!draft.GroupConfirmed)
        {
            var conflicts = await Task.Run(() =>
            {
                using var recognizer = CreateRecognizer(active.Library, active.Root);
                return references.SelectMany(sample => { var audio = ReadSample(sample);
                    return recognizer.Analyze(audio.Samples, audio.SampleRate, cancellation: token).Scores
                        .Where(s => s.GroupId != draft.GroupId && s.Score >= active.Library.Groups.Single(g => g.Id == s.GroupId).Threshold)
                        .Select(s => s.GroupId); }).Distinct().ToArray();
            }, token);
            if (conflicts.Length > 0 && draft.IsNewItem)
            { draft.Status = "需要确认同音关系"; SaveDraft(draft); return new(null, "与已有音效组相近，请试听并选择同音组后确认", conflicts); }
        }
        var profile = Clone(Profile());
        if (draft.IsNewItem)
        {
            profile.Items.RemoveAll(i => i.Item.Id == draft.Item.Id);
            profile.Items.Add(new(draft.Item, draft.GroupId));
        }
        foreach (var sample in draft.Samples)
        { profile.Samples.RemoveAll(s => s.Id == sample.Id); profile.Samples.Add(sample with { GroupId = draft.GroupId }); }
        var built = await Build(profile, token);
        if (built.Version is not { } version) return built;
        using var trial = CreateRecognizer(version.Library, version.Root);
        foreach (var check in checks)
        {
            var clip = ReadSample(check);
            var result = await Task.Run(() => trial.Recognize(clip.Samples, clip.SampleRate, cancellation: token), token);
            if (!result.Candidates.Any(c => c.Item.Id == draft.Item.Id))
            { draft.Status = "本轮试识别未通过，请补录"; SaveDraft(draft); return built with { Version = null, Message = draft.Status }; }
        }
        draft.Status = "检查通过，待启用"; SaveDraft(draft); return built;
    }
    public async Task<LibraryBuildResult> Build(PersonalProfile profile, CancellationToken token = default)
    {
        await buildGate.WaitAsync(token);
        try { return await Task.Run(() => BuildCore(Clone(profile), token), token); }
        finally { buildGate.Release(); }
    }
    private LibraryBuildResult BuildCore(PersonalProfile profile, CancellationToken token)
    {
        ValidateProfile(profile);
        var id = $"versions/{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var output = Local(id); Directory.CreateDirectory(output);
        var library = Clone(JsonFile.Read<SoundLibrary>(Path.Combine(BaseRoot, "library.json")));
        var baseReferences = ReferenceAudio.Load(BaseRoot);
        if (baseReferences.Samples.Count == 0) return new(null, "基础库缺少可重建的参考音频，请使用完整程序包", []);
        var references = Clone(baseReferences);
        foreach (var reference in baseReferences.Samples) CopyRelative(ReferenceAudio.VerifiedPath(BaseRoot, reference), output, reference.File);
        foreach (var item in library.Items.Where(i => i.Thumbnail is not null)) CopyRelative(LibraryBuilder.ResolveFile(BaseRoot, item.Thumbnail!), output, item.Thumbnail!);
        foreach (var entry in profile.Items.Where(i => i.Enabled))
        {
            if (library.Items.Any(i => i.Id == entry.Item.Id)) throw new InvalidDataException("个人物品编号与基础库重复");
            library.Items.Add(entry.Item);
            if (entry.Item.Thumbnail is not null) CopyRelative(Local(entry.Item.Thumbnail), output, entry.Item.Thumbnail);
            var group = library.Groups.FirstOrDefault(g => g.Id == entry.GroupId);
            if (group is null) library.Groups.Add(group = new() { Id = entry.GroupId, Name = entry.Item.Name, Threshold = DefaultGroupThreshold(library), ItemIds = [] });
            group.ItemIds = group.ItemIds.Append(entry.Item.Id).Distinct().ToArray();
        }
        foreach (var sample in profile.Samples.Where(s => s.Enabled && !s.CheckOnly))
        {
            token.ThrowIfCancellationRequested();
            if (profile.Items.Any(i => i.Item.Id == sample.ItemId && !i.Enabled)) continue;
            var group = library.Groups.FirstOrDefault(g => g.Id == sample.GroupId);
            if (group is null || !group.ItemIds.Contains(sample.ItemId)) throw new InvalidDataException("样本关联的物品或音效组不存在");
            var clip = ReadSample(sample); var issue = AudioIssue(clip);
            if (issue is not null) throw new InvalidDataException($"样本 {sample.Id}：{issue}");
            if (references.Samples.Any(s => s.GroupId == group.Id && s.Sha256.Equals(sample.Sha256, StringComparison.OrdinalIgnoreCase))) continue;
            var relative = $"audio/user-{sample.Id}.wav"; CopyRelative(Local(sample.File), output, relative);
            group.Templates.Add(new() { Id = "user-" + sample.Id, RecordingId = sample.RecordingId, SourceUrl = sample.Source,
                AudioSha256 = sample.OriginalSha256, StartSeconds = sample.StartSeconds, EndSeconds = sample.EndSeconds,
                Features = AudioFeatures.Extract(clip.Samples, clip.SampleRate) });
            references.Samples.Add(new(group.Id, "user-" + sample.Id, relative, sample.Sha256, sample.RecordingId));
        }
        if (library.Groups.Any(g => g.Templates.Count == 0 && g.Action == "pickup")) throw new InvalidDataException("新物品没有启用的参考样本，请先补样本或禁用该物品");
        library.Version = "personal-" + Path.GetFileName(output); library.ValidationStatus = "personal-reference-checked";
        library.Validate();
        var report = Path.Combine(output, "build-report.json");
        ReferenceSample[] skipped;
        if (library.PrimaryEngine == "inmatch")
        {
            // Templates are computed from the reference audio at load time; the
            // catalogue is bound to the reference manifest by hash instead of an index file.
            skipped = references.Samples.Where(s => !InMatchRecognizer.CanIndex(WaveAudio.Read(ReferenceAudio.VerifiedPath(output, s)))).ToArray();
            JsonFile.Write(report, new { expected = references.Samples.Count, built = references.Samples.Count - skipped.Length, skipped = skipped.Select(s => new {
                s.GroupId, s.TemplateId, reason = "未能生成声纹；音频可能过短或无有效频谱，请重新录制" }).ToArray() });
            if (skipped.Length > 0) return new(null, $"有 {skipped.Length} 段未生成声纹，原库保持不变", [], report);
            JsonFile.Write(Path.Combine(output, "references.json"), references);
            library.EngineIndexSha256 = ReferenceAudio.Hash(Path.Combine(output, "references.json"));
        }
        else
        {
            var archive = Path.Combine(output, "build.srz");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                WriteJson(zip, "manifest.json", new { schema = 1, name = library.Version, items = library.Groups.Where(g => g.Action == "pickup").Select(g => g.Id).ToArray() });
                foreach (var group in library.Groups.Where(g => g.Action == "pickup"))
                {
                    var samples = references.Samples.Where(s => s.GroupId == group.Id).ToArray();
                    WriteJson(zip, $"items/{group.Id}/meta.json", new { id = group.Id, name = group.Name, threshold = group.Threshold,
                        samples = samples.Select(s => new { file = $"samples/{s.TemplateId}.wav", storedSampleRate = 48000, storedChannels = 1, storedBitsPerSample = 16 }).ToArray() });
                    foreach (var sample in samples) zip.CreateEntryFromFile(ReferenceAudio.VerifiedPath(output, sample), $"items/{group.Id}/samples/{sample.TemplateId}.wav");
                }
            }
            var info = new ProcessStartInfo(engine) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("build"); info.ArgumentList.Add(archive); info.ArgumentList.Add(Path.Combine(output, "radar-index.bin"));
            using (var process = Process.Start(info) ?? throw new IOException("无法启动建库引擎"))
            {
                using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } });
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000)) { process.Kill(); throw new IOException("建库超时，原音效库仍保留"); }
                Task.WhenAll(stdout, stderr).GetAwaiter().GetResult(); token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new IOException("建库失败：" + stderr.Result);
            }
            var included = IndexSamples(Path.Combine(output, "radar-index.bin"));
            skipped = references.Samples.Where(s => !included.Contains((s.GroupId, $"samples/{s.TemplateId}.wav"))).ToArray();
            JsonFile.Write(report, new { expected = references.Samples.Count, built = included.Count, skipped = skipped.Select(s => new {
                s.GroupId, s.TemplateId, reason = "引擎未生成声纹；音频可能过短或无有效频谱，请重新录制" }).ToArray() });
            if (skipped.Length > 0) return new(null, $"有 {skipped.Length} 段未生成声纹，原库保持不变", [], report);
            File.Delete(archive);
            library.EngineIndexSha256 = ReferenceAudio.Hash(Path.Combine(output, "radar-index.bin"));
            JsonFile.Write(Path.Combine(output, "references.json"), references);
        }
        JsonFile.Write(Path.Combine(output, "library.json"), library);
        JsonFile.Write(Path.Combine(output, "personal.json"), profile);
        var baseline = ResolveActive();
        using var before = CreateRecognizer(baseline.Library, baseline.Root);
        using var after = CreateRecognizer(library, output);
        var checks = new List<object>(); var failures = 0;
        foreach (var sample in ReferenceAudio.Load(baseline.Root).Samples)
        {
            token.ThrowIfCancellationRequested(); var audio = WaveAudio.Read(ReferenceAudio.VerifiedPath(baseline.Root, sample));
            var oldGroups = before.Recognize(audio.Samples, audio.SampleRate, cancellation: token).Candidates.Select(c => c.GroupId).ToHashSet();
            var newGroups = after.Recognize(audio.Samples, audio.SampleRate, cancellation: token).Candidates.Select(c => c.GroupId).ToHashSet();
            // Removing/disable personal samples is intentional; the bundled groups must still match.
            var bundled = baseReferences.Samples.Any(r => r.TemplateId == sample.TemplateId);
            var stillPresent = library.Groups.Select(g => g.Id).ToHashSet();
            oldGroups.IntersectWith(stillPresent);
            var pass = !bundled || oldGroups.SetEquals(newGroups);
            if (!pass) failures++;
            checks.Add(new { sample.GroupId, sample.TemplateId, passed = pass });
        }
        JsonFile.Write(Path.Combine(output, "reference-checks.json"), new { independent = false, failures, checks });
        return failures == 0 ? new(new(library, output), "建库与参考回归通过（非独立准确率验收）", [], report)
            : new(null, $"{failures} 项原有参考回归未通过，请检查新样本与同音关系", [], report);
    }
    public void Activate(LibraryVersion version)
    {
        LoadVersion(version.Root);
        var relative = Path.GetRelativePath(Root, version.Root); Local(relative);
        var previous = ResolveActive().Root;
        JsonFile.Write(StatePath, new PersonalLibraryState(relative, previous.Equals(BaseRoot, StringComparison.OrdinalIgnoreCase) ? null : Path.GetRelativePath(Root, previous)));
        RecoveryNotice = "";
    }
    public LibraryVersion Rollback()
    {
        var state = State();
        var version = state.Previous is null ? LoadVersion(BaseRoot) : LoadVersion(Local(state.Previous));
        JsonFile.Write(StatePath, new PersonalLibraryState(state.Previous, state.Current)); return version;
    }
    public LibraryVersion PreviousVersion()
    { var previous = State().Previous; return LoadVersion(previous is null ? BaseRoot : Local(previous)); }
    public void Export(string file)
    {
        var profile = Profile();
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                WriteJson(zip, "personal.json", profile);
                var files = profile.Samples.SelectMany(s => new[] { s.File, s.SourceFile }).Concat(profile.Items.Select(i => i.Item.Thumbnail).OfType<string>()).Distinct();
                foreach (var relative in files) zip.CreateEntryFromFile(Local(relative), relative.Replace('\\', '/'));
            }
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public PersonalProfile Import(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        if (zip.Entries.Count > 4000 || zip.Entries.Sum(e => e.Length) > 512L * 1024 * 1024) throw new InvalidDataException("个人库包过大");
        var entry = zip.GetEntry("personal.json") ?? throw new InvalidDataException("个人库包缺少清单");
        using var reader = new StreamReader(entry.Open());
        var imported = JsonSerializer.Deserialize<PersonalProfile>(reader.ReadToEnd(), JsonFile.Options) ?? throw new InvalidDataException("清单为空");
        ValidateProfile(imported);
        var current = Clone(Profile());
        foreach (var sample in imported.Samples)
            if (current.Samples.Any(s => s.Id == sample.Id && s != sample)) throw new InvalidDataException("样本编号冲突，导入已取消");
        foreach (var item in imported.Items)
            if (current.Items.Any(i => i.Item.Id == item.Item.Id && i != item)) throw new InvalidDataException("物品编号冲突，导入已取消");
        var needed = imported.Samples.SelectMany(s => new[] { s.File, s.SourceFile }).Concat(imported.Items.Select(i => i.Item.Thumbnail).OfType<string>()).Distinct().ToArray();
        foreach (var path in needed)
        {
            Local(path);
            if (Path.GetExtension(path).ToLowerInvariant() is not (".wav" or ".png" or ".jpg" or ".jpeg")) throw new InvalidDataException("个人库包含不支持的文件");
            if (zip.GetEntry(path.Replace('\\', '/')) is null) throw new InvalidDataException("个人库包缺少文件：" + path);
        }
        foreach (var path in needed)
        {
            var asset = zip.GetEntry(path.Replace('\\', '/'))!;
            using var stream = asset.Open(); using var buffer = new MemoryStream(); stream.CopyTo(buffer); var bytes = buffer.ToArray();
            if (File.Exists(Local(path)) && !File.ReadAllBytes(Local(path)).SequenceEqual(bytes)) throw new InvalidDataException("资源文件冲突，未覆盖已有文件");
            Directory.CreateDirectory(Path.GetDirectoryName(Local(path))!); File.WriteAllBytes(Local(path), bytes);
        }
        foreach (var sample in imported.Samples) { ReadSample(sample); ReadSource(sample); if (current.Samples.All(s => s.Id != sample.Id)) current.Samples.Add(sample); }
        foreach (var item in imported.Items) if (current.Items.All(i => i.Item.Id != item.Item.Id)) current.Items.Add(item);
        return current;
    }
    private static void ValidateProfile(PersonalProfile profile)
    {
        static bool Id(string id) => !string.IsNullOrEmpty(id) && id.Length <= 128 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        if (profile.Items.Select(i => i.Item.Id).Distinct().Count() != profile.Items.Count || profile.Samples.Select(s => s.Id).Distinct().Count() != profile.Samples.Count)
            throw new InvalidDataException("个人库编号重复");
        foreach (var item in profile.Items)
            if (!Id(item.Item.Id) || !Id(item.GroupId) || string.IsNullOrWhiteSpace(item.Item.Name) || item.Item.Name.Length > 100 ||
                item.Item.GridWidth is < 1 or > 10 || item.Item.GridHeight is < 1 or > 10 || item.Item.ReferenceValue < 0)
                throw new InvalidDataException("个人物品名称、编号或格数无效");
        foreach (var sample in profile.Samples)
            if (!Id(sample.Id) || !Id(sample.ItemId) || !Id(sample.GroupId) || !double.IsFinite(sample.StartSeconds) || !double.IsFinite(sample.EndSeconds) || sample.StartSeconds < 0 || sample.EndSeconds <= sample.StartSeconds)
                throw new InvalidDataException("个人样本元数据无效");
    }
    // A new personal sound group starts at the bundled catalogue's floor for the
    // active engine: .82 for the in-match matcher, the historical .75 otherwise.
    public static double DefaultGroupThreshold(SoundLibrary library) => library.PrimaryEngine == "inmatch" ? .82 : .75;
    private IRecognizer CreateRecognizer(SoundLibrary library, string root) => library.PrimaryEngine == "soundradar"
        ? new RadarRecognizer(library, Path.Combine(root, "radar-index.bin"), engine)
        : RecognizerFactory.Create(library, root);
    private static void CopyRelative(string from, string root, string relative)
    { var path = LibraryBuilder.ResolveFile(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(from, path, true); }
    private static void WriteJson(ZipArchive archive, string name, object data)
    { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(JsonSerializer.Serialize(data, JsonFile.Options)); }
    internal static HashSet<(string Group, string Sample)> IndexSamples(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        string Text() => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "SRZ1" || reader.ReadUInt32() != 1) throw new InvalidDataException("索引格式不支持");
        Text(); Text(); Text(); reader.ReadUInt32(); var count = reader.ReadUInt64(); var groups = reader.ReadUInt32();
        if (count > 100000 || groups > 10000) throw new InvalidDataException("索引数量异常");
        var ids = new List<string>();
        for (var i = 0; i < groups; i++) { ids.Add(Text()); Text(); reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadSingle(); reader.ReadUInt32(); }
        var result = new HashSet<(string, string)>();
        for (ulong i = 0; i < count; i++) { var name = Text(); reader.ReadUInt32(); var group = reader.ReadUInt16(); reader.ReadSingle();
            if (group >= ids.Count) throw new InvalidDataException("索引组编号无效"); result.Add((ids[group], name)); }
        return result;
    }
}
