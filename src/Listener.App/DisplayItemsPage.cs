using System.Windows.Automation;

namespace Listener.App;

// 展示物品: which catalogue items the overlay shows, laid out the way a player meets them — one
// sound, several possible items — and inside each sound by 红, 金, 紫, 蓝. Every click saves at
// once; hidden items still count in a grid size's 大红概率.
internal sealed class DisplayItemsPage : StackPanel
{
    private static readonly (string Tier, Brush Accent)[] Tiers =
        [("红", Theme.Hot), ("金", Theme.Gold), ("紫", Theme.Brush("#B9A3E3")), ("蓝", Theme.Brush("#7FB4E8"))];
    private static Brush Accent(string? tier) => Tiers.FirstOrDefault(entry => entry.Tier == tier).Accent ?? Theme.Muted;

    private SoundLibrary? library;
    private string root = "";
    private IReadOnlyList<SoundClass> classes = [];
    private readonly TextBlock summary = Theme.Label("", 22);
    private readonly WrapPanel tierChips = new() { Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBox search = new() { Width = 220, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0) };
    private readonly TextBlock searchHint = Theme.Label("搜索物品名称", 12, Theme.Faint);
    private readonly TextBlock saveState = Theme.Label("", 10, Theme.Faint);
    private readonly StackPanel classList = new();
    private readonly HashSet<string> expanded = [];
    private readonly List<Action> chipRefreshers = [], refreshers = [];
    private DisplayFilter Filter => DisplayPreferences.Filter;
    private string Query => search.Text.Trim();

    public int ClassCount => classes.Count;
    public int TileCount => classes.Sum(sound => sound.Items.Count);
    // The page's own controls above the class list: rarity chips and presets.
    public IEnumerable<Button> HeaderButtons => tierChips.Children.OfType<Button>().Concat(presets);
    private readonly List<Button> presets = [];

    public DisplayItemsPage()
    {
        var header = new StackPanel();
        summary.FontWeight = FontWeights.SemiBold; summary.Margin = new Thickness(0);
        header.Children.Add(summary);
        var explain = Theme.Label("取消勾选的物品不再出现在浮窗里，但仍计入每格的大红概率。设置保存在本机用户目录，下载的每个版本都通用；默认全部显示。", 11, Theme.Muted);
        explain.Margin = new Thickness(0, 2, 0, 10); header.Children.Add(explain);
        header.Children.Add(Theme.Label("按稀有度", 10, Theme.Muted));
        header.Children.Add(tierChips);
        var tools = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        foreach (var (label, keep) in new (string, Func<ItemDefinition, bool>)[]
                 { ("全部显示", _ => true), ("只看大红", item => item.IsGold), ("红 + 金", item => item.RarityRank <= 1) })
        {
            var preset = Theme.Button(label, (_, _) => Filter.ShowOnly(AllItems.Select(item => item.Id), id => keep(Item(id))));
            preset.Padding = new Thickness(12, 6, 12, 6); preset.FontSize = 12; preset.Margin = new Thickness(0, 0, 8, 0);
            preset.ToolTip = label == "全部显示" ? "恢复默认：所有物品都在浮窗显示" : $"只在浮窗显示{label.Replace("只看", "")}物品，其余隐藏";
            DockPanel.SetDock(preset, Dock.Left); tools.Children.Add(preset); presets.Add(preset);
        }
        var searchBox = new Grid { VerticalAlignment = VerticalAlignment.Center };
        search.Background = Theme.Background; search.Foreground = Theme.Text; search.BorderBrush = Theme.Line; search.CaretBrush = Theme.Text;
        AutomationProperties.SetName(search, "搜索物品名称");
        searchHint.Margin = new Thickness(10, 0, 0, 0); searchHint.VerticalAlignment = VerticalAlignment.Center; searchHint.IsHitTestVisible = false;
        searchBox.Children.Add(search); searchBox.Children.Add(searchHint);
        search.TextChanged += (_, _) => { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Refresh(); };
        DockPanel.SetDock(searchBox, Dock.Right); tools.Children.Add(searchBox);
        header.Children.Add(tools);
        saveState.Margin = new Thickness(0, 8, 0, 0); header.Children.Add(saveState);
        var headerCard = Theme.Box(header, 18); headerCard.Margin = new Thickness(0, 0, 0, 12); Children.Add(headerCard);
        Children.Add(classList);
        DisplayPreferences.Changed += () => Dispatcher.BeginInvoke(Refresh);
    }

