using System.Diagnostics;
using System.Net.Http;
using System.Windows.Automation;

namespace Listener.App;

internal sealed partial class MainWindow
{
    private readonly Border updateBanner = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 14) };
    private readonly TextBlock updateTitle = Theme.Label("", 14);
    private readonly TextBlock updateDetail = Theme.Label("", 11, Theme.Muted);
    private readonly TextBlock updateStatus = Theme.Label("", 11, Theme.Muted);
    private readonly CheckBox autoUpdates = new() { Content = "启动时自动检查更新", Foreground = Theme.Text, Margin = new Thickness(0, 8, 0, 8) };
    private Button? installButton;
    private ReleaseInfo? availableUpdate;
    private bool updating;

    private static bool Checking => Environment.GetCommandLineArgs().Any(arg => arg is "--ui-smoke" or "--performance-smoke");

    // A slim strip above the page: what is new, and one button to install it.
    private Border BuildUpdateBanner()
    {
        var grid = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        installButton = Theme.Button("立即更新", async (_, _) => await InstallUpdate(), true);
        installButton.Padding = new Thickness(14, 6, 14, 6);
        var notes = Theme.Button("更新说明", (_, _) => OpenLink(availableUpdate?.Page ?? Updater.ReleasesPage));
        var skip = Theme.Button("跳过此版本", (_, _) =>
        {
            if (availableUpdate is not null) { settings.SkippedVersion = availableUpdate.Tag; SaveQuietly(); }
            updateBanner.Visibility = Visibility.Collapsed;
        });
        foreach (var button in new[] { notes, skip }) { button.Padding = new Thickness(10, 6, 10, 6); button.FontSize = 11; }
        actions.Children.Add(installButton); actions.Children.Add(notes); actions.Children.Add(skip);
        DockPanel.SetDock(actions, Dock.Right); grid.Children.Add(actions);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        updateTitle.FontWeight = FontWeights.SemiBold; updateTitle.Margin = new Thickness(0);
        updateDetail.Margin = new Thickness(0, 2, 0, 0); updateDetail.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(updateTitle); text.Children.Add(updateDetail);
        grid.Children.Add(text);
        updateBanner.Child = grid; updateBanner.Background = Theme.Raised; updateBanner.BorderBrush = Theme.Gold;
        updateBanner.BorderThickness = new Thickness(1); updateBanner.Padding = new Thickness(14, 10, 10, 10);
        AutomationProperties.SetName(updateBanner, "发现新版本");
        return updateBanner;
    }

    // 诊断 page: version, automatic checks, a manual check, and where the shared data lives.
    private UIElement BuildUpdateSection()
    {
        var box = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        box.Children.Add(Theme.Label("版本与更新", 18));
        box.Children.Add(Theme.Label($"当前版本 v{Updater.Current}", 12, Theme.Text));
        autoUpdates.IsChecked = settings.AutoCheckUpdates;
        autoUpdates.Click += (_, _) => { settings.AutoCheckUpdates = autoUpdates.IsChecked == true; SaveQuietly(); };
        box.Children.Add(autoUpdates);
        var row = new WrapPanel();
        row.Children.Add(Theme.Button("检查更新", async (_, _) => await CheckForUpdates(manual: true)));
        row.Children.Add(Theme.Button("打开下载页", (_, _) => OpenLink(Updater.ReleasesPage)));
        box.Children.Add(row);
        updateStatus.Margin = new Thickness(0, 4, 0, 0); updateStatus.TextWrapping = TextWrapping.Wrap; box.Children.Add(updateStatus);
        box.Children.Add(Theme.Label("数据目录", 13));
        var data = Theme.Label(UserData.Root, 11, Theme.Muted); data.TextWrapping = TextWrapping.Wrap; box.Children.Add(data);
        box.Children.Add(Theme.Label("设置、展示物品、个人音效库与录音都保存在这里，所有下载的版本共用；更新或换新文件夹不会丢失。", 11, Theme.Faint));
        box.Children.Add(Theme.Button("打开数据文件夹", (_, _) => OpenLink(UserData.Root)));
        return box;
    }

    private void StartAutomaticUpdateCheck()
    {
        if (Checking || !settings.AutoCheckUpdates) return;
        if (settings.LastUpdateCheck is { } last && DateTimeOffset.Now - last < TimeSpan.FromHours(6) && availableUpdate is null) return;
        _ = CheckForUpdates(manual: false);
    }

    private async Task CheckForUpdates(bool manual)
    {
        if (updating) return;
        updateStatus.Text = "正在检查更新…"; updateStatus.Foreground = Theme.Muted;
        try
        {
            using var http = Updater.CreateClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(manual ? 30 : 20));
            var latest = await Updater.Latest(http, timeout.Token);
            settings.LastUpdateCheck = DateTimeOffset.Now; SaveQuietly();
            if (latest is null || !Updater.IsNewer(latest.Version, Updater.Current))
            {
                updateStatus.Text = $"已是最新版本 v{Updater.Current}";
                if (manual) updateBanner.Visibility = Visibility.Collapsed;
                return;
            }
            if (!manual && settings.SkippedVersion == latest.Tag) { updateStatus.Text = $"有新版本 {latest.Tag}（已跳过，可在这里手动检查后更新）"; return; }
            ShowUpdate(latest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            updateStatus.Text = "检查更新失败：" + (ex is TaskCanceledException ? "连接更新服务器超时，可稍后重试或打开下载页" : ex.Message);
            updateStatus.Foreground = Theme.Alert;
        }
    }

    private void ShowUpdate(ReleaseInfo latest)
    {
        availableUpdate = latest;
        updateTitle.Text = $"发现新版本 {latest.Tag}";
        var size = latest.Package is { Size: > 0 } package ? $" · {package.Size / 1048576d:0} MB" : "";
        updateDetail.Text = $"{latest.Name}{size} · 当前 v{Updater.Current}。更新会下载并校验后替换本文件夹的程序，设置和个人库不受影响，完成后自动重启。";
        updateBanner.Visibility = Visibility.Visible;
        installButton!.IsEnabled = latest.Package is not null && (latest.Sha256 is not null || latest.Checksums is not null);
        updateStatus.Text = $"发现新版本 {latest.Tag}";
        // Only point it out from the tray when the game is not in front.
        if (!IsVisible && !controller.Foreground && tray is not null)
            tray.ShowBalloonTip(5000, BrandAssets.AppName, $"发现新版本 {latest.Tag}，打开设置即可一键更新。", System.Windows.Forms.ToolTipIcon.Info);
    }

    private async Task InstallUpdate()
    {
        if (availableUpdate is not { } release || updating) return;
        if (!Updater.CanWrite(AppContext.BaseDirectory))
        {
            MessageBox.Show(this, "程序所在的文件夹不能写入，无法自动替换。已为你打开下载页，请下载后解压到新文件夹使用；设置和个人库会自动沿用。", "更新");
            OpenLink(release.Page); return;
        }
        updating = true; installButton!.IsEnabled = false;
        try
        {
            using var http = Updater.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            var progress = new Progress<double>(share => updateDetail.Text = $"正在下载 {share:P0}…");
            var package = await Updater.Prepare(http, release, progress, CancellationToken.None);
            updateDetail.Text = "校验通过，正在退出并安装，完成后会自动重启…";
            Updater.Launch(package);
            await ExitApplication();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            updating = false; installButton.IsEnabled = true;
            updateDetail.Text = "更新失败：" + ex.Message + "。可以重试，或点「更新说明」手动下载。";
        }
    }

    private void SaveQuietly()
    {
        if (Checking) return;
        try { settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void OpenLink(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
