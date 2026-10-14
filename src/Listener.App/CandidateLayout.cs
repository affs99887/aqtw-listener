namespace Listener.App;

// Every candidate stays on one surface. The host grows to the required size;
// images only shrink to a readable floor while the new size is calculated. When
// the primary arrangement still overflows, an optional denser one is tried.
internal sealed class CandidateLayout(
    Func<IReadOnlyList<Candidate>, double, double, FrameworkElement> build,
    Func<double> preferredThumbnail,
    Func<double> minimumThumbnail,
    Func<IReadOnlyList<Candidate>, double, double, FrameworkElement?>? fallback = null) : Decorator
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

    private FrameworkElement? MeasureItems(Func<IReadOnlyList<Candidate>, double, double, FrameworkElement?> builder, double width, double thumbnail)
    {
        var view = builder(items, width, thumbnail);
        if (view is null) return null;
        // Attach before measuring to inherit the normal WPF styles and fonts.
        Child = view; view.Measure(new Size(width, double.PositiveInfinity));
        return view;
    }

    private (FrameworkElement? View, double Size) Fit(Func<IReadOnlyList<Candidate>, double, double, FrameworkElement?> builder, double width, double height)
    {
        var minimum = minimumThumbnail();
        var size = preferredThumbnail();
        var view = MeasureItems(builder, width, size);
        while (view is not null && view.DesiredSize.Height > height + .5 && size > minimum)
        {
            size = Math.Max(minimum, size - 8);
            view = MeasureItems(builder, width, size);
        }
        return (view, size);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsFinite(constraint.Width) ? Math.Max(1, constraint.Width) : 640;
        var height = double.IsFinite(constraint.Height) ? Math.Max(0, constraint.Height) : double.PositiveInfinity;
        if (dirty || constraint != lastConstraint)
        {
            var (view, size) = Fit(build, width, height);
            if (view!.DesiredSize.Height > height + .5 && fallback is not null)
            {
                var (dense, denseSize) = Fit(fallback, width, height);
                if (dense is not null && dense.DesiredSize.Height <= height + .5) (view, size) = (dense, denseSize);
                else view = MeasureItems(build, width, size)!;
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