    private ItemDefinition Item(string id) => library!.Items.First(item => item.Id == id);
    private IEnumerable<ItemDefinition> AllItems => classes.SelectMany(sound => sound.Items).DistinctBy(item => item.Id);
    private bool Matches(ItemDefinition item) => Query.Length == 0 || item.Name.Contains(Query, StringComparison.OrdinalIgnoreCase);

    public void Show(SoundLibrary shownLibrary, string libraryRoot)
    {
        if (ReferenceEquals(shownLibrary, library) && libraryRoot == root) { Refresh(); return; }
        library = shownLibrary; root = libraryRoot;
        classes = SoundClasses.Build(library, ReferenceAudio.Load(root));
        expanded.Clear(); if (classes.Count > 0) expanded.Add(classes[0].Id);
        BuildTierChips(); BuildClasses(); Refresh();
    }

    private void BuildTierChips()
    {
        tierChips.Children.Clear(); chipRefreshers.Clear();
        foreach (var (tier, accent) in Tiers)
        {
            var items = AllItems.Where(item => item.Rarity == tier).ToArray();
            if (items.Length == 0) continue;
            var count = Theme.Label("", 12, Theme.Text); count.Margin = new Thickness(0); count.VerticalAlignment = VerticalAlignment.Center;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Width = 10, Height = 10, Background = accent, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            var name = Theme.Label(tier, 13, accent); name.Margin = new Thickness(0, 0, 8, 0); name.FontWeight = FontWeights.SemiBold;
            name.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(name); row.Children.Add(count);
            var chip = Theme.Button("", (_, _) => Filter.Set(items.Select(item => item.Id), !items.All(Filter.IsShown)));
            chip.Content = row; chip.Padding = new Thickness(12, 6, 12, 6); chip.Margin = new Thickness(0, 4, 8, 0);
            AutomationProperties.SetName(chip, $"{tier}色物品全部显示或隐藏");
            tierChips.Children.Add(chip);
            chipRefreshers.Add(() =>
            {
                var shown = items.Count(Filter.IsShown);
                count.Text = $"{shown}/{items.Length}";
                chip.BorderBrush = shown == items.Length ? accent : shown == 0 ? Theme.Line : Theme.Faint;
                chip.Opacity = shown == 0 ? .55 : 1;
                chip.ToolTip = shown == items.Length ? $"点击隐藏全部 {items.Length} 件{tier}色物品" : $"点击显示全部 {items.Length} 件{tier}色物品";
            });
        }
    }

    private void BuildClasses()
    {
        classList.Children.Clear(); refreshers.Clear();
        foreach (var sound in classes) classList.Children.Add(new ClassView(this, sound).Card);
    }

    public void Refresh()
    {
        if (library is null) return;
        var all = AllItems.ToArray();
        var shown = all.Count(Filter.IsShown);
        summary.Text = $"显示 {shown} / {all.Length} 件";
        saveState.Text = DisplayPreferences.LastSaveError is { } error ? "保存失败：" + error : "已自动保存 · " + DisplayPreferences.FilePath;
        saveState.Foreground = DisplayPreferences.LastSaveError is null ? Theme.Faint : Theme.Alert;
        foreach (var refresh in chipRefreshers) refresh();
        foreach (var refresh in refreshers) refresh();
    }

