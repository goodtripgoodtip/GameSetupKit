using System.Diagnostics;

namespace GameInstaller.WinForms;

public sealed partial class InstallerForm : Form, IInstallerSetupInteraction
{
    private enum Stage { Location, Review, Install, Play }

    private readonly InstallerDefinition definition;
    private readonly string packageDirectory;
    private readonly InstallerWindowOptions options;
    private readonly TableLayoutPanel stages = Stack();
    private readonly TableLayoutPanel page = Stack();
    private readonly TableLayoutPanel content = Stack();
    private readonly TableLayoutPanel actions = Stack();
    private readonly FlowLayoutPanel consent = Flow();
    private readonly FlowLayoutPanel buttons = Flow();
    private readonly Panel body = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
    private readonly TableLayoutPanel frame = Stack();
    private readonly Label title = Label("");
    private readonly Label summary = Label("");
    private readonly Panel divider = new() { Height = 1, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 18) };
    private readonly List<Button> tabs = [];
    private InstallerService service;
    private InstallerStatus? status;
    private InstallerPackage? package;
    private string location;
    private string? runtimeParent;
    private string? sourceFolder;
    private bool custom;
    private bool localOnly;
    private bool allowHardware;
    private bool locationExpanded;
    private bool agreed;
    private bool closeAfterStop;
    private bool firstRun = true;
    private bool fitQueued;
    private float scale = 1;
    private InstallerNetworkChoice network;
    private InstallerShortcuts shortcuts;
    private InstallIntent intent;
    private readonly HashSet<string> selection = new(StringComparer.OrdinalIgnoreCase);
    private Stage currentStage;
    private CancellationTokenSource? operation;

    public InstallerForm(InstallerDefinition definition, string packageDirectory, InstallerWindowOptions? options = null)
    {
        this.definition = definition;
        this.packageDirectory = Path.GetFullPath(packageDirectory);
        this.options = options ?? new InstallerWindowOptions();
        package = this.options.Package;
        location = this.options.DefaultProductDirectory ?? (this.options.DefaultInstallParent is { } parent
            ? InstallerService.GetProductDirectory(definition.Id, parent)
            : InstallerService.FindManagedInstallation(definition, this.options.Platform) ?? (definition.Managed is not null
                ? InstallerService.GetManagedProductDirectory(definition) : InstallerService.GetProductDirectory(definition.Id)));
        sourceFolder = this.options.DefaultSourceGameDirectory;
        shortcuts = definition.Managed?.DefaultShortcuts ?? new InstallerShortcuts(false, false);
        localOnly = package?.Offline == true;
        ResetSelection();
        service = CreateService();

        Application.SetColorMode(SystemColorMode.Dark);
        Text = this.options.Title ?? $"{definition.Name} Setup · {definition.Version}";
        Icon = this.options.Icon ?? Icon;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = package is null ? new Size(720, 510) : new Size(700, 535);
        ClientSize = package is null ? new Size(1020, 650) : new Size(1020, 615);
        InstallerTheme.Apply(this);
        InstallerTheme.Console(console);
        InstallerTheme.Apply(details);
        divider.BackColor = InstallerTheme.Divider;
        title.ForeColor = InstallerTheme.Heading(this.options.AccentColor);
        BuildFrame();
        InstallerWindow.FitWorkArea(this, .54, .55);
        InstallerWindow.ScaleText(this, new Size(1000, 540), title, console, value =>
        {
            scale = value;
            page.Padding = new Padding(Px(42), Px(32), Px(42), 0);
            actions.Padding = new Padding(Px(42), 0, Px(42), Px(20));
            diagnostics.Padding = new Padding(Px(42), Px(6), Px(42), Px(7));
            foreach (Button tab in tabs)
                tab.MinimumSize = new Size(0, Px(50));
            FitPage();
        });
        Resize += (_, _) => FitPage();
        DpiChanged += (_, _) => QueueFit();
        FormClosing += (_, args) =>
        {
            if (operation is null)
                return;
            args.Cancel = true;
            closeAfterStop = true;
            operation.Cancel();
        };
        Shown += async (_, _) => await StartAsync();
        ShowLocation();
    }

    public bool SetupCompleted { get; private set; }

    public static void ShowStartupError(string title, string message, Icon? icon = null)
    {
        Application.SetColorMode(SystemColorMode.Dark);
        using var window = new Form { Text = title, Icon = icon, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(820, 380) };
        InstallerTheme.Apply(window);
        var layout = Stack();
        layout.Padding = new Padding(36, 26, 36, 22);
        var heading = Label(title);
        var problem = Label(message);
        problem.MaximumSize = new Size(740, 0);
        Add(layout, heading);
        Add(layout, problem);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        InstallerTheme.Primary(close);
        Add(layout, close);
        window.Controls.Add(layout);
        window.CancelButton = close;
        InstallerWindow.ScaleText(window, new Size(820, 380), heading);
        window.ShowDialog();
    }

    private InstallerService CreateService() => new(definition, packageDirectory, installParent: null, logger: new WindowLogger(WriteLog),
        options: new InstallerServiceOptions
        {
            ProductDirectory = location, RuntimeParent = runtimeParent, Platform = options.Platform,
            Package = package, LauncherExecutable = options.LauncherExecutable, UseInstalledPayload = options.OpenInstalledProduct
        });

    private void BuildFrame()
    {
        stages.ColumnCount = 4;
        stages.RowCount = 1;
        stages.BackColor = InstallerTheme.StageBar;
        foreach (Stage stage in Enum.GetValues<Stage>())
        {
            stages.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var tab = new Button { Text = stage.ToString(), Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty,
                AccessibleRole = AccessibleRole.PageTab };
            InstallerTheme.Secondary(tab);
            tab.Click += (_, _) => Navigate(stage);
            tabs.Add(tab);
            stages.Controls.Add(tab, (int)stage, 0);
        }
        Add(page, title);
        Add(page, summary);
        Add(page, divider);
        Add(page, content);
        body.Controls.Add(page);
        BuildDiagnostics();
        buttons.FlowDirection = FlowDirection.RightToLeft;
        Add(actions, consent);
        Add(actions, buttons);
        frame.Dock = DockStyle.Fill;
        frame.AutoSize = false;
        Add(frame, stages);
        Add(frame, body, SizeType.Percent, 100);
        Add(frame, diagnostics);
        Add(frame, actions);
        Controls.Add(frame);
    }

    private void Navigate(Stage stage)
    {
        if (operation is not null || currentStage == stage)
            return;
        if (stage == Stage.Location)
            ShowLocation();
        else if (stage == Stage.Review && (definition.Source is null || status?.SourceGame is not null))
            ShowSelection();
        else if (stage == Stage.Play && status?.CanLaunch == true)
            ShowPlay();
    }

    private void Page(Stage stage, string heading, string? description = null)
    {
        currentStage = stage;
        title.Text = heading;
        summary.Text = description ?? "";
        summary.Visible = !string.IsNullOrEmpty(description);
        Clear(content);
        Clear(buttons);
        Clear(consent);
        consent.Visible = false;
        AcceptButton = CancelButton = null;
        body.AutoScrollPosition = Point.Empty;
        SetTabs();
        QueueFit();
    }

    private void SetTabs()
    {
        for (int index = 0; index < tabs.Count; index++)
        {
            Stage stage = (Stage)index;
            Button tab = tabs[index];
            bool selected = currentStage == stage;
            tab.BackColor = selected ? InstallerTheme.Divider : InstallerTheme.StageBar;
            tab.ForeColor = selected ? ForeColor : InstallerTheme.MutedText;
            tab.AccessibleDescription = selected ? "Current setup stage" : "Setup stage";
            tab.TabStop = selected || (operation is null && (stage == Stage.Location || (stage == Stage.Review && status is not null) ||
                (stage == Stage.Play && status?.CanLaunch == true)));
        }
    }

    private Button Button(string text, Action click, bool primary = false, bool cancel = false)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(Px(112), Px(44)), Margin = new Padding(7, 6, 0, 0) };
        if (primary)
            InstallerTheme.Primary(button);
        else
            InstallerTheme.Secondary(button);
        button.Click += (_, _) => click();
        buttons.Controls.Add(button);
        if (primary)
            buttons.Controls.SetChildIndex(button, 0);
        if (primary)
            AcceptButton = button;
        if (cancel)
            CancelButton = button;
        return button;
    }

    private void Primary(string text, Action click) => Button(text, click, primary: true);

    private void Link(string text, Action click)
    {
        var link = MakeLink(text);
        link.LinkClicked += (_, _) => click();
        Add(content, link);
    }

    private static LinkLabel MakeLink(string text)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 3, 0, 7) };
        InstallerTheme.Apply(link);
        return link;
    }

    private static Label Label(string text) => new() { Text = text, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 9) };

    private static TableLayoutPanel Stack() => new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty
    };

    private static FlowLayoutPanel Flow() => new()
    { Dock = DockStyle.Top, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };

    private static void Add(TableLayoutPanel stack, Control child, SizeType size = SizeType.AutoSize, float height = 0)
    {
        int row = stack.RowCount++;
        stack.RowStyles.Add(new RowStyle(size, height));
        stack.Controls.Add(child, 0, row);
    }

    private static void Clear(Control parent)
    {
        while (parent.Controls.Count > 0)
            parent.Controls[0].Dispose();
        if (parent is TableLayoutPanel stack)
        {
            stack.RowStyles.Clear();
            stack.RowCount = 0;
        }
    }

    private int Px(int value) => (int)MathF.Round(value * scale);

    private void FitPage()
    {
        int width = Math.Max(180, body.ClientSize.Width - page.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
        title.MaximumSize = new Size(width, 0);
        summary.MaximumSize = new Size(width, 0);
        foreach (Control element in content.Controls)
            if (element is System.Windows.Forms.Label)
                element.MaximumSize = new Size(width, 0);
        FitDiagnostics();
        QueueFit();
    }

    private void QueueFit()
    {
        if (!IsHandleCreated || fitQueued)
            return;
        fitQueued = true;
        BeginInvoke(() =>
        {
            if (IsDisposed)
                return;
            fitQueued = false;
            if (WindowState != FormWindowState.Normal)
                return;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int cap = Math.Max(MinimumSize.Height, area.Height - 32);
            int chrome = Height - ClientSize.Height + actions.PreferredSize.Height + stages.PreferredSize.Height + diagnostics.PreferredSize.Height;
            int wanted = Math.Min(cap, page.PreferredSize.Height + chrome + 6);
            if (Height < wanted)
                SetBounds(Left, Math.Clamp(Top - (wanted - Height) / 2, area.Top, Math.Max(area.Top, area.Bottom - wanted)), Width, wanted);
        });
    }

    private void OpenPath(string path)
    {
        try { using Process? launch = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception error) { Add(content, Label(error.Message)); }
    }

    private sealed class WindowLogger(Action<InstallerLogEntry> write) : IInstallerLogger
    {
        public void Write(InstallerLogEntry entry) => write(entry);
    }
}
