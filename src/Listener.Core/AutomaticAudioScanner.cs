namespace Listener.Core;

public sealed record AudioScanWindow(float[] Samples, int SampleRate, double EndSeconds);

// Overlapping windows allow recognition even when remote input never reaches
// Windows mouse hooks. The caller owns one recognition task; busy work is skipped,
// never queued. All timestamps share AudioTimeline's monotonic clock.
public sealed class AutomaticAudioScanner
{
    public const double IntervalSeconds = .55;
    public const double WindowSeconds = 1.15;
    private double nextScanAt;
    public double LastWindowRms { get; private set; }

    public void Reset(double now)
    {
        nextScanAt = now + .4;
        LastWindowRms = 0;
    }

    public AudioScanWindow? TryTakeWindow(AudioTimeline timeline, double now, bool active, bool busy)
    {
        if (!active) { Reset(now); return null; }
        if (busy || now < nextScanAt) return null;
        nextScanAt = now + IntervalSeconds;
        // Allow the most recent WASAPI packet to arrive before slicing the ring.
        var end = now - .08;
        var samples = timeline.Slice(end - WindowSeconds, end);
        LastWindowRms = AudioFeatures.Rms(samples);
        return LastWindowRms >= .00008 ? new(samples, timeline.SampleRate, end) : null;
    }
}
