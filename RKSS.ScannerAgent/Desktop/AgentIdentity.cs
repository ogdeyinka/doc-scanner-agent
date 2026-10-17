using System.Diagnostics;
using System.Drawing;
using System.Security.Principal;

namespace RKSS.ScannerAgent.Desktop;

/// <summary>Names and paths shared by the service, the desktop helper and the manager window.</summary>
internal static class AgentIdentity
{
    public const string ProductName = "RKSS Scanner Agent";
    public const string ServiceName = "RKSSScannerAgent";
    public const string ProtocolStartUrl = "rkss-scanner-agent://start";

    // Session-local names: one desktop helper and one manager window per signed-in user.
    public const string DesktopMutexName = @"Local\RKSSScannerAgent.Desktop";
    public const string DesktopStopEventName = @"Local\RKSSScannerAgent.Desktop.Stop";
    public const string ManagerMutexName = @"Local\RKSSScannerAgent.Manager";

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "RKSS.ScannerAgent.exe");
    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    public static string LogsPath => Path.Combine(AppContext.BaseDirectory, "logs");

    public static string Version
    {
        get
        {
            var version = FileVersionInfo.GetVersionInfo(ExePath).ProductVersion ?? "";
            var plus = version.IndexOf('+');
            return plus > 0 ? version[..plus] : version;
        }
    }

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static bool IsDesktopHelperRunning()
    {
        if (!Mutex.TryOpenExisting(DesktopMutexName, out var mutex)) return false;
        mutex.Dispose();
        return true;
    }

    /// <summary>Asks the desktop helper in this session to shut down.</summary>
    public static bool SignalDesktopHelperStop()
    {
        if (!EventWaitHandle.TryOpenExisting(DesktopStopEventName, out var stop)) return false;
        using (stop) stop.Set();
        return true;
    }

    public static Icon LoadIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(ExePath) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }
}