    // One sound class: a header that summarises and toggles the class, and tiers of item tiles.
    private sealed class ClassView
    {
        private readonly DisplayItemsPage page;
        private readonly SoundClass sound;
        public Border Card { get; }
        private readonly StackPanel body = new() { Margin = new Thickness(0, 8, 0, 0) };
        private readonly CheckBox all = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        private readonly TextBlock chevron = Theme.Glyph("\uE76C", 11);
        private readonly TextBlock count = Theme.Label("", 11, Theme.Muted);
        private readonly StackPanel tierSummary = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        public ClassView(DisplayItemsPage page, SoundClass sound)
        {
            this.page = page; this.sound = sound;
            var header = new DockPanel { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            chevron.Margin = new Thickness(0, 0, 10, 0); chevron.Foreground = Theme.Muted; chevron.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(chevron, Dock.Left); header.Children.Add(chevron);
            all.ToolTip = "整类显示或隐藏"; AutomationProperties.SetName(all, $"{sound.Name}整类显示或隐藏");
            all.Click += (_, _) => page.Filter.Set(sound.Items.Select(item => item.Id), !sound.Items.All(page.Filter.IsShown));
            DockPanel.SetDock(all, Dock.Left); header.Children.Add(all);
            count.Margin = new Thickness(14, 0, 0, 0); count.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(count, Dock.Right); header.Children.Add(count);
            DockPanel.SetDock(tierSummary, Dock.Right); header.Children.Add(tierSummary);
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = Theme.Label(sound.Name, 14); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0);
            title.Children.Add(name);
            // A few names tell the player which sound this is, whatever the class is called.
            var examples = sound.Items.Count == 1 ? "" : " · " + string.Join("、", sound.Items.Take(3).Select(item => item.Name)) + (sound.Items.Count > 3 ? " 等" : "");
            var subtitle = Theme.Label(sound.Items.Count == 1 ? "独立声音" : $"同一声音 · {sound.Items.Count} 件{examples}", 10, Theme.Faint);
            subtitle.Margin = new Thickness(0, 1, 0, 0); title.Children.Add(subtitle);
            header.Children.Add(title);
            header.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && IsInside(source, all)) return;
                if (!page.expanded.Remove(sound.Id)) page.expanded.Add(sound.Id);
                page.Refresh();
            };
            var stack = new StackPanel(); stack.Children.Add(header); stack.Children.Add(body);
            Card = new Border { Child = stack, Background = Theme.Panel, BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetName(Card, sound.Name);
            // The header counts each rarity across the class; the body follows the overlay: one
            // section per grid size, and inside it one row per rarity.
            foreach (var (tier, accent) in Tiers.Append(("", Theme.Muted)))
            {
                var items = sound.Items.Where(item => InTier(item, tier)).ToArray();
                if (items.Length > 0) AddTierSummary(tier, accent, items);
            }
            foreach (var size in sound.Items.GroupBy(item => item.Cells).OrderBy(group => group.Key))
                body.Children.Add(CellSection(size.Key, size.ToArray()));
            page.refreshers.Add(Refresh);
        }

        private static bool InTier(ItemDefinition item, string tier) => tier.Length == 0 ? !Tiers.Any(t => t.Tier == item.Rarity) : item.Rarity == tier;

        private FrameworkElement CellSection(int cells, ItemDefinition[] items)
        {
            var section = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var title = Theme.Label(cells == 0 ? "格数待核实" : $"{cells} 格", 13); title.Margin = new Thickness(0); title.FontWeight = FontWeights.Bold;
            title.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(title, Dock.Left); heading.Children.Add(title);
            var amount = Theme.Label("", 11, Theme.Muted); amount.Margin = new Thickness(8, 0, 0, 0); amount.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(amount, Dock.Left); heading.Children.Add(amount);
            var toggle = Theme.Button("", (_, _) => page.Filter.Set(items.Select(item => item.Id), !items.All(page.Filter.IsShown)));
            toggle.Padding = new Thickness(8, 2, 8, 2); toggle.FontSize = 10; toggle.Margin = new Thickness(10, 0, 0, 0); toggle.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(toggle, $"{cells} 格整格显示或隐藏");
            DockPanel.SetDock(toggle, Dock.Left); heading.Children.Add(toggle);
            heading.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            section.Children.Add(heading);
            var rows = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            foreach (var (tier, accent) in Tiers.Append(("", Theme.Muted)))
            {
                var tierItems = items.Where(item => InTier(item, tier)).ToArray();
                if (tierItems.Length > 0) rows.Children.Add(TierRow(tier, accent, tierItems));
            }
            section.Children.Add(rows);
            page.refreshers.Add(() =>
            {
                var shown = items.Count(page.Filter.IsShown);
                amount.Text = $"{items.Length} 件 · 显示 {shown}";
                toggle.Content = shown == items.Length ? "整格隐藏" : "整格显示";
                section.Visibility = items.Any(page.Matches) ? Visibility.Visible : Visibility.Collapsed;
            });
            return section;
        }

        private static bool IsInside(DependencyObject source, DependencyObject container)
        {
            for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
                if (ReferenceEquals(node, container)) return true;
            return false;
        }

        private void AddTierSummary(string tier, Brush accent, ItemDefinition[] items)
        {
            var label = Theme.Label("", 11, accent); label.Margin = new Thickness(10, 0, 0, 0); label.FontWeight = FontWeights.SemiBold;
            tierSummary.Children.Add(label);
            page.refreshers.Add(() => { label.Text = $"{(tier.Length == 0 ? "其他" : tier)} {items.Count(page.Filter.IsShown)}/{items.Length}"; label.Opacity = items.Any(page.Filter.IsShown) ? 1 : .45; });
        }

