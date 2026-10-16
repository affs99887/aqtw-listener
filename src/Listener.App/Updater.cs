using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Listener.App;

internal sealed record ReleaseAsset(string Name, string Url, long Size);
internal sealed record ReleaseInfo(Version Version, string Tag, string Name, string Notes, string Page, ReleaseAsset? Package, ReleaseAsset? Checksums)
{
    // Set when the release came from a signed latest.json: the package hash and every place it can be fetched from, in order.
    public string? Sha256 { get; init; }
    public IReadOnlyList<string> PackageUrls { get; init; } = [];
}

// Finds the newest release, downloads its Windows package, checks the hash and unpacks it; once
// the assistant has exited, a small script copies it over this folder (local-data kept, the
// bundled library replaced as a whole), rolls back on any failure, and starts the assistant again.
//
// GitHub is often unreachable from mainland China, so the release is first looked up in a signed
// latest.json kept on Gitee (and copied to GitHub Pages). The signature, not the server, is what
// makes it trustworthy: its hash decides what may be installed. The GitHub Releases API with the
// release's SHA256SUMS remains the last resort.
internal static class Updater
{
    public const string Repository = "affs99887/aqtw-listener";
    public const string GiteeRepository = "scape0goat/aqtw-listener";
    public static string ReleasesPage => $"https://gitee.com/{GiteeRepository}/releases";
    public static string GitHubReleasesPage => $"https://github.com/{Repository}/releases/latest";
    public static readonly string[] ManifestUrls =
    [
        $"https://gitee.com/{GiteeRepository}/raw/master/latest.json",
        "https://affs99887.github.io/aqtw-listener/update/latest.json",
    ];
    // ECDSA P-256, SubjectPublicKeyInfo. The private half lives only on the releasing machine (see scripts/update-manifest.cs).
    public const string ManifestPublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+OtniNnT68OmXQbwjyYIwUxrOvjJchXpK87UELasUWBQ7DICeltiVsLzAjpgud37vv+0P7b/VicdIcR2GXgS4g==";
    private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(8);
    // A source that sends nothing for this long is abandoned so the next one can be tried.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    public static string WorkFolder => Path.Combine(Path.GetTempPath(), "AqtwListener-update");

