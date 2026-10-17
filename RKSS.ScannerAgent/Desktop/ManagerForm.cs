using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RKSS.ScannerAgent.Desktop;

/// <summary>
/// "RKSS Scanner Agent Manager": shows whether the scanner service (WIA) and the desktop
/// helper (TWAIN) are running, starts / stops them, and edits appsettings.json
/// (allowed web addresses, ports, scans folder). Changing settings or controlling the
/// Windows service needs administrator rights; the window offers to reopen elevated.
/// </summary>
internal sealed class ManagerForm : Form
{
    private const string DiasOrigin = "https://dias.nama.gov.ng";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(1.5) };
    private static readonly Color Green = Color.FromArgb(21, 128, 61);
    private static readonly Color Amber = Color.FromArgb(180, 83, 9);
    private static readonly Color Red = Color.FromArgb(185, 28, 28);

    private readonly bool _elevated = AgentIdentity.IsElevated;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 4000 };
    private readonly ToolTip _toolTip = new();

    private readonly Label _serviceStatus = StatusLabel();
    private readonly Label _helperStatus = StatusLabel();
    private readonly Button _serviceStart = SmallButton("Start");
    private readonly Button _serviceStop = SmallButton("Stop");
    private readonly Button _serviceRestart = SmallButton("Restart");
    private readonly Button _helperStart = SmallButton("Start");
    private readonly Button _helperStop = SmallButton("Stop");

    private readonly ListBox _origins = new() { Dock = DockStyle.Fill, IntegralHeight = false, Height = 110 };
    private readonly TextBox _originInput = new() { Dock = DockStyle.Fill, PlaceholderText = "https://example.gov.ng" };
    private readonly Button _originAdd = SmallButton("Add");
    private readonly Button _originRemove = SmallButton("Remove");
    private readonly TextBox _serviceUrl = new() { Dock = DockStyle.Fill };
    private readonly TextBox _desktopUrl = new() { Dock = DockStyle.Fill };
    private readonly TextBox _storagePath = new() { Dock = DockStyle.Fill };
    private readonly Button _browse = SmallButton("Browse…");
    private readonly CheckBox _serviceAutoStart = new() { Text = "Start the scanner service automatically when Windows starts", AutoSize = true };
    private readonly CheckBox _helperAutoStart = new() { Text = "Start the desktop helper in the background when anyone signs in", AutoSize = true };
    private readonly Button _save = new() { Text = "Save and apply", AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
    private readonly Button _close = new() { Text = "Close", AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
    private readonly Label _message = new() { AutoSize = true, MaximumSize = new Size(600, 0), Padding = new Padding(0, 4, 0, 4) };

    private JsonNode? _settings;
    private string _savedServiceUrl = "http://127.0.0.1:8890";
    private string _savedDesktopUrl = "http://127.0.0.1:8891";
    private bool _dirty;
    private bool _busy;
    private bool _refreshing;

    public ManagerForm()
    {
        // Sizes below are written for 96 DPI; Windows scales them up on high-resolution screens.
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"{AgentIdentity.ProductName} Manager";
        Icon = AgentIdentity.LoadIcon();
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 560);
        Size = new Size(680, 760);
        BackColor = Color.White;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(18, 14, 18, 12),
            AutoScroll = true,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = AgentIdentity.ProductName,
            Font = new Font("Segoe UI Semibold", 14f),
            AutoSize = true,
        });
        root.Controls.Add(new Label
        {
            Text = $"Version {AgentIdentity.Version} · lets DIAS in your browser use the scanners connected to this computer.",
            ForeColor = Color.DimGray,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8),
        });

        if (!_elevated) root.Controls.Add(BuildAdminBanner());
        root.Controls.Add(BuildStatusGroup());
        root.Controls.Add(BuildSettingsGroup());
        root.Controls.Add(BuildStartupGroup());

        // Messages and Save / Close stay visible below the scrolling area.
        Controls.Add(BuildFooter());
        root.BringToFront();

        WireEvents();
        LoadSettings();
        ApplyPermissions();
        ResumeLayout(false);
        PerformLayout();
    }

    // ── Layout ──────────────────────────────────────────────────────────────

    private Control BuildAdminBanner()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Color.FromArgb(255, 251, 235),
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0, 0, 0, 10),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = "Changing settings or starting / stopping the scanner service needs administrator rights.",
            ForeColor = Amber,
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Anchor = AnchorStyles.Left,
        }, 0, 0);
        var elevate = new Button { Text = "Run as administrator", AutoSize = true, Anchor = AnchorStyles.Right };
        elevate.Click += (_, _) => RelaunchElevated();
        panel.Controls.Add(elevate, 1, 0);
        return panel;
    }

    private Control BuildStatusGroup()
    {
        var group = Group("Status");
        var grid = Grid(3);
        grid.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 190);
        grid.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100);
        grid.ColumnStyles[2] = new ColumnStyle(SizeType.AutoSize);

        grid.Controls.Add(RowLabel("Scanner service (WIA)"), 0, 0);
        grid.Controls.Add(_serviceStatus, 1, 0);
        grid.Controls.Add(ButtonRow(_serviceStart, _serviceStop, _serviceRestart), 2, 0);

        grid.Controls.Add(RowLabel("Desktop helper (TWAIN)"), 0, 1);
        grid.Controls.Add(_helperStatus, 1, 1);
        grid.Controls.Add(ButtonRow(_helperStart, _helperStop), 2, 1);

        grid.Controls.Add(Hint("The service starts with Windows. The desktop helper starts when you sign in and runs in the background — find its icon next to the clock."), 0, 2);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 2)!, 3);

        group.Controls.Add(grid);
        return group;
    }

    private Control BuildSettingsGroup()
    {
        var group = Group("Settings");
        var grid = Grid(2);
        grid.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 190);
        grid.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100);
        var row = 0;

        grid.Controls.Add(RowLabel("Allowed web addresses"), 0, row);
        var originsPanel = Grid(2);
        originsPanel.Margin = Padding.Empty;
        originsPanel.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);
        originsPanel.ColumnStyles[1] = new ColumnStyle(SizeType.AutoSize);
        originsPanel.Controls.Add(_origins, 0, 0);
        var listButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = Padding.Empty };
        listButtons.Controls.Add(_originRemove);
        originsPanel.Controls.Add(listButtons, 1, 0);
        originsPanel.Controls.Add(_originInput, 0, 1);
        originsPanel.Controls.Add(_originAdd, 1, 1);
        grid.Controls.Add(originsPanel, 1, row++);
        grid.Controls.Add(Hint($"Only these websites may use the scanner. DIAS is {DiasOrigin}. This computer's own addresses (localhost) are always allowed."), 1, row++);

        grid.Controls.Add(RowLabel("Service address (WIA)"), 0, row);
        grid.Controls.Add(_serviceUrl, 1, row++);
        grid.Controls.Add(RowLabel("Desktop helper address (TWAIN)"), 0, row);
        grid.Controls.Add(_desktopUrl, 1, row++);
        grid.Controls.Add(Hint("DIAS expects ports 8890 and 8891. Change them only if another program already uses these ports. Addresses must stay on 127.0.0.1 (this computer only)."), 1, row++);

        grid.Controls.Add(RowLabel("Scans folder"), 0, row);
        var pathPanel = Grid(2);
        pathPanel.Margin = Padding.Empty;
        pathPanel.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);
        pathPanel.ColumnStyles[1] = new ColumnStyle(SizeType.AutoSize);
        pathPanel.Controls.Add(_storagePath, 0, 0);
        pathPanel.Controls.Add(_browse, 1, 0);
        grid.Controls.Add(pathPanel, 1, row++);
        grid.Controls.Add(Hint("Where scanned pages are kept on this computer before they are sent to DIAS."), 1, row);

        group.Controls.Add(grid);
        return group;
    }

    private Control BuildStartupGroup()
    {
        var group = Group("Startup");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(_serviceAutoStart);
        panel.Controls.Add(_helperAutoStart);
        panel.Controls.Add(Hint("Recommended: both on. The service handles most scanners (WIA); the helper is needed for scanners that only offer TWAIN."));
        group.Controls.Add(panel);
        return group;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(18, 4, 18, 12),
            BackColor = Color.FromArgb(248, 250, 252),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_message, 0, 0);
        footer.SetColumnSpan(_message, 2);

        var left = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        var logs = new LinkLabel { Text = "Open logs folder", AutoSize = true, Margin = new Padding(0, 6, 14, 0) };
        logs.LinkClicked += (_, _) => OpenFolder(AgentIdentity.LogsPath);
        var scans = new LinkLabel { Text = "Open scans folder", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        scans.LinkClicked += (_, _) => OpenFolder(_storagePath.Text.Trim());
        left.Controls.Add(logs);
        left.Controls.Add(scans);
        footer.Controls.Add(left, 0, 1);

        var right = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        right.Controls.Add(_save);
        right.Controls.Add(_close);
        footer.Controls.Add(right, 1, 1);
        AcceptButton = _save;
        CancelButton = _close;
        return footer;
    }

    // ── Events ──────────────────────────────────────────────────────────────

    private void WireEvents()
    {
        Shown += async (_, _) => await RefreshStatusAsync();
        _timer.Tick += async (_, _) => await RefreshStatusAsync();
        _timer.Start();
        FormClosing += OnFormClosing;

        _serviceStart.Click += async (_, _) => await RunBusyAsync("Starting the scanner service…", () => ControlService(start: true, stop: false));
        _serviceStop.Click += async (_, _) => await RunBusyAsync("Stopping the scanner service…", () => ControlService(start: false, stop: true));
        _serviceRestart.Click += async (_, _) => await RunBusyAsync("Restarting the scanner service…", () => ControlService(start: true, stop: true));
        _helperStart.Click += async (_, _) => await RunBusyAsync("Starting the desktop helper…", StartHelperAsync);
        _helperStop.Click += async (_, _) => await RunBusyAsync("Closing the desktop helper…", StopHelperAsync);

        _originAdd.Click += (_, _) => AddOrigin();
        _originInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AddOrigin();
        };
        _originRemove.Click += (_, _) =>
        {
            if (_origins.SelectedIndex < 0) return;
            _origins.Items.RemoveAt(_origins.SelectedIndex);
            _dirty = true;
        };
        _serviceUrl.TextChanged += (_, _) => _dirty = true;
        _desktopUrl.TextChanged += (_, _) => _dirty = true;
        _storagePath.TextChanged += (_, _) => _dirty = true;
        _serviceAutoStart.CheckedChanged += (_, _) => _dirty = true;
        _helperAutoStart.CheckedChanged += (_, _) => _dirty = true;
        _browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _storagePath.Text, Description = "Folder for scanned pages" };
            if (dialog.ShowDialog(this) == DialogResult.OK) _storagePath.Text = dialog.SelectedPath;
        };
        _save.Click += async (_, _) => await SaveAndApplyAsync();
        _close.Click += (_, _) => Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            return;
        }
        if (_dirty && _elevated &&
            MessageBox.Show(this, "Close without saving your changes?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            e.Cancel = true;
        }
    }

    private void AddOrigin()
    {
        var text = _originInput.Text.Trim();
        if (text.Length == 0) return;
        if (!TryNormalizeOrigin(text, out var origin))
        {
            ShowMessage($"“{text}” is not a web address. Use the form https://name.domain (no page path).", Red);
            return;
        }
        if (_origins.Items.Cast<string>().Any(o => o.Equals(origin, StringComparison.OrdinalIgnoreCase)))
        {
            ShowMessage($"{origin} is already in the list.", Amber);
        }
        else
        {
            _origins.Items.Add(origin);
            _dirty = true;
            ShowMessage("");
        }
        _originInput.Clear();
    }

    // ── Settings ────────────────────────────────────────────────────────────

    private void LoadSettings()
    {
        try
        {
            _settings = JsonNode.Parse(File.ReadAllText(AgentIdentity.SettingsPath),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var section = _settings?["ScannerAgent"];
            _savedServiceUrl = section?["BindUrl"]?.GetValue<string>() ?? _savedServiceUrl;
            _savedDesktopUrl = section?["DesktopBindUrl"]?.GetValue<string>() ?? _savedDesktopUrl;
            _serviceUrl.Text = _savedServiceUrl;
            _desktopUrl.Text = _savedDesktopUrl;
            _storagePath.Text = section?["StoragePath"]?.GetValue<string>() ?? @"C:\RKSS\ScannerAgent\Scans";
            _origins.Items.Clear();
            if (section?["AllowedOrigins"] is JsonArray origins)
            {
                foreach (var origin in origins)
                {
                    var value = origin?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(value)) _origins.Items.Add(value);
                }
            }
        }
        catch (Exception ex)
        {
            _settings = null;
            ShowMessage($"The settings file could not be read ({AgentIdentity.SettingsPath}): {ex.Message}", Red);
        }
        _serviceAutoStart.Checked = ServiceStartsAutomatically();
        _helperAutoStart.Checked = HelperStartsAtSignIn();
        _dirty = false;
    }

    private async Task SaveAndApplyAsync()
    {
        if (_settings is null) return;

        if (!TryNormalizeBindUrl(_serviceUrl.Text, out var serviceUrl))
        {
            ShowMessage("Service address must look like http://127.0.0.1:8890.", Red);
            _serviceUrl.Focus();
            return;
        }
        if (!TryNormalizeBindUrl(_desktopUrl.Text, out var desktopUrl))
        {
            ShowMessage("Desktop helper address must look like http://127.0.0.1:8891.", Red);
            _desktopUrl.Focus();
            return;
        }
        if (serviceUrl == desktopUrl)
        {
            ShowMessage("The service and the desktop helper need different ports.", Red);
            return;
        }
        var origins = _origins.Items.Cast<string>().ToList();
        if (origins.Count == 0)
        {
            ShowMessage("Add at least one allowed web address, for example " + DiasOrigin + ".", Red);
            return;
        }
        var storage = _storagePath.Text.Trim();
        if (!Path.IsPathFullyQualified(storage))
        {
            ShowMessage("Scans folder must be a full path, for example C:\\RKSS\\ScannerAgent\\Scans.", Red);
            return;
        }

        await RunBusyAsync("Saving settings and restarting the agent…", async () =>
        {
            Directory.CreateDirectory(storage);
            var section = _settings["ScannerAgent"] as JsonObject ?? new JsonObject();
            _settings["ScannerAgent"] = section;
            section["BindUrl"] = serviceUrl;
            section["DesktopBindUrl"] = desktopUrl;
            section["AllowedOrigins"] = new JsonArray(origins.Select(o => (JsonNode?)JsonValue.Create(o)).ToArray());
            section["StoragePath"] = storage;

            var path = AgentIdentity.SettingsPath;
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, _settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Copy(path, path + ".bak", overwrite: true);
            File.Move(temp, path, overwrite: true);
            _savedServiceUrl = serviceUrl;
            _savedDesktopUrl = desktopUrl;
            await SetServiceAutoStartAsync(_serviceAutoStart.Checked);
            SetHelperStartsAtSignIn(_helperAutoStart.Checked);
            _dirty = false;

            // Both programs read the settings when they start.
            if (ServiceInstalled()) await ControlService(start: true, stop: true);
            if (AgentIdentity.IsDesktopHelperRunning())
            {
                await StopHelperAsync();
                await StartHelperAsync();
            }
            return "Settings saved. The scanner service and desktop helper are using them now.";
        });
    }

    private void ApplyPermissions()
    {
        var canEdit = _elevated && _settings is not null;
        foreach (var control in new Control[] { _originInput, _originAdd, _originRemove, _browse, _save })
        {
            control.Enabled = canEdit;
        }
        _serviceAutoStart.Enabled = canEdit && ServiceInstalled();
        _helperAutoStart.Enabled = canEdit;
        _serviceUrl.ReadOnly = _desktopUrl.ReadOnly = _storagePath.ReadOnly = !canEdit;
        _save.Visible = _elevated;
    }

    // ── Service and helper control ──────────────────────────────────────────

    private static bool ServiceInstalled()
    {
        try
        {
            using var controller = new ServiceController(AgentIdentity.ServiceName);
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // ── Startup with Windows ────────────────────────────────────────────────

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "RKSSScannerAgentDesktop";

    private static bool ServiceStartsAutomatically()
    {
        try
        {
            using var controller = new ServiceController(AgentIdentity.ServiceName);
            return controller.StartType == ServiceStartMode.Automatic;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task SetServiceAutoStartAsync(bool automatic)
    {
        if (!ServiceInstalled() || ServiceStartsAutomatically() == automatic) return;
        using var sc = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"),
            $"config {AgentIdentity.ServiceName} start= {(automatic ? "auto" : "demand")}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        await sc.WaitForExitAsync();
        if (sc.ExitCode != 0) throw new InvalidOperationException($"Could not change how the scanner service starts (sc.exe exit code {sc.ExitCode}).");
    }

    /// <summary>The installer registers the helper for every user (HKLM Run); the manager switches that on or off.</summary>
    private static bool HelperStartsAtSignIn()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    private static void SetHelperStartsAtSignIn(bool enabled)
    {
        using var key = Registry.LocalMachine.CreateSubKey(RunKeyPath, writable: true);
        if (enabled) key.SetValue(RunValueName, $"\"{AgentIdentity.ExePath}\" --desktop");
        else key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private static Task<string?> ControlService(bool start, bool stop) => Task.Run(() =>
    {
        using var controller = new ServiceController(AgentIdentity.ServiceName);
        if (stop && controller.Status != ServiceControllerStatus.Stopped)
        {
            if (controller.Status != ServiceControllerStatus.StopPending) controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
        if (start)
        {
            controller.Refresh();
            if (controller.Status != ServiceControllerStatus.Running)
            {
                if (controller.Status != ServiceControllerStatus.StartPending) controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
        }
        return (string?)null;
    });

    private async Task<string?> StartHelperAsync()
    {
        if (AgentIdentity.IsDesktopHelperRunning()) return null;
        if (_elevated)
        {
            // Start it through Explorer so it runs as the signed-in user, not as administrator.
            Process.Start(new ProcessStartInfo("explorer.exe", AgentIdentity.ProtocolStartUrl) { UseShellExecute = false });
        }
        else
        {
            Process.Start(new ProcessStartInfo(AgentIdentity.ExePath, "--desktop") { UseShellExecute = true });
        }
        for (var i = 0; i < 40 && !AgentIdentity.IsDesktopHelperRunning(); i++) await Task.Delay(250);
        return AgentIdentity.IsDesktopHelperRunning() ? null : "The desktop helper did not start. Check the logs folder.";
    }

    private static async Task<string?> StopHelperAsync()
    {
        if (!AgentIdentity.SignalDesktopHelperStop()) return null;
        for (var i = 0; i < 40 && AgentIdentity.IsDesktopHelperRunning(); i++) await Task.Delay(250);
        return AgentIdentity.IsDesktopHelperRunning() ? "The desktop helper did not close. Sign out and back in, or restart the computer." : null;
    }

    private async Task RunBusyAsync(string working, Func<Task<string?>> action)
    {
        if (_busy) return;
        _busy = true;
        UseWaitCursor = true;
        SetButtonsEnabled(false);
        ShowMessage(working, Color.DimGray);
        try
        {
            var result = await action();
            ShowMessage(result ?? "Done.", result is null || result.StartsWith("Settings saved") ? Green : Amber);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.ServiceProcess.TimeoutException or IOException or UnauthorizedAccessException)
        {
            ShowMessage(ex.InnerException?.Message ?? ex.Message, Red);
        }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
            ApplyPermissions();
            await RefreshStatusAsync();
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        foreach (var button in new[] { _serviceStart, _serviceStop, _serviceRestart, _helperStart, _helperStop, _save, _originAdd, _originRemove, _browse })
        {
            button.Enabled = enabled;
        }
    }

    // ── Status ──────────────────────────────────────────────────────────────

    private async Task RefreshStatusAsync()
    {
        if (_refreshing || _busy || IsDisposed) return;
        _refreshing = true;
        try
        {
            ServiceControllerStatus? status = null;
            try
            {
                using var controller = new ServiceController(AgentIdentity.ServiceName);
                status = controller.Status;
            }
            catch (InvalidOperationException)
            {
                // not installed
            }

            var serviceAnswers = status == ServiceControllerStatus.Running && await HealthAsync(_savedServiceUrl) is not null;
            var helperRunning = AgentIdentity.IsDesktopHelperRunning();
            var helperHealth = helperRunning ? await HealthAsync(_savedDesktopUrl) : null;
            var helperAnswers = helperHealth is not null;
            var twainMissing = helperHealth?["twain"]?.GetValue<bool>() == false;
            if (IsDisposed) return;

            switch (status)
            {
                case null:
                    SetStatus(_serviceStatus, "Not installed", Red);
                    break;
                case ServiceControllerStatus.Running:
                    SetStatus(_serviceStatus, serviceAnswers ? $"Running — {Short(_savedServiceUrl)}" : "Running, but not answering", serviceAnswers ? Green : Amber);
                    break;
                case ServiceControllerStatus.Stopped:
                    SetStatus(_serviceStatus, "Stopped", Red);
                    break;
                default:
                    SetStatus(_serviceStatus, status.ToString()!.Replace("Pending", "…"), Amber);
                    break;
            }

            if (!helperRunning) SetStatus(_helperStatus, "Not running", Red);
            else if (twainMissing) SetStatus(_helperStatus, "Running, but no TWAIN driver is installed", Amber);
            else SetStatus(_helperStatus, helperAnswers ? $"Running — {Short(_savedDesktopUrl)}" : "Starting…", helperAnswers ? Green : Amber);
            _toolTip.SetToolTip(_helperStatus, twainMissing
                ? "The 64-bit TWAIN Data Source Manager (twaindsm.dll) is missing. Scanners that also support WIA work through the scanner service."
                : "");

            if (!_busy)
            {
                var canControl = _elevated && status is not null;
                _serviceStart.Enabled = canControl && status == ServiceControllerStatus.Stopped;
                _serviceStop.Enabled = canControl && status == ServiceControllerStatus.Running;
                _serviceRestart.Enabled = canControl && status == ServiceControllerStatus.Running;
                _helperStart.Enabled = !helperRunning;
                _helperStop.Enabled = helperRunning;
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static async Task<JsonNode?> HealthAsync(string baseUrl)
    {
        try
        {
            using var response = await Http.GetAsync($"{baseUrl.TrimEnd('/')}/health");
            if (!response.IsSuccessStatusCode) return null;
            return JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? new JsonObject();
        }
        catch
        {
            return null;
        }
    }

    private static void SetStatus(Label label, string text, Color color)
    {
        label.Text = "● " + text;
        label.ForeColor = color;
    }

    private static string Short(string url) => url.Replace("http://", "");

    private void ShowMessage(string text, Color? color = null)
    {
        _message.Text = text;
        _message.ForeColor = color ?? Color.DimGray;
    }

    // ── Validation ──────────────────────────────────────────────────────────

    internal static bool TryNormalizeOrigin(string text, out string origin)
    {
        origin = "";
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0) return false;
        origin = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return true;
    }

    internal static bool TryNormalizeBindUrl(string text, out string url)
    {
        url = "";
        var trimmed = text.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1" || uri.AbsolutePath != "/" || uri.Query.Length > 0) return false;
        if (!trimmed.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) || uri.Port < 1024) return false;
        url = $"http://127.0.0.1:{uri.Port}";
        return true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void RelaunchElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AgentIdentity.ExePath, "--manage --relaunched") { UseShellExecute = true, Verb = "runas" });
            _dirty = false;
            Close();
        }
        catch (Win32Exception)
        {
            ShowMessage("Administrator rights were not granted. Ask your IT officer to change these settings.", Amber);
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not open {path}: {ex.Message}", Amber);
        }
    }

    private static GroupBox Group(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        AutoSize = true,
        Padding = new Padding(10, 6, 10, 8),
        Margin = new Padding(0, 0, 0, 10),
    };

    private static TableLayoutPanel Grid(int columns)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = columns };
        for (var i = 0; i < columns; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    private static Label RowLabel(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 4) };

    private static Label StatusLabel() => new() { Text = "● Checking…", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.DimGray, Margin = new Padding(0, 8, 8, 4) };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        ForeColor = Color.DimGray,
        Font = new Font("Segoe UI", 8.5f),
        Margin = new Padding(0, 0, 0, 8),
    };

    private static Button SmallButton(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(2) };

    private static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(buttons);
        return row;
    }
}
