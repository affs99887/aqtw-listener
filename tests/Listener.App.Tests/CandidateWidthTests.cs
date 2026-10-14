using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Listener.App;
using Listener.Core;

internal static class CandidateWidthTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("候选浮窗每行最多四件，三档字号和两种高度下保持适中比例", CompactWidths);
        yield return ("全部拿起音效类采用实际可用宽高，名称与格数概率保持完整", PickupClasses);
    }

    private static (string Root, SoundLibrary Library) Library()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "library");
        return (root, JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json")));
    }

    // A sound can match multiple overlapping groups; test the entire resulting class.
    private static IEnumerable<Candidate[]> Classes(SoundLibrary library)
    {
        var remaining = library.Groups.Where(group => group.Action == "pickup").ToList();
        while (remaining.Count > 0)
        {
            var ids = remaining[0].ItemIds.ToHashSet();
            for (var grew = true; grew;)
            {
                grew = false;
                foreach (var group in remaining.Where(group => group.ItemIds.Any(ids.Contains)).ToArray())
                {
                    foreach (var id in group.ItemIds) grew |= ids.Add(id);
                    remaining.Remove(group);
                }
            }
            yield return library.Items.Where(item => ids.Contains(item.Id)).Select(item => new Candidate(item, .9, "width-test")).ToArray();
        }
    }

    private static void CompactWidths()
    {
        var (root, library) = Library();
        var six = new[] { "热成像模块", "金豹雕像", "古董茶壶", "激光指示模块", "金狮雕像", "胶囊电视" }
            .Select(name => new Candidate(library.Items.Single(item => item.Name == name), .9, "width-test")).ToArray();
        foreach (var candidates in new[] { six.Take(1).ToArray(), six, PreviewScenarios.Thirteen(library, .9), Classes(library).MaxBy(items => items.Length)! })
        foreach (var font in new[] { .9, 1, 1.15 })
        foreach (var height in new[] { 600d, 880d })
        {
            var panel = Create(root, candidates, font);
            var width = panel.GetPreferredWidth(1200, height);
            Check(width <= 900 * font + 1, $"{candidates.Length} candidates unnecessarily cover {width}px at font {font}");
            if (height == 880 && candidates.Length <= 6)
                Check(width <= 660, "a small result uses unnecessary columns");
            Layout(panel, width, height);
            Verify(panel, candidates);
            var rowCounts = Descendants<Border>(panel).Where(card => card.Tag is Candidate)
                .GroupBy(card => Math.Round(Bounds(card, panel).Top, 1)).Select(row => row.Count()).ToArray();
            Check(rowCounts.Max() <= 4, "a candidate row has more than four items");
            if (height == 880)
            {
                var expectedColumns = candidates.Length == 1 ? 1 : candidates.Length <= 6 ? 2 : candidates.Length <= 13 ? 3 : 4;
                Check(rowCounts.Max() == expectedColumns, $"{candidates.Length} candidates became an unnecessarily narrow strip");
            }
            var toolbar = Descendants<StackPanel>(panel).Single(element => Equals(element.Tag, "width-test-toolbar"));
            var toolbarBounds = Bounds(toolbar, panel);
            var summary = Descendants<TextBlock>(panel).Single(label => label.Text == $"{candidates.Length} 件候选");
            var info = Descendants<TextBlock>(panel).Single(label => label.Text == "\uE946");
            Check(toolbarBounds.Right <= panel.ActualWidth + 1 && Bounds(summary, panel).Left >= 0 &&
                Bounds(info, panel).Right <= toolbarBounds.Left + 1, "toolbar clips the result summary");
            if (height == 880)
            {
                var cards = Descendants<Border>(panel).Where(card => card.Tag is Candidate).ToArray();
                var right = cards.Max(card => Bounds(card, panel).Right);
                var rightGap = panel.ActualWidth - right;
                if (candidates.Length == 1)
                {
                    var first = Bounds(cards[0], panel);
                    Check(Math.Abs(panel.ActualWidth - (first.Left + 2 * first.Width + 3)) <= 3,
                        "minimum width should reserve exactly two cards plus the size label");
                }
                else Check(rightGap <= 5,
                    $"the window retains an unnecessary empty strip ({rightGap:0}px)");
                if (font == 1 && candidates.Length == 13) Check(width >= 700 && width <= 800 && panel.ActualHeight < 650,
                    "thirteen candidates should form a balanced three-column panel");
            }
            var buttons = Descendants<Button>(panel).Where(button => button.Tag is Candidate).ToArray();
            var requested = new List<string>(); panel.ReferenceRequested += candidate => requested.Add(candidate.Item.Id);
            foreach (var button in buttons)
            {
                var candidate = (Candidate)button.Tag;
                Check(Equals(button.Content, "▶") && button.ActualWidth >= 24 * font && button.ActualWidth <= 32 * font &&
                    AutomationProperties.GetName(button).Contains(candidate.Item.Name) && button.ToolTip is not null &&
                    button.ContextMenu?.Items.Count == 1, "compact playback lost its label, target or recording menu");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Check(requested.SequenceEqual(buttons.Select(button => ((Candidate)button.Tag).Item.Id)), "playback targets the wrong candidate");
        }
    }

    private static void PickupClasses()
    {
        var (root, library) = Library();
        foreach (var candidates in Classes(library))
        foreach (var font in new[] { .9, 1, 1.15 })
        foreach (var height in new[] { 600d, 880d })
        {
            var panel = Create(root, candidates, font);
            Layout(panel, panel.GetPreferredWidth(1200, height), height);
            Verify(panel, candidates);
            var labels = Descendants<TextBlock>(panel).ToArray();
            foreach (var group in candidates.GroupBy(candidate => candidate.Item.Cells))
            {
                var expected = $"大红概率 {group.Count(candidate => candidate.Item.IsGold) / (double)group.Count():P0}";
                Check(labels.Any(label => AutomationProperties.GetName(label) == expected ||
                    label.Text == $"{group.Key} 格\n{expected}"), "grid-size probability disappeared: " + expected);
            }
        }
    }

    private static CandidatePanel Create(string root, Candidate[] candidates, double font)
    {
        var panel = new CandidatePanel(root, new Settings { FontScale = font }, interactive: true, compact: true);
        TextElement.SetFontFamily(panel, new FontFamily("Microsoft YaHei UI"));
        panel.SetAudioPreview(_ => { }, _ => true);
        panel.SetReferenceChoices(_ => new ContextMenu { Items = { new MenuItem { Header = "另一段录音" } } });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Tag = "width-test-toolbar" };
        foreach (var caption in new[] { "▶ 回放", "■ 停止", "保存", "录音 ▾", "暂停监听" })
        {
            var button = Theme.Button(caption, (_, _) => { });
            button.Height = 28 * font; button.Padding = new Thickness(9, 0, 9, 0);
            button.Margin = new Thickness(0, 0, 4, 0); button.MinWidth = 44 * font; button.FontSize = 11 * font;
            toolbar.Children.Add(button);
        }
        panel.SetToolbar(toolbar);
        panel.ShowResult(new(1, RecognitionStatus.Matched, true, candidates, 1, "width-test"));
        panel.SetFreshness(DateTimeOffset.Now);
        return panel;
    }

    private static void Verify(CandidatePanel panel, Candidate[] candidates)
    {
        var cards = Descendants<Border>(panel).Where(card => card.Tag is Candidate).ToArray();
        Check(!panel.NeedsScroll && cards.Length == candidates.Length &&
            candidates.Select(candidate => candidate.Item.Id).ToHashSet().SetEquals(cards.Select(card => ((Candidate)card.Tag).Item.Id)),
            "candidate is missing or outside the available height: " + panel.LayoutInfo);
        Check(cards.GroupBy(card => Math.Round(Bounds(card, panel).Top, 1)).All(row => row.Count() <= 4),
            "a candidate row exceeds the four-item limit");
        foreach (var card in cards)
        {
            var item = ((Candidate)card.Tag).Item;
            var bounds = Bounds(card, panel);
            Check(bounds.Right <= panel.ActualWidth + 1 && bounds.Bottom <= panel.ActualHeight + 1, "card is outside the window: " + item.Name);
            var text = Descendants<TextBlock>(card).ToArray();
            Check(text.Any(label => label.Text == item.Name) && text.Any(label => label.Text == "差异率 10.0%"), "name or difference is missing: " + item.Name);
            var name = text.Single(label => label.Text == item.Name);
            Check(name.TextAlignment == TextAlignment.Left && name.HorizontalAlignment == HorizontalAlignment.Left,
                "candidate name is centered: " + item.Name);
            foreach (var element in Descendants<FrameworkElement>(card).Where(element => element is TextBlock or Button))
            {
                var inside = Bounds(element, card);
                Check(inside.Left >= -1 && inside.Top >= -1 && inside.Right <= card.ActualWidth + 1 && inside.Bottom <= card.ActualHeight + 1,
                    "text/button is outside the card: " + item.Name);
                if (element is not TextBlock label) continue;
                var copy = new TextBlock { Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize,
                    FontWeight = label.FontWeight, TextWrapping = label.TextWrapping };
                copy.Measure(new Size(label.TextWrapping == TextWrapping.NoWrap ? double.PositiveInfinity : label.ActualWidth,
                    double.PositiveInfinity));
                Check(copy.DesiredSize.Width <= label.ActualWidth + 1 && copy.DesiredSize.Height <= label.ActualHeight + 1,
                    "text is cut off: " + item.Name + " / " + label.Text);
            }
        }
    }

    private static void Layout(CandidatePanel panel, double width, double height)
    {
        panel.Measure(new Size(width, height)); panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height)); panel.UpdateLayout();
    }
    private static Rect Bounds(FrameworkElement element, Visual parent) => element.TransformToAncestor(parent).TransformBounds(new Rect(element.RenderSize));
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var value2 in Descendants<T>(child)) yield return value2;
        }
    }
}
