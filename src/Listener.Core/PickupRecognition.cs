namespace Listener.Core;

public sealed record PickupAnalysis(RecognitionAnalysis Analysis, AudioScanWindow Window);

public static class PickupRecognition
{
    // Continuous listening also encounters footsteps, UI clicks and other items'
    // putdown sounds. Replaying five full raids with the in-match matcher put
    // genuine pickups at 0.92–0.98 and the loose first-person false alarms at
    // 0.85–0.90, so an unattended single-event verdict needs a little more than
    // the catalogue floor; matching never becomes a mandatory two-action pair.
    public const double MinimumAutomaticScore = .86;
    public static PickupAnalysis Analyze(IRecognizer recognizer, AudioScanWindow window,
        long operationId = 0, CancellationToken cancellation = default)
    {
        // Analyse the pickup itself, anchored on its onset. The clip keeps the room
        // tone before the sound so the matcher can subtract it, and enough tail to
        // hold the longest catalogue sound. A later putdown or unrelated sound
        // cannot veto the pickup or force the user to wait for a second action.
        var pickup = window.Focus();
        var analysis = recognizer.AnalyzeAt(pickup.Samples, pickup.SampleRate, window.OnsetSeconds - pickup.StartSeconds, operationId, true, cancellation);
        if (analysis.Result is { Status: RecognitionStatus.Matched, BestMatch.Score: < MinimumAutomaticScore })
            analysis = analysis with { Result = analysis.Result with
            {
                Status = RecognitionStatus.Unknown, Candidates = [], Message = "拿起声证据偏弱 · 继续监听"
            } };
        return new(analysis, pickup);
    }
}
