namespace Listener.Core;

public sealed record AudioScanWindow(float[] Samples, int SampleRate, double EndSeconds, double OnsetSeconds)
{
    public double StartSeconds => EndSeconds - Samples.Length / (double)SampleRate;

    // Analyse the pickup itself. Later putdown/UI sounds in the lookback window
    // must not decide whether this first event was a valid pickup.
    public AudioScanWindow Focus(double before = .08, double after = .36)
    {
        var first = (int)Math.Clamp(Math.Round((OnsetSeconds - before - StartSeconds) * SampleRate), 0, Samples.Length);
        var last = (int)Math.Clamp(Math.Round((OnsetSeconds + after - StartSeconds) * SampleRate), first, Samples.Length);
        return new(Samples.AsSpan(first, last - first).ToArray(), SampleRate,
            StartSeconds + last / (double)SampleRate, OnsetSeconds);
    }
}

// Overlapping windows allow recognition even when remote input never reaches
// Windows mouse hooks. The caller owns one recognition task; busy work is skipped,
// never queued. All timestamps share AudioTimeline's monotonic clock.
public sealed class AutomaticAudioScanner
{
    public const double IntervalSeconds = .18;
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
        var start = end - WindowSeconds;
        var onset = FindOnset(samples, timeline.SampleRate, lastOnsetAt - start);
        if (onset is null) return null;
        var onsetAt = end - WindowSeconds + onset.Value;
        // Wait until the entire pickup sound is available. An overlapping next
        // window still contains its rising edge.
        if (end - onsetAt < .3 || onsetAt - lastOnsetAt < .32) return null;
        lastOnsetAt = onsetAt;
        return new(samples, timeline.SampleRate, end, onsetAt);
    }

    private static double? FindOnset(float[] samples, int rate, double previousOnset)
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
        // Compare each rise to the preceding 120 ms, not digital silence. In a
        // match the room tone continues before, during and after item sounds.
        // A local decay distinguishes a short event from capture/music startup;
        // it does not wait for the user to release the item or for a silent tail.
        var preceding = new double[6];
        for (var frame = 6; frame + 15 < energy.Length; frame++)
        {
            var onset = frame * block / (double)rate;
            if (onset - previousOnset < .32) continue;
            Array.Copy(energy, frame - 6, preceding, 0, 6);
            Array.Sort(preceding);
            var background = (preceding[2] + preceding[3]) / 2;
            var threshold = Math.Max(.00012, background * 1.7);
            if (energy[frame] < threshold || energy[frame - 1] >= threshold) continue;
            var limit = Math.Min(energy.Length, frame + 32);
            var peakFrame = frame;
            for (var i = frame + 1; i < limit; i++)
                if (energy[i] > energy[peakFrame]) peakFrame = i;
            var peak = energy[peakFrame];
            if (peak < Math.Max(.0002, background * 2.2) ||
                energy.Skip(frame).Take(5).Count(value => value > threshold) < 2) continue;
            var decayLevel = background + (peak - background) * .45;
            var decayed = false;
            for (var i = Math.Max(frame + 5, peakFrame + 1); i + 1 < limit; i++)
                if (energy[i] <= decayLevel && energy[i + 1] <= decayLevel) { decayed = true; break; }
            if (!decayed) continue;
            return onset;
        }
        return null;
    }
}
