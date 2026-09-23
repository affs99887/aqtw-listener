namespace Listener.Core;

public sealed record PickupAnalysis(RecognitionAnalysis Analysis, AudioScanWindow Window);

public static class PickupRecognition
{
    // Continuous listening also encounters footsteps and UI transients. Keep a
    // small margin over the catalog's experimental 0.75 floor for an unattended
    // single-event verdict; matching never becomes a mandatory two-action pair.
    public const double MinimumAutomaticScore = .78;
    public static PickupAnalysis Analyze(IRecognizer recognizer, AudioScanWindow window,
        long operationId = 0, CancellationToken cancellation = default)
    {
        var pickup = window.Focus();
        var primary = recognizer.Analyze(pickup.Samples, pickup.SampleRate, operationId, true, cancellation);
        if (primary.Result is { Status: RecognitionStatus.Matched, BestMatch.Score: < MinimumAutomaticScore })
            primary = primary with { Result = primary.Result with
            {
                Status = RecognitionStatus.Unknown, Candidates = [], Message = "拿起声证据偏弱 · 继续监听"
            } };
        // A complete pickup is sufficient. A later putdown or unrelated sound
        // cannot veto it or force the user to wait for a second action.
        if (primary.Result.Status != RecognitionStatus.Unknown) return new(primary, pickup);
        var longer = window.Focus(.08, .52);
        if (longer.Samples.Length <= pickup.Samples.Length + pickup.SampleRate * .04) return new(primary, pickup);
        var support = recognizer.Analyze(longer.Samples, longer.SampleRate, operationId, true, cancellation);
        var resolved = AutomaticRecognitionConsensus.Resolve(primary, support);
        return resolved.Result.Status == RecognitionStatus.Matched ? new(resolved, longer) : new(primary, pickup);
    }
}
