using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace RKSS.ScannerAgent.Desktop;

/// <summary>
/// The desktop (TWAIN) helper runs in the background with no window: only an icon in the
/// notification area, whose menu opens the manager window or closes the helper.
/// </summary>
internal static class DesktopTray
{
    public static void Start(IHostApplicationLifetime lifetime, string bindUrl)
    {
        // The manager window (or a new installer) asks the helper to close through this event.
        var stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AgentIdentity.DesktopStopEventName);
        ThreadPool.RegisterWaitForSingleObject(stopEvent, (_, _) => lifetime.StopApplication(), null, Timeout.Infinite, true);

        var thread = new Thread(() => RunTray(lifetime, bindUrl))
        {
            IsBackground = true,
            Name = "RKSS tray icon",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void RunTray(IHostApplicationLifetime lifetime, string bindUrl)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var context = new ApplicationContext();
        using var menu = new ContextMenuStrip();
        using var icon = new NotifyIcon
        {
            Icon = AgentIdentity.LoadIcon(),
            Text = $"{AgentIdentity.ProductName} — running ({bindUrl.Replace("http://", "")})",
            ContextMenuStrip = menu,
            Visible = true,
        };

        var manage = menu.Items.Add("Manage scanner agent…", null, (_, _) => OpenManager());
        manage.Font = new Font(manage.Font, FontStyle.Bold);
        menu.Items.Add("Open status page", null, (_, _) => OpenUrl($"{bindUrl}/health"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Close desktop helper", null, (_, _) => lifetime.StopApplication());
        icon.DoubleClick += (_, _) => OpenManager();

        // When the web host stops (menu, manager window, installer), remove the icon and end this thread.
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        lifetime.ApplicationStopping.Register(() => ui.Post(_ =>
        {
            icon.Visible = false;
            context.ExitThread();
        }, null));

        Application.Run(context);
    }

    private static void OpenManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AgentIdentity.ExePath, "--manage") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open the manager window: {ex.Message}", AgentIdentity.ProductName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No default browser; nothing else to do.
        }
    }
}