    public static Version Current { get; } = ParseVersion(typeof(Updater).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? new(0, 0);

    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var core = text.Trim().TrimStart('v', 'V').Split('+', '-', ' ')[0];
        return Version.TryParse(core.Contains('.') ? core : core + ".0", out var version) ? version : null;
    }
    // 0.5.2 and 0.5.2.0 are the same release; a missing part counts as zero.
    public static bool IsNewer(Version candidate, Version current) => Normalise(candidate) > Normalise(current);
    private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"AqtwListener-Updater/{Current}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static async Task<ReleaseInfo?> Latest(HttpClient http, CancellationToken cancellation, string publicKey = ManifestPublicKey)
    {
        // Both copies are asked at once; a stale or unreachable one simply loses to the other.
        var signed = await Task.WhenAll(ManifestUrls.Select(url => TryManifest(http, url, publicKey, cancellation)));
        if (signed.OfType<ReleaseInfo>().MaxBy(release => Normalise(release.Version)) is { } newest) return newest;
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellation);
        return ParseRelease(json);
    }

    private static async Task<ReleaseInfo?> TryManifest(HttpClient http, string url, string publicKey, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(ManifestTimeout);
        try
        {
            var body = await http.GetByteArrayAsync(url, limit.Token);
            var signature = await http.GetStringAsync(url + ".sig", limit.Token);
            return ParseManifest(body, signature, publicKey);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or FormatException or JsonException
            or KeyNotFoundException or InvalidOperationException or CryptographicException) { return null; }
    }

    // null unless the signature matches and the package entry is usable.
    public static ReleaseInfo? ParseManifest(byte[] body, string signature, string publicKey = ManifestPublicKey)
    {
        using (var key = ECDsa.Create())
        {
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!key.VerifyData(body, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256)) return null;
        }
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var tag = root.GetProperty("tag").GetString() ?? "";
        if (ParseVersion(tag) is not { } version) return null;
        var package = root.GetProperty("package");
        var sha256 = package.GetProperty("sha256").GetString()?.ToLowerInvariant() ?? "";
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) return null;
        var urls = package.GetProperty("urls").EnumerateArray().Select(url => url.GetString() ?? "")
            .Where(url => url.StartsWith("https://", StringComparison.Ordinal)).ToArray();
        if (urls.Length == 0) return null;
        string Text(JsonElement element, string name, string fallback) =>
            element.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text ? text : fallback;
        var asset = new ReleaseAsset(Text(package, "name", Path.GetFileName(urls[0])), urls[0],
            package.TryGetProperty("size", out var size) ? size.GetInt64() : 0);
        return new(version, tag, Text(root, "name", tag), Text(root, "notes", ""), Text(root, "page", ReleasesPage), asset, null)
            { Sha256 = sha256, PackageUrls = urls };
    }

    public static ReleaseInfo? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (ParseVersion(tag) is not { } version) return null;
        var assets = root.TryGetProperty("assets", out var list)
            ? list.EnumerateArray().Select(asset => new ReleaseAsset(asset.GetProperty("name").GetString() ?? "",
                asset.GetProperty("browser_download_url").GetString() ?? "", asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0)).ToArray()
            : [];
        return new(version, tag, root.TryGetProperty("name", out var name) ? name.GetString() ?? tag : tag,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            root.TryGetProperty("html_url", out var page) ? page.GetString() ?? GitHubReleasesPage : GitHubReleasesPage,
            assets.FirstOrDefault(asset => asset.Name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)),
            assets.FirstOrDefault(asset => asset.Name.StartsWith("SHA256SUMS", StringComparison.OrdinalIgnoreCase)));
    }

    // "<hash>  <file name>" lines as written by sha256sum and the release script.
    public static string? ExpectedHash(string sums, string fileName) => sums.Split('\n')
        .Select(line => line.Trim().Split(' ', 2, StringSplitOptions.TrimEntries))
        .Where(parts => parts.Length == 2 && parts[1].TrimStart('*') == fileName && parts[0].Length == 64)
        .Select(parts => parts[0].ToLowerInvariant()).FirstOrDefault();

    public static bool CanWrite(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".update-probe-{Environment.ProcessId}");
            File.WriteAllText(probe, ""); File.Delete(probe); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // Download, verify and unpack; returns the folder that holds the new program files.
    public static async Task<string> Prepare(HttpClient http, ReleaseInfo release, IProgress<double> progress, CancellationToken cancellation)
    {
        if (release.Package is null) throw new InvalidOperationException("这个版本没有 Windows 便携包。");
        if (release.Sha256 is null && release.Checksums is null) throw new InvalidOperationException("这个版本没有附带校验文件，为安全起见不自动安装。");
        var folder = Path.Combine(WorkFolder, release.Version.ToString());
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        string expected; string[] attempts;
        if (release.Sha256 is { } signed) { expected = signed; attempts = [.. release.PackageUrls]; }
        else
        {
            var sums = await http.GetStringAsync(release.Checksums!.Url, cancellation);
            expected = ExpectedHash(sums, release.Package.Name) ?? throw new InvalidDataException("校验文件里没有这个便携包的记录。");
            attempts = [release.Package.Url];
        }
        var zip = Path.Combine(folder, release.Package.Name);
        Exception? failure = null;
        foreach (var url in attempts)
        {
            try { await Download(http, url, zip, release.Package.Size, progress, cancellation); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException && !cancellation.IsCancellationRequested)
            {
                failure = ex; continue;
            }
            await using var stream = File.OpenRead(zip);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation)).ToLowerInvariant();
            if (actual == expected) { failure = null; break; }
            failure = new InvalidDataException("下载的文件校验不一致。");
        }
        if (failure is not null) throw new InvalidDataException("下载失败，请稍后重试或手动下载。" + failure.Message, failure);
        var unpacked = Path.Combine(folder, "package");
        ZipFile.ExtractToDirectory(zip, unpacked);
        // The package may hold the files directly or inside one folder.
        var top = Directory.GetFiles(unpacked, "*.exe").Length > 0 ? unpacked
            : Directory.GetDirectories(unpacked).SingleOrDefault(dir => Directory.GetFiles(dir, "*.exe").Length > 0)
              ?? throw new InvalidDataException("便携包里找不到程序文件。");
        if (!File.Exists(Path.Combine(top, "library", "library.json"))) throw new InvalidDataException("便携包缺少音效库。");
        return top;
    }

    private static async Task Download(HttpClient http, string url, string destination, long fallbackSize, IProgress<double> progress, CancellationToken cancellation)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stall.CancelAfter(StallTimeout);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? fallbackSize;
        await using var input = await response.Content.ReadAsStreamAsync(stall.Token);
        await using var output = File.Create(destination);
        var buffer = new byte[1 << 16]; long done = 0; int read;
        while ((read = await input.ReadAsync(buffer, stall.Token)) > 0)
        {
            stall.CancelAfter(StallTimeout);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
            done += read; if (total > 0) progress.Report(Math.Min(1, done / (double)total));
        }
    }

    // The program the new package starts with: the same file name as now when it still exists,
    // otherwise the package's own launcher (not the command-line tool or the dump helper).
    public static string Launcher(string package, string currentPath)
    {
        var current = Path.GetFileName(currentPath);
        if (File.Exists(Path.Combine(package, current))) return current;
        return Directory.GetFiles(package, "*.exe").Select(Path.GetFileName)
            .FirstOrDefault(name => name is not null && !name.StartsWith("Listener.Cli", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("createdump.exe", StringComparison.OrdinalIgnoreCase)) ?? current;
    }

    public static string InstallScript(string package, string target, int processId, string launcher, string currentExe, string backup, string log)
    {
        static string Q(string value) => "'" + value.Replace("'", "''") + "'";
        return $$"""
            $ErrorActionPreference = 'Stop'
            $log = {{Q(log)}}; $source = {{Q(package)}}; $target = {{Q(target)}}; $backup = {{Q(backup)}}
            function Note($text) { Add-Content -LiteralPath $log -Value ("[{0}] {1}" -f (Get-Date -Format s), $text) -Encoding UTF8 }
            function Copy-Tree($from, $to, $skip) {
                $arguments = @($from, $to, '/E', '/R:2', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
                if ($skip) { $arguments += @('/XD', (Join-Path $from 'local-data')) }
                & robocopy @arguments | Out-Null
                if ($LASTEXITCODE -ge 8) { throw "robocopy $from -> $to exit $LASTEXITCODE" }
            }
            try {
                Note 'waiting for the assistant to exit'
                $running = Get-Process -Id {{processId}} -ErrorAction SilentlyContinue
                if ($running) { if (-not $running.WaitForExit(90000)) { throw 'the assistant did not exit' } }
                Start-Sleep -Milliseconds 400
                if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
                Copy-Tree $target $backup $true
                Note 'backed up the program files'
                $library = Join-Path $target 'library'
                if (Test-Path -LiteralPath $library) { Remove-Item -LiteralPath $library -Recurse -Force }
                Copy-Tree $source $target $true
                Note 'installed'
                Start-Process -FilePath (Join-Path $target {{Q(launcher)}}) -WorkingDirectory $target
            } catch {
                Note ('failed: ' + $_)
                try {
                    if (Test-Path -LiteralPath $backup) {
                        $library = Join-Path $target 'library'
                        if (Test-Path -LiteralPath $library) { Remove-Item -LiteralPath $library -Recurse -Force }
                        Copy-Tree $backup $target $false
                        Note 'restored the previous version'
                    }
                } catch { Note ('restore failed: ' + $_) }
                Start-Process -FilePath {{Q(currentExe)}} -WorkingDirectory $target
            }
            """;
    }

    public static void Launch(string package)
    {
        var target = AppContext.BaseDirectory.TrimEnd('\\');
        var currentExe = Environment.ProcessPath ?? Path.Combine(target, "AqtwListener.exe");
        var script = Path.Combine(WorkFolder, "install.ps1");
        var text = InstallScript(package, target, Environment.ProcessId, Launcher(package, currentExe), currentExe,
            Path.Combine(WorkFolder, "rollback"), UserData.File("update.log"));
        File.WriteAllText(script, text, new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script }) start.ArgumentList.Add(argument);
        Process.Start(start);
    }
}
