namespace Listener.Core;

public sealed record RecognitionEntry(long Id, DateTimeOffset FirstSeen, DateTimeOffset LastSeen,
    string Source, bool Automatic, int Matches, RecognitionResult Result);

// Session-only results. Audio is never stored here. Call from the UI thread.
public sealed class RecognitionHistory
{
    public const int Capacity = 100;
    private readonly List<RecognitionEntry> entries = [];
    private long nextId;
    public IReadOnlyList<RecognitionEntry> Entries => entries.AsReadOnly();
    public RecognitionEntry? Latest { get; private set; }
    public event Action? Changed;

    public bool Remember(RecognitionResult result, string source, bool automatic, DateTimeOffset at)
    {
        if (result.Status != RecognitionStatus.Matched || result.CandidateCount == 0) return false;
        result = result with { Candidates = Array.AsReadOnly(result.Candidates.ToArray()) };
        var previous = entries.FirstOrDefault();
        var sameOperation = previous is not null && previous.Result.OperationId == result.OperationId;
        var gap = previous is null ? double.PositiveInfinity : (at - previous.LastSeen).TotalSeconds;
        var overlappingScan = previous is { Automatic: true } && automatic && gap is >= 0 and <= 2
            && SameCandidates(previous.Result, result);
        RecognitionEntry entry;
        if (previous is not null && (sameOperation || overlappingScan))
        {
            entry = previous with { LastSeen = at, Result = result,
                Matches = previous.Matches + (sameOperation ? 0 : 1) };
            entries[0] = entry;
        }
        else
        {
            entry = new(++nextId, at, at, source, automatic, 1, result);
            entries.Insert(0, entry);
            if (entries.Count > Capacity) entries.RemoveAt(entries.Count - 1);
        }
        Latest = entry; Changed?.Invoke(); return true;
    }

    private static bool SameCandidates(RecognitionResult first, RecognitionResult second) =>
        first.Candidates.Select(c => (c.Item.Id, c.GroupId)).ToHashSet()
            .SetEquals(second.Candidates.Select(c => (c.Item.Id, c.GroupId)));

    public void ClearCurrent() { Latest = null; Changed?.Invoke(); }
    public void ClearHistory() { entries.Clear(); Changed?.Invoke(); }
}
