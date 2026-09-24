namespace Listener.Core;

public enum SoundPhase { Unknown, Pickup, Putdown }

// Pressing the mouse button on an item picks it up and releasing the button puts
// it down; the game plays that action's sound a frame or two later. A sound is
// attributed to the latest button edge shortly before its onset. Remote sessions
// whose input never reaches this PC leave every sound Unknown, and timing decides.
// Timestamps share AudioTimeline's clock. Call from one thread.
public sealed class MouseEdgeLog
{
    // Game frame plus audio pipeline delay; an input timestamp may also land just
    // after an onset the scanner places a block early.
    public const double MaxDelaySeconds = .35, EarlySeconds = .06;
    private readonly (double At, bool Press)[] edges = new (double, bool)[16];
    private int next, count;
    public void Press(double at) => Add(at, true);
    public void Release(double at) => Add(at, false);
    private void Add(double at, bool press)
    {
        edges[next] = (at, press); next = (next + 1) % edges.Length; count = Math.Min(count + 1, edges.Length);
    }
    public SoundPhase Classify(double onsetSeconds)
    {
        (double At, bool Press)? latest = null;
        for (var i = 0; i < count; i++)
        {
            var edge = edges[i];
            if (edge.At > onsetSeconds + EarlySeconds || edge.At < onsetSeconds - MaxDelaySeconds) continue;
            if (latest is null || edge.At > latest.Value.At) latest = edge;
        }
        return latest is null ? SoundPhase.Unknown : latest.Value.Press ? SoundPhase.Pickup : SoundPhase.Putdown;
    }
}
