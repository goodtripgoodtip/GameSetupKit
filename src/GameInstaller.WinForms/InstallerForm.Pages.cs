using System.Text;

namespace GameInstaller.WinForms;

public sealed partial class InstallerForm
{
    private async Task StartAsync()
    {
        if (options.UninstallOnOpen || options.OpenInstalledProduct)
        {
            await DiscoverAsync(openInstalled: true);
            return;
        }
        await RunAsync(() => service.InspectAsync(sourceFolder, operation!.Token), found =>
        {
            LoadStatus(found);
            ShowLocation();
        }, ShowLocationError);
    }

    private void ShowLocation()
    {
        Page(Stage.Location, definition.Name, definition.Managed?.Subtitle ?? options.Description);
        if (definition.Source is { } original)
        {
            string game = original.DisplayName ?? "the original game";
            Add(content, Label($"Requires {game}{(original.SteamAppId is null ? "" : " on Steam")}. Original game files are supplied by your own installation."));
            Add(content, Label(original.CompatibilityNotice));
        }
        if (status?.InstalledVersion is { } version)
            Add(content, Label($"Version {version} is installed. Reinstall keeps your selected content and settings."));
        if (locationExpanded)
        {
            var destination = new TextBox { Text = location, ReadOnly = true, Dock = DockStyle.Top, AccessibleName = "Install location" };
            InstallerTheme.Apply(destination);
            Add(content, destination);
            Link("Browse…", () => ShowLocationPicker(runtime: false, ShowLocation));
            if (definition.Managed?.AllowProgramFiles == true)
                Link("Use Program Files", () => ChangeLocation(InstallerService.GetManagedProductDirectory(definition, true), false, ShowLocation));
            if (definition.Managed is not null && status?.InstalledVersion is null)
                Link("Choose a drive for the private copy and downloads…", () => ShowLocationPicker(runtime: true, ShowLocation));
            if (runtimeParent is not null)
                Add(content, Label("Runtime and downloads: " + service.RuntimeDirectory));
        }
        Link(locationExpanded ? "Hide location options" : "Change install location", () => { locationExpanded = !locationExpanded; ShowLocation(); });
        if (definition.Managed is not null)
        {
            Add(content, Check("Create a desktop shortcut", shortcuts.Desktop, value => shortcuts = shortcuts with { Desktop = value }));
            Add(content, Check("Add a Start menu shortcut", shortcuts.StartMenu, value => shortcuts = shortcuts with { StartMenu = value }));
        }
        if (status?.CanRemove == true)
            Link("Uninstall", async () => await BeginRemoveAsync(ShowLocation));
        if (status?.CanLaunch == true)
            Link("Open installed product", ShowPlay);
        if (status?.PendingRequest is { } pending)
            Link("Resume setup", async () => { RestoreRequest(pending); await DiscoverAsync(); });
        Primary(status?.InstalledVersion is null ? "Continue" : "Reinstall", async () =>
        {
            intent = status?.InstalledVersion is null ? InstallIntent.Install : InstallIntent.Reinstall;
            await DiscoverAsync();
        });
        Button("Close", Close, cancel: true);
    }

    private void ShowLocationError(string error)
    {
        ShowLocation();
        Add(content, Label(error));
    }

    private async Task DiscoverAsync(bool openInstalled = false)
    {
        Page(Stage.Review, $"Getting {definition.Name} ready", definition.Source is null ? "Checking setup files…" : "Finding the original game…");
        Button("Stop setup", CancelOperation, cancel: true);
        await RunAsync(() => service.InspectAsync(sourceFolder, operation!.Token), found =>
        {
            LoadStatus(found);
            if (openInstalled && options.UninstallOnOpen)
                _ = BeginRemoveAsync(ShowLocation);
            else if (openInstalled && status?.CanLaunch == true)
                ShowPlay();
            else if (definition.Source is not null && found.SourceGame is null)
                ShowSourceMissing(found.SourceProblem);
            else
                ShowSelection();
        }, ShowLocationError);
    }

    private void LoadStatus(InstallerStatus found)
    {
        status = found;
        sourceFolder = found.SourceGame?.Directory ?? sourceFolder;
        if (!firstRun)
            return;
        firstRun = false;
        shortcuts = found.Shortcuts ?? shortcuts;
        network = found.Network;
        if (found.InstalledVersion is not null)
            SetSelection(found.InstalledComponents);
    }

