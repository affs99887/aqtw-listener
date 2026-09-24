namespace Listener.Core;

public enum MouseEdge { None, Press, Release }

// Hook notifications arrive before Windows updates GetAsyncKeyState. Give that
// state time to settle, and merge both sources so a held drag fires only once.
public sealed class MouseButtonTracker
{
    private readonly ClickLatch latch = new();
    private double lastHook = double.NegativeInfinity;
    public bool HookDown(double now)
    {
        lastHook = now;
        return latch.Down();
    }
    public bool HookUp(double now)
    {
        lastHook = now;
        return latch.Up();
    }
    public bool Poll(bool down, double now) => Sample(down, now) == MouseEdge.Press;
    public MouseEdge Sample(bool down, double now)
    {
        if (now - lastHook < .08) return MouseEdge.None;
        if (down) return latch.Down() ? MouseEdge.Press : MouseEdge.None;
        return latch.Up() ? MouseEdge.Release : MouseEdge.None;
    }
}
