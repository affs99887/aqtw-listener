namespace Listener.App;

// Every candidate stays on one surface. The host grows to the required size;
// images only shrink to a readable floor while the new size is calculated.
internal sealed class CandidateLayout(
    Func<IReadOnlyList<Candidate>, double, double, FrameworkElement> build,
    Func<double> preferredThumbnail,
    Func<double> minimumThumbnail) : Decorator
{
    private IReadOnlyList<Candidate> items = [];
    private Size lastConstraint = new(double.PositiveInfinity, double.PositiveInfinity);
    private bool dirty = true;
    private bool notificationPending;
    public IReadOnlyList<string> RenderedIds { get; private set; } = [];
    public bool NeedsScroll { get; private set; }
    public double ContentHeight { get; private set; }
    public double ThumbnailSize { get; private set; }
    public event Action? Changed;

    public void Refresh() { dirty = true; InvalidateMeasure(); }

    public void SetItems(IReadOnlyList<Candidate> value)
    {
        items = value; Refresh();
    }

    private FrameworkElement MeasureItems(double width, double thumbnail)
    {
        var view = build(items, width, thumbnail);
        // Attach before measuring to inherit the normal WPF styles and fonts.
        Child = view; view.Measure(new Size(width, double.PositiveInfinity));
        return view;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsFinite(constraint.Width) ? Math.Max(1, constraint.Width) : 640;
        var height = double.IsFinite(constraint.Height) ? Math.Max(0, constraint.Height) : double.PositiveInfinity;
        if (dirty || constraint != lastConstraint)
        {
            var preferred = preferredThumbnail();
            var minimum = minimumThumbnail();
            var size = preferred;
            var view = MeasureItems(width, size);
            while (view.DesiredSize.Height > height + .5 && size > minimum)
            {
                size = Math.Max(minimum, size - 8);
                view = MeasureItems(width, size);
            }
            NeedsScroll = view.DesiredSize.Height > height + .5;
            ContentHeight = view.DesiredSize.Height; ThumbnailSize = size;
            RenderedIds = items.Select(c => c.Item.Id).ToArray();
            dirty = false; lastConstraint = constraint;
            if (!notificationPending)
            {
                notificationPending = true;
                _ = Dispatcher.BeginInvoke(new Action(() => { notificationPending = false; Changed?.Invoke(); }));
            }
        }
        Child?.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, Math.Min(height, ContentHeight));
    }

    protected override Size ArrangeOverride(Size size)
    {
        Child?.Arrange(new Rect(0, 0, size.Width, ContentHeight));
        return size;
    }
}