    private void ShowSourceMissing(string? problem)
    {
        Page(Stage.Review, "Find your original game", $"Setup couldn’t find a supported copy of {definition.Source?.DisplayName ?? "the original game"}.");
        if (problem is not null)
            DetailLink("Show detection details", problem, () => ShowSourceMissing(problem));
        Link("How to find it in Steam", () =>
        {
            Page(Stage.Review, "Find your game in Steam", "In your Steam Library, right-click the game and choose Manage, then Browse local files.");
            Link("Choose the game folder or Steam library…", ShowSourcePicker);
            Button("Back", () => ShowSourceMissing(problem), cancel: true);
        });
        Primary("Find game…", ShowSourcePicker);
        Button("Back", ShowLocation, cancel: true);
    }

    private void ShowSourcePicker()
    {
        Page(Stage.Review, "Choose the original game", "Select the installed game folder or its Steam library.");
        Add(content, new InstallerPathPicker(sourceFolder, folders: true, path =>
        {
            sourceFolder = path;
            _ = DiscoverAsync();
        }, () => { if (status?.SourceGame is not null) ShowSelection(); else ShowSourceMissing(status?.SourceProblem); }));
    }

    private void ShowLocationPicker(bool runtime, Action back)
    {
        Page(currentStage, runtime ? "Private game and downloads" : "Choose an install location",
            runtime ? "Choose a writable folder on the drive setup should use." : "Choose a writable folder for this product.");
        Add(content, new InstallerPathPicker(runtime ? runtimeParent ?? location : location, folders: true,
            path => ChangeLocation(path, runtime, back), back, allowNew: true));
    }

    private void ChangeLocation(string path, bool runtime, Action back)
    {
        (string oldLocation, string? oldRuntime) = (location, runtimeParent);
        if (runtime)
            runtimeParent = path;
        else
        {
            location = path;
            runtimeParent = null;
        }
        try { service = CreateService(); }
        catch
        {
            (location, runtimeParent) = (oldLocation, oldRuntime);
            throw;
        }
        _ = RunAsync(() => service.InspectAsync(sourceFolder, operation!.Token), found =>
        {
            firstRun = true;
            LoadStatus(found);
            back();
        }, ShowLocationError);
    }

    private void ShowSelection()
    {
        agreed = false;
        string heading = intent == InstallIntent.AddComponents ? "Add optional content" : custom ? "Customize installation" : "Easy Install";
        Page(Stage.Review, heading, definition.Source is null ? "Choose what to install." : "Your original game is ready. Choose what to install.");
        if (package?.Offline == true)
            Add(content, Label("Offline setup uses bundled and local files."));
        if (definition.Components.Count > 0)
            ShowComponents();
        if (definition.Components.SelectMany(item => item.Content.Steps).Concat(definition.Required.Steps)
            .Any(step => step.Preferred is { } route && (package?.Offline != true || route.WorksOffline)))
            Add(content, Check("Check for faster preparation on this PC", allowHardware, value => allowHardware = value));
        if (custom)
        {
            if (package?.Offline != true)
                Add(content, Check("Use downloads I already have", localOnly, value => localOnly = value));
            if (definition.Managed?.PrivateNetworkPrograms.Count > 0)
                Link("Game network privacy", () => ShowPrivacy(applyNow: false));
            if (definition.Source is not null)
                Link("Find original game again…", ShowSourcePicker);
            if (definition.Managed is not null && status?.InstalledVersion is null)
                Link("Choose another drive for the private copy and downloads…", () => ShowLocationPicker(true, ShowSelection));
            Link("Open an offline setup file…", () => ChooseOffline(ShowSelection));
        }
        if (intent != InstallIntent.AddComponents)
            Link(custom ? "Use recommended settings" : "Customize installation", () =>
            {
                custom = !custom;
                if (!custom)
                {
                    ResetSelection();
                    allowHardware = false;
                    localOnly = package?.Offline == true;
                }
                ShowSelection();
            });
        if (status?.CanRemove == true)
            Link("Uninstall", async () => await BeginRemoveAsync(ShowSelection));
        Primary("Continue", async () => await PreviewAsync());
        Button("Back", intent == InstallIntent.AddComponents ? ShowPlay : ShowLocation, cancel: true);
    }

