namespace Listener.App;

internal sealed class HistoryWindow : Window
{
    private readonly RecognitionHistory history;
    private readonly ListBox records = new() { Background = Theme.Panel, Foreground = Theme.Text,
        BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly CandidatePanel details;
    private readonly TextBlock count = Theme.Label("", 12, Theme.Muted);
    private readonly TextBlock detailTitle = Theme.Label("", 15);
    private readonly TextBlock detailNote = Theme.Label("", 11, Theme.Gold);
    private bool refreshing;
    internal IReadOnlyList<long> VisibleRecordIds => records.Items.Cast<ListBoxItem>().Select(row => ((RecognitionEntry)row.Tag).Id).ToArray();

    public HistoryWindow(RecognitionHistory history, string libraryRoot, Settings settings, Action<RecognitionEntry>? compare = null)
    {
        this.history = history;
        BrandAssets.StyleWindow(this);
        Title = "识别历史 · 本次运行"; Width = Math.Min(1060, SystemParameters.WorkArea.Width);
        WindowPlacement.UseContentHeight(this); ResizeMode = ResizeMode.CanMinimize;
        Background = Theme.Background; Foreground = Theme.Text; FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // The same cards as the overlay. Previews stay in the overlay, which pauses
        // sampling before it plays anything.
        details = new(libraryRoot, new Settings { ShowNames = true, ThumbnailSize = settings.ThumbnailSize, FontScale = settings.FontScale },
            interactive: true, compact: true, preview: false);
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Theme.Label("识别历史", 24));
        header.Children.Add(Theme.Label("保留本次运行最近 100 条。相邻重复声音会合并；拿起后 1 秒内的其他音效标为疑似放下声，不替换浮窗；退出软件后清空。", 12, Theme.Muted));
        var actions = new DockPanel();
        if (compare is not null)
        {
            var button = Theme.Button("在浮窗中对比", (_, _) => { if ((records.SelectedItem as ListBoxItem)?.Tag is RecognitionEntry entry) compare(entry); });
            DockPanel.SetDock(button, Dock.Right); actions.Children.Add(button);
        }
        var clear = Theme.Button("清空历史", (_, _) => history.ClearHistory());
        DockPanel.SetDock(clear, Dock.Right); actions.Children.Add(clear); actions.Children.Add(count); header.Children.Add(actions);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        body.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(body);
        records.Margin = new Thickness(0, 0, 14, 0);
        ScrollViewer.SetHorizontalScrollBarVisibility(records, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(records, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(records, true);
        body.Children.Add(records);
        var detail = new DockPanel();
        var detailHeader = new StackPanel { Margin = new Thickness(2, 0, 0, 8) };
        detailTitle.Margin = new Thickness(0, 0, 0, 2); detailTitle.FontWeight = FontWeights.SemiBold;
        detailNote.Margin = new Thickness(0);
        detailHeader.Children.Add(detailTitle); detailHeader.Children.Add(detailNote);
        DockPanel.SetDock(detailHeader, Dock.Top); detail.Children.Add(detailHeader); detail.Children.Add(details);
        Grid.SetColumn(detail, 1); body.Children.Add(detail);
        records.SelectionChanged += (_, _) => { if (!refreshing) ShowSelection(); };
        history.Changed += Refresh;
        Closed += (_, _) => history.Changed -= Refresh;
        Refresh();
    }
    // "4 格 大红 3/6 · 6 格 大红 1/5": what the player reads first, per size group.
    internal static string SizeSummary(RecognitionResult result) => string.Join(" · ", result.Candidates
        .GroupBy(c => c.Item.Id).Select(g => g.First().Item)
        .GroupBy(item => item.Cells).OrderBy(g => g.Key)
        .Select(g => $"{(g.Key == 0 ? "格数待核实" : $"{g.Key} 格")} 大红 {g.Count(item => item.IsGold)}/{g.Count()}"));
    private void Refresh()
    {
        var selected = (records.SelectedItem as ListBoxItem)?.Tag as RecognitionEntry;
        refreshing = true;
        records.Items.Clear(); ListBoxItem? restore = null;
        foreach (var entry in history.Entries)
        {
            var content = new StackPanel { Margin = new Thickness(8) };
            var heading = new DockPanel();
            if (entry.FollowUp)
            {
                var tag = new Border { Background = Theme.Raised, BorderBrush = Theme.Gold, BorderThickness = new Thickness(1),
                    Padding = new Thickness(5, 0, 5, 1), VerticalAlignment = VerticalAlignment.Center,
                    Child = Theme.Label("疑似放下声", 10, Theme.Gold), ToolTip = "拿起结果后 1 秒内出现的其他音效，多为同一件物品的放下声；未替换当时的浮窗结果" };
                ((TextBlock)tag.Child).Margin = new Thickness(0);
                DockPanel.SetDock(tag, Dock.Right); heading.Children.Add(tag);
            }
            heading.Children.Add(Theme.Label($"{entry.LastSeen:HH:mm:ss} · {entry.Source}", 13));
            content.Children.Add(heading);
            content.Children.Add(Theme.Label(SizeSummary(entry.Result) + (entry.Matches > 1 ? $" · 合并 {entry.Matches} 次" : ""), 11,
                entry.Result.GoldCount > 0 ? Theme.Collectible : Theme.Muted));
            var best = entry.Result.BestMatch;
            content.Children.Add(Theme.Label((best is null ? "" : $"最低{CandidatePanel.DifferenceText(best.Score)} · ") +
                string.Join("、", entry.Result.Candidates.Take(3).Select(c => c.Item.Name)) +
                (entry.Result.CandidateCount > 3 ? "等" : ""), 11, Theme.Muted));
            foreach (var label in content.Children.OfType<TextBlock>().Concat(heading.Children.OfType<TextBlock>()))
            { label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; }
            var row = new ListBoxItem { Content = content, Tag = entry, Height = 86, Opacity = entry.FollowUp ? .72 : 1 };
            records.Items.Add(row);
            if (entry.Id == selected?.Id) restore = row;
        }
        count.Text = $"{history.Entries.Count} / {RecognitionHistory.Capacity} 条 · 点击记录查看全部候选";
        if (restore is not null) records.SelectedItem = restore;
        else if (records.Items.Count > 0) records.SelectedIndex = 0;
        refreshing = false; ShowSelection();
    }
    private void ShowSelection()
    {
        var entry = (records.SelectedItem as ListBoxItem)?.Tag as RecognitionEntry;
        if (entry?.LibraryRoot is { Length: > 0 } root) details.SetLibraryRoot(root);
        detailTitle.Text = entry is null ? "本次运行尚无历史记录"
            : $"{entry.LastSeen:yyyy-MM-dd HH:mm:ss} · {entry.Source}" + (entry.Matches > 1 ? $" · 合并 {entry.Matches} 次" : "");
        detailNote.Text = entry?.FollowUp == true ? "疑似放下声：出现在上一次拿起结果之后 1 秒内，未替换当时的浮窗结果。" : "";
        detailNote.Visibility = detailNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        details.ShowResult(entry is null
            ? new(0, RecognitionStatus.Unknown, true, [], 0, "识别成功后会保存在这里")
            : entry.Result with { Message = $"{entry.Source} · 完整候选" });
    }
}
