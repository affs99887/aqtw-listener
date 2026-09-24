using System.Windows.Automation;
using System.Windows.Media.Imaging;

namespace Listener.App;

internal enum StatusTone { Off, Listening, Busy, Paused, Failed }

internal static class Theme
{
    // Keep in step with Theme.xaml and the game's own menus: neutral near-black
    // panels, very dark buttons with a thin teal-grey outline, off-white text.
    public static readonly Brush Background = Brush("#111212"), Panel = Brush("#1B1C1C"), Muted = Brush("#9AA3A1"),
        Text = Brush("#E6ECE7"), Accent = Brush("#C9D6D3"), Gold = Brush("#E7C780"), Line = Brush("#353E3E"),
        Raised = Brush("#222525"), Selected = Brush("#2C3434"), Collectible = Brush("#D17C79"),
        Live = Brush("#5CC98A"), Busy = Brush("#7FB4E8"), Alert = Brush("#E57373"), Faint = Brush("#7A8382"), Hot = Brush("#F0645B");
    public static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    // A size group without red items recedes; a mostly-red group is the brightest thing on the overlay.
    public static Brush RedShare(double share) => share <= 0 ? Faint : share < .5 ? Collectible : Hot;
    public static Brush Tone(StatusTone tone) => tone switch
    {
        StatusTone.Listening => Live, StatusTone.Busy => Busy, StatusTone.Paused => Gold, StatusTone.Failed => Alert, _ => Faint
    };
    public static TextBlock Label(string text, double size = 13, Brush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7) };
    public static Border Box(UIElement child, double padding = 16) => new()
    { Child = child, Padding = new Thickness(padding), Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1) };
    public static Button Button(string text, RoutedEventHandler click, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 4, 8, 4),
            Background = primary ? Accent : Raised, Foreground = primary ? Background : Text, BorderThickness = new Thickness(1), BorderBrush = primary ? Accent : Line,
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal, Cursor = System.Windows.Input.Cursors.Hand };
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
    private readonly TextBlock age = Theme.Label("", 12, Theme.Live);
    private readonly TextBlock title = Theme.Label("行商听音", 18);
    private readonly ContentControl toolbarHost = new();
    private Border? summaryBar;
    private DateTimeOffset? resultAt;
    private readonly CandidateLayout layout;
    private readonly Dictionary<string, BitmapImage> images = new();
    private readonly Dictionary<TextBlock, double> textSizes = new();
    private string libraryRoot;
    private readonly Settings settings;
    private readonly bool compact, minimal, interactive, design, previewButtons;
    private RecognitionResult? lastResult;
    private IReadOnlyList<Candidate> primary = [], near = [];
    private HashSet<string> nearIds = [];
    private bool lastDemo, lastListening, catalogView;
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
    // 100% minus the similarity to the reference that candidate matched: lower is closer.
    internal static string DifferenceText(double score) => "差异率 " + (double.IsFinite(score)
        ? (100 * (1 - Math.Clamp(score, 0, 1))).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "—");
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
        return new Border { Child = text, Background = exact ? Theme.Gold : Theme.Brush("#3E4B4A"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(2), IsHitTestVisible = false };
    }
    private const string DifferenceHelp = "差异率 = 100% − 本次声音与该物品参考音效的相似度，越低越接近；共用同一条参考的物品数值相同。不是单件物品的确定率。";
    private const string NoPlayback = "暂无未经处理的原声：识别用参考经过音量归一化和裁剪，与游戏里听到的不同，因此不用于试听";
    private const string Disclaimer = "大红概率按同格候选计算，不是游戏的真实出货概率。\n" + DifferenceHelp;
    // Live results fade after this long so an earlier pickup is not read as the current one.
    public const double StaleSeconds = 10;
    private bool ShowNames => settings.ShowNames || interactive;
    private double OuterPadding => design ? 0 : compact ? 7 : 16;

    public CandidatePanel(string libraryRoot, Settings settings, bool interactive = true, bool compact = false, bool minimal = false, bool preview = true)
    {
        this.libraryRoot = libraryRoot; this.settings = settings; this.compact = compact; this.minimal = minimal; this.interactive = interactive;
        previewButtons = interactive && preview;
        // The live, click-through overlay and the paused operation view share
        // one candidate layout. Only the latter exposes playback controls.
        design = compact && !minimal;
        layout = new(BuildGroups, () => PreferredThumbnail, () => design ? 80 : 56);
        layout.Changed += () => LayoutChanged?.Invoke();
        Background = Theme.Background; BorderBrush = Theme.Line; BorderThickness = design ? new Thickness(0) : new Thickness(1);
        Padding = new Thickness(OuterPadding);
        var body = new DockPanel(); Child = body;
        if (design)
        {
            BuildListeningChrome(body);
            foreach (var (label, size) in new[] { (ratioLabel, 11d), (ratio, 40d), (count, 14d),
                (headline, 11d), (designCount, 16d), (note, 10d), (activityLabel, 12d), (age, 12d) }) textSizes[label] = size;
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
        // Every fixed band above the candidates covers the game, so the result
        // summary and the actions share one row.
        var row = new DockPanel { LastChildFill = true };
        summaryBar = new Border { Child = row, Height = 40, Background = Theme.Panel, BorderBrush = Theme.Line,
            BorderThickness = new Thickness(0, 0, 0, 1) };
        toolbarHost.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(toolbarHost, Dock.Right); row.Children.Add(toolbarHost);
        var summary = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 8, 0), ClipToBounds = true };
        foreach (var label in new[] { designCount, count, age })
        {
            label.Margin = new Thickness(0, 0, 10, 0); label.VerticalAlignment = VerticalAlignment.Center;
            label.TextWrapping = TextWrapping.NoWrap; summary.Children.Add(label);
        }
        designCount.FontWeight = FontWeights.SemiBold; count.FontWeight = FontWeights.SemiBold;
        age.ToolTip = $"识别到这件货物的时间；超过 {StaleSeconds:0} 秒后候选变暗，避免误当成当前货物";
        var info = Theme.Glyph("\uE946", 13); info.Foreground = Theme.Muted; info.ToolTip = Disclaimer;
        info.Margin = new Thickness(0, 1, 0, 0); ToolTipService.SetInitialShowDelay(info, 0);
        AutomationProperties.SetName(info, Disclaimer); summary.Children.Add(info);
        row.Children.Add(summary);
        DockPanel.SetDock(summaryBar, Dock.Top); body.Children.Add(summaryBar);
        if (!interactive)
        {
            activityLabel.Margin = new Thickness(0);
            activityLabel.FontWeight = FontWeights.SemiBold;
            activityLabel.VerticalAlignment = VerticalAlignment.Center;
            var hint = Theme.Label($"{settings.InteractionHotkey} 打开回放和试听", 11 * FontScale, Theme.Muted);
            hint.Margin = new Thickness(12, 0, 0, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            var activity = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            progress.IsIndeterminate = true;
            progress.VerticalAlignment = VerticalAlignment.Center;
            progress.Margin = new Thickness(0, 0, 10, 0); progress.Width = 70;
            activityLabel.TextWrapping = TextWrapping.NoWrap;
            activity.Children.Add(progress); activity.Children.Add(activityLabel); activity.Children.Add(hint);
            toolbarHost.Content = activity;
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
            if (design) count.Text = activity.Message;
        }
        var failed = activity.Status is RecognitionStatus.Unknown or RecognitionStatus.NoSound or
            RecognitionStatus.Interference or RecognitionStatus.LibraryEmpty or RecognitionStatus.Error;
        activityLabel.Text = failed && !activity.Message.StartsWith("识别失败", StringComparison.Ordinal)
            ? "识别失败 · " + activity.Message : activity.Message;
        activityLabel.Foreground = failed ? Theme.Brush("#FF8B8B") : activity.Busy ? Theme.Accent : Theme.Text;
        activityBanner.Background = failed ? Theme.Brush("#42262B") : activity.Busy ? Theme.Brush("#22302F") : Theme.Panel;
        activityBanner.BorderBrush = failed ? Theme.Brush("#BE6565") : activity.Busy ? Theme.Accent : Theme.Line;
        progress.Visibility = activity.Busy ? Visibility.Visible : Visibility.Collapsed;
    }
    public void SetLibraryRoot(string root) { if (root == libraryRoot) return; libraryRoot = root; images.Clear(); RefreshAppearance(); }
    public void SetToolbar(UIElement? toolbar)
    {
        toolbarHost.Content = toolbar;
        toolbarHost.Margin = design ? new Thickness(0, 0, 6, 0) : toolbar is null ? new Thickness(0) : new Thickness(0, 0, 0, 6);
        LayoutChanged?.Invoke();
    }
    // Live results only: show how long ago the pickup was heard, and fade the
    // candidates once that is long enough to be an earlier item's result.
    public void SetFreshness(DateTimeOffset? heardAt) { resultAt = heardAt; RefreshFreshness(); }
    public void RefreshFreshness()
    {
        if (!design) return;
        if (resultAt is not { } at || primary.Count == 0)
        {
            age.Visibility = Visibility.Collapsed; layout.Opacity = 1; return;
        }
        var seconds = Math.Max(0, (DateTimeOffset.Now - at).TotalSeconds);
        var stale = seconds >= StaleSeconds;
        age.Text = seconds < 3 ? "刚刚" : seconds < 60 ? $"{Math.Floor(seconds):0} 秒前"
            : seconds < 3600 ? $"{Math.Floor(seconds / 60):0} 分钟前" : $"{at.ToLocalTime():HH:mm} 识别";
        age.Foreground = stale ? Theme.Muted : Theme.Live;
        age.Visibility = Visibility.Visible;
        layout.Opacity = stale ? .55 : 1;
    }
    // A new pickup replaced the shown result: pulse the summary row once.
    public void Flash()
    {
        if (summaryBar is null) return;
        var pulse = new SolidColorBrush(Color.FromRgb(0x2F, 0x55, 0x49));
        summaryBar.Background = pulse;
        pulse.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(
            ((SolidColorBrush)Theme.Panel).Color, TimeSpan.FromMilliseconds(900))
        { EasingFunction = new System.Windows.Media.Animation.QuadraticEase() });
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
        var catalog = catalogView = demo && result is { CandidateCount: > 0 } && result.Candidates.All(candidate => candidate.GroupId == "catalog");
        ratioLabel.Text = catalog ? "目录物品数量" : "候选数量";
        primary = result?.Candidates.GroupBy(c => c.Item.Id).Select(g => g.OrderByDescending(c => c.Score).First()).ToArray() ?? [];
        note.Text = "大红概率按同格候选计算，非真实出货概率。";
        value.Text = ""; phase.Text = ""; value.Visibility = Visibility.Collapsed;
        // The live summary row carries status text only; each card shows its own 差异率.
        count.Foreground = design ? Theme.Muted : Theme.Text;
        count.Visibility = design && primary.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (primary.Count == 0)
        {
            ratio.Text = "0"; count.Text = "等待识别"; total.Text = "";
            designCount.Text = "暂无候选";
            headline.Text = result?.Message ?? (listening ? "正在监听物品声音" : "开启监听后，拖动一件行商货物");
            if (design) count.Text = headline.Text;
        }
        else
        {
            ratio.Text = primary.Count.ToString();
            designCount.Text = catalog ? $"{primary.Count} 件目录物品" : $"{primary.Count} 件候选";
            count.Text = design ? "" : catalog ? $"{primary.Count} 件目录物品" : $"{primary.Count} 件候选";
            headline.Text = design ? "按格数查看大红概率"
                : catalog ? "新增目录物品需补拾取音效后才能参与识别" : demo ? "布局演示 · 非识别结果" : result!.Message;
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
            summaryBar!.Height = Math.Round((CandidateCount > 20 ? 36 : 40) * FontScale);
            RefreshFreshness();
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
                Child = Theme.Label("物品声音出现后，在这里查看图片并试听原声", 11 * FontScale, Theme.Muted) };
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
            return new Border { Padding = new Thickness(18), Child = Theme.Label("物品声音出现后，在这里查看候选并试听原声", 12 * FontScale, Theme.Muted) };
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
            group.Key.Cells == 0 ? "格数待核实" : $"{group.Key.Cells} 格", 15 * FontScale);
        title.Margin = new Thickness(0); title.FontWeight = FontWeights.Bold;
        title.TextTrimming = TextTrimming.None; title.VerticalAlignment = VerticalAlignment.Bottom;
        var amount = Theme.Label($"{ordered.Length} 件", 11 * FontScale, Theme.Muted);
        amount.Margin = new Thickness(6, 0, 0, 1); amount.TextTrimming = TextTrimming.None;
        amount.VerticalAlignment = VerticalAlignment.Bottom;
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        titleRow.Children.Add(title); titleRow.Children.Add(amount);
        var heading = new StackPanel { Margin = new Thickness(8, 6, 4, 0) };
        heading.Children.Add(titleRow);
        if (!group.Key.Reference)
        {
            // The grid size is visible on the trader's screen; this share is what
            // the player reads to decide, so it is the largest number in the row.
            var redCount = ordered.Count(candidate => candidate.Item.IsGold);
            var share = redCount / (double)ordered.Length;
            var help = $"{redCount}/{ordered.Length} 件为大红；按该格候选计算，不是游戏的真实出货概率。";
            var caption = Theme.Label($"大红概率 {redCount}/{ordered.Length}", 10 * FontScale, Theme.Muted);
            caption.Margin = new Thickness(0); caption.ToolTip = help; heading.Children.Add(caption);
            var probability = new TextBlock { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None,
                Margin = new Thickness(0, -2, 0, 0), ToolTip = help };
            probability.Inlines.Add(new System.Windows.Documents.Run($"{share:P0}")
                { FontSize = 24 * FontScale, FontWeight = FontWeights.Bold, Foreground = Theme.RedShare(share) });
            AutomationProperties.SetName(probability, $"大红概率 {share:P0}");
            heading.Children.Add(probability);
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
        // Only a lone card has room for a button column beside the name; in
        // pairs the button goes under the name so the name stays on one line.
        var sideButton = wide && rowSize == 1 && previewButtons;
        var grid = new Grid { Height = height - 2 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(thumbnail + 2) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (sideButton) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96 * FontScale) });
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
        // Dense cards have no spare text line: the 差异率 rides on the picture's bottom edge.
        if (!prominent && !catalogView)
        {
            var strip = Theme.Label(DifferenceText(candidate.Score), 9 * FontScale, Theme.Text);
            strip.Margin = new Thickness(0); strip.TextWrapping = TextWrapping.NoWrap; strip.TextAlignment = TextAlignment.Center;
            picture.Children.Add(new Border { Child = strip, Background = Theme.Brush("#CC111212"), VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(2, 0, 2, 1), Margin = new Thickness(2), ToolTip = DifferenceHelp });
        }
        grid.Children.Add(picture);
        var details = new StackPanel { Margin = new Thickness(wide ? 11 : 3, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = wide ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
            MaxWidth = wide ? rowSize == 1 ? 360 : 225 : double.PositiveInfinity };
        // Red names mark red items; the card needs no second marker for that.
        var name = Theme.Label(item.Name, (wide ? rowSize == 1 ? 18 : 15 : prominent ? 13 : 11) * FontScale,
            item.IsGold ? Theme.Collectible : Theme.Text);
        name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0); name.TextWrapping = TextWrapping.Wrap;
        name.TextTrimming = TextTrimming.None; name.ToolTip = item.Name;
        details.Children.Add(name);
        details.Children.Add(new Border { Height = 2 });
        if (prominent)
        {
            // One row with the grid size keeps the card's original line count; it wraps only when narrow.
            var meta = new WrapPanel();
            if (!catalogView)
            {
                var difference = Theme.Label(DifferenceText(candidate.Score), (wide ? rowSize == 1 ? 14 : 12 : 11) * FontScale, Theme.Muted);
                difference.FontWeight = FontWeights.SemiBold; difference.Margin = new Thickness(0, 0, 10, 0); difference.TextWrapping = TextWrapping.Wrap;
                difference.TextTrimming = TextTrimming.None; difference.VerticalAlignment = VerticalAlignment.Center;
                difference.ToolTip = DifferenceHelp; meta.Children.Add(difference);
            }
            var dimensions = Theme.Label(item.GridLabel, 10 * FontScale, Theme.Muted);
            dimensions.Margin = new Thickness(0); dimensions.VerticalAlignment = VerticalAlignment.Center; meta.Children.Add(dimensions);
            details.Children.Add(meta);
        }
        else
        {
            var parts = new[] { CandidateStatus(candidate), item.GridLabel }.Where(part => part.Length > 0);
            var meta = Theme.Label(string.Join(" · ", parts), 9 * FontScale, Theme.Accent);
            meta.Margin = new Thickness(0); meta.TextWrapping = TextWrapping.Wrap; meta.TextTrimming = TextTrimming.None;
            details.Children.Add(meta);
        }
        if (previewButtons)
        {
            var play = Theme.Button("▶ 听样本", (_, _) => { playReference?.Invoke(candidate); ReferenceRequested?.Invoke(candidate); });
            play.Height = sideButton ? 30 : wide ? 26 : 20; play.Padding = new Thickness(0);
            play.Margin = sideButton ? new Thickness(0, 0, 8, 0) : wide ? new Thickness(0, 6, 0, 0) : new Thickness(0);
            if (wide && !sideButton) { play.Width = 96 * FontScale; play.HorizontalAlignment = HorizontalAlignment.Left; }
            play.FontSize = (wide ? 10 : 9) * FontScale;
            play.Tag = candidate; play.IsEnabled = referenceAvailable?.Invoke(candidate) ?? (playReference is not null || ReferenceRequested is not null);
            play.ToolTip = play.IsEnabled ? $"试听 {item.Name} 的原声（未经处理的游戏录音）" : NoPlayback;
            play.ContextMenu = referenceChoices?.Invoke(candidate); ToolTipService.SetShowOnDisabled(play, true);
            AutomationProperties.SetName(play, $"试听{item.Name}的原声");
            if (sideButton) { Grid.SetColumn(play, 2); grid.Children.Add(play); }
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
        var match = Theme.Label(DifferenceText(candidate.Score), 12 * FontScale, Theme.Gold);
        match.FontWeight = FontWeights.SemiBold;
        match.Margin = new Thickness(0); match.TextWrapping = TextWrapping.NoWrap;
        match.ToolTip = DifferenceHelp;
        dimensions.Margin = new Thickness(2, 0, 2, 1);
        match.Margin = new Thickness(2, 0, 2, 3);
        stack.Children.Add(dimensions); stack.Children.Add(match);
        Button? play = null;
        if (interactive)
        {
            play = Theme.Button("▶  听样本", (_, _) => { playReference?.Invoke(candidate); ReferenceRequested?.Invoke(candidate); });
            play.Height = 32; play.Padding = new Thickness(0); play.Margin = new Thickness(0); play.FontSize = 11 * FontScale;
            play.Tag = candidate; play.IsEnabled = referenceAvailable?.Invoke(candidate) ?? (playReference is not null || ReferenceRequested is not null);
            play.ToolTip = play.IsEnabled ? $"试听 {item.Name} 的原声（未经处理的游戏录音）" : NoPlayback;
            play.ContextMenu = referenceChoices?.Invoke(candidate);
            ToolTipService.SetShowOnDisabled(play, true); AutomationProperties.SetName(play, $"试听{item.Name}的原声");
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
            var compactMeta = Theme.Label($"{DifferenceText(candidate.Score)} · {item.GridLabel}",
                9 * FontScale, Theme.Accent);
            compactMeta.Margin = new Thickness(0, 0, 0, 2); compactMeta.TextWrapping = TextWrapping.NoWrap;
            compactMeta.TextTrimming = TextTrimming.CharacterEllipsis; compactMeta.ToolTip = DifferenceHelp;
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
        var metadata = new DockPanel { Margin = new Thickness(0, 3, 0, previewButtons ? 4 : 0) };
        var dimensions = Theme.Label(item.GridLabel, 10 * FontScale, Theme.Muted);
        dimensions.Margin = new Thickness(0); dimensions.TextWrapping = TextWrapping.NoWrap;
        DockPanel.SetDock(dimensions, Dock.Left); metadata.Children.Add(dimensions);
        // Below 48 DIP the grid row moves into a picture-wide column with no room left.
        if (!catalogView && !(previewButtons && thumbnail <= 48))
        {
            // Shares the grid row so dense layouts keep their card height.
            var difference = Theme.Label(DifferenceText(candidate.Score), 10 * FontScale, Theme.Muted);
            difference.Margin = new Thickness(6, 0, 0, 0); difference.TextWrapping = TextWrapping.NoWrap;
            difference.TextTrimming = TextTrimming.CharacterEllipsis; difference.TextAlignment = TextAlignment.Right;
            difference.ToolTip = DifferenceHelp; metadata.Children.Add(difference);
        }
        stack.Children.Add(metadata);
        if (previewButtons)
        {
            var play = Theme.Button("▶ 听样本", (_, _) => { playReference?.Invoke(candidate); ReferenceRequested?.Invoke(candidate); });
            play.Padding = new Thickness(2, 3, 2, 3); play.Margin = new Thickness(0); play.FontSize = 10 * FontScale;
            play.Tag = candidate; play.IsEnabled = referenceAvailable?.Invoke(candidate) ?? (playReference is not null || ReferenceRequested is not null);
            play.ToolTip = play.IsEnabled ? $"试听 {item.Name} 的原声（未经处理的游戏录音）" : NoPlayback;
            play.ContextMenu = referenceChoices?.Invoke(candidate);
            ToolTipService.SetShowOnDisabled(play, true);
            AutomationProperties.SetName(play, $"试听{item.Name}的原声");
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
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 4), Tag = candidate,
            ToolTip = $"{item.Name}\n{item.GridLabel} · {(item.IsGold ? "大红" : "非大红")}\n" +
                (catalogView ? "" : DifferenceText(candidate.Score) + "\n") +
                (item.ReferenceValue.HasValue ? $"联络人回收参考价：{item.ReferenceValue:N0}" : "联络人回收参考价待核实") +
                (highest ? "\n本次候选中已知参考价最高" : "") + (nearIds.Contains(item.Id) ? "\n相近参考音效，不计入候选占比" : "") };
        AutomationProperties.SetName(border, item.Name);
        return border;
    }
}
