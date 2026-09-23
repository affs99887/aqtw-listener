using System.Windows.Automation;
using System.Windows.Media.Imaging;

namespace Listener.App;

internal static class Theme
{
    public static readonly Brush Background = Brush("#101116"), Panel = Brush("#1A1B21"), Muted = Brush("#A3A8B1"),
        Text = Brush("#E9EAF0"), Accent = Brush("#C2CDD8"), Gold = Brush("#E7C780"), Line = Brush("#3B3D46"),
        Raised = Brush("#252831"), Selected = Brush("#30343D"), Collectible = Brush("#D17C79");
    public static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    public static TextBlock Label(string text, double size = 13, Brush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7) };
    public static Border Box(UIElement child, double padding = 16) => new()
    { Child = child, Padding = new Thickness(padding), Background = Panel, CornerRadius = new CornerRadius(1), BorderBrush = Line, BorderThickness = new Thickness(1) };
    public static Button Button(string text, RoutedEventHandler click, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 4, 8, 4),
            Background = primary ? Accent : Raised, Foreground = primary ? Background : Text, BorderThickness = new Thickness(1), BorderBrush = primary ? Accent : Line,
            FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand };
        button.Click += click; return button;
    }
    public static TextBlock Glyph(string glyph, double size = 18) => new()
    { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, VerticalAlignment = VerticalAlignment.Center };
}

