namespace Listener.Core;

public sealed class ClickLatch
{
    private bool held;
    public bool Down()
    {
        if (held) return false;
        held = true; return true;
    }
    public bool Up()
    {
        var released = held;
        held = false; return released;
    }
}
