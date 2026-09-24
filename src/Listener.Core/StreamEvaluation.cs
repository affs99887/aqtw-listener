namespace Listener.Core;

public sealed record StreamEvent(double AnalyzedAt, double Onset, double ClipStart, double ClipEnd,
    RecognitionStatus Status, GroupScore[] Scores, string[] CandidateIds, string Action = "pickup");

// Replays an explicitly supplied WAV through the same sound gate and pickup
// analysis as live listening. Reports contain measurements, never PCM samples.
public static class StreamEvaluation
{
    public static IReadOnlyList<StreamEvent> Run(IRecognizer recognizer, AudioClip clip)
    {
        var timeline = new AudioTimeline(clip.SampleRate);
        var scanner = new AutomaticAudioScanner(); scanner.Reset(0);
        var events = new List<StreamEvent>();
        var block = Math.Max(1, clip.SampleRate / 10);
        for (var first = 0; first < clip.Samples.Length; first += block)
        {
            var length = Math.Min(block, clip.Samples.Length - first);
            timeline.Append(clip.Samples.AsSpan(first, length).ToArray(), first / (double)clip.SampleRate);
            var now = (first + length) / (double)clip.SampleRate;
            var window = scanner.TryTakeWindow(timeline, now, true, false);
            if (window is null) continue;
            var pickup = PickupRecognition.Analyze(recognizer, window);
            events.Add(new(now, window.OnsetSeconds, pickup.Window.StartSeconds, pickup.Window.EndSeconds,
                pickup.Analysis.Result.Status, pickup.Analysis.Scores.Take(3).ToArray(),
                pickup.Analysis.Result.Candidates.Select(candidate => candidate.Item.Id).ToArray()));
        }
        return events;
    }
}
