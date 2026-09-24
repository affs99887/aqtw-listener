namespace Listener.Core;

public sealed record RecognitionEntry(long Id, DateTimeOffset FirstSeen, DateTimeOffset LastSeen,
    string Source, bool Automatic, int Matches, RecognitionResult Result,
    Guid? SnapshotId = null, string LibraryVersion = "", string LibraryRoot = "",
    double? AudioSeconds = null, bool FollowUp = false);

// Session-only results. Audio is never stored here. Call from the UI thread.
public sealed class RecognitionHistory
{
    public const int Capacity = 100;
    // A different catalogue sound 0.15–1.0 s after an accepted automatic pickup is
    // almost always the same item's putdown (or a UI click) resembling another
    // item's pickup reference: replaying five raids showed 激光指示模块→天线,
    // 量子F2000→红外线理疗灯 and 专用电池组→水壶 pairs 0.3–0.6 s apart. Such a
    // follow-up stays in the list for review but does not replace the pickup on
    // the overlay unless it is clearly stronger.
    public const double FollowUpMinSeconds = .15, FollowUpMaxSeconds = 1.0, FollowUpMargin = .05;
    private readonly List<RecognitionEntry> entries = [];
    private long nextId;
    public IReadOnlyList<RecognitionEntry> Entries => entries.AsReadOnly();
    public RecognitionEntry? Latest { get; private set; }
    public event Action? Changed;

    public bool Remember(RecognitionResult result, string source, bool automatic, DateTimeOffset at,
        Guid? snapshotId = null, string libraryVersion = "", string libraryRoot = "", double? audioSeconds = null)
    {
        if (result.Status != RecognitionStatus.Matched || result.CandidateCount == 0) return false;
        result = result with { Candidates = Array.AsReadOnly(result.Candidates.ToArray()) };
        var previous = entries.FirstOrDefault();
        var sameOperation = previous is not null && previous.LibraryVersion == libraryVersion && previous.Result.OperationId == result.OperationId;
        var gap = previous is null ? double.PositiveInfinity : (at - previous.LastSeen).TotalSeconds;
        var overlappingScan = previous is { Automatic: true } && automatic && gap is >= 0 and <= 2
            && previous.LibraryVersion == libraryVersion && SameCandidates(previous.Result, result);
        var held = automatic && Latest is { } shown && IsFollowUp(shown, result, libraryVersion, audioSeconds);
        RecognitionEntry entry;
        if (previous is not null && (sameOperation || overlappingScan))
        {
            held &= previous.FollowUp;
            entry = previous with { LastSeen = at, Result = result, SnapshotId = snapshotId, LibraryVersion = libraryVersion, LibraryRoot = libraryRoot,
                Matches = previous.Matches + (sameOperation ? 0 : 1), AudioSeconds = previous.AudioSeconds ?? audioSeconds, FollowUp = held };
            entries[0] = entry;
        }
        else
        {
            entry = new(++nextId, at, at, source, automatic, 1, result, snapshotId, libraryVersion, libraryRoot, audioSeconds, held);
            entries.Insert(0, entry);
            if (entries.Count > Capacity) entries.RemoveAt(entries.Count - 1);
        }
        if (!held) Latest = entry;
        Changed?.Invoke(); return true;
    }

    private static bool IsFollowUp(RecognitionEntry shown, RecognitionResult result, string libraryVersion, double? audioSeconds) =>
        shown is { Automatic: true, FollowUp: false } && shown.LibraryVersion == libraryVersion
        && shown.AudioSeconds is { } shownAt && audioSeconds is { } now
        && now - shownAt is >= FollowUpMinSeconds and <= FollowUpMaxSeconds
        && !SameCandidates(shown.Result, result)
        && (result.BestMatch?.Score ?? 0) < (shown.Result.BestMatch?.Score ?? 0) + FollowUpMargin;

    private static bool SameCandidates(RecognitionResult first, RecognitionResult second) =>
        first.Candidates.Select(c => (c.Item.Id, c.GroupId)).ToHashSet()
            .SetEquals(second.Candidates.Select(c => (c.Item.Id, c.GroupId)));

    public void ClearCurrent() { Latest = null; Changed?.Invoke(); }
    public void ClearHistory() { entries.Clear(); Changed?.Invoke(); }
}
