using System.Security.Cryptography;
using System.Text;

namespace GameInstaller.Sample;

internal static class SamplePackages
{
    internal static async Task BuildAsync(string bootstrap, string destination)
    {
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(destination);
        string inputs = Path.Combine(destination, "inputs");
        string payload = Path.Combine(inputs, "payload");
        string assets = Path.Combine(inputs, "offline");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(payload, "mod-settings.txt"), "The sample mod writes to the staged game copy.\n");
        foreach (string name in new[] { "LICENSE", "HOBBY-TERMS.txt" })
        {
            string source = Path.Combine(Path.GetDirectoryName(bootstrap)!, name);
            if (File.Exists(source))
                File.Copy(source, Path.Combine(payload, name), overwrite: true);
        }
        var standard = new InstallerPackageOptions(bootstrap, payload, Path.Combine(destination, "GameSetupKit-Demo-Setup.exe"),
            SampleRecipes.ModId, SampleRecipes.Version) { InstalledLauncher = bootstrap };
        await InstallerPackage.BuildAsync(standard);
        await InstallerPackage.BuildAsync(standard with
        {
            Output = Path.Combine(destination, "GameSetupKit-Demo-Setup-Offline.exe"), Offline = true,
            OfflineAssets =
            [
                Write("bonus-zip", SampleFixtures.ScenePackFile, SampleFixtures.ScenePack()),
                Write("alternate-zip", SampleFixtures.AlternatePack, SampleFixtures.ScenePack(alternate: true)),
                Write("scene", SampleFixtures.OptionalSceneFile, Encoding.UTF8.GetBytes(SampleFixtures.OptionalSceneText))
            ]
        });
        string[] names = ["GameSetupKit-Demo-Setup.exe", "GameSetupKit-Demo-Setup-Offline.exe"];
        await File.WriteAllLinesAsync(Path.Combine(destination, "SHA256SUMS.txt"), names.Select(name =>
        {
            using Stream input = File.OpenRead(Path.Combine(destination, name));
            return $"{Convert.ToHexString(SHA256.HashData(input))}  {name}";
        }));

        OfflinePackageAsset Write(string id, string name, byte[] content)
        {
            string file = Path.Combine(assets, name);
            File.WriteAllBytes(file, content);
            return new OfflinePackageAsset(SampleRecipes.Pinned(id, name, content), file);
        }
    }
}
