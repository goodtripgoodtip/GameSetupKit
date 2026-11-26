using System.Security.Cryptography;
using System.Text;

namespace GameInstaller.Sample;

internal static class SampleRecipes
{
    internal const string PreparedScene = "Prepared/optional-scene.txt";
    internal const string PreparedInput = "Prepared/scene-input.txt";
    internal static string Version { get; } = typeof(SampleRecipes).Assembly.GetName().Version!.ToString(3);
    internal const string StandaloneId = "SampleStandalone";
    internal const string ModId = "SamplePrivateMod";

    internal static InstallerDefinition Standalone() => new(StandaloneId, "Game Setup Kit Standalone Demo", Version, InstallMode.Standalone)
    {
        Required = new InstallContent { Payloads = [new InstallerPayload("BundledGame", "", PayloadKind.Directory)] },
        Launch = DemoLaunch(),
        Notice = InstallerNotice.HobbyRelease,
        NetworkDisclosure = "The sample installer and demo game work offline.",
        Managed = new ManagedInstaller("Good Trip Good Tip") { Subtitle = "A standalone setup example" }
    };

    internal static InstallerDefinition Mod(string? fixtureDirectory)
    {
        byte[] optional = Encoding.UTF8.GetBytes(SampleFixtures.OptionalSceneText);
        byte[] pack = SampleFixtures.ScenePack();
        byte[] profile = Encoding.UTF8.GetBytes(SampleFixtures.PreferredProfileText);
        return new InstallerDefinition(ModId, "Game Setup Kit Demo", Version, InstallMode.PrivateGameCopy)
        {
            Source = new GameSource(SampleFixtures.Executable)
            {
                DisplayName = "the built-in demo game",
                SteamAppId = SampleFixtures.AppId,
                SteamDirectory = Environment.GetEnvironmentVariable("GAMEINSTALLER_SAMPLE_MANUAL_STEAM") == "1" ? null : fixtureDirectory,
                CompatibilityNotice = "The demo includes a simulated Steam library. You can try it without installing Steam. Tested on Windows 11.",
                SupportedBuildIds = [SampleFixtures.BuildId],
                RequiredFiles = [SampleFixtures.StockSceneFile],
                FileHashes = new Dictionary<string, string>
                {
                    [SampleFixtures.StockSceneFile] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SampleFixtures.StockSceneText)))
                }
            },
            Required = new InstallContent
            {
                Payloads = [new InstallerPayload("mod-settings.txt", "mod-settings.txt")]
            },
            Components =
            [
                new InstallerComponent("bonus-scenes", "Install bonus scene pack", new InstallContent
                {
                    Assets = [Pinned("bonus-zip", SampleFixtures.ScenePackFile, pack) with { LocalFiles = [SampleFixtures.ScenePackFile] }],
                    Payloads = [new InstallerPayload("", "", PayloadKind.Zip) { AssetId = "bonus-zip" }]
                }) { Description = "Verify and extract the recommended demo ZIP.", AllowSkip = true, GroupId = "scene-packs" },
                new InstallerComponent("alternate-scenes", "Install alternate scene pack", new InstallContent
                {
                    Assets = [Pinned("alternate-zip", SampleFixtures.AlternatePack, SampleFixtures.ScenePack(alternate: true))],
                    Payloads = [new InstallerPayload("", "", PayloadKind.Zip) { AssetId = "alternate-zip" }]
                }) { Description = "Use a different verified ZIP.", Recommended = false, AllowSkip = true, GroupId = "scene-packs" },
                new InstallerComponent("prepared-scene", "Prepare optional scene", new InstallContent
                {
                    Assets = [Pinned("scene", SampleFixtures.OptionalSceneFile, optional)],
                    Steps =
                    [
                        new InstallerStep("Prepare the optional scene", PrepareSceneAsync)
                        {
                            Description = "Run the sample worker to create Prepared/optional-scene.txt from the verified scene file.",
                            CheckpointFiles = [PreparedInput, PreparedScene],
                            Preferred = new InstallerPreferredStep("simulated preferred worker",
                                "Check one mock accelerator profile file in the sample folder. The demo does not inspect actual hardware.",
                                token => HasPreferredProfile(fixtureDirectory, token), PreparePreferredAsync)
                            {
                                Assets = [Pinned("preferred-profile", SampleFixtures.PreferredProfile, profile) with { LocalFiles = [SampleFixtures.PreferredProfile] }]
                            }
                        }
                    ]
                }) { Description = "Select optional-scene.txt when prompted. A separate worker prepares the scene.", AllowSkip = true }
            ],
            ComponentGroups = [new InstallerComponentGroup("scene-packs", "Scene pack", Exclusive: true)],
            Managed = new ManagedInstaller("Good Trip Good Tip")
            {
                Subtitle = "Try a Windows game and mod installer",
                AllowLimitedPlay = true,
                AllowProgramFiles = true,
                PrivateNetworkPrograms = [SampleFixtures.Executable, SampleFixtures.CrashReporter]
            },
            Settings =
            [
                new InstallerSetting("showExtras", "Presentation", "Show installed scenes when the demo opens"),
                new InstallerSetting("rememberSession", "Saves", "Show a reminder after saving", Recommended: false)
            ],
            Launch = DemoLaunch(),
            Notice = InstallerNotice.HobbyRelease,
            NetworkDisclosure = "The sample uses mock files and works offline. These settings apply to installer downloads."
        };
    }

    private static LaunchDefinition DemoLaunch() => new(SampleFixtures.Executable)
    {
        Arguments = ["--demo-game"],
        Environment = new Dictionary<string, string>
        {
            ["GAMEINSTALLER_SAMPLE_DATA"] = "{data}",
            ["GAMEINSTALLER_SAMPLE_SETTINGS"] = "{settings}"
        }
    };

    private static async Task PrepareSceneAsync(InstallerStepContext context, CancellationToken token)
    {
        await context.RunCheckpointAsync("scene-input", [PreparedInput], ct => context.RunToolAsync(context.GetWorkingPath(SampleFixtures.Executable),
            ["--prepare-demo", context.GetAsset("scene"), context.GetWorkingPath(PreparedInput)], ct), token);
        context.ReportProgress("Preparing the second scene checkpoint");
        if (File.Exists(Path.Combine(context.PackageDirectory, SampleFixtures.PauseWork)) ||
            Environment.GetEnvironmentVariable("GAMEINSTALLER_SAMPLE_SLOW_WORK") == "1")
            await Task.Delay(TimeSpan.FromSeconds(10), token);
        await context.RunToolAsync(context.GetWorkingPath(SampleFixtures.Executable),
            ["--prepare-demo", context.GetWorkingPath(PreparedInput), context.GetWorkingPath(PreparedScene)], token);
    }

    internal static InstallerAsset Pinned(string id, string file, byte[] content) =>
        new(id, file, content.LongLength, Convert.ToHexString(SHA256.HashData(content)));

    private static Task<bool> HasPreferredProfile(string? fixtureDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(fixtureDirectory is not null && File.Exists(Path.Combine(fixtureDirectory, SampleFixtures.PreferredProfile)));
    }

    private static Task PreparePreferredAsync(InstallerStepContext context, CancellationToken token)
    {
        context.Log($"Using the verified {Path.GetFileName(context.GetAsset("preferred-profile"))} fixture.");
        if (File.Exists(Path.Combine(context.PackageDirectory, SampleFixtures.SimulateFailure)) ||
            Environment.GetEnvironmentVariable("GAMEINSTALLER_SAMPLE_FAIL_PREFERRED") == "1")
            throw new InvalidOperationException("The sample simulated a preferred worker failure.");
        return PrepareSceneAsync(context, token);
    }
}