internal sealed class CandidatePanel : Border
{
    private readonly TextBlock status = Theme.Label("监听已关闭", 12, Theme.Accent);
    private readonly TextBlock diagnostics = Theme.Label("", 10, Theme.Muted);
    private readonly TextBlock ratio = Theme.Label("—", 34, Theme.Gold);
    private readonly TextBlock count = Theme.Label("等待识别", 13);
    private readonly TextBlock phase = Theme.Label("", 11, Theme.Muted);
    private readonly TextBlock headline = Theme.Label("到达行商后，按快捷键开启", 12, Theme.Muted);
    private readonly TextBlock activityLabel = Theme.Label("等待声音", 13);
    private readonly Border activityBanner = new() { Background = Theme.Panel, BorderBrush = Theme.Line,
        BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock value = Theme.Label("", 12, Theme.Gold);
    private readonly TextBlock ratioLabel = Theme.Label("候选数量", 11, Theme.Muted);
    private readonly TextBlock total = Theme.Label("", 11, Theme.Muted);
    private readonly TextBlock note = Theme.Label("大红概率按同格候选计算，不代表真实出货概率。", 10, Theme.Muted);
    private readonly TextBlock interactionHint = Theme.Label("", 10, Theme.Muted);
    private readonly TextBlock designCount = Theme.Label("0 件候选", 31);
    private readonly TextBlock title = Theme.Label("行商听音", 18);
    private readonly ContentControl toolbarHost = new();
    private Grid? listeningSummary;
    private readonly CandidateLayout layout;
    private readonly Dictionary<string, BitmapImage> images = new();
    private readonly Dictionary<TextBlock, double> textSizes = new();
    private string libraryRoot;
    private readonly Settings settings;
    private readonly bool compact, minimal, interactive, design;
    private RecognitionResult? lastResult;
    private IReadOnlyList<Candidate> primary = [], near = [];
    private HashSet<string> nearIds = [];
    private bool lastDemo, lastListening;
    private Action<Candidate>? playReference;
    private Func<Candidate, bool>? referenceAvailable;
    private Func<Candidate, ContextMenu?>? referenceChoices;
    private readonly ProgressBar progress = new() { Width = 110, Height = 5, Visibility = Visibility.Collapsed,
        Margin = new Thickness(10, 0, 0, 0), Foreground = Theme.Accent };
    internal string Summary => primary.Count > 0 ? $"{headline.Text} · {primary.Count} 件候选" : headline.Text;
    internal IReadOnlyList<string> VisibleIds => layout.RenderedIds;
    internal IReadOnlyList<string> RenderedIds => layout.RenderedIds;
    internal bool NeedsScroll => layout.NeedsScroll;
    internal double ContentHeight => layout.ContentHeight;
    internal double ThumbnailSize => layout.ThumbnailSize;
    internal int CandidateCount => primary.Count + nearIds.Count;
    internal string LayoutInfo => $"{RenderedIds.Count} 件 · 图片 {ThumbnailSize:0}px · 内容 {ContentHeight:0}px · 滚动 {NeedsScroll}";
    public event Action? LayoutChanged;
    public event Action<Candidate>? ReferenceRequested;
    private double FontScale => double.IsFinite(settings.FontScale) ? Math.Clamp(settings.FontScale, .9, 1.15) : 1;
    private double PreferredThumbnail => design ? CandidateCount > 6 ? 96 : 112
        : double.IsFinite(settings.ThumbnailSize) ? Math.Clamp(settings.ThumbnailSize, 60, 120) : 88;
    private static string MatchPercent(double score) => double.IsFinite(score) ? Math.Clamp(score, 0, 1).ToString("P0") : "—";
    private string CandidateStatus(Candidate candidate) => lastDemo ? "" : nearIds.Contains(candidate.Item.Id)
        ? "相近参考" : candidate.Tag == RecognitionTag.Exact ? "精确识别" :
        candidate.Score < .86 ? "疑似" : "";
    private Border? TagBadge(Candidate candidate)
    {
        var label = CandidateStatus(candidate);
        if (label.Length == 0) return null;
        var exact = candidate.Tag == RecognitionTag.Exact && !nearIds.Contains(candidate.Item.Id);
        var text = Theme.Label(label, 9 * FontScale, exact ? Theme.Background : Theme.Text);
        text.Margin = new Thickness(0); text.FontWeight = FontWeights.Bold;
        return new Border { Child = text, Background = exact ? Theme.Gold : Theme.Brush("#4B5967"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(2), IsHitTestVisible = false };
    }
    private const string MatchHelp = "匹配度是录音与参考音效的相似分数；同一音效组的物品共享分数，不是单件物品的确定率。";
    private bool ShowNames => settings.ShowNames || interactive;
    private double OuterPadding => design ? 0 : compact ? 7 : 16;

    public CandidatePanel(string libraryRoot, Settings settings, bool interactive = true, bool compact = false, bool minimal = false)
    {
        this.libraryRoot = libraryRoot; this.settings = settings; this.compact = compact; this.minimal = minimal; this.interactive = interactive;
        // The live, click-through overlay and the paused operation view share
        // one candidate layout. Only the latter exposes playback controls.
        design = compact && !minimal;
        layout = new(BuildGroups, () => PreferredThumbnail, () => design ? 80 : 56);
        layout.Changed += () => LayoutChanged?.Invoke();
        Background = Theme.Background; BorderBrush = Theme.Line; BorderThickness = design ? new Thickness(0) : new Thickness(1);
        CornerRadius = new CornerRadius(1); Padding = new Thickness(OuterPadding);
        var body = new DockPanel(); Child = body;
        if (design)
        {
            BuildListeningChrome(body);
            foreach (var (label, size) in new[] { (ratioLabel, 11d), (ratio, 40d), (count, 16d),
                (headline, 11d), (designCount, 18d), (note, 10d), (activityLabel, 12d) }) textSizes[label] = size;
            RefreshAppearance();
            return;
        }
        if (!interactive)
        {
            var activityRow = new DockPanel();
            activityLabel.Margin = new Thickness(0); activityLabel.FontWeight = FontWeights.Bold;
            activityLabel.VerticalAlignment = VerticalAlignment.Center;
            progress.IsIndeterminate = true; progress.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(progress, Dock.Right); activityRow.Children.Add(progress);
            activityRow.Children.Add(activityLabel); activityBanner.Child = activityRow;
            DockPanel.SetDock(activityBanner, Dock.Top); body.Children.Add(activityBanner);
        }
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        if (!compact)
        {
            var titleRow = new DockPanel();
            if (interactive) { DockPanel.SetDock(progress, Dock.Right); titleRow.Children.Add(progress); }
            title.FontWeight = FontWeights.Bold; titleRow.Children.Add(title); header.Children.Add(titleRow);
            header.Children.Add(status); header.Children.Add(diagnostics);
            header.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 2, 0, 9) });
        }
        else if (!interactive)
        {
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            title.FontWeight = FontWeights.Bold; title.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(title, Dock.Left); titleRow.Children.Add(title);
            status.Margin = new Thickness(0); status.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(status); header.Children.Add(titleRow);
        }
        var summary = new DockPanel { Margin = new Thickness(0, 0, 0, compact ? 4 : 8) };
        var ratioBlock = new StackPanel { Margin = new Thickness(0, 0, 17, 0) };
        ratioLabel.Margin = new Thickness(0, 0, 0, 0); ratio.Margin = new Thickness(0);
        ratioBlock.Children.Add(ratioLabel); ratioBlock.Children.Add(ratio);
        DockPanel.SetDock(ratioBlock, Dock.Left); summary.Children.Add(ratioBlock);
        var context = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        count.FontWeight = FontWeights.SemiBold; count.Margin = new Thickness(0, 0, 0, 3);
        headline.Margin = new Thickness(0, 0, 0, 2); total.Margin = new Thickness(0);
        context.Children.Add(count); context.Children.Add(headline);
        if (!compact) context.Children.Add(total);
        else { note.Margin = new Thickness(0); context.Children.Add(note); }
        summary.Children.Add(context); header.Children.Add(summary);
        if (!compact) { header.Children.Add(value); header.Children.Add(phase); }
        if (minimal) header.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(toolbarHost, Dock.Top); body.Children.Add(toolbarHost);
        if (!compact && !minimal)
        {
            var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            footer.Children.Add(note);
            footer.Children.Add(Theme.Label("实验音效库 · 阈值和同音关系待实机验证", 10, Theme.Muted));
            DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        }
        else if (compact && !interactive && !minimal)
        {
            interactionHint.Margin = new Thickness(0, 3, 0, 0);
            DockPanel.SetDock(interactionHint, Dock.Bottom); body.Children.Add(interactionHint);
        }
        body.Children.Add(layout);
        foreach (var (label, size) in new[] { (status, 12d), (diagnostics, 10d), (ratio, compact ? 30d : 38d),
            (count, 13d), (phase, 11d), (headline, compact ? 10d : 12d), (value, 12d), (ratioLabel, 10d),
            (total, 11d), (note, 10d), (title, compact ? 14d : 18d), (interactionHint, 10d) }) textSizes[label] = size;
        RefreshAppearance();
    }

    private void BuildListeningChrome(DockPanel body)
    {
        var summary = new Grid { Height = 42, Background = Theme.Background };
        listeningSummary = summary;
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        summary.ColumnDefinitions.Add(new ColumnDefinition());
        summary.SizeChanged += (_, _) =>
        {
            var width = summary.ActualWidth;
            summary.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(width * .25, 135, 210));
        };
        designCount.FontWeight = FontWeights.SemiBold;
        designCount.Margin = new Thickness(18, 0, 0, 0);
        designCount.VerticalAlignment = VerticalAlignment.Center;
        summary.Children.Add(designCount);
        var context = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        count.FontWeight = FontWeights.SemiBold; count.Margin = new Thickness(0);
        headline.Margin = new Thickness(0); context.Children.Add(count); context.Children.Add(headline);
        Grid.SetColumn(context, 1); summary.Children.Add(context);
        DockPanel.SetDock(summary, Dock.Top); body.Children.Add(summary);
        DockPanel.SetDock(toolbarHost, Dock.Top); body.Children.Add(toolbarHost);
        toolbarHost.Height = 52; toolbarHost.Background = Theme.Panel;
        if (!interactive)
        {
            activityLabel.Margin = new Thickness(0);
            activityLabel.FontWeight = FontWeights.SemiBold;
            activityLabel.VerticalAlignment = VerticalAlignment.Center;
            var hint = Theme.Label($"{settings.InteractionHotkey} 打开回放和试听", 11 * FontScale, Theme.Muted);
            hint.Margin = new Thickness(0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel { Margin = new Thickness(14, 0, 14, 0) };
            DockPanel.SetDock(hint, Dock.Right);
            row.Children.Add(hint);
            progress.IsIndeterminate = true;
            progress.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(progress, Dock.Right);
            row.Children.Add(progress);
            row.Children.Add(activityLabel);
            toolbarHost.Content = row;
        }
        body.Children.Add(layout);
    }

    public void SetState(string text) { status.Text = text; status.ToolTip = text; }
    public void SetDiagnostics(string text) { diagnostics.Text = text; if (compact) ToolTip = text; }
    public void SetActivity(RecognitionActivity activity)
    {
        if (!design || primary.Count == 0)
        {
            headline.Text = activity.Message;
            headline.ToolTip = activity.Message;
        }
        var failed = activity.Status is RecognitionStatus.Unknown or RecognitionStatus.NoSound or
            RecognitionStatus.Interference or RecognitionStatus.LibraryEmpty or RecognitionStatus.Error;
        activityLabel.Text = failed && !activity.Message.StartsWith("识别失败", StringComparison.Ordinal)
            ? "识别失败 · " + activity.Message : activity.Message;
        activityLabel.Foreground = failed ? Theme.Brush("#FF8B8B") : activity.Busy ? Theme.Accent : Theme.Text;
        activityBanner.Background = failed ? Theme.Brush("#42262B") : activity.Busy ? Theme.Brush("#263747") : Theme.Panel;
        activityBanner.BorderBrush = failed ? Theme.Brush("#BE6565") : activity.Busy ? Theme.Accent : Theme.Line;
        progress.Visibility = activity.Busy ? Visibility.Visible : Visibility.Collapsed;
    }
    public void SetLibraryRoot(string root) { if (root == libraryRoot) return; libraryRoot = root; images.Clear(); RefreshAppearance(); }
    public void SetToolbar(UIElement? toolbar)
    {
        toolbarHost.Content = toolbar;
        toolbarHost.Margin = design ? new Thickness(0) : toolbar is null ? new Thickness(0) : new Thickness(0, 0, 0, 6);
        LayoutChanged?.Invoke();
    }
    public void SetAudioPreview(Action<Candidate> play, Func<Candidate, bool> available)
    { playReference = play; referenceAvailable = available; layout.Refresh(); }
    public void SetReferenceChoices(Func<Candidate, ContextMenu?> choices)
    { referenceChoices = choices; layout.Refresh(); }
    public void SetNearCandidates(IReadOnlyList<Candidate> candidates)
    { near = candidates; RefreshItems(); }

    public void ShowResult(RecognitionResult? result, bool demo = false, bool listening = false)
    {
        lastResult = result; lastDemo = demo; lastListening = listening;
        var catalog = demo && result is { CandidateCount: > 0 } && result.Candidates.All(candidate => candidate.GroupId == "catalog");
        ratioLabel.Text = catalog ? "目录物品数量" : "候选数量";
        primary = result?.Candidates.GroupBy(c => c.Item.Id).Select(g => g.OrderByDescending(c => c.Score).First()).ToArray() ?? [];
        note.Text = "大红概率按同格候选计算，非真实出货概率。";
        value.Text = ""; phase.Text = ""; value.Visibility = Visibility.Collapsed;
        if (primary.Count == 0)
        {
            ratio.Text = "0"; count.Text = "等待识别"; total.Text = "";
            designCount.Text = "暂无候选";
            headline.Text = result?.Message ?? (listening ? "正在监听物品声音" : "开启监听后，拖动一件行商货物");
        }
        else
        {
            ratio.Text = primary.Count.ToString();
            designCount.Text = catalog ? $"{primary.Count} 件目录物品" : $"{primary.Count} 件候选";
            count.Text = design ? $"最高匹配度 {MatchPercent(primary.Max(candidate => candidate.Score))}"
                : catalog ? $"{primary.Count} 件目录物品" : $"{primary.Count} 件候选";
            headline.Text = design ? "按格数查看大红概率"
                : catalog ? "新增目录物品需补拾取音效后才能参与识别" : demo ? "布局演示 · 非识别结果" : result!.Message;
            if (design) headline.ToolTip = MatchHelp;
            total.Text = "大红概率按各格候选计算";
            phase.Text = demo ? "示例用于检查格数分组和缩略图展示" : $"{(result!.IsFinal ? "稳定结果" : "初步结果")}  ·  {result.ElapsedMilliseconds:0} ms  ·  操作 #{result.OperationId}";
            var highest = result!.HighestValue;
            value.Text = highest is null ? "" : $"已知参考价最高：{highest.Item.Name}  ¥{highest.Item.ReferenceValue:N0}";
            if (highest is not null) value.Visibility = Visibility.Visible;
        }
        RefreshItems();
    }

    private void RefreshItems()
    {
        var primaryIds = primary.Select(c => c.Item.Id).ToHashSet();
        var references = near.Where(c => !primaryIds.Contains(c.Item.Id)).GroupBy(c => c.Item.Id)
            .Select(g => g.OrderByDescending(c => c.Score).First()).ToArray();
        nearIds = references.Select(c => c.Item.Id).ToHashSet();
        if (design)
        {
            var dense = CandidateCount > 20;
            listeningSummary!.Height = 42;
            toolbarHost.Height = dense ? 40 : CandidateCount > 6 ? 44 : 52;
        }
        layout.SetItems(primary.OrderBy(c => c.Item.Cells).ThenByDescending(c => c.Item.IsGold)
            .ThenByDescending(c => c.Item.ReferenceValue).Concat(references.OrderBy(c => c.Item.Cells)).ToArray());
        LayoutChanged?.Invoke();
    }

    // A readable card width sets the upper bound for ordinary recognition
    // results. Those results may use more rows before they occupy more game width.
    public double GetPreferredWidth(double maxWidth, double maxHeight)
    {
        var limit = double.IsFinite(maxWidth) ? Math.Max(1, maxWidth) : 1200;
        if (design)
        {
            var designGroups = primary.Concat(near.Where(c => !primary.Any(p => p.Item.Id == c.Item.Id)))
                .GroupBy(c => (Reference: nearIds.Contains(c.Item.Id), c.Item.Cells)).ToArray();
            var candidateCount = designGroups.Sum(group => group.Count());
            var designWidth = candidateCount > 20 ? 1600 : candidateCount > 12 ? 900 :
                candidateCount > 6 ? 820 : designGroups.Length >= 3 || candidateCount >= 5 ? 738 :
                designGroups.Length == 2 || candidateCount >= 3 ? 660 : 620;
            return Math.Min(limit, designWidth);
        }
        var cardWidth = CardWidth(PreferredThumbnail);
        var groups = primary.Concat(near.Where(c => !primary.Any(p => p.Item.Id == c.Item.Id)))
            .GroupBy(c => (Reference: nearIds.Contains(c.Item.Id), c.Item.Cells));
        var natural = groups.Sum(g => g.Select(c => c.Item.Id).Distinct().Count() * (cardWidth + 4) + 12);
        var minimum = minimal ? 210 : compact ? 380 * FontScale : 430 * FontScale;
        return Math.Min(limit, Math.Max(minimum, natural + 2 * OuterPadding + 2));
    }

    private FrameworkElement BuildGroups(IReadOnlyList<Candidate> candidates, double availableWidth, double thumbnail)
    {
        if (design) return BuildDesignGroups(candidates, availableWidth, thumbnail);
        if (compact && !interactive && candidates.Count > 20)
            return BuildDenseDesignGroups(candidates, availableWidth, thumbnail);
        if (candidates.Count == 0)
        {
            return new Border { Padding = new Thickness(0, compact ? 8 : 20, 0, compact ? 8 : 20),
                Child = Theme.Label("物品声音出现后，在这里查看图片并试听参考音效", 11 * FontScale, Theme.Muted) };
        }
        var rows = new StackPanel();
        var highest = lastResult?.HighestValue;
        var grouped = candidates.GroupBy(c => (Reference: nearIds.Contains(c.Item.Id), c.Item.Cells))
            .OrderBy(g => g.Key.Reference).ThenBy(g => g.Key.Cells).ToArray();
        var cardWidth = CardWidth(thumbnail);
        var baseTileWidth = cardWidth + 4;
        var baseColumns = Math.Max(1, (int)((availableWidth - 12) / baseTileWidth));
        var rowGroups = new List<int>();
        var rowWidth = 0d;
        void AddRow()
        {
            if (rowGroups.Count == 0) return;
            var count = rowGroups.Sum(index => grouped[index].Count());
            // Keep each size group together. Once a row is chosen, distribute
            // its unused width among its cards instead of leaving a dark strip.
            var canStretch = rowGroups.All(index => grouped[index].Count() <= baseColumns);
            var rowCardWidth = canStretch && count > 0
                ? cardWidth + Math.Max(0, Math.Floor((availableWidth - rowWidth - 2) / count))
                : cardWidth;
            var tileWidth = rowCardWidth + 4;
            var columns = Math.Max(1, (int)((availableWidth - 12) / tileWidth));
            var visualRow = new WrapPanel();
            rows.Children.Add(visualRow);
            foreach (var index in rowGroups)
            {
                var group = grouped[index];
                // The surrounding border and gap use twelve DIPs per group.
                var sectionWidth = Math.Max(1, Math.Min(availableWidth - 4, Math.Min(group.Count(), columns) * tileWidth + 8));
                var section = new StackPanel { Width = Math.Max(1, sectionWidth - 6) };
                var box = new Border { Child = section, BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
                    Padding = new Thickness(3, 0, 1, 0), Margin = new Thickness(0, 0, 4, 4) };
                visualRow.Children.Add(box);
                var row = new DockPanel { Margin = new Thickness(3, thumbnail <= 48 ? 2 : 3, 3, thumbnail <= 48 ? 2 : 3) };
                var badge = Theme.Label($"{group.Count()} 件", 10 * FontScale, Theme.Muted);
                badge.Margin = new Thickness(5, 0, 0, 0); badge.TextWrapping = TextWrapping.NoWrap;
                DockPanel.SetDock(badge, Dock.Right); row.Children.Add(badge);
                var label = Theme.Label(group.Key.Cells == 0 ? "格数待核实" :
                    group.Key.Reference ? $"参考 · {group.Key.Cells} 格" : $"{group.Key.Cells} 格", 12 * FontScale);
                label.FontWeight = FontWeights.SemiBold; label.Margin = new Thickness(0); label.TextWrapping = TextWrapping.NoWrap;
                row.Children.Add(label); section.Children.Add(row);
                var wrap = new WrapPanel(); section.Children.Add(wrap);
                foreach (var candidate in group.OrderByDescending(c => c.Item.IsGold).ThenByDescending(c => c.Item.ReferenceValue))
                    wrap.Children.Add(Card(candidate, highest?.Item.Id == candidate.Item.Id,
                        !group.Key.Reference && candidate.Score >= (lastResult?.BestMatch?.Score ?? 1) - .00001, thumbnail,
                        Math.Min(rowCardWidth, Math.Max(1, section.Width - 4))));
            }
            rowGroups.Clear(); rowWidth = 0;
        }
        for (var i = 0; i < grouped.Length; i++)
        {
            var groupWidth = Math.Min(availableWidth, Math.Min(grouped[i].Count(), baseColumns) * baseTileWidth + 12);
            if (rowGroups.Count > 0 && rowWidth + groupWidth > availableWidth + .01) AddRow();
            rowGroups.Add(i); rowWidth += groupWidth;
        }
        AddRow();
        return rows;
    }

    private FrameworkElement BuildDesignGroups(IReadOnlyList<Candidate> candidates, double availableWidth, double thumbnail)
    {
        if (candidates.Count == 0)
            return new Border { Padding = new Thickness(18), Child = Theme.Label("物品声音出现后，在这里查看候选并试听参考音效", 12 * FontScale, Theme.Muted) };
        if (candidates.Count > 20) return BuildDenseDesignGroups(candidates, availableWidth, thumbnail);
        return BuildSectionedDesignGroups(candidates, availableWidth, thumbnail);
    }

    private FrameworkElement BuildSectionedDesignGroups(IReadOnlyList<Candidate> candidates,
        double availableWidth, double thumbnail)
    {
        var groups = candidates.GroupBy(candidate => (Reference: nearIds.Contains(candidate.Item.Id), candidate.Item.Cells))
            .OrderBy(group => group.Key.Reference).ThenBy(group => group.Key.Cells).ToArray();
        var sections = new StackPanel();
        foreach (var group in groups)
            sections.Children.Add(BuildSizeSection(group, availableWidth, thumbnail));
        return sections;
    }

    private FrameworkElement BuildSizeSection(
        IGrouping<(bool Reference, int Cells), Candidate> group, double width, double thumbnail)
    {
        const double labelWidth = 112;
        var cardAreaWidth = Math.Max(1, width - labelWidth - 2);
        var ordered = group.OrderByDescending(candidate => candidate.Item.IsGold)
            .ThenByDescending(candidate => candidate.Item.ReferenceValue).ToArray();
        // Fill the section with balanced rows: five items become 3+2 and nine
        // become 3+3+3, instead of stretching a lone last card across the row.
        var fourImage = Math.Max(80, Math.Min(thumbnail, cardAreaWidth / 4 - 96 * FontScale));
        var maxColumns = cardAreaWidth >= 4 * (fourImage + 96 * FontScale) ? 4
            : cardAreaWidth >= 3 * (thumbnail + 96 * FontScale) ? 3
            : cardAreaWidth >= 2 * (thumbnail + 112 * FontScale) ? 2 : 1;
        var rowCount = (int)Math.Ceiling(ordered.Length / (double)maxColumns);
        var baseCount = ordered.Length / rowCount;
        var extra = ordered.Length % rowCount;
        var cardRows = new StackPanel();
        for (int rowIndex = 0, offset = 0; rowIndex < rowCount; rowIndex++)
        {
            var rowSize = baseCount + (rowIndex < extra ? 1 : 0);
            var rowItems = ordered.Skip(offset).Take(rowSize).ToArray();
            offset += rowSize;
            var cardWidth = cardAreaWidth / rowSize;
            var imageSize = rowSize == 4 ? Math.Max(80, Math.Min(thumbnail, cardWidth - 96 * FontScale))
                : thumbnail >= 96 && rowSize == 1 ? thumbnail + 16 : thumbnail;
            var rowHeight = Math.Max(imageSize + 2, rowSize == 4 ? 96 * FontScale : 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Height = rowHeight };
            foreach (var candidate in rowItems)
                row.Children.Add(DenseDesignCard(candidate, imageSize, cardWidth, rowHeight, rowSize));
            cardRows.Children.Add(row);
        }
        var title = Theme.Label(group.Key.Reference ? $"参考 · {group.Key.Cells} 格" :
            group.Key.Cells == 0 ? "格数待核实" : $"{group.Key.Cells} 格", 14 * FontScale);
        title.Margin = new Thickness(0, 0, 0, 2); title.FontWeight = FontWeights.Bold;
        title.TextTrimming = TextTrimming.None;
        var amount = Theme.Label($"{ordered.Length} 件候选", 10 * FontScale, Theme.Muted);
        amount.Margin = new Thickness(0, 0, 0, 2); amount.TextTrimming = TextTrimming.None;
        var heading = new StackPanel { Margin = new Thickness(8, 5, 4, 0) };
        heading.Children.Add(title); heading.Children.Add(amount);
        if (!group.Key.Reference)
        {
            var redCount = ordered.Count(candidate => candidate.Item.IsGold);
            var probability = Theme.Label($"大红概率 {redCount / (double)ordered.Length:P0}",
                12 * FontScale, Theme.Collectible);
            probability.Margin = new Thickness(0); probability.FontWeight = FontWeights.SemiBold;
            probability.ToolTip = "按该格候选中的大红件数计算；不是游戏的真实出货概率。";
            heading.Children.Add(probability);
            var fraction = Theme.Label($"{redCount}/{ordered.Length} 件大红", 10 * FontScale, Theme.Muted);
            fraction.Margin = new Thickness(0); heading.Children.Add(fraction);
        }
        var label = new Border { Width = labelWidth, Child = heading, Background = Theme.Raised,
            BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0) };
        var grid = new Grid { Width = Math.Max(1, width - 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(label); Grid.SetColumn(cardRows, 1); grid.Children.Add(cardRows);
        return new Border { Child = grid, Width = width, Background = Theme.Panel,
            BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 3) };
    }

    private FrameworkElement BuildDenseDesignGroups(IReadOnlyList<Candidate> candidates, double availableWidth, double thumbnail)
    {
        var usableWidth = Math.Max(1, availableWidth - 4);
        var grouped = candidates.GroupBy(c => (Reference: nearIds.Contains(c.Item.Id), c.Item.Cells))
            .OrderBy(group => group.Key.Reference).ThenBy(group => group.Key.Cells).ToArray();
        var categoryWidth = grouped.Any(group => group.Key.Reference) ? 96d : 82d;
        var targetCardWidth = availableWidth < 1300 ? 128d : 145d;
        var rowHeight = Math.Max(thumbnail + 2, 58);
        var packedRows = new List<List<(Candidate? Item, string? Category)>>();
        var pending = new List<(Candidate? Item, string? Category)>();
        var usedWidth = 0d;
        void AddRow()
        {
            if (pending.Count == 0) return;
            packedRows.Add([.. pending]); pending.Clear(); usedWidth = 0;
        }
        foreach (var group in grouped)
        {
            var entries = group.OrderByDescending(candidate => candidate.Item.IsGold)
                .ThenByDescending(candidate => candidate.Item.ReferenceValue).ToArray();
            if (usedWidth > 0 && usedWidth + categoryWidth + targetCardWidth > usableWidth) AddRow();
            var category = group.Key.Cells == 0 ? "格数待核实" : group.Key.Reference
                ? $"参考 {group.Key.Cells} 格"
                : $"{group.Key.Cells} 格\n大红概率 {entries.Count(candidate => candidate.Item.IsGold) / (double)entries.Length:P0}";
            pending.Add((null, category));
            usedWidth += categoryWidth;
            foreach (var candidate in entries)
            {
                if (pending.Any(entry => entry.Item is not null) && usedWidth + targetCardWidth > usableWidth)
                    AddRow();
                pending.Add((candidate, null)); usedWidth += targetCardWidth;
            }
        }
        AddRow();
        static int ItemCount(List<(Candidate? Item, string? Category)> row) => row.Count(entry => entry.Item is not null);
        var balancedCount = Math.Max(2, candidates.Count / packedRows.Count);
        for (var index = packedRows.Count - 1; index > 0; index--)
        {
            var row = packedRows[index]; var previous = packedRows[index - 1];
            while (ItemCount(row) < balancedCount && ItemCount(previous) > 1)
            {
                var moved = previous[^1]; previous.RemoveAt(previous.Count - 1); row.Insert(0, moved);
                if (previous.Count > 0 && previous[^1].Category is not null)
                {
                    row.Insert(0, previous[^1]); previous.RemoveAt(previous.Count - 1);
                }
            }
        }
        var rows = new StackPanel();
        foreach (var entries in packedRows)
        {
            var categoryCount = entries.Count(entry => entry.Category is not null);
            var cardWidth = Math.Max(1, (usableWidth - categoryCount * categoryWidth) / ItemCount(entries));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Height = rowHeight };
            foreach (var entry in entries)
            {
                if (entry.Category is not null)
                {
                    var category = Theme.Label(entry.Category, 10 * FontScale, Theme.Text);
                    category.Margin = new Thickness(2, 0, 2, 0); category.VerticalAlignment = VerticalAlignment.Center;
                    category.HorizontalAlignment = HorizontalAlignment.Center;
                    category.TextAlignment = TextAlignment.Center; category.FontWeight = FontWeights.SemiBold;
                    row.Children.Add(new Border { Child = category, Width = categoryWidth, Height = rowHeight,
                        Background = Theme.Raised,
                        BorderBrush = Theme.Line, BorderThickness = new Thickness(1) });
                }
                else row.Children.Add(DenseDesignCard(entry.Item!, thumbnail, cardWidth, rowHeight));
            }
            rows.Children.Add(row);
        }
        return rows;
    }

    private UIElement DenseDesignCard(Candidate candidate, double thumbnail, double width, double height, int rowSize = 0)
    {
        var item = candidate.Item;
        var prominent = design && CandidateCount <= 20;
        var wide = prominent && rowSize is 1 or 2;
        var grid = new Grid { Height = height - 2 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(thumbnail + 2) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (wide && interactive)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rowSize == 1 ? 156 : 126) });
        var picture = new Grid { Width = thumbnail, Height = thumbnail, Background = Theme.Background };
        if (item.Thumbnail is not null)
        {
            try
            {
                var path = Path.Combine(libraryRoot, item.Thumbnail);
                if (!images.TryGetValue(path, out var image))
                {
                    image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 180; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); images[path] = image;
                }
                picture.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(2) });
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException)
            { picture.Children.Add(Theme.Label("暂无图片", 9 * FontScale, Theme.Muted)); }
        }
        else picture.Children.Add(Theme.Label("暂无图片", 9 * FontScale, Theme.Muted));
        if (TagBadge(candidate) is { } badge) picture.Children.Add(badge);
        grid.Children.Add(picture);
        var details = new StackPanel { Margin = new Thickness(wide ? 11 : 3, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = wide ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
            MaxWidth = wide ? rowSize == 1 ? 360 : 225 : double.PositiveInfinity };
        var heading = new DockPanel();
        var dot = new System.Windows.Shapes.Ellipse { Fill = item.IsGold ? Theme.Gold : Theme.Muted,
            Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(dot, Dock.Right); heading.Children.Add(dot);
        var name = Theme.Label(item.Name, (wide ? rowSize == 1 ? 18 : 15 : prominent ? 13 : 11) * FontScale,
            item.IsGold ? Theme.Collectible : Theme.Text);
        name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0); name.TextWrapping = TextWrapping.Wrap;
        name.TextTrimming = TextTrimming.None; name.ToolTip = item.Name;
        heading.Children.Add(name); details.Children.Add(heading);
        var match = Theme.Label(prominent ? $"匹配度 {MatchPercent(candidate.Score)}"
            : $"{CandidateStatus(candidate)} · 匹配{MatchPercent(candidate.Score)} · {item.GridLabel}",
            (wide ? rowSize == 1 ? 15 : 13 : prominent ? 12 : 9) * FontScale,
            prominent ? Theme.Gold : Theme.Accent);
        match.FontWeight = FontWeights.SemiBold;
        match.Margin = new Thickness(0); match.TextWrapping = TextWrapping.Wrap;
        match.TextTrimming = TextTrimming.None; match.ToolTip = MatchHelp;
        details.Children.Add(match);
        if (prominent)
        {
            var dimensions = Theme.Label(item.GridLabel, 10 * FontScale, Theme.Muted);
            dimensions.Margin = new Thickness(0); details.Children.Add(dimensions);
        }
        if (interactive)
        {
            var play = Theme.Button("▶ 听样本", (_, _) => { playReference?.Invoke(candidate); ReferenceRequested?.Invoke(candidate); });
            play.Height = wide ? 30 : 20; play.Padding = new Thickness(0);
            play.Margin = wide ? new Thickness(0, 0, 8, 0) : new Thickness(0);
            play.FontSize = (wide ? 10 : 9) * FontScale;
            play.Tag = candidate; play.IsEnabled = referenceAvailable?.Invoke(candidate) ?? (playReference is not null || ReferenceRequested is not null);
            play.ToolTip = play.IsEnabled ? $"试听 {item.Name} 对应的参考音效" : "当前音效库没有可试听的音频样本";
            play.ContextMenu = referenceChoices?.Invoke(candidate); ToolTipService.SetShowOnDisabled(play, true);
            AutomationProperties.SetName(play, $"试听{item.Name}的参考音效");
            if (wide) { Grid.SetColumn(play, 2); grid.Children.Add(play); }
            else details.Children.Add(play);
        }
        Grid.SetColumn(details, 1); grid.Children.Add(details);
        return new Border { Child = grid, Width = width, Height = height, Background = Theme.Panel,
            BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Tag = candidate };
    }

    private UIElement DesignCard(Candidate candidate, double thumbnail, double width)
    {
        var item = candidate.Item;
        var stack = new StackPanel { Margin = new Thickness(5, 4, 5, 4) };
        var heading = new DockPanel { Margin = new Thickness(4, 1, 4, 2) };
        var dot = new System.Windows.Shapes.Ellipse { Fill = item.IsGold ? Theme.Gold : Theme.Muted,
            Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(dot, Dock.Right); heading.Children.Add(dot);
        var name = Theme.Label(item.Name, 14 * FontScale, item.IsGold ? Theme.Collectible : Theme.Text);
        name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0); name.TextTrimming = TextTrimming.None;
        name.TextWrapping = TextWrapping.Wrap; heading.Children.Add(name); stack.Children.Add(heading);
        var picture = new Grid { Height = thumbnail, Background = Theme.Background, Margin = new Thickness(0, 0, 0, 3) };
        if (item.Thumbnail is not null)
        {
            try
            {
                var path = Path.Combine(libraryRoot, item.Thumbnail);
                if (!images.TryGetValue(path, out var image))
                {
                    image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 180; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); images[path] = image;
                }
                picture.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(4) });
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException)
            { picture.Children.Add(Theme.Label("暂无图片", 10 * FontScale, Theme.Muted)); }
        }
        else picture.Children.Add(Theme.Label("暂无图片", 10 * FontScale, Theme.Muted));
        if (TagBadge(candidate) is { } pictureBadge) picture.Children.Add(pictureBadge);
        stack.Children.Add(picture);
        var dimensions = Theme.Label($"◇ {item.GridLabel}", 10 * FontScale, Theme.Muted);
        dimensions.Margin = new Thickness(0);
        var match = Theme.Label($"匹配度 {MatchPercent(candidate.Score)}", 12 * FontScale, Theme.Gold);
        match.FontWeight = FontWeights.SemiBold;
        match.Margin = new Thickness(0); match.TextWrapping = TextWrapping.NoWrap;
        match.ToolTip = MatchHelp;
        dimensions.Margin = new Thickness(2, 0, 2, 1);
        match.Margin = new Thickness(2, 0, 2, 3);
        stack.Children.Add(dimensions); stack.Children.Add(match);
        Button? play = null;
        if (interactive)
        {
            play = Theme.Button("▶  听样本", (_, _) => { playReference?.Invoke(candidate); ReferenceRequested?.Invoke(candidate); });
            play.Height = 32; play.Padding = new Thickness(0); play.Margin = new Thickness(0); play.FontSize = 11 * FontScale;
            play.Tag = candidate; play.IsEnabled = referenceAvailable?.Invoke(candidate) ?? (playReference is not null || ReferenceRequested is not null);
            play.ToolTip = play.IsEnabled ? $"试听 {item.Name} 对应的参考音效" : "当前音效库没有可试听的音频样本";
            play.ContextMenu = referenceChoices?.Invoke(candidate);
            ToolTipService.SetShowOnDisabled(play, true); AutomationProperties.SetName(play, $"试听{item.Name}的参考音效");
            stack.Children.Add(play);
        }
        if (thumbnail <= 48 && play is not null)
        {
            // A narrow game viewport needs the same controls in less height.
            // Keep text readable and place the small picture beside the actions.
            stack.Children.Clear(); stack.Margin = new Thickness(4, 3, 4, 4);
            var dense = new Grid();
            dense.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(thumbnail + 4) });
            dense.ColumnDefinitions.Add(new ColumnDefinition());
            picture.Margin = new Thickness(0, 0, 4, 0); dense.Children.Add(picture);
            var details = new StackPanel();
            heading.Margin = new Thickness(0, 0, 0, 1); details.Children.Add(heading);
            var compactMeta = Theme.Label($"匹配度 {MatchPercent(candidate.Score)} · {item.GridLabel}",
                9 * FontScale, Theme.Accent);
            compactMeta.Margin = new Thickness(0, 0, 0, 2); compactMeta.TextWrapping = TextWrapping.NoWrap;
            compactMeta.TextTrimming = TextTrimming.CharacterEllipsis; compactMeta.ToolTip = MatchHelp;
            details.Children.Add(compactMeta);
            play.Content = "▶ 试听"; play.Height = 26; play.FontSize = 10 * FontScale;
            details.Children.Add(play);
            Grid.SetColumn(details, 1); dense.Children.Add(details); stack.Children.Add(dense);
        }
        return new Border { Child = stack, Width = Math.Max(1, width - 4), Background = Theme.Panel,
            BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), Tag = candidate };
    }

    public void RefreshAppearance()
    {
        LayoutTransform = Transform.Identity;
        interactionHint.Text = $"{settings.InteractionHotkey} · 回放 / 试听 / 字号";
        foreach (var (label, size) in textSizes) label.FontSize = size * FontScale;
        if (design && ActualWidth > 0)
            ratio.FontSize = (ActualWidth < 460 ? 30 : ActualWidth < 600 ? 32 : 40) * FontScale;
        ShowResult(lastResult, lastDemo, lastListening);
    }

    private double CardWidth(double thumbnail)
    {
        // Listening cards without names only need room for the picture and
        // dimensions. Reserving the interaction controls' text width here
        // would stop image compaction from making dense results fit.
        var textWidth = interactive ? 92 : ShowNames ? 72 : 44;
        return Math.Ceiling(Math.Max(textWidth * FontScale, thumbnail + 10));
    }
    private UIElement Card(Candidate candidate, bool highest, bool best, double thumbnail, double width)
    {
        var item = candidate.Item;
        var stack = new StackPanel { Width = Math.Max(1, width - 10), Margin = new Thickness(4, 3, 4, 4) };
        TextBlock? name = null;
        if (ShowNames)
        {
            name = new TextBlock { Text = item.Name, Foreground = item.IsGold ? Theme.Collectible : Theme.Text,
                FontWeight = FontWeights.SemiBold, FontSize = 11 * FontScale, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 3) };
            stack.Children.Add(name);
        }
        var tile = new Grid { Height = thumbnail, Background = Theme.Background };
        if (item.Thumbnail is not null)
        {
            try
            {
                var path = Path.Combine(libraryRoot, item.Thumbnail);
                if (!images.TryGetValue(path, out var image))
                {
                    image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 200; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); images[path] = image;
                }
                tile.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(2) });
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException)
            { tile.Children.Add(Theme.Label("暂无图片", 10 * FontScale, Theme.Muted)); }
        }
        else tile.Children.Add(Theme.Label("暂无图片", 10 * FontScale, Theme.Muted));
        if (TagBadge(candidate) is { } tileBadge) tile.Children.Add(tileBadge);
        stack.Children.Add(tile);
        var metadata = new DockPanel { Margin = new Thickness(0, 3, 0, interactive ? 4 : 0) };
        var dot = new System.Windows.Shapes.Ellipse { Fill = item.IsGold ? Theme.Gold : Theme.Muted,
            Width = 5, Height = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 2, 0) };
        DockPanel.SetDock(dot, Dock.Right); metadata.Children.Add(dot);
        var dimensions = Theme.Label(item.GridLabel, 10 * FontScale, Theme.Muted);
        dimensions.Margin = new Thickness(0); metadata.Children.Add(dimensions); stack.Children.Add(metadata);
        if (interactive)
        {
            var play = Theme.Button("▶  听参考", (_, _) => { playReference?.Invoke(candidate); ReferenceRequested?.Invoke(candidate); });
            play.Padding = new Thickness(2, 3, 2, 3); play.Margin = new Thickness(0); play.FontSize = 10 * FontScale;
            play.Tag = candidate; play.IsEnabled = referenceAvailable?.Invoke(candidate) ?? (playReference is not null || ReferenceRequested is not null);
            play.ToolTip = play.IsEnabled ? $"试听 {item.Name} 对应的参考音效" : "当前音效库没有可试听的音频样本";
            play.ContextMenu = referenceChoices?.Invoke(candidate);
            ToolTipService.SetShowOnDisabled(play, true);
            AutomationProperties.SetName(play, $"试听{item.Name}的参考音效");
            stack.Children.Add(play);
            if (thumbnail <= 48)
            {
                // In a shallow game strip, put the same image and controls side
                // by side before resorting to scrolling. Text never shrinks.
                stack.Children.Clear();
                stack.Margin = new Thickness(4, 2, 4, 2);
                var dense = new Grid();
                var imageWidth = Math.Min(thumbnail, Math.Max(24, width * .31));
                dense.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(imageWidth + 5) });
                dense.ColumnDefinitions.Add(new ColumnDefinition());
                var picture = new StackPanel(); tile.Height = imageWidth;
                picture.Children.Add(tile); picture.Children.Add(metadata); dense.Children.Add(picture);
                var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                if (name is not null) { name.Margin = new Thickness(0, 0, 0, 1); actions.Children.Add(name); }
                play.Content = "▶ 试听"; play.Padding = new Thickness(1, 1, 1, 1); actions.Children.Add(play);
                Grid.SetColumn(actions, 1); dense.Children.Add(actions); stack.Children.Add(dense);
            }
        }
        var border = new Border { Child = stack, Width = width, Background = Theme.Panel,
            BorderBrush = highest ? Theme.Gold : best && !lastDemo ? Theme.Accent : Theme.Line,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 0, 4, 4), Tag = candidate,
            ToolTip = $"{item.Name}\n{item.GridLabel} · {(item.IsGold ? "大红" : "非大红")}\n" +
                (item.ReferenceValue.HasValue ? $"联络人回收参考价：{item.ReferenceValue:N0}" : "联络人回收参考价待核实") +
                (highest ? "\n本次候选中已知参考价最高" : "") + (nearIds.Contains(item.Id) ? "\n相近参考音效，不计入候选占比" : "") };
        AutomationProperties.SetName(border, item.Name);
        return border;
    }
}