    private void ShowComponents()
    {
        foreach (InstallerComponentGroup group in definition.ComponentGroups)
        {
            InstallerComponent[] items = definition.Components.Where(item => item.GroupId?.Equals(group.Id, StringComparison.OrdinalIgnoreCase) == true).ToArray();
            if (items.Length == 0)
                continue;
            var choices = Stack();
            Add(choices, Label(group.Name));
            if (group.Exclusive)
            {
                Add(choices, Radio("Original / no extra", !items.Any(item => selection.Contains(item.Id)), () =>
                {
                    foreach (InstallerComponent item in items)
                        selection.Remove(item.Id);
                }));
                foreach (InstallerComponent item in items)
                    Add(choices, Radio(item.Name, selection.Contains(item.Id), () =>
                    {
                        foreach (InstallerComponent choice in items)
                            selection.Remove(choice.Id);
                        selection.Add(item.Id);
                    }));
            }
            else
                foreach (InstallerComponent item in items)
                    Add(choices, ComponentChoice(item));
            Add(content, choices);
        }
        foreach (InstallerComponent item in definition.Components.Where(item => item.GroupId is null))
            Add(content, ComponentChoice(item));
    }

    private InstallerCheckBox ComponentChoice(InstallerComponent component) => Check(component.Name, selection.Contains(component.Id), value =>
    {
        if (value)
            selection.Add(component.Id);
        else
            selection.Remove(component.Id);
    }, component.Description);

    private void ChooseOffline(Action back)
    {
        Page(currentStage, "Open the offline installer", "Choose the matching offline setup executable to use its bundled files.");
        Add(content, new InstallerPathPicker(options.ManualFileDirectory, false, file =>
        {
            InstallerPackage candidate = InstallerPackage.Open(file) ?? throw new InvalidDataException("This file does not contain a setup package.");
            if (!candidate.Offline || candidate.ProductId != definition.Id || candidate.Version != definition.Version)
                throw new InvalidDataException("Choose the offline file for this product and version.");
            InstallerPackage? previous = package;
            package = candidate;
            try { service = CreateService(); }
            catch { package = previous; throw; }
            localOnly = true;
            back();
        }, back, "*.exe"));
    }

    private async Task PreviewAsync()
    {
        var request = new InstallRequest(sourceFolder, definition.Components.Where(item => selection.Contains(item.Id)).Select(item => item.Id).ToArray())
        {
            Mode = custom ? SetupMode.Custom : SetupMode.Easy, Intent = intent, Shortcuts = shortcuts,
            Network = custom ? network : InstallerNetworkChoice.Unchanged,
            DownloadPolicy = localOnly ? InstallDownloadPolicy.LocalOnly : custom ? InstallDownloadPolicy.AskEachTime : InstallDownloadPolicy.ApproveReviewed,
            AllowCapabilityChecks = allowHardware
        };
        Page(Stage.Review, "Preparing your review", "Checking the source, destination, and selected files…");
        Button("Stop setup", CancelOperation, cancel: true);
        await RunAsync(() => service.PreviewAsync(request, operation!.Token), ShowReview, error =>
        {
            ShowSelection();
            Add(content, Label(error));
        });
    }

