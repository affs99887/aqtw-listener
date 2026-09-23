namespace Listener.Core;

public sealed record AudioScanWindow(float[] Samples, int SampleRate, double EndSeconds, double OnsetSeconds);

// Overlapping windows allow recognition even when remote input never reaches
// Windows mouse hooks. The caller owns one recognition task; busy work is skipped,
// never queued. All timestamps share AudioTimeline's monotonic clock.
public sealed class AutomaticAudioScanner
{
    public const double IntervalSeconds = .55;
    public const double WindowSeconds = 1.15;
    private double nextScanAt;
    private double lastOnsetAt = double.NegativeInfinity;
    public double LastWindowRms { get; private set; }

    public void Reset(double now)
    {
        nextScanAt = now + .4;
        lastOnsetAt = double.NegativeInfinity;
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
        if (LastWindowRms < .00008) return null;
        var onset = FindOnset(samples, timeline.SampleRate);
        if (onset is null) return null;
        var onsetAt = end - WindowSeconds + onset.Value;
        // Wait until the entire pickup sound is available. An overlapping next
        // window still contains its rising edge.
        if (end - onsetAt < .4 || onsetAt - lastOnsetAt < .6) return null;
        lastOnsetAt = onsetAt;
        return new(samples, timeline.SampleRate, end, onsetAt);
    }

    private static double? FindOnset(float[] samples, int rate)
    {
        var block = Math.Max(1, rate / 50);
        var energy = new double[(samples.Length + block - 1) / block];
        for (var frame = 0; frame < energy.Length; frame++)
        {
            double sum = 0;
            var start = frame * block;
            var count = Math.Min(block, samples.Length - start);
            for (var i = start; i < start + count; i++) sum += samples[i] * samples[i];
            energy[frame] = Math.Sqrt(sum / count);
        }
        var peak = energy.Max();
        if (peak < .0002) return null;
        var threshold = Math.Max(.00012, peak * .08);
        // Persistent music and ambience may have a rising edge when capture
        // starts, but a pickup has fallen away again within this window.
        if (energy[^1] > threshold || energy[^2] > threshold) return null;
        // A sustained sound already present at the start of this window is not
        // a new pickup. A clear rise above the preceding sound is required.
        for (var frame = 2; frame < energy.Length; frame++)
        {
            if (energy[frame] < threshold || energy[frame - 1] >= threshold) continue;
            var before = Math.Min(energy[frame - 1], energy[frame - 2]);
            if (before > threshold * .8 || peak < Math.Max(.0002, before * 2.2)) continue;
            return frame * block / (double)rate;
        }
        return null;
    }
}
