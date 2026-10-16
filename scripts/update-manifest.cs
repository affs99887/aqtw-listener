// Writes and checks latest.json, the signed description of the newest release that the
// assistant reads before it falls back to the GitHub API.
//
//   dotnet run scripts/update-manifest.cs -- keygen
//   dotnet run scripts/update-manifest.cs -- write --zip <file> --tag v0.5.5 --name "v0.5.5 · …" --notes <file.md>
//       --page <url> --out <folder> --url <first source> [--url <next source> …]
//   dotnet run scripts/update-manifest.cs -- verify <latest.json>
//
// The private key stays in the user's profile, never in the repository. The public half is
// the ManifestPublicKey constant in src/Listener.App/Updater.cs; verify reads it from there.
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

var root = FindRoot();
var defaultKey = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aqtw-release", "update-signing-key.pem");
if (args.Length == 0) return Fail("expected keygen, write or verify");
var options = Options(args.Skip(1).ToArray());
switch (args[0])
{
    case "keygen":
    {
        var path = One(options, "key") ?? defaultKey;
        if (File.Exists(path)) return Fail($"{path} already exists; delete it first if a new key is really wanted");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
        Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        return 0;
    }
    case "write":
    {
        var zip = Required(options, "zip"); var tag = Required(options, "tag"); var output = Required(options, "out");
        var urls = options.TryGetValue("url", out var list) ? list : [];
        if (urls.Count == 0 || urls.Any(url => !url.StartsWith("https://", StringComparison.Ordinal))) return Fail("give at least one https --url");
        byte[] hash; using (var stream = File.OpenRead(zip)) hash = SHA256.HashData(stream);
        var notes = One(options, "notes") is { } file ? File.ReadAllText(file) : "";
        var body = Json(writer =>
        {
            writer.WriteNumber("schema", 1);
            writer.WriteString("tag", tag);
            writer.WriteString("name", One(options, "name") ?? tag);
            writer.WriteString("notes", notes);
            writer.WriteString("page", One(options, "page") ?? "");
            writer.WriteString("published", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WriteStartObject("package");
            writer.WriteString("name", Path.GetFileName(zip));
            writer.WriteNumber("size", new FileInfo(zip).Length);
            writer.WriteString("sha256", Convert.ToHexString(hash).ToLowerInvariant());
            writer.WriteStartArray("urls"); foreach (var url in urls) writer.WriteStringValue(url); writer.WriteEndArray();
            writer.WriteEndObject();
        });
        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(One(options, "key") ?? defaultKey));
        if (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) != PublicKey())
            return Fail("this private key does not match ManifestPublicKey in Updater.cs");
        Directory.CreateDirectory(output);
        var manifest = Path.Combine(output, "latest.json");
        File.WriteAllBytes(manifest, body);
        File.WriteAllText(manifest + ".sig", Convert.ToBase64String(key.SignData(body, HashAlgorithmName.SHA256)));
        Console.WriteLine(manifest);
        return 0;
    }
    case "verify":
    {
        var manifest = args.Length > 1 ? args[1] : Required(options, "file");
        var body = File.ReadAllBytes(manifest);
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey()), out _);
        if (!key.VerifyData(body, Convert.FromBase64String(File.ReadAllText(manifest + ".sig").Trim()), HashAlgorithmName.SHA256))
            return Fail("signature does not match");
        using var document = JsonDocument.Parse(body);
        var package = document.RootElement.GetProperty("package");
        Console.Write(Encoding.UTF8.GetString(Json(writer =>
        {
            writer.WriteBoolean("passed", true);
            writer.WriteString("tag", document.RootElement.GetProperty("tag").GetString());
            writer.WriteString("name", package.GetProperty("name").GetString());
            writer.WriteNumber("size", package.GetProperty("size").GetInt64());
            writer.WriteString("sha256", package.GetProperty("sha256").GetString());
            writer.WriteStartArray("urls"); foreach (var url in package.GetProperty("urls").EnumerateArray()) writer.WriteStringValue(url.GetString()); writer.WriteEndArray();
        })));
        return 0;
    }
    default: return Fail("unknown command " + args[0]);
}

string PublicKey()
{
    var source = File.ReadAllText(Path.Combine(root, "src", "Listener.App", "Updater.cs"));
    return Regex.Match(source, "ManifestPublicKey\\s*=\\s*\"([A-Za-z0-9+/=]+)\"").Groups[1].Value is { Length: > 0 } value
        ? value : throw new InvalidOperationException("ManifestPublicKey not found in Updater.cs");
}

static byte[] Json(Action<Utf8JsonWriter> fill)
{
    using var buffer = new MemoryStream();
    // LF on every machine: the signature covers the exact bytes, and git may rewrite CRLF on the way to a host.
    using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
    {
        writer.WriteStartObject(); fill(writer); writer.WriteEndObject();
    }
    buffer.Write("\n"u8);
    return buffer.ToArray();
}

static Dictionary<string, List<string>> Options(string[] rest)
{
    var result = new Dictionary<string, List<string>>();
    for (var i = 0; i + 1 < rest.Length; i++)
    {
        if (!rest[i].StartsWith("--")) continue;
        var name = rest[i][2..];
        if (!result.TryGetValue(name, out var values)) result[name] = values = [];
        values.Add(rest[++i]);
    }
    return result;
}

static string? One(Dictionary<string, List<string>> options, string name) => options.TryGetValue(name, out var values) ? values[^1] : null;
static string Required(Dictionary<string, List<string>> options, string name) => One(options, name) ?? throw new ArgumentException("missing --" + name);
static int Fail(string message) { Console.Error.WriteLine(message); return 1; }

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) return dir.FullName;
    throw new InvalidOperationException("run this from the repository");
}
