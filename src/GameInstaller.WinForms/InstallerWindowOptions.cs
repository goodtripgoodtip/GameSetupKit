namespace GameInstaller.WinForms;

public sealed record InstallerWindowOptions
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public Icon? Icon { get; init; }
    public Color? AccentColor { get; init; }
    public string? DefaultInstallParent { get; init; }
    public string? DefaultSourceGameDirectory { get; init; }
    public string? ManualFileDirectory { get; init; }
    public IInstallerLogger? LogSink { get; init; }
    public string? DefaultProductDirectory { get; init; }
    public InstallerPackage? Package { get; init; }
    public IInstallerPlatform? Platform { get; init; }
    public string? LauncherExecutable { get; init; }
    public bool UninstallOnOpen { get; init; }
    public bool OpenInstalledProduct { get; init; }
}
