using Listener.Core;
using System.IO;

internal static class PersonalLibraryTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("声音快照绑定结果与版本，缓存淘汰保护选中项和文字历史", SnapshotCache);
        yield return ("建库拒绝无效、重复和重叠录音，裁剪保留录音身份，草稿可恢复", DraftQuality);
        yield return ("真实引擎个人库：同音确认、三参考一试识别、启用、导入导出、回退", VersionLifecycle);
        yield return ("留出片段失败和取消建库不替换当前版本", FailedTrial);
        yield return ("新音效组使用0.75阈值，原阈值保留，个人样本可禁用与删除", NewGroup);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception("invalid input accepted"); }
    private static string Base => Path.Combine(AppContext.BaseDirectory, "library");
    private static string Engine => Path.Combine(AppContext.BaseDirectory, "engine", "Listener.Engine.exe");
    private static PersonalLibraryStore Store() => new(Base, Path.Combine(Path.GetTempPath(), "aqtw-feature-tests", Guid.NewGuid().ToString("N")), Engine);
    private static AudioClip Clip(ReferenceSample sample) => WaveAudio.Read(ReferenceAudio.VerifiedPath(Base, sample));
    private static AudioClip Variant(AudioClip clip, double gain) => new(clip.Samples.Select(s => (float)(s * gain)).ToArray(), clip.SampleRate);
    private static LearningDraft Draft(PersonalLibraryStore store, bool guided = false)
    {
        var group = store.ResolveActive().Library.Groups.First(g => g.Templates.Count > 0);
        return new() { Item = new("user-fixture", "工程回归测试物品", true, null), GroupId = group.Id, IsNewItem = true, Guided = guided, GroupConfirmed = true };
    }
    private static void SnapshotCache()
    {
        var library = new SoundLibrary { Version = "test-v1" };
        var item = new ItemDefinition("test", "test", true, null);
        var candidates = new[] { new Candidate(item, .9, "group") };
        var analysis = new RecognitionAnalysis(new(1, RecognitionStatus.Matched, true, candidates, 1, "test"), [new("group", .9)]);
        var samples = new float[100]; samples[0] = .5f;
        var store = new SessionAudioStore(800);
        var first = store.Add(new(samples, 24000), analysis, "test", library, "v1"); store.Pin(first.Id);
        samples[0] = 0; candidates[0] = new(item with { Id = "changed" }, .1, "different");
        var second = store.Add(new(samples, 24000), analysis, "test", library, "v1");
        var third = store.Add(new(samples, 24000), analysis, "test", new() { Version = "test-v2" }, "v2");
        Check(first.Audio?.Samples[0] == .5f && first.Analysis.Result.BestMatch?.Item.Id == "test", "snapshot mutated with caller");
        Check(second.Audio is null && first.Audio is not null && store.AudioBytes == 800, "cache evicted selected sound");
        Check(first.Library.Version == "test-v1" && third.Library.Version == "test-v2", "history reinterpreted");
        for (var i = 0; i < 24; i++) { store.Add(new(samples, 24000), analysis, "test", library, "v1"); store.Retain([second.Id]); }
        Check(store.Recent.Count == 20 && store.Get(second.Id) is not null && store.Get(first.Id) is not null && store.AudioBytes <= 800, "retention policy failed");
        var history = new RecognitionHistory();
        history.Remember(analysis.Result, "auto", true, DateTimeOffset.Now, first.Id, "v1", "root1");
        history.Remember(analysis.Result, "auto", true, DateTimeOffset.Now, third.Id, "v2", "root2");
        Check(history.Entries.Count == 2, "different library versions merged");
    }
    private static void DraftQuality()
    {
        var store = Store(); var draft = Draft(store); var source = Clip(ReferenceAudio.Load(Base).Samples[0]);
        Reject(() => store.AddSample(draft, new(new float[24000], 24000), "silence"));
        Reject(() => store.AddSample(draft, new(Enumerable.Repeat(1f, 24000).ToArray(), 24000), "clipping"));
        Reject(() => store.AddSample(draft, new([.1f], 24000), "short"));
        var first = store.AddSample(draft, source, "fixture", captureSession: "capture", captureStart: 10, captureEnd: 11.2);
        Reject(() => store.AddSample(draft, source, "duplicate"));
        Reject(() => store.AddSample(draft, Variant(source, .8), "overlap", captureSession: "capture", captureStart: 10.55, captureEnd: 11.75));
        store.TrimSample(draft, first, .01, first.EndSeconds, false);
        var trimmed = draft.Samples.Single();
        Check(trimmed.OriginalSha256 == first.OriginalSha256 && trimmed.RecordingId == first.RecordingId, "trim changed independent recording identity");
        var reopened = new PersonalLibraryStore(Base, store.Root, Engine).Drafts().Single();
        Check(reopened.Samples.Count == 1 && reopened.Samples[0].StartSeconds == .01, "draft did not persist");
        Check(store.ReadSource(trimmed).Samples.Length == source.Samples.Length, "source lost");
    }
    private static void VersionLifecycle()
    {
        var store = Store(); var draft = Draft(store, guided: true);
        var sample = ReferenceAudio.Load(Base).Samples.First(s => s.GroupId == draft.GroupId); var source = Clip(sample);
        draft.GroupId = "user-fixture"; draft.GroupConfirmed = false;
        foreach (var gain in new[] { .7, .8, .9 }) store.AddSample(draft, Variant(source, gain), "derived regression fixture");
        var heldout = store.AddSample(draft, Variant(source, .6), "derived fixture, not independent accuracy", checkOnly: true);
        var conflict = store.BuildDraft(draft).GetAwaiter().GetResult();
        Check(conflict.Version is null && conflict.Conflicts.Contains(sample.GroupId), "new item bypassed same-sound confirmation");
        draft.GroupId = sample.GroupId; draft.GroupConfirmed = true;
        var built = store.BuildDraft(draft).GetAwaiter().GetResult();
        Check(built.Version is not null, "build/trial failed: " + built.Message + " " + built.ReportPath);
        var version = built.Version!;
        Check(ReferenceAudio.Load(version.Root).Samples.Count == 17, "held-out sample leaked into index or references missing");
        Check(ReferenceAudio.Load(version.Root).Samples.All(s => !s.TemplateId.Contains(heldout.Id)), "heldout indexed");
        store.Activate(version);
        Check(store.ResolveActive().Library.Items.Any(i => i.Id == draft.Item.Id), "new item not activated");
        Check(new PersonalLibraryStore(Base, store.Root, Engine).ResolveActive().Root == version.Root, "active version not persistent");
        var file = Path.Combine(store.Root, "export.aqtwlib"); store.Export(file); store.Export(file);
        var importedStore = Store(); var profile = importedStore.Import(file);
        Check(profile.Items.Count == 1 && profile.Samples.Count == 4 && importedStore.ReadSource(profile.Samples[0]).Samples.Length > 0, "export/import lost assets");
        var imported = importedStore.Build(profile).GetAwaiter().GetResult(); Check(imported.Version is not null, "imported profile failed build");
        Check(store.Rollback().Root == Base && !store.Profile().Items.Any(), "rollback did not restore base");
        Check(store.Rollback().Root == version.Root, "undo rollback did not restore personal version");
        File.WriteAllText(Path.Combine(version.Root, "library.json"), "broken");
        Check(store.ResolveActive().Root == Base && store.RecoveryNotice.Length > 0, "corrupt version did not recover");
    }
    private static void FailedTrial()
    {
        var store = Store(); var draft = Draft(store, guided: true);
        var samples = ReferenceAudio.Load(Base).Samples;
        var source = Clip(samples.First(s => s.GroupId == draft.GroupId));
        foreach (var gain in new[] { .7, .8, .9 }) store.AddSample(draft, Variant(source, gain), "regression fixture");
        using var recognizer = new RadarRecognizer(store.ResolveActive().Library, Path.Combine(Base, "radar-index.bin"), Engine);
        var wrong = samples.First(s => s.GroupId != draft.GroupId && !recognizer.Recognize(Clip(s).Samples, Clip(s).SampleRate).Candidates.Any(c => c.GroupId == draft.GroupId));
        store.AddSample(draft, Clip(wrong), "deliberately wrong heldout", checkOnly: true);
        var built = store.BuildDraft(draft).GetAwaiter().GetResult();
        Check(built.Version is null && built.Message.Contains("试识别") && store.ResolveActive().Root == Base, "failed trial replaced base");
        Check(store.Drafts().Single().Samples.Count == 4, "failure erased draft");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { store.Build(new(), cancel.Token).GetAwaiter().GetResult(); throw new Exception("canceled build succeeded"); }
        catch (OperationCanceledException) { }
        Check(store.ResolveActive().Root == Base, "cancellation replaced library");
    }
    private static void NewGroup()
    {
        var store = Store(); var original = store.ResolveActive();
        var draft = Draft(store); draft.GroupId = "user-independent"; draft.GroupConfirmed = false;
        var source = new AudioClip(Enumerable.Range(0, 57600).Select(i => (float)(.15 * Math.Sin(2 * Math.PI * (1700 * i / 48000.0 + 2100 * Math.Pow(i / 48000.0, 2))) * Math.Sin(Math.PI * i / 57600))).ToArray(), 48000);
        store.AddSample(draft, source, "synthetic chirp, engineering fixture");
        var built = store.BuildDraft(draft).GetAwaiter().GetResult();
        Check(built.Version is not null, "independent group build failed: " + built.Message);
        var version = built.Version!;
        Check(version.Library.Groups.Single(g => g.Id == draft.GroupId).Threshold == .75 && original.Library.Groups.All(old => version.Library.Groups.Single(g => g.Id == old.Id).Threshold == old.Threshold), "threshold changed");
        using (var recognizer = new RadarRecognizer(version.Library, Path.Combine(version.Root, "radar-index.bin"), Engine))
        {
            var analysis = recognizer.Analyze(source.Samples, source.SampleRate);
            Check(analysis.Result.Candidates.Any(c => c.Item.Id == draft.Item.Id) && analysis.Scores.Count == version.Library.Groups.Count, "all-score analysis/recognition missing");
        }
        store.Activate(version); var profile = store.Profile(); profile.Items[0] = profile.Items[0] with { Enabled = false };
        profile.Samples[0] = profile.Samples[0] with { Enabled = false };
        var disabled = store.Build(profile).GetAwaiter().GetResult(); Check(disabled.Version is not null && disabled.Version.Library.Items.Count == original.Library.Items.Count, "disable failed");
        store.Activate(disabled.Version!); profile.Items.Clear(); profile.Samples.Clear();
        var deleted = store.Build(profile).GetAwaiter().GetResult(); Check(deleted.Version is not null, "delete failed");
    }
}
