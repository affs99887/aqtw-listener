namespace Listener.Core;

public sealed record GroupScore(string GroupId, double Score);
// Putdown names the putdown-sound group this audio matched instead of any pickup:
// the sound is an item being put down or transferred and must not be recognised.
// Separation is the best candidate's lead over the strongest pickup group that did
// not make the candidate list; a large lead marks a clean, unambiguous match.
public sealed record RecognitionAnalysis(RecognitionResult Result, IReadOnlyList<GroupScore> Scores, GroupScore? Putdown = null, double Separation = 0);

public sealed class AnalysisSnapshot
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
    public required string Source { get; init; }
    public required RecognitionAnalysis Analysis { get; init; }
    public required SoundLibrary Library { get; init; }
    public required string LibraryRoot { get; init; }
    public string? CaptureSession { get; init; }
    public double? CaptureStart { get; init; }
    public double? CaptureEnd { get; init; }
    public AudioClip? Audio { get; internal set; }
    public override string ToString() => $"{At:HH:mm:ss} · {(Analysis.Result.Status == RecognitionStatus.Matched ? "已匹配" : "未匹配")} · {Source}";
}

// UI-thread owned. Selected snapshots are pinned, and history keeps immutable metadata.
public sealed class SessionAudioStore(long maximumBytes = 32 * 1024 * 1024)
{
    private readonly List<AnalysisSnapshot> snapshots = [];
    private Guid? pinned;
    public IReadOnlyList<AnalysisSnapshot> Recent => snapshots.TakeLast(20).Reverse().ToArray();
    public long AudioBytes => snapshots.Sum(s => (long)(s.Audio?.Samples.Length ?? 0) * sizeof(float));
    public AnalysisSnapshot? Get(Guid? id) => snapshots.FirstOrDefault(s => s.Id == id);
    public void Pin(Guid? id) { pinned = id; Trim(); }
    public AnalysisSnapshot Add(AudioClip audio, RecognitionAnalysis analysis, string source, SoundLibrary library, string root,
        string? captureSession = null, double? start = null, double? end = null)
    {
        var snapshot = new AnalysisSnapshot { Source = source, Analysis = analysis with {
            Result = analysis.Result with { Candidates = analysis.Result.Candidates.ToArray() }, Scores = analysis.Scores.ToArray() },
            Library = library, LibraryRoot = root, CaptureSession = captureSession, CaptureStart = start, CaptureEnd = end,
            Audio = new(audio.Samples.ToArray(), audio.SampleRate) };
        snapshots.Add(snapshot); Trim(); return snapshot;
    }
    public void Retain(IEnumerable<Guid?> historyIds)
    {
        var keep = historyIds.Where(i => i.HasValue).Select(i => i!.Value).Concat(Recent.Select(s => s.Id)).ToHashSet();
        snapshots.RemoveAll(s => s.Id != pinned && !keep.Contains(s.Id)); Trim();
    }
    private void Trim()
    {
        var bytes = AudioBytes;
        foreach (var snapshot in snapshots)
        {
            if (bytes <= maximumBytes) break;
            if (snapshot.Id == pinned || snapshot.Audio is null) continue;
            bytes -= (long)snapshot.Audio.Samples.Length * sizeof(float); snapshot.Audio = null;
        }
    }
}
