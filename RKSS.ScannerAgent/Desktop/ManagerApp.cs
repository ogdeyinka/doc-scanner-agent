using System.Windows.Forms;

namespace RKSS.ScannerAgent.Desktop;

internal static class ManagerApp
{
    public static void Run(bool relaunched)
    {
        using var single = new Mutex(false, AgentIdentity.ManagerMutexName);
        bool owned;
        try
        {
            // After "Run as administrator" the first window is still closing; give it a moment.
            owned = single.WaitOne(relaunched ? 5000 : 0);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }
        if (!owned) return; // already open

        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ManagerForm());
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        single.ReleaseMutex();
    }
}
