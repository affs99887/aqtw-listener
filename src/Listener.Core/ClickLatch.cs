namespace Listener.Core;

public sealed class ClickLatch
{
    private bool held;
    public bool Down()
    {
        if (held) return false;
        held = true; return true;
    }
    public void Up() => held = false;
}
