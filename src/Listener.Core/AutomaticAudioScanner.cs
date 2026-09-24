namespace Listener.Core;

public sealed record AudioScanWindow(float[] Samples, int SampleRate, double EndSeconds, double OnsetSeconds)
{
    public double StartSeconds => EndSeconds - Samples.Length / (double)SampleRate;

    // Hand the matcher the pickup with enough context before it to estimate the
    // room tone, and enough after it to hold the longest catalogue sound (~0.33 s).
    // Later putdown/UI sounds must not decide whether this event was a valid pickup.
    public AudioScanWindow Focus(double before = .45, double after = .36)
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
//
// Onsets are judged on the audio above ~700 Hz. In a raid the broadband level is
// dominated by rumble, footsteps and room tone, which hid the quiet item clicks
// from a broadband detector; the item sounds themselves live almost entirely
// above that corner. The background is the lower quartile of the preceding
// 300 ms, so a louder click or footstep just before the pickup cannot mask it.
public sealed class AutomaticAudioScanner
{
    public const double IntervalSeconds = .18;
    public const double WindowSeconds = 1.15;
    public const double HighPassHz = 700;
    // Audio that must exist after the onset before the window is analysed: the
    // longest catalogue sound is ~0.33 s and the matcher searches up to +128 ms.
    public const double TailSeconds = .36;
    // At the trader a faint interface click can precede the item's own sound by
    // 0.12–0.19 s; the item sound must still open its own event, so the refractory
    // only has to outlast the two-click putdown sounds (40 ms apart).
    public const double RefractorySeconds = .12;
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
        if (end - onsetAt < TailSeconds || onsetAt - lastOnsetAt < RefractorySeconds) return null;
        lastOnsetAt = onsetAt;
        return new(samples, timeline.SampleRate, end, onsetAt);
    }

    // 2nd-order Butterworth high-pass (RBJ cookbook), applied from a zero state to
    // each window. The first blocks only ever serve as background.
    internal static float[] HighPass(float[] samples, int rate, double cornerHz = HighPassHz)
    {
        var omega = 2 * Math.PI * cornerHz / rate;
        var cos = Math.Cos(omega); var alpha = Math.Sin(omega) / (2 * Math.Sqrt(2));
        var a0 = 1 + alpha;
        double b0 = (1 + cos) / 2 / a0, b1 = -(1 + cos) / a0, b2 = (1 + cos) / 2 / a0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0;
        var output = new float[samples.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            double x = samples[i];
            var y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            output[i] = (float)y;
        }
        return output;
    }

    internal static double[] BlockEnergies(float[] samples, int rate)
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
        return energy;
    }

    private static double? FindOnset(float[] samples, int rate, double previousOnset)
    {
        var block = Math.Max(1, rate / 50);
        var energy = BlockEnergies(HighPass(samples, rate), rate);
        // Background = mean of the 2nd–4th quietest 20 ms blocks of the preceding 300 ms:
        // unaffected by a transient (or a longer sound) that ended shortly before,
        // and not fooled by a single dropout block.
        const int preceding = 15, quantileLow = 1, quantileHigh = 4, peakSpan = 8, decaySpan = 32;
        var sorted = new double[preceding];
        for (var frame = preceding; frame + 5 < energy.Length; frame++)
        {
            var onset = frame * block / (double)rate;
            if (onset - previousOnset < RefractorySeconds) continue;
            Array.Copy(energy, frame - preceding, sorted, 0, preceding);
            Array.Sort(sorted);
            double background = 0;
            for (var i = quantileLow; i < quantileHigh; i++) background += sorted[i];
            background /= quantileHigh - quantileLow;
            var threshold = Math.Max(.00012, background * 1.7);
            // A rising edge: this block clears the threshold and the previous one did not.
            if (energy[frame] < threshold || energy[frame - 1] >= threshold) continue;
            // Item sounds reach their loudest point within ~160 ms; a later, louder
            // sound must not decide this event's decay.
            var peak = energy[frame];
            for (var i = frame + 1; i < Math.Min(energy.Length, frame + peakSpan); i++) peak = Math.Max(peak, energy[i]);
            if (peak < Math.Max(.0002, background * 2.2) ||
                energy.Skip(frame).Take(5).Count(value => value > threshold) < 2) continue;
            // A short event falls back toward the background; capture start-up,
            // music or a continuous drone does not. When the window ends before the
            // decay, a later overlapping window confirms the same rising edge.
            var decayLevel = background + (peak - background) * .45;
            var decayed = false;
            for (var i = frame + 2; i + 1 < Math.Min(energy.Length, frame + decaySpan); i++)
                if (energy[i] <= decayLevel && energy[i + 1] <= decayLevel) { decayed = true; break; }
            if (!decayed) continue;
            return onset;
        }
        return null;
    }
}