    private void ShowReview(InstallReview review)
    {
        string[] selected = definition.Components.Where(item => review.Components.Contains(item.Id)).Select(item => item.Name).ToArray();
        Page(Stage.Review, "Ready to install", selected.Length == 0 ? "Base installation only." : "Includes " + string.Join(", ", selected) + ".");
        if (review.DownloadPolicy == InstallDownloadPolicy.LocalOnly)
            Add(content, Label("Setup will use bundled and local files."));
        else if (review.Assets.Any(NeedsDownload))
        {
            long bytes = review.Assets.Where(NeedsDownload).Sum(file => file.Bytes);
            Add(content, Label(review.DownloadPolicy == InstallDownloadPolicy.ApproveReviewed
                ? $"Install approves up to {SizeText(bytes)} in downloads for the selected features."
                : $"Setup will ask before downloading files. The selected files total up to {SizeText(bytes)}."));
        }
        if (!custom && definition.Managed?.PrivateNetworkPrograms.Count > 0)
            Add(content, Label("Easy Install skips Windows Firewall."));
        if (custom && network == InstallerNetworkChoice.BlockOutbound && definition.Managed is { } managed)
            Add(content, Label($"Custom will review outbound block rules for {string.Join(" and ", managed.PrivateNetworkPrograms)} before asking for Windows permission."));
        if (review.NetworkDisclosure is { Length: > 0 } disclosure)
            Add(content, Label(disclosure));
        DetailLink("Show installation details", ReviewText(review), () => ShowReview(review));
        Button install = Button(intent == InstallIntent.Reinstall ? "Reinstall" : "Install", async () => await InstallAsync(review), primary: true);
        Button("Back", ShowSelection, cancel: true);
        if (review.Notice is { } notice)
        {
            consent.Visible = true;
            var terms = MakeLink("Read the Hobby Release Terms");
            terms.Text = "Read the " + notice.Title;
            terms.LinkClicked += (_, _) => ShowText(Stage.Review, notice.Title, notice.Text, () => ShowReview(review));
            consent.Controls.Add(terms);
            consent.Controls.Add(Check("I agree to the terms", agreed, value => { agreed = value; install.Enabled = value; }));
            install.Enabled = agreed;
        }
    }

    private static bool NeedsDownload(InstallReviewAsset file) => file.Url is not null && !file.IsEmbedded && !file.HasCachedCandidate &&
        !file.RequiresSeparateApproval;

    private static string ReviewText(InstallReview review)
    {
        var text = new StringBuilder();
        text.AppendLine($"Install location: {review.ProductDirectory}");
        if (review.Source is { } source)
            text.AppendLine($"Source: {source.Directory} (build {source.BuildId ?? "manual"})");
        if (review.FreeBytes is long free)
            text.AppendLine($"Runtime drive available: {SizeText(free)}");
        foreach (InstallReviewAction action in review.Actions)
            text.AppendLine($"{action.Name}: {action.Description}");
        foreach (InstallReviewAsset file in review.Assets)
        {
            text.AppendLine($"\n{file.FileName} · {SizeText(file.Bytes)} · SHA-256 {file.Sha256}");
            text.AppendLine(file.IsEmbedded ? "Verified in the offline package." : file.Url?.AbsoluteUri ?? "Provide a local copy if needed.");
            if (file.Hosts.Count > 0)
                text.AppendLine($"Approved hosts: {string.Join(", ", file.Hosts)}");
            if (file.RequiresSeparateApproval)
                text.AppendLine("Setup will ask for separate approval if this route is selected.");
        }
        return text.ToString();
    }

    private void DetailLink(string label, string text, Action back) => Link(label, () => ShowText(currentStage, label, text, back));

    private void ShowText(Stage stage, string heading, string text, Action back)
    {
        Page(stage, heading);
        Add(content, ReadOnlyText(text));
        Primary("Back", back);
    }

    private TextBox ReadOnlyText(string text)
    {
        var panel = new TextBox { Text = text.ReplaceLineEndings(Environment.NewLine), ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Top, Height = Math.Clamp((int)(ClientSize.Height * .45f), 140, Px(410)) };
        InstallerTheme.Apply(panel);
        return panel;
    }

    private void ShowPrivacy(bool applyNow)
    {
        Page(applyNow ? Stage.Play : Stage.Review, "Game network privacy", "Choose whether the private game copy may connect to the network.");
        InstallerNetworkChoice choice = network;
        Add(content, Radio("Keep the current rules", choice == InstallerNetworkChoice.Unchanged, () => choice = InstallerNetworkChoice.Unchanged));
        Add(content, Radio("Allow normal network access", choice == InstallerNetworkChoice.Allow, () => choice = InstallerNetworkChoice.Allow));
        Add(content, Radio("Block outbound traffic from the private copy", choice == InstallerNetworkChoice.BlockOutbound,
            () => choice = InstallerNetworkChoice.BlockOutbound));
        Add(content, Label($"Block covers {string.Join(" and ", definition.Managed!.PrivateNetworkPrograms)} on all network profiles. Setup shows the rules before requesting Windows permission."));
        Primary("Save choice", async () =>
        {
            network = choice;
            if (!applyNow || choice == InstallerNetworkChoice.Unchanged)
            {
                if (applyNow) ShowPlay(); else ShowSelection();
                return;
            }
            await RunAsync(async () => { await service.ApplyNetworkChoiceAsync(choice, this, operation!.Token); return true; },
                _ => ShowPlay(), error => { ShowPrivacy(true); Add(content, Label(error)); });
        });
        Button("Back", applyNow ? ShowPlay : ShowSelection, cancel: true);
    }