        private FrameworkElement TierRow(string tier, Brush accent, ItemDefinition[] items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var side = new StackPanel { Width = 72, Margin = new Thickness(0, 6, 8, 0) };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new Border { Width = 8, Height = 8, Background = accent, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            var name = Theme.Label($"{(tier.Length == 0 ? "其他" : tier)} · {items.Length}", 12, accent); name.Margin = new Thickness(0); name.FontWeight = FontWeights.SemiBold;
            title.Children.Add(name); side.Children.Add(title);
            var toggle = Theme.Button("", (_, _) => page.Filter.Set(items.Select(item => item.Id), !items.All(page.Filter.IsShown)));
            toggle.Padding = new Thickness(6, 2, 6, 2); toggle.FontSize = 10; toggle.Margin = new Thickness(0, 5, 0, 0); toggle.HorizontalAlignment = HorizontalAlignment.Left;
            side.Children.Add(toggle);
            DockPanel.SetDock(side, Dock.Left); row.Children.Add(side);
            var tiles = new WrapPanel();
            foreach (var item in items) tiles.Children.Add(Tile(item, accent));
            row.Children.Add(tiles);
            AutomationProperties.SetName(toggle, $"{(tier.Length == 0 ? "其他" : tier)}色这一行显示或隐藏");
            page.refreshers.Add(() =>
            {
                toggle.Content = items.All(page.Filter.IsShown) ? "隐藏" : "显示";
                row.Visibility = items.Any(page.Matches) ? Visibility.Visible : Visibility.Collapsed;
            });
            return row;
        }

        private Button Tile(ItemDefinition item, Brush accent)
        {
            var grid = new Grid { Width = 176, Height = 50 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            var picture = new Grid { Width = 46, Height = 46, Background = Theme.Background, VerticalAlignment = VerticalAlignment.Center };
            picture.Children.Add(ItemPictures.Create(item, page.root, 1, 96, 9));
            grid.Children.Add(picture);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
            var name = Theme.Label(item.Name, 12, item.IsGold ? Theme.Collectible : Theme.Text);
            name.Margin = new Thickness(0); name.FontWeight = FontWeights.SemiBold; name.TextWrapping = TextWrapping.Wrap; name.TextTrimming = TextTrimming.None;
            text.Children.Add(name);
            var size = Theme.Label(item.GridLabel, 10, Theme.Muted); size.Margin = new Thickness(0, 1, 0, 0); text.Children.Add(size);
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            var mark = Theme.Glyph("\uE73E", 13); mark.VerticalAlignment = VerticalAlignment.Center; mark.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(mark, 2); grid.Children.Add(mark);
            var tile = Theme.Button("", (_, _) => page.Filter.Set([item.Id], !page.Filter.IsShown(item)));
            tile.Content = grid; tile.Padding = new Thickness(2); tile.Margin = new Thickness(0, 0, 6, 6); tile.Tag = item;
            tile.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(tile, item.Name);
            page.refreshers.Add(() =>
            {
                var shown = page.Filter.IsShown(item);
                tile.BorderBrush = shown ? accent : Theme.Line;
                tile.Background = shown ? Theme.Raised : Theme.Background;
                tile.Opacity = shown ? 1 : .42;
                mark.Text = shown ? "\uE73E" : "";
                mark.Foreground = accent;
                tile.ToolTip = shown ? $"{item.Name} · 在浮窗显示（点击隐藏）" : $"{item.Name} · 不在浮窗显示（点击恢复）";
                AutomationProperties.SetHelpText(tile, shown ? "显示" : "隐藏");
                tile.Visibility = page.Matches(item) ? Visibility.Visible : Visibility.Collapsed;
            });
            return tile;
        }

        private void Refresh()
        {
            var shown = sound.Items.Count(page.Filter.IsShown);
            all.IsChecked = shown == sound.Items.Count ? true : shown == 0 ? false : null;
            count.Text = $"显示 {shown}/{sound.Items.Count}";
            var searching = page.Query.Length > 0;
            var anyMatch = sound.Items.Any(page.Matches);
            Card.Visibility = anyMatch ? Visibility.Visible : Visibility.Collapsed;
            var open = searching ? anyMatch : page.expanded.Contains(sound.Id);
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = open ? "\uE70D" : "\uE76C";
        }
    }
}
