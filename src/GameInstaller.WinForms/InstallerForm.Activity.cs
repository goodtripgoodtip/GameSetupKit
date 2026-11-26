using System.Diagnostics;

namespace GameInstaller.WinForms;

public sealed partial class InstallerForm
{
    private readonly TableLayoutPanel diagnostics = Stack();
    private readonly TextBox console = new()
    {
        Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
        AccessibleName = "Setup activity console", Margin = Padding.Empty
    };
    private readonly LinkLabel details = MakeLink("Show setup details");
    private readonly List<string> history = [];
    private readonly Panel consoleHost = new() { Dock = DockStyle.Top, Visible = false, Margin = Padding.Empty };
    private InstallerLog? operationLog;
    private string? latestLog;
    private bool detailsOpen;
    private Button openLog = null!;
    private Button copyLog = null!;
    private Label? currentAction;
    private Label? overallAction;
    private ProgressBar? currentProgress;
    private ProgressBar? overallProgress;
    private InstallerProgress? lastProgress;

    private void BuildDiagnostics()
    {
        var line = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = Padding.Empty };
        line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        line.Controls.Add(details, 0, 0);
        details.LinkClicked += (_, _) => ToggleDetails();
        openLog = UtilityButton("Open setup log", () => { if (latestLog is not null) OpenPath(latestLog); });
        copyLog = UtilityButton("Copy details", CopyDetails);
        openLog.Visible = copyLog.Visible = false;
        line.Controls.Add(openLog, 1, 0);
        line.Controls.Add(copyLog, 2, 0);
        consoleHost.Controls.Add(console);
        Add(diagnostics, line);
        Add(diagnostics, consoleHost);
        diagnostics.Visible = false;
    }

    private static Button UtilityButton(string text, Action click)
    {
        var button = new Button { Text = text, AutoSize = true };
        InstallerTheme.Secondary(button);
        button.Click += (_, _) => click();
        return button;
    }

    private void ToggleDetails()
    {
        detailsOpen = !detailsOpen;
        details.Text = detailsOpen ? "Hide setup details" : "Show setup details";
        consoleHost.Visible = detailsOpen;
        openLog.Visible = copyLog.Visible = detailsOpen;
        if (detailsOpen)
            RenderHistory();
        FitDiagnostics();
        QueueFit();
    }

    private void FitDiagnostics()
    {
        if (detailsOpen)
            consoleHost.Height = Math.Clamp(ClientSize.Height * 3 / 10, Px(145), Px(300));
    }

    private void CopyDetails()
    {
        if (history.Count == 0)
            return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, history)); }
        catch (System.Runtime.InteropServices.ExternalException error) { Add(content, Label(error.Message)); }
    }

    private void WriteLog(InstallerLogEntry entry)
    {
        operationLog?.Write(entry);
        if (!IsHandleCreated || IsDisposed)
            return;
        BeginInvoke(() =>
        {
            string message = detailsOpen || entry.Level != InstallerLogLevel.Info ? entry.Message : FriendlyLog(entry);
            history.Add($"{entry.Timestamp.LocalDateTime:HH:mm:ss}  {message}");
            if (history.Count > 500)
                history.RemoveRange(0, 100);
            if (detailsOpen)
                RenderHistory();
        });
    }

    private static string FriendlyLog(InstallerLogEntry entry) => entry.Area switch
    {
        "Agreement" => "Hobby Release Terms accepted.",
        "Download" when entry.Message.StartsWith("Checking bundled candidate:", StringComparison.Ordinal) => "Checking a local file…",
        "Install" when entry.Message.StartsWith("Source game:", StringComparison.Ordinal) => "Original game verified.",
        _ => entry.Message
    };

    private void RenderHistory()
    {
        console.Lines = history.ToArray();
        console.SelectionStart = console.TextLength;
        console.ScrollToCaret();
        openLog.Enabled = latestLog is not null && File.Exists(latestLog);
        copyLog.Enabled = history.Count != 0;
    }

    private async Task RunAsync<T>(Func<Task<T>> work, Action<T> complete, Action<string> failed, bool log = false)
    {
        if (operation is not null)
            return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        SetTabs();
        T result = default!;
        string? failure = null;
        try
        {
            if (log)
            {
                operationLog = new InstallerLog(service.LogDirectory, additionalSink: options.LogSink);
                latestLog = operationLog.Path;
                diagnostics.Visible = true;
            }
            result = await Task.Run(work).ConfigureAwait(false);
        }
        catch (OperationCanceledException error)
        {
            failure = cancellation.IsCancellationRequested ? "Setup stopped. Verified downloads and completed work are available for Retry." : error.Message;
        }
        catch (Exception error) { failure = error.Message; }
        await InvokeAsync(() =>
        {
            operationLog?.Dispose();
            operationLog = null;
            operation = null;
            SetTabs();
            if (closeAfterStop)
            {
                Close();
                return;
            }
            if (failure is null)
                complete(result);
            else
                failed(failure);
        }).ConfigureAwait(false);
    }

    private async Task InstallAsync(InstallReview review)
    {
        string? accepted = agreed ? review.Notice?.Version : null;
        ShowActivity($"Installing {definition.Name}");
        var progress = new ActivityProgress(this);
        await RunAsync(async () =>
        {
            InstallResult result = await service.InstallReviewedAsync(review, this, progress, operation!.Token, accepted);
            return (result, await service.InspectAsync(sourceFolder, operation.Token));
        }, value =>
        {
            LoadStatus(value.Item2);
            SetupCompleted = value.result.SkippedComponents.Count == 0;
            if (SetupCompleted)
            {
                locationExpanded = false;
                ShowPlay();
            }
            else
                ShowPaused(string.Join(Environment.NewLine, value.result.SkippedComponents.Select(item => $"{item.Name}: {item.Reason}")));
        }, error => _ = RefreshPauseAsync(error), log: true);
    }

    private async Task RefreshPauseAsync(string error)
    {
        await RunAsync(() => service.InspectAsync(sourceFolder, operation!.Token), found =>
        {
            LoadStatus(found);
            ShowPaused(error);
        }, _ => ShowPaused(error));
    }

    private void ShowPaused(string message)
    {
        Page(Stage.Install, "Setup paused", message);
        Primary("Retry", async () => await PreviewAsync());
        if (package?.Offline != true)
            Link("Use an offline installer to resume…", () => ChooseOffline(() => ShowPaused(message)));
        if (definition.Managed?.AllowLimitedPlay == true && status?.InstalledVersion is not null && status.PendingRequest is not null)
            Link("Continue with installed features", () =>
            {
                try
                {
                    service.AcceptLimitedPlay();
                    status = status with { CanLaunch = definition.Launch is not null, LimitedPlayAccepted = true };
                    ShowPlay();
                }
                catch (Exception error) { Add(content, Label(error.Message)); }
            });
        Link("Change setup options", ShowSelection);
        Button("Close", Close, cancel: true);
    }

    private async Task PlayAsync()
    {
        ShowActivity($"Opening {definition.Name}", Stage.Play);
        await RunAsync(() => service.LaunchAsync(operation!.Token), _ => ShowPlay(), error =>
        {
            Page(Stage.Play, "Couldn’t start the game", error);
            Primary("Retry", async () => await PlayAsync());
            Button("Back", ShowPlay, cancel: true);
        }, log: true);
    }

    private async Task BeginRemoveAsync(Action back)
    {
        await RunAsync(() => service.PreviewRemoveAsync(operation!.Token), review => ConfirmRemoval(review, 0, back),
            error => { back(); Add(content, Label(error)); });
    }

    private void ConfirmRemoval(RemoveReview review, int index, Action back)
    {
        InstallReviewAction step = review.Steps[index];
        Page(Stage.Location, $"Uninstall {definition.Name} · {index + 1} of {review.Steps.Count}", step.Description);
        Add(content, Label(step.Name));
        bool last = index + 1 == review.Steps.Count;
        Primary(last ? "Confirm uninstall" : "Confirm and continue", async () =>
        {
            if (last)
                await RemoveAsync(review, back);
            else
                ConfirmRemoval(review, index + 1, back);
        });
        Button("Cancel", back, cancel: true);
    }

    private async Task RemoveAsync(RemoveReview review, Action back)
    {
        ShowActivity($"Uninstalling {definition.Name}");
        var progress = new ActivityProgress(this);
        await RunAsync(async () =>
        {
            RemoveResult removed = await service.RemoveReviewedAsync(review, RemovalApproval.All, this, progress, operation!.Token);
            return (removed, await service.InspectAsync(sourceFolder, operation.Token));
        }, value =>
        {
            LoadStatus(value.Item2);
            SetupCompleted = true;
            Page(Stage.Location, "Uninstall complete", $"Removed {value.removed.RemovedFiles:N0} owned files. Your saves and other personal files stay.");
            if (!options.UninstallOnOpen)
                Link("Return to setup", ShowLocation);
            Primary("Close", Close);
        }, error =>
        {
            Page(Stage.Location, "Uninstall paused", error);
            Primary("Retry", async () => await BeginRemoveAsync(back));
            Button("Back", back, cancel: true);
        }, log: true);
    }

    private void ShowActivity(string heading, Stage stage = Stage.Install, bool continuing = false)
    {
        if (!continuing)
            lastProgress = null;
        Page(stage, heading, "This may take a few minutes.");
        diagnostics.Visible = true;
        currentAction = Label("Getting things ready…");
        currentProgress = new ProgressBar { Maximum = 1000, Height = Px(22), Dock = DockStyle.Top, AccessibleName = "Current step" };
        overallAction = Label("");
        overallProgress = new ProgressBar { Maximum = 1000, Height = Px(18), Dock = DockStyle.Top, AccessibleName = "Overall progress" };
        Add(content, currentAction);
        Add(content, currentProgress);
        Add(content, overallAction);
        Add(content, overallProgress);
        Button("Stop setup", CancelOperation, cancel: true);
        if (lastProgress is { } progress)
            Report(progress);
    }

    private void Report(InstallerProgress progress)
    {
        lastProgress = progress;
        if (currentAction is null || currentAction.IsDisposed || currentProgress is null || overallAction is null || overallProgress is null)
            return;
        string message = detailsOpen || progress.Stage is null ? progress.Message : progress.Stage;
        if (progress.Total is > 0 && progress.Completed is long completed)
        {
            double fraction = Math.Clamp((double)completed / progress.Total.Value, 0, 1);
            currentProgress.Style = ProgressBarStyle.Blocks;
            currentProgress.Value = (int)(fraction * 1000);
            currentAction.Text = $"{message} · {fraction:P0}";
        }
        else
        {
            currentProgress.Style = ProgressBarStyle.Marquee;
            currentAction.Text = message;
        }
        int total = Math.Max(1, progress.StageCount);
        overallProgress.Value = Math.Clamp(progress.StagesCompleted * 1000 / total, 0, 1000);
        overallAction.Text = progress.StageCount > 0 ? $"{progress.StagesCompleted} of {total} steps complete" +
            (progress.NextStage is { } next ? $" · Next: {next}" : "") : "Setup is working…";
    }

    public Task<DownloadDecision> ChooseDownloadAsync(DownloadQuestion question, CancellationToken cancellationToken) =>
        AskAsync<DownloadDecision>(complete => ShowAsset(question, complete), cancellationToken);

    public Task<bool> ApproveCapabilityAsync(InstallerCapabilityQuestion question, CancellationToken cancellationToken) =>
        AskAsync<bool>(complete =>
        {
            Page(Stage.Install, "Use faster preparation?", $"This PC supports {question.Name}. The standard route is also available.");
            foreach (InstallReviewAsset asset in question.Assets)
                Add(content, Label($"{asset.FileName} · {SizeText(asset.Bytes)}{(asset.Url is null ? " · Local file" : $" · {asset.Url.IdnHost}")}"));
            DetailLink("Show file details", string.Join(Environment.NewLine, question.Assets.Select(asset =>
                $"{asset.FileName}\n{asset.Url}\nSHA-256 {asset.Sha256}")), () => ShowCapability(question, complete));
            AddCapabilityButtons(complete);
        }, cancellationToken);

    private void ShowCapability(InstallerCapabilityQuestion question, Action<bool> complete)
    {
        Page(Stage.Install, "Use faster preparation?", $"This PC supports {question.Name}. The standard route is also available.");
        foreach (InstallReviewAsset asset in question.Assets)
            Add(content, Label($"{asset.FileName} · {SizeText(asset.Bytes)}"));
        AddCapabilityButtons(complete);
    }

    private void AddCapabilityButtons(Action<bool> complete)
    {
        Primary("Use faster preparation", () => complete(true));
        Button("Use standard route", () => complete(false), cancel: true);
    }

    public Task<bool> ApprovePlatformChangesAsync(IReadOnlyList<InstallReviewAction> changes, CancellationToken cancellationToken) =>
        AskAsync<bool>(complete =>
        {
            Page(Stage.Install, "Review Windows changes", "Windows may ask for administrator approval after you continue.");
            foreach (InstallReviewAction change in changes)
                Add(content, Label($"{change.Name}{Environment.NewLine}{change.Description}"));
            Primary("Continue to Windows approval", () => complete(true));
            Button("Keep current settings", () => complete(false), cancel: true);
        }, cancellationToken);

    private void ShowAsset(DownloadQuestion question, Action<DownloadDecision> complete)
    {
        Page(Stage.Install, "Setup file needed", question.Asset.FileName + " · " + SizeText(question.Asset.Bytes));
        if (question.LastError is { } reason)
            Add(content, Label(reason));
        Add(content, Label(question.CanDownload ? $"Download the file or choose a verified local copy. Attempt {question.Attempt} of {question.MaxAttempts}." :
            "Choose the file or a folder that contains it. Setup also checks that folder for other missing files."));
        string detailsText = $"{question.Asset.FileName}\nSHA-256 {question.Asset.Sha256}\n{question.Asset.Url}";
        DetailLink("Show file details", detailsText, () => ShowAsset(question, complete));
        if (question.CanOpenDownloadLink && question.Asset.Url is { } url)
            Link("Open download link", () => OpenPath(url.AbsoluteUri));
        if (question.CanDownload)
            Primary("Download", () => complete(new DownloadDecision(DownloadChoice.Download)));
        Button("Choose file…", () => PickAsset(question, complete, folder: false), primary: !question.CanDownload);
        Button("Choose folder…", () => PickAsset(question, complete, folder: true));
        if (question.CanSkip)
            Button("Skip this extra", () => complete(new DownloadDecision(DownloadChoice.Skip)));
        Button("Stop setup", () => complete(new DownloadDecision(DownloadChoice.Cancel)), cancel: true);
    }

    private void PickAsset(DownloadQuestion question, Action<DownloadDecision> complete, bool folder)
    {
        Page(Stage.Install, folder ? "Choose a folder" : "Choose a local file", $"Select a verified copy of {question.Asset.FileName}.");
        Add(content, new InstallerPathPicker(options.ManualFileDirectory, folder, filename =>
            complete(new DownloadDecision(folder ? DownloadChoice.UseLocalFolder : DownloadChoice.UseLocalFile, filename)),
            () => ShowAsset(question, complete), folder ? "*" : question.Asset.FileName));
    }

    private async Task<T> AskAsync<T>(Action<Action<T>> show, CancellationToken token)
    {
        var answer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration cancel = token.Register(() => answer.TrySetCanceled(token));
        await InvokeAsync(() => show(value =>
        {
            ShowActivity($"Working on {definition.Name}", continuing: true);
            answer.TrySetResult(value);
        }), token);
        return await answer.Task.ConfigureAwait(false);
    }

    private void CancelOperation() => operation?.Cancel();

    private sealed class ActivityProgress(InstallerForm owner) : IProgress<InstallerProgress>
    {
        public void Report(InstallerProgress value)
        {
            if (owner.IsHandleCreated && !owner.IsDisposed)
                owner.BeginInvoke(() => owner.Report(value));
        }
    }
}
