using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Options;
using NTwain;
using NTwain.Data;
using RKSS.ScannerAgent.Contracts;
using RKSS.ScannerAgent.Models;
using RKSS.ScannerAgent.Options;

namespace RKSS.ScannerAgent.Services;

public sealed class TwainScannerService(IOptions<ScannerAgentOptions> options, ILogger<TwainScannerService> logger) : IScannerService
{
    public const string MissingDsmMessage =
        "TWAIN is not available on this computer: the 64-bit TWAIN Data Source Manager (twaindsm.dll) is not installed. Install the scanner maker's TWAIN driver, or use the scanner through WIA.";

    private static readonly Lazy<bool> DsmAvailable = new(() =>
    {
        // NTwain calls twaindsm.dll on its own message-loop thread; if the DLL is missing the
        // exception is raised there and ends the whole process, so check before opening a session.
        if (!NativeLibrary.TryLoad("twaindsm.dll", typeof(TwainScannerService).Assembly, null, out var handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    });

    /// <summary>True when the 64-bit TWAIN Data Source Manager is installed (System32 or next to the agent).</summary>
    public static bool IsTwainAvailable => DsmAvailable.Value;

    public IReadOnlyList<ScannerInfoDto> ListScanners()
    {
        if (!Environment.UserInteractive)
        {
            logger.LogInformation("TWAIN discovery skipped because the agent is not running in an interactive desktop session.");
            return [];
        }
        if (!IsTwainAvailable)
        {
            logger.LogWarning(MissingDsmMessage);
            return [];
        }

        return RunOnSta(() =>
        {
            var session = OpenSession();
            try
            {
                return session.GetSources()
                    .Select(source => new ScannerInfoDto(source.Name, source.Name, "TWAIN", "TWAIN"))
                    .ToList();
            }
            finally
            {
                CloseSession(session);
            }
        });
    }

    public Task<ScanResult> ScanAsync(ScanRequest request, CancellationToken ct)
    {
        if (!Environment.UserInteractive)
        {
            throw new InvalidOperationException("TWAIN scanning requires the RKSS Scanner Agent to run in the signed-in user's desktop session. Use WIA for Windows Service deployments.");
        }
        if (!IsTwainAvailable) throw new InvalidOperationException(MissingDsmMessage);

        return Task.Run(() => RunOnSta(() => ScanCore(request, ct)), ct);
    }

    private ScanResult ScanCore(ScanRequest request, CancellationToken ct)
    {
        Directory.CreateDirectory(options.Value.StoragePath);
        var jobId = Guid.NewGuid().ToString("N");
        var result = new ScanResult { JobId = jobId };
        using var form = new Form
        {
            ShowInTaskbar = false,
            WindowState = FormWindowState.Minimized,
            Width = 1,
            Height = 1,
            Opacity = 0
        };

        var session = OpenSession();
        try
        {
            var source = ResolveSource(session, request.ScannerId)
                ?? throw new InvalidOperationException("No TWAIN scanner found. Install the scanner's TWAIN driver and run the agent interactively.");

            result.ScannerName = source.Name;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            session.DataTransferred += (_, e) =>
            {
                try
                {
                    var imagePath = SaveTransferredImage(e, jobId, result.ImagePaths.Count + 1);
                    if (!string.IsNullOrWhiteSpace(imagePath) && (!request.RemoveBlankPages || !IsBlankPage(imagePath)))
                    {
                        result.ImagePaths.Add(imagePath);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to save TWAIN transferred page");
                }
            };

            session.TransferError += (_, _) => completion.TrySetException(new InvalidOperationException("TWAIN transfer failed."));
            session.SourceDisabled += (_, _) => completion.TrySetResult();

            using var registration = ct.Register(() =>
            {
                try { source.Close(); } catch { }
                completion.TrySetCanceled(ct);
            });

            form.Load += (_, _) =>
            {
                try
                {
                    if (source.Open() != ReturnCode.Success)
                    {
                        completion.TrySetException(new InvalidOperationException($"Unable to open TWAIN scanner \"{source.Name}\"."));
                        return;
                    }

                    ConfigureSource(source, request);
                    var mode = request.ShowScannerUi ? SourceEnableMode.ShowUI : SourceEnableMode.NoUI;
                    var showUi = request.ShowScannerUi || !source.Capabilities.CapUIControllable.IsSupported;
                    if (source.Enable(mode, showUi, form.Handle) != ReturnCode.Success)
                    {
                        completion.TrySetException(new InvalidOperationException($"Unable to start TWAIN scanner \"{source.Name}\"."));
                    }
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            };

            completion.Task.ContinueWith(_ => form.BeginInvoke(new Action(form.Close)));
            Application.Run(form);
            completion.Task.GetAwaiter().GetResult();
        }
        finally
        {
            CloseSession(session);
        }

        if (result.ImagePaths.Count == 0) throw new InvalidOperationException("No page was scanned.");
        return result;
    }

    private TwainSession OpenSession()
    {
        var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetEntryAssembly() ?? typeof(TwainScannerService).Assembly);
        var session = new TwainSession(appId)
        {
            SynchronizationContext = SynchronizationContext.Current
        };
        if (session.State < 3)
        {
            session.Open();
        }
        return session;
    }

    private static void CloseSession(TwainSession session)
    {
        try
        {
            if (session.State == 4)
            {
                session.CurrentSource.Close();
            }
            if (session.State == 3)
            {
                session.Close();
            }
        }
        catch { }
    }

    private static DataSource? ResolveSource(TwainSession session, string? scannerId)
    {
        var sources = session.GetSources().ToList();
        if (string.IsNullOrWhiteSpace(scannerId)) return sources.FirstOrDefault();
        return sources.FirstOrDefault(s =>
            string.Equals(s.Name, scannerId, StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains(scannerId, StringComparison.OrdinalIgnoreCase));
    }

    private static void ConfigureSource(DataSource source, ScanRequest request)
    {
        TrySet(() => source.Capabilities.ICapXResolution.SetValue(request.Dpi));
        TrySet(() => source.Capabilities.ICapYResolution.SetValue(request.Dpi));
        TrySet(() => source.Capabilities.ICapPixelType.SetValue(request.ColorMode switch
        {
            "bw" => PixelType.BlackWhite,
            "grayscale" => PixelType.Gray,
            _ => PixelType.RGB
        }));
        TrySet(() => source.Capabilities.CapDuplexEnabled.SetValue(request.Duplex ? BoolType.True : BoolType.False));
        TrySet(() => source.Capabilities.ICapSupportedSizes.SetValue(request.PaperSize switch
        {
            "a4" => SupportedSize.A4,
            "letter" => SupportedSize.USLetter,
            "legal" => SupportedSize.USLegal,
            _ => SupportedSize.None
        }));
        TrySet(() => source.Capabilities.CapXferCount.SetValue(request.MaxPages));
    }

    private string? SaveTransferredImage(DataTransferredEventArgs e, string jobId, int page)
    {
        var imagePath = Path.Combine(options.Value.StoragePath, $"rkss_twain_{jobId}_{page:D3}.jpg");
        if (File.Exists(imagePath)) File.Delete(imagePath);

        if (e.NativeData != IntPtr.Zero)
        {
            using var stream = e.GetNativeImageStream();
            if (stream == null) return null;
            using var image = Image.FromStream(stream);
            image.Save(imagePath, ImageFormat.Jpeg);
            return imagePath;
        }

        if (!string.IsNullOrWhiteSpace(e.FileDataPath) && File.Exists(e.FileDataPath))
        {
            using var image = Image.FromFile(e.FileDataPath);
            image.Save(imagePath, ImageFormat.Jpeg);
            return imagePath;
        }

        return null;
    }

    private static T RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error != null) throw error;
        return result!;
    }

    private static void TrySet(Func<ReturnCode> setter)
    {
        try { _ = setter(); }
        catch { }
    }

    private static bool IsBlankPage(string imagePath)
    {
        try
        {
            using var bitmap = new Bitmap(imagePath);
            var strideX = Math.Max(1, bitmap.Width / 40);
            var strideY = Math.Max(1, bitmap.Height / 40);
            var brightPixels = 0;
            var sampledPixels = 0;

            for (var y = 0; y < bitmap.Height; y += strideY)
            {
                for (var x = 0; x < bitmap.Width; x += strideX)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    var brightness = (pixel.R + pixel.G + pixel.B) / 3;
                    if (brightness > 245) brightPixels++;
                    sampledPixels++;
                }
            }

            return sampledPixels > 0 && brightPixels / (double)sampledPixels > 0.985;
        }
        catch
        {
            return false;
        }
    }
}
