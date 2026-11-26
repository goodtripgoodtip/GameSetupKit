using GameInstaller.WinForms;
using System.Text.Json;

namespace GameInstaller.Sample;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.Dark);
        if (args is ["--prepare-demo", string asset, string destination])
            return PrepareDemo(asset, destination);
        if (args is ["--demo-game"])
        {
            Application.Run(CreateDemoGame());
            return 0;
        }

        try
        {
            if (args is ["--package-sample", string bootstrap, string packageOutput])
            {
                SamplePackages.BuildAsync(Path.GetFullPath(bootstrap), packageOutput).GetAwaiter().GetResult();
                return 0;
            }
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The sample executable is unavailable.");
            InstallerPackage? package = Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ? null : InstallerPackage.Open(executable);
            string? product = WindowsInstallerPlatform.GetCommandProductId(args) ?? package?.ProductId;
            bool isMod = product is not SampleRecipes.StandaloneId && !args.Contains("--standalone", StringComparer.Ordinal);
            InstallerDefinition recipe = isMod ? SampleRecipes.Mod(null) : SampleRecipes.Standalone();
            if (WindowsInstallerPlatform.RunWorkerAsync(recipe, args).GetAwaiter().GetResult() is int workerExit)
                return workerExit;
            if (WindowsInstallerPlatform.PrepareUninstallHostAsync(args).GetAwaiter().GetResult())
                return 0;
            _ = WindowsInstallerPlatform.TryGetLaunchDirectory(args, out string? installedDirectory, out bool uninstall);
            string? sampleRoot = Environment.GetEnvironmentVariable("GAMEINSTALLER_SAMPLE_ROOT");
            if (!string.IsNullOrWhiteSpace(sampleRoot))
                sampleRoot = Path.GetFullPath(sampleRoot);
            else
                sampleRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameInstallerSample");
            SampleFixtures fixtures = SampleFixtures.Create(sampleRoot, package, isMod);
            InstallerDefinition definition = isMod ? SampleRecipes.Mod(fixtures.Root) : SampleRecipes.Standalone();
            bool useWindows = Environment.GetEnvironmentVariable("GAMEINSTALLER_SAMPLE_WINDOWS") == "1" || installedDirectory is not null;
            var platform = new SamplePlatform(sampleRoot, definition, package, useWindows);
            using Icon? icon = Icon.ExtractAssociatedIcon(executable);
            var options = new InstallerWindowOptions
            {
                Description = isMod ? "Prepare a private copy of the mock game with optional bonus scenes."
                    : "Install and launch the bundled demo game.",
                DefaultProductDirectory = installedDirectory ?? platform.FindInstallation(definition) ?? Path.Combine(sampleRoot, "Products", definition.Id),
                ManualFileDirectory = Path.Combine(fixtures.Root, SampleFixtures.UserFiles),
                Icon = icon, Platform = platform, Package = package, UninstallOnOpen = uninstall,
                OpenInstalledProduct = installedDirectory is not null && !uninstall
            };
            using var installer = new InstallerForm(definition, fixtures.Root, options);
            Application.Run(installer);
            if (uninstall)
                WindowsInstallerPlatform.CleanupUninstallHost();
            return 0;
        }
        catch (Exception error)
        {
            if (args.Length > 0 && args[0] == "--package-sample")
                Console.Error.WriteLine(error);
            else
                InstallerForm.ShowStartupError("Game Setup Kit Demo", error.Message);
            return 1;
        }
    }

    private static int PrepareDemo(string asset, string destination)
    {
        try
        {
            Console.WriteLine("PROGRESS\t0\t1\tPreparing the optional scene");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, File.ReadAllText(asset));
            Console.WriteLine("PROGRESS\t1\t1\tOptional scene prepared");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static bool? ChooseConfiguration()
    {
        using var selection = new Form
        {
            Text = "Game Installer sample", StartPosition = FormStartPosition.CenterScreen, Font = new Font("Segoe UI", 10),
            ClientSize = new Size(408, 133), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        var choices = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 16, 10) };
        choices.Controls.Add(new Label
        {
            Text = "Choose a demo. It creates sample files in your local application data folder before opening setup.",
            AutoSize = true, MaximumSize = new Size(368, 0), Margin = new Padding(3, 0, 3, 10)
        });
        choices.Controls.Add(new Button { Text = "Standalone game", Size = new Size(165, 38), DialogResult = DialogResult.Yes });
        choices.Controls.Add(new Button { Text = "NeonLoop-style mod", Size = new Size(174, 38), DialogResult = DialogResult.No });
        selection.Controls.Add(choices);
        return selection.ShowDialog() switch { DialogResult.Yes => false, DialogResult.No => true, _ => null };
    }

    private static Form CreateDemoGame()
    {
        string root = AppContext.BaseDirectory;
        string scene = Path.Combine(root, SampleFixtures.StockSceneFile);
        string optional = Path.Combine(root, SampleRecipes.PreparedScene);
        string? saveDirectory = Environment.GetEnvironmentVariable("GAMEINSTALLER_SAMPLE_DATA");
        string? settingsPath = Environment.GetEnvironmentVariable("GAMEINSTALLER_SAMPLE_SETTINGS");
        Dictionary<string, bool> settings = File.Exists(settingsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(settingsPath)) ?? [] : [];
        var window = new Form
        {
            Text = "Installer demo game", StartPosition = FormStartPosition.CenterScreen, Font = new Font("Segoe UI", 11),
            ClientSize = new Size(470, 200)
        };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18) };
        layout.Controls.Add(new Label { Text = File.Exists(scene) ? File.ReadAllText(scene).Trim() : "Standalone game launched.", AutoSize = true });
        string bonus = Path.Combine(root, SampleFixtures.BonusScene);
        if (settings.GetValueOrDefault("showExtras", true))
        {
            if (File.Exists(bonus))
                layout.Controls.Add(new Label { Text = File.ReadAllText(bonus).Trim(), AutoSize = true });
            layout.Controls.Add(new Label { Text = File.Exists(optional) ? File.ReadAllText(optional).Trim() : "Optional scene unavailable.", AutoSize = true });
        }
        var status = new Label { AutoSize = true };
        var save = new Button { Text = "Write sample save", AutoSize = true, Enabled = !string.IsNullOrWhiteSpace(saveDirectory) };
        save.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(saveDirectory!);
                File.WriteAllText(Path.Combine(saveDirectory!, "sample-save.txt"), "Sample player save.");
            status.Text = settings.GetValueOrDefault("rememberSession") ? "Sample save written. You can open it next time." : "Sample save written.";
            }
            catch (Exception error) { status.Text = error.Message; }
        };
        layout.Controls.Add(save);
        layout.Controls.Add(status);
        window.Controls.Add(layout);
        return window;
    }
}
