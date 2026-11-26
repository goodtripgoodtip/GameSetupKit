using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace GameInstaller.Sample;

internal sealed record SampleFixtures(string Root, string StockGame)
{
    internal const string Executable = "GameInstaller.Sample.exe";
    internal const uint AppId = 4200002026;
    internal const string BuildId = "1001";
    internal const string StockSceneFile = "stock-scene.txt";
    internal const string StockSceneText = "The mock game's stock scene is available.\n";
    internal const string OptionalSceneFile = "optional-scene.txt";
    internal const string UserFiles = "UserFiles";
    internal const string OptionalSceneText = "The verified optional scene is ready.\n";
    internal const string ScenePackFile = "bonus-scenes.zip";
    internal const string BonusScene = "Extras/bonus-scene.txt";
    internal const string PreferredProfile = "mock-accelerator.txt";
    internal const string PreferredProfileText = "Mock accelerator profile.\n";
    internal const string SimulateFailure = "simulate-preferred-failure.flag";
    internal const string PauseWork = "pause-second-step.flag";
    internal const string CrashReporter = "DemoCrashReporter.exe";
    internal const string AlternatePack = "alternate-scenes.zip";
    private static readonly string StockPath = Path.Combine("steamapps", "common", "Mock Game");

    internal static SampleFixtures Create(string? sampleRoot, InstallerPackage? package = null, bool mod = true)
    {
        string assembly = Path.Combine(AppContext.BaseDirectory, "GameInstaller.Sample.dll");
        bool bundled = !File.Exists(assembly);
        string app = bundled ? Environment.ProcessPath ?? throw new InvalidOperationException("Could not find the sample executable.")
            : Path.Combine(AppContext.BaseDirectory, Executable);
        string source = bundled ? app : assembly;
        using Stream input = File.OpenRead(source);
        string fingerprint = package?.Fingerprint ?? Convert.ToHexString(SHA256.HashData(input));

        string root = sampleRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameInstallerSample");
        string parent = Path.Combine(root, "Fixtures");
        string fixtures = Path.Combine(parent, fingerprint + (mod ? "-mod" : "-standalone"));
        if (Directory.Exists(fixtures))
            return new SampleFixtures(fixtures, Path.Combine(fixtures, StockPath));

        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            string stock = Path.Combine(stage, StockPath);
            if (mod)
            {
                SeedDemo(app, stock, bundled, package);
                File.WriteAllText(Path.Combine(stock, StockSceneFile), StockSceneText);
                File.WriteAllText(Path.Combine(stock, CrashReporter), "Synthetic executable fixture for the firewall preview.");
            }
            else
                SeedDemo(app, Path.Combine(stage, "BundledGame"), bundled, package);
            string userFiles = Path.Combine(stage, UserFiles);
            Directory.CreateDirectory(userFiles);
            File.WriteAllText(Path.Combine(userFiles, OptionalSceneFile), OptionalSceneText);
            File.WriteAllBytes(Path.Combine(userFiles, AlternatePack), ScenePack(alternate: true));
            File.WriteAllBytes(Path.Combine(stage, ScenePackFile), ScenePack());
            File.WriteAllText(Path.Combine(stage, PreferredProfile), PreferredProfileText);
            File.WriteAllText(Path.Combine(stage, "mod-settings.txt"), "The sample mod writes to the staged game copy.\n");
            string manifest = $"\"AppState\"\n{{\n\"appid\" \"{AppId}\"\n\"installdir\" \"Mock Game\"\n\"buildid\" \"{BuildId}\"\n}}\n";
            if (mod)
                File.WriteAllText(Path.Combine(stage, "steamapps", $"appmanifest_{AppId}.acf"), manifest);
            try { Directory.Move(stage, fixtures); }
            catch (IOException) when (Directory.Exists(fixtures)) { }
        }
        finally
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, recursive: true);
        }
        return new SampleFixtures(fixtures, Path.Combine(fixtures, StockPath));
    }

    internal static byte[] ScenePack(bool alternate = false)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry scene = zip.CreateEntry(BonusScene, CompressionLevel.NoCompression);
            scene.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            scene.ExternalAttributes = 0;
            using Stream content = scene.Open();
            content.Write(Encoding.UTF8.GetBytes(alternate ? "The alternate scene pack is installed.\n" : "The verified bonus scene pack is installed.\n"));
        }
        return stream.ToArray();
    }

    private static void SeedDemo(string app, string folder, bool bundled, InstallerPackage? package)
    {
        Directory.CreateDirectory(folder);
        string demo = Path.Combine(folder, Executable);
        if (package is not null)
            package.CopyLauncherAsync(demo).GetAwaiter().GetResult();
        else
            File.Copy(app, demo);
        string origin = AppContext.BaseDirectory;
        foreach (string notice in new[] { "LICENSE", "HOBBY-TERMS.txt" })
        {
            string file = Path.Combine(origin, notice);
            if (File.Exists(file))
                File.Copy(file, Path.Combine(folder, notice));
        }
        bool looseRuntime = File.Exists(Path.Combine(origin, "hostpolicy.dll"));
        if (bundled && !looseRuntime)
            return;
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GameInstaller.Sample.dll", "GameInstaller.Sample.runtimeconfig.json", "GameInstaller.Sample.deps.json",
            "GameInstaller.Core.dll", "GameInstaller.WinForms.dll"
        };
        foreach (string file in Directory.EnumerateFiles(origin))
        {
            string name = Path.GetFileName(file);
            if (required.Contains(name) || (looseRuntime && Path.GetExtension(name).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
                File.Copy(file, Path.Combine(folder, name));
        }
    }
}
