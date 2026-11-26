# Integrate Game Installer

## Define a product

Create a Windows executable, reference `GameInstaller.WinForms`, and run `InstallerForm` on the STA UI thread. To use the sample’s dark theme, call `Application.SetColorMode(SystemColorMode.Dark)` after `ApplicationConfiguration.Initialize()` and before creating any windows. Windows high-contrast settings take precedence. For an existing UI, reference `GameInstaller.Core` and create an `InstallerService` with the same definition and package directory. [WinForms dark mode](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net100#dark-mode)

Start with a stable product ID, a display name, and a version you change when payloads, compatible builds, or setup behavior change. `InstallerWindowOptions` sets the title, description, icon, accent color, initial folders, and an optional log sink. Package files can live beside the installer under a folder such as `Payload`.

| Mode | Definition |
| --- | --- |
| `Standalone` | List bundled files, directories, or ZIPs under `Required.Payloads`. Point `LaunchDefinition` at the packaged executable relative to the installed folder. |
| `PrivateGameCopy` | Supply `GameSource` and any required or optional overlays. Setup copies the game first. Put all patch outputs in the candidate copy. |
| `LauncherOnly` | Supply `GameSource` and any wrapper files to install. Launch the wrapper from the installed folder, or use `LaunchTarget.SourceGame` to start the stock executable. Pass the stock directory to the wrapper with `{source}`. |

For Steam, use the app ID, expected relative executable, and any required files. Setup detects Steam and its registered libraries automatically. Set `SteamDirectory` only for a portable client or a test library. Users can locate the game folder, `steamapps`, or the library manually when detection fails. A manual folder inside a Steam library can use its matching manifest. An external copy needs pinned hashes if you also require a build ID and no matching Steam manifest exists. Add SHA-256 for files a patch is sensitive to. Keep original game files read-only and out of the release package.

Set `GameSource.DisplayName` to name the original game in the informational requirement notice. That notice appears for source-game installers without a confirmation step. Its default compatibility text describes limited Windows 11 and supported release testing. Set `CompatibilityNotice` to the actual testing scope for the product.

Payload source paths are relative to the package. Destinations are relative to the candidate install. Use an empty destination to merge a directory or ZIP at its root. A `File` payload needs a filename in its destination. A ZIP entry must stay within the candidate and may not be a symbolic link. Directory sources and destinations must be separate. Files supplied later in a recipe can replace earlier files at the same destination. Components are applied in definition order. Each component can use base assets and its own assets, so it works on its own when selected.

## Install, launch, and remove

- `InspectAsync` reports the source game, any source problem, and the active version and components. It does not install anything.
- `PreviewAsync` reads the source and returns the destination and available space, the selected actions, local inputs, possible online assets, capability checks, and any release terms. Show that review before calling `InstallReviewedAsync` with the same definition, package directory, and destination. Treat a definition and its collections as fixed once reviewed. The service checks the source again before setup.
- The main **Install** button selects `Recommended` components and uses `ApproveReviewed`. **Customize installation** exposes individual choices and uses `AskEachTime`. An explicit local-only choice applies in either mode. `InstallAsync` can still apply the default per-file approval or local-only policy for definitions without a release notice. Use an empty component list to install the base. Optional setup first makes a base package available when no usable install exists.
- Set `AllowSkip` on independent optional components. The user can skip a missing asset or setup can report a component failure in `InstallResult.SkippedComponents`. Setup discards the partial candidate and rebuilds it with the remaining selection using verified cached inputs. Other installation failures keep the previous usable install available.
- `LaunchAsync` uses the active install receipt, passes arguments and environment variables, and returns the process ID. Each argument is a separate value. `{install}`, `{data}`, and `{source}` expand to the installed content, persistent data, and original game folders. Add `BlockingProcessNames` without `.exe` for additional processes that must be closed. Launch callbacks can prepare runtime configuration or set window state for the current definition version. Startup customization failures after the game launches appear in the log.
- `RemoveAsync` serves unmanaged definitions and keeps their existing cache behavior. Managed definitions use `PreviewRemoveAsync` and `RemoveReviewedAsync` with `RemovalApproval.All` after the three separate confirmations. Retry an interrupted removal with a fresh review. Receipt-owned generation files are removed if they still match. Saves, logs, and changed or untracked game files stay.

## Managed setup

- Set `InstallerDefinition.Managed` to a `ManagedInstaller` with a stable publisher and product ID. Default placement is `%LOCALAPPDATA%\Programs\<publisher>\<id>`. The UI can discover a previous install, accept a different target, and choose a separate runtime drive before the first install. `InstallerServiceOptions` exposes the same locations, package, launcher executable, and platform adapter to other hosts.
- Build a self-contained launcher with the desired application icon and an `asInvoker` manifest. Route `GetCommandProductId`, `RunWorkerAsync`, `PrepareUninstallHostAsync`, and `TryGetLaunchDirectory` before opening the wizard. Set `DefaultProductDirectory`, `OpenInstalledProduct`, or `UninstallOnOpen` from the result. Call `CleanupUninstallHost` when the uninstall wizard exits. The default Windows adapter owns the per-user Apps entry, optional Desktop and Start menu shortcuts, and the installed launcher. `AllowProgramFiles` adds the exact product-specific Program Files location. With another host, set `UseInstalledPayload` to reuse and verify the cached base package when launching the installed product.
- Use `InstallerComponentGroup`, `GroupId`, and `Requires` for grouped choices and dependencies. `InstallIntent.Reinstall` rebuilds packaged files. `PreviewReinstallAsync` uses the installed choices by default. `AddComponents` retains installed selections and replaces an exclusive choice when a new one is selected. `InstallerSetting` groups appear before first play, and `ReadSettings` and `SaveSettings` are available to other hosts. `{settings}` expands to the settings file for launch hooks and environment values.
- `IInstallerSetupInteraction` extends the per-file interaction with separate approval for preferred assets and scoped Windows changes. For private game copies, `PrivateNetworkPrograms` lists product-relative EXEs. Easy works without a firewall lookup. Custom previews the exact owned paths before elevation, and `ApplyNetworkChoiceAsync` can change policy after installation. Keep privacy approvals distinct from download acceptance.
- `InstallerStatus.PendingRequest` and `LimitedPlayAccepted` drive recovery. Set `AllowLimitedPlay` only when the base is usable independently, then call `AcceptLimitedPlay` after the user opts in. Reusing an existing valid generation and the last verified base preserves progress between attempts.

The default product folder is `%LOCALAPPDATA%\GameInstaller\<product-id>`. It has `Data`, `Logs`, `Cache`, `Staging`, and versioned content under `Installs` as needed. Supply the parent folder when changing the location. Use `{data}` for user saves and settings where your game supports it. Files written alongside an executable stay with that generation, so prefer the persistent data directory when saves should follow updates. The installer tracks the process it starts. If a wrapper starts a separate game process and exits, configure the child process name as a guard.

## Downloads and preparation

- Configure an `InstallerAsset` with its unique ID, filename, byte count, and SHA-256. Setup checks the filename beside the package and any extra package-relative `LocalFiles` before prompting. Add an HTTPS URL for automatic downloads and specify any approved cross-host redirects. Cache entries, bundled candidates, and manually selected files all use the same size and hash checks.
- The WinForms host supplies `IInstallerInteraction`. In another host, return `Download`, `UseLocalFile` or `UseLocalFolder` with the chosen path, `Skip` when offered, or `Cancel`. Setup remembers selected folders for the rest of the run. Easy approves exact reviewed online assets. Custom and managed retries can ask before transferring a file. Local-only mode disables the installer’s HTTP requests and download link buttons.
- Attach a file or ZIP to a payload via its `AssetId` and use an empty `Source`. For custom work add an `InstallerStep` and use `context.GetAsset(id)` to get the verified path. Write installed outputs under `WorkingDirectory` using `GetWorkingPath`. Use a product and recipe-specific subfolder under `CacheDirectory` for reusable intermediate data. `SourceGameDirectory` is for read-only input.
- `RunToolAsync` takes a full path to a tool you own or trust, a list of arguments, a cancellation token, and optional environment variables. It captures standard output and error, includes exit codes in the log, and treats a nonzero exit code as failure. A worker can print `PROGRESS<TAB>done<TAB>total<TAB>message` on stdout, using an actual tab between fields and zero as the total for an unknown length. For in-process preparation, call `ReportProgress` and `Log` directly. Keep network acquisition in the asset API so the UI can obtain consent and verify each file.
- Describe what each step reads or writes with `InstallerStep.Description`. Set `MinimumFreeBytes` for any required working space at the start of a content step. Progress includes the current transfer or worker action separately from the overall stage count and next action.
- Keep a standard `InstallerStep.RunAsync` path. Add an `InstallerPreferredStep` for a faster path with a specific local probe, a plain-language description, a timeout, and optional route-specific assets. It runs only for selected work and explicit opt-in. Decline, uncertainty, a failed probe, or a failed preferred worker uses the standard path. Use temporary outputs in preferred workers and publish them only after success so fallback starts clean. Keep probes read-only, honor cancellation, and query only the capability the step needs.
- Use `InstallerStep.CheckpointFiles` to reuse verified step outputs. For a long step, call `RunCheckpointAsync` around each independently reusable output group. Keep preparation assumptions in the product version and verified asset inputs. Use `NetworkDisclosure` for product-specific connections, separately from installer downloads and optional firewall controls.

## Release terms

Set `InstallerDefinition.Notice` to `InstallerNotice.HobbyRelease` if the shipped MIT License and hobby terms apply to your release. Supply an `InstallerNotice` with your own version, title, and text for other products. In WinForms, setup shows the full notice and requires an unchecked-by-default agreement before install. With another UI, show `InstallReview.Notice` and pass `acceptedNoticeVersion` to `InstallReviewedAsync` only after the user explicitly agrees. Change the notice version when terms change.

Setup records the accepted version, terms hash, product version, and time in a local receipt under `Agreements`. Keep the original license and applicable third-party notices with source and binary releases. Review enforceability and any required consumer disclosures for your release jurisdiction. [MIT License](https://opensource.org/license/mit) · [Berman v. Freedom Financial Network](https://cdn.ca9.uscourts.gov/datastore/opinions/2022/04/05/20-16900.pdf)

## Logging

The WinForms window creates a UTF-8 text log for each install, launch, and removal attempt. **Show setup details** opens the bottom console, with **Open setup log** and **Copy details**. Logs under `Logs` include an ISO timestamp, severity, area, and message. Use `InstallerLog`, an `IInstallerLogger`, or an `IProgress<InstallerLogEntry>` sink in another host.

Log output stays local. URLs are logged without query strings and child arguments and environment variables are not logged automatically. Keep credentials out of custom messages and tool output. Review logs for local paths and tool output before sharing them.

## Package for users

Publish the product executable project, not the entire solution. In Visual Studio use a Folder profile targeting Windows x64 and self-contained deployment. From PowerShell at the solution root, publish the sample with:

```powershell
dotnet publish .\samples\GameInstaller.Sample\GameInstaller.Sample.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\dist\sample
```

Keep external payloads beside the published installer in the same structure that the definition expects. The sample generates its dummy fixtures on demand and bundles the native runtime for the single-file option. Redistribute only payloads you have rights to distribute. [Microsoft publishing documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/)

For a one-file installer, pass the clean bootstrap EXE, payload folder, output, product ID, and version to `InstallerPackage.BuildAsync`. Set `InstalledLauncher` to the portable launcher to install. Use `Offline = true` and `OfflineAssets` to include pinned optional content in a second EXE. Open the final EXE with `InstallerPackage.Open` and supply the result to the window or service. Setup validates the archive checksum and manifest and verifies the extracted files and selected embedded assets. Build the payload before signing the final EXE. The reader handles a trailing PE certificate table.