    private void ShowPlay()
    {
        Page(Stage.Play, definition.Launch is null ? "Setup complete" : "Ready to play", definition.Name + " is installed.");
        if (status?.SettingsPending == true)
        {
            ShowSettings(firstLaunch: true);
            return;
        }
        if (status?.CanLaunch == true)
            Primary("Play Now", async () => await PlayAsync());
        if (status?.PendingRequest is { } pending)
            Link("Resume setup", () => { RestoreRequest(pending); ShowSelection(); });
        if (definition.Components.Count > 0)
            Link("Get optional content", () => { intent = InstallIntent.AddComponents; custom = true; SetSelection(status?.InstalledComponents ?? []); ShowSelection(); });
        Link("Setup options", ShowInstalledOptions);
        Button("Close", Close, cancel: true);
    }

    private void ShowInstalledOptions()
    {
        Page(Stage.Play, "Setup options", $"Manage this installation of {definition.Name}.");
        Add(content, Label("Installed in " + location));
        Link("Open install location", () => OpenPath(location));
        if (definition.Settings.Count > 0)
            Link("Product settings", () => ShowSettings(false));
        if (definition.Managed?.PrivateNetworkPrograms.Count > 0)
            Link("Game network privacy", () => ShowPrivacy(true));
        Link("Reinstall with Custom options", () => { intent = InstallIntent.Reinstall; custom = true; ShowSelection(); });
        Link("Uninstall", async () => await BeginRemoveAsync(ShowInstalledOptions));
        Button("Back", ShowPlay, cancel: true);
    }

    private void ShowSettings(bool firstLaunch)
    {
        Page(Stage.Play, "Product settings", firstLaunch ? "Choose your starting settings. You can change them later." : null);
        var settings = new Dictionary<string, bool>(service.ReadSettings());
        foreach (var group in definition.Settings.GroupBy(setting => setting.Group))
        {
            Add(content, Label(group.Key));
            foreach (InstallerSetting setting in group)
                Add(content, Check(setting.Name, settings[setting.Id], value => settings[setting.Id] = value));
        }
        Primary("Save and continue", () =>
        {
            try
            {
                service.SaveSettings(settings);
                status = status! with { SettingsPending = false };
                ShowPlay();
            }
            catch (Exception error) { Add(content, Label(error.Message)); }
        });
        Button(firstLaunch ? "Close" : "Back", firstLaunch ? Close : ShowPlay, cancel: true);
    }

    private void ResetSelection()
    {
        SetSelection(definition.Components.Where(item => item.Recommended).Select(item => item.Id));
        foreach (InstallerComponentGroup group in definition.ComponentGroups.Where(item => item.Exclusive))
            foreach (InstallerComponent item in definition.Components.Where(item => item.GroupId == group.Id && selection.Contains(item.Id)).Skip(1))
                selection.Remove(item.Id);
    }

    private void SetSelection(IEnumerable<string> items)
    {
        selection.Clear();
        selection.UnionWith(items);
    }

    private void RestoreRequest(InstallRequest request)
    {
        SetSelection(request.Components ?? []);
        sourceFolder = request.SourceGameDirectory ?? sourceFolder;
        custom = request.Mode == SetupMode.Custom;
        localOnly = request.DownloadPolicy == InstallDownloadPolicy.LocalOnly;
        allowHardware = request.AllowCapabilityChecks;
        network = request.Network;
        intent = request.Intent;
    }

    private static InstallerCheckBox Check(string text, bool value, Action<bool> changed, string? description = null)
    {
        var box = new InstallerCheckBox { Text = text, Checked = value, AccessibleDescription = description };
        box.CheckedChanged += (_, _) => changed(box.Checked);
        return box;
    }

    private static InstallerRadioButton Radio(string text, bool value, Action selected)
    {
        var radio = new InstallerRadioButton { Text = text, Checked = value };
        radio.CheckedChanged += (_, _) => { if (radio.Checked) selected(); };
        return radio;
    }

    private static string SizeText(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576d:0.##} MiB" : $"{bytes:N0} bytes";
}
