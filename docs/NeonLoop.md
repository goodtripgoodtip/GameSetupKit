# Port the NeonLoop installer

The sample shows the launcher and setup pattern with generated fixtures. Use it as a starting point when the real project is ready to adopt the installer. The existing game-specific launcher and cutscene pipeline live in NeonLoop's `GitSource` folder.

| NeonLoop behavior | Where it fits |
| --- | --- |
| Find the Windows Steam game and support known builds | Configure `PrivateGameCopy` with Steam app ID `330820`, `ROM.exe`, the supported Steam build IDs, and the hashes of files the patcher requires. Keep the manual source picker available. |
| Prepare the private copy with settings, shaders, and the local patcher | List locally supplied, cleared files in `Required.Payloads`. Use a trusted preparation step for the existing patcher. Read stock inputs from the source game directory and write output into the private candidate. Keep the original Steam launch available through Steam. |
| Single dark Location, Review, Install, and Play window | Use `InstallerForm` with the product subtitle, icon, and accent. The sample uses the reference icon, sizing, stage strip, bottom console, and themed path and consent pages. |
| Easy, Custom, and original or selected extras | Set `Recommended`, `InstallerComponentGroup`, and `Requires`. Mark independent optional components skippable and declare whether the base can be played while setup is incomplete. |
| Standard and offline EXEs, pinned downloads and ZIP imports | Use `InstallerPackage.BuildAsync` with the cleared base payload and optional offline asset pins. Configure `InstallerAsset` for the online equivalents and in-window file or folder recovery. |
| Optional NVIDIA checks and CPU fallback | Use `InstallerPreferredStep`, `WindowsSetupProbes.NvidiaMeetsRequirementsAsync`, and pinned route-specific assets. The shared wizard requests separate approval for the preferred assets. Run the product worker from the private cache and leave the standard route available. |
| Progress, interrupted preparation, and later additions | Report progress through `InstallerStepContext`. Checkpoint reusable output groups with `RunCheckpointAsync`. Use `InstallIntent.AddComponents` for content installed from Play. |
| Grouped first-run options and playing the game | Use `InstallerSetting` for installer-facing choices and `LaunchDefinition` for arguments, working directory, and window hooks. A before-launch hook can call `WindowsSetupProbes.EnsureSteamClientAsync`. The game owns its live soundtrack selection, effects, F10, and other player controls. |
| Network privacy | Declare the private executables in `ManagedInstaller.PrivateNetworkPrograms`. Easy skips the firewall. Custom reviews exact outbound blocks before it requests administrator approval. The platform adapter removes only recorded, matching rules. |
| Managed setup, move the runtime, reinstall, and uninstall | Configure `ManagedInstaller` for placement, Windows shortcuts, Installed Apps, and optional Program Files. Use `PreviewReinstallAsync` and the three-step removal review. Keep the active version while preparing a replacement and preserve player saves and unrelated files. |

The mod project remains the owner of its patches, native runtime files, rendering and audio dependencies, music packs, media recipes, and their redistribution decisions. Keep those product inputs local when adapting the sample.
