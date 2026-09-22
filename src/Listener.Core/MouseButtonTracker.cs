namespace Listener.Core;

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
    public void HookUp(double now)
    {
        lastHook = now;
        latch.Up();
    }
    public bool Poll(bool down, double now)
    {
        if (now - lastHook < .08) return false;
        if (down) return latch.Down();
        latch.Up();
        return false;
    }
}
