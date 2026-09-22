namespace Listener.App;

// Measure the full result first: short results request only their natural height.
// Pagination is used only when the parent supplies a screen-height limit.
internal sealed class CandidatePages(Func<IReadOnlyList<Candidate>, double, FrameworkElement> build) : Decorator
{
    private IReadOnlyList<Candidate> items = [];
    private readonly List<(int Start, int Count)> pages = [];
    private Size lastConstraint = new(double.PositiveInfinity, double.PositiveInfinity);
    private bool dirty = true;
    private double desiredHeight;
    private int renderedPage = -1;
    public int PageIndex { get; private set; }
    public int PageCount => Math.Max(1, pages.Count);
    public IReadOnlyList<string> VisibleIds => pages.Count == 0 ? [] : items.Skip(pages[PageIndex].Start)
        .Take(pages[PageIndex].Count).Select(c => c.Item.Id).ToArray();
    public bool Fits { get; private set; } = true;
    public event Action? Changed;

    public void SetItems(IReadOnlyList<Candidate> value)
    {
        if (!items.Select(c => c.Item.Id).SequenceEqual(value.Select(c => c.Item.Id))) PageIndex = 0;
        items = value; dirty = true; renderedPage = -1; InvalidateMeasure();
    }
    public void MovePage(int delta)
    {
        var next = Math.Clamp(PageIndex + delta, 0, PageCount - 1);
        if (next == PageIndex) return;
        PageIndex = next; InvalidateMeasure(); Changed?.Invoke();
    }
    private FrameworkElement MeasureItems(int start, int count, double width)
    {
        // Attach before measuring so the normal font and styles are inherited.
        var view = build(items.Skip(start).Take(count).ToArray(), width);
        Child = view; view.Measure(new Size(width, double.PositiveInfinity)); return view;
    }
    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsFinite(constraint.Width) ? Math.Max(1, constraint.Width) : 436;
        if (dirty || constraint != lastConstraint)
        {
            var full = MeasureItems(0, items.Count, width);
            desiredHeight = Math.Min(full.DesiredSize.Height, constraint.Height);
            pages.Clear(); Fits = true;
            if (full.DesiredSize.Height <= constraint.Height + .1 || items.Count == 0)
                pages.Add((0, items.Count));
            else
            {
                var start = 0;
                while (start < items.Count)
                {
                    var low = 1; var high = items.Count - start; var count = 0;
                    while (low <= high)
                    {
                        var middle = (low + high) / 2;
                        var view = MeasureItems(start, middle, width);
                        if (view.DesiredSize.Height <= constraint.Height + .1) { count = middle; low = middle + 1; }
                        else high = middle - 1;
                    }
                    if (count == 0) { count = 1; Fits = false; }
                    pages.Add((start, count)); start += count;
                }
            }
            PageIndex = Math.Min(PageIndex, PageCount - 1);
            dirty = false; lastConstraint = constraint; renderedPage = -1;
            // Keep pagination controls out of this measure pass.
            _ = Dispatcher.BeginInvoke(new Action(() => Changed?.Invoke()));
        }
        if (renderedPage != PageIndex)
        {
            var page = pages[PageIndex];
            if (Fits) MeasureItems(page.Start, page.Count, width);
            else
            {
                Child = Theme.Label("可用空间过小，请放大窗口或在识别历史查看。", 12, Theme.Muted);
                Child.Measure(new Size(width, double.PositiveInfinity));
            }
            renderedPage = PageIndex;
        }
        return new Size(width, Math.Max(0, desiredHeight));
    }
    protected override Size ArrangeOverride(Size size)
    {
        Child?.Arrange(new Rect(0, 0, size.Width, Math.Min(size.Height, Child.DesiredSize.Height)));
        return size;
    }
}
