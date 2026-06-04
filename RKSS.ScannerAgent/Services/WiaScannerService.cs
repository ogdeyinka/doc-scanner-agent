using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Options;
using RKSS.ScannerAgent.Contracts;
using RKSS.ScannerAgent.Models;
using RKSS.ScannerAgent.Options;

namespace RKSS.ScannerAgent.Services;

public sealed class WiaScannerService(IOptions<ScannerAgentOptions> options, ILogger<WiaScannerService> logger) : IScannerService
{
    private const int WIA_DPS_DOCUMENT_HANDLING_SELECT = 3088;
    private const int SCANNER_DEVICE_TYPE = 1;
    private const int FEEDER = 1;
    private const int FLATBED = 2;
    private const int DUPLEX = 4;
    private const int WIA_IPS_XRES = 6147;
    private const int WIA_IPS_YRES = 6148;
    private const int WIA_IPS_CUR_INTENT = 6146;
    private const int WIA_IPS_XEXTENT = 6151;
    private const int WIA_IPS_YEXTENT = 6152;
    private const int WIA_IPS_BRIGHTNESS = 6154;
    private const int WIA_IPS_CONTRAST = 6155;
    private const string JPEG = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";

    public IReadOnlyList<ScannerInfoDto> ListScanners()
    {
        var list = new List<ScannerInfoDto>();
        dynamic dm = CreateComObject("WIA.DeviceManager");
        for (int i = 1; i <= dm.DeviceInfos.Count; i++)
        {
            dynamic info = dm.DeviceInfos[i];
            if ((int)info.Type != SCANNER_DEVICE_TYPE) continue;
            var id = SafeGet(info.Properties, "Unique Device ID") ?? i.ToString();
            var name = SafeGet(info.Properties, "Name") ?? $"Scanner {i}";
            var vendor = SafeGet(info.Properties, "Manufacturer") ?? "Unknown";
            list.Add(new ScannerInfoDto(id, name, vendor, "WIA"));
        }
        return list;
    }

    public Task<ScanResult> ScanAsync(ScanRequest request, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<ScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(ScanCore(request, ct));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        ct.Register(() => completion.TrySetCanceled(ct));
        return completion.Task;
    }

    private ScanResult ScanCore(ScanRequest request, CancellationToken ct)
    {
        Directory.CreateDirectory(options.Value.StoragePath);
        var jobId = Guid.NewGuid().ToString("N");
        var result = new ScanResult { JobId = jobId };
        var deviceInfo = ResolveDevice(request.ScannerId);
        if (deviceInfo == null) throw new InvalidOperationException("No WIA scanner found. Install HP full scanner driver and confirm Windows can scan first.");

        result.ScannerName = SafeGet(deviceInfo.Properties, "Name") ?? "WIA Scanner";
        var device = deviceInfo.Connect();
        ConfigureDocumentHandling(device, request);

        int page = 0;
        while (page < request.MaxPages && !ct.IsCancellationRequested)
        {
            try
            {
                var item = device.Items[1];
                ConfigureItem(item, request);
                dynamic image;
                if (request.ShowScannerUi)
                {
                    dynamic dialog = CreateComObject("WIA.CommonDialog");
                    image = dialog.ShowTransfer(item, JPEG, true);
                }
                else
                {
                    image = item.Transfer(JPEG);
                }

                page++;
                var imagePath = Path.Combine(options.Value.StoragePath, $"nama_scan_{jobId}_{page:D3}.jpg");
                if (File.Exists(imagePath)) File.Delete(imagePath);
                image.SaveFile(imagePath);
                if (!request.RemoveBlankPages || !IsBlankPage(imagePath))
                {
                    result.ImagePaths.Add(imagePath);
                }
            }
            catch (COMException ex) when (IsNoMorePages(ex))
            {
                logger.LogInformation("ADF completed after {PageCount} pages", page);
                break;
            }
            catch (Exception ex)
            {
                if (page > 0) break;
                throw new InvalidOperationException($"Scanning failed: {ex.Message}", ex);
            }

            if (!request.UseAdf && !ShouldScanAnotherFlatbedPage(request, page)) break;
        }

        if (result.ImagePaths.Count == 0) throw new InvalidOperationException("No page was scanned.");
        return result;
    }

    private dynamic? ResolveDevice(string? scannerId)
    {
        dynamic dm = CreateComObject("WIA.DeviceManager");
        for (int i = 1; i <= dm.DeviceInfos.Count; i++)
        {
            dynamic info = dm.DeviceInfos[i];
            if ((int)info.Type != SCANNER_DEVICE_TYPE) continue;
            var id = SafeGet(info.Properties, "Unique Device ID") ?? i.ToString();
            var name = SafeGet(info.Properties, "Name") ?? "";
            if (string.IsNullOrWhiteSpace(scannerId) || id == scannerId || name.Contains(scannerId, StringComparison.OrdinalIgnoreCase))
                return info;
        }
        return null;
    }

    private static void ConfigureDocumentHandling(dynamic device, ScanRequest request)
    {
        var mode = request.PaperSource == "flatbed" ? FLATBED : FEEDER;
        if (request.Duplex) mode |= DUPLEX;
        SetProp(device.Properties, WIA_DPS_DOCUMENT_HANDLING_SELECT, mode);
    }

    private static void ConfigureItem(dynamic item, ScanRequest request)
    {
        SetProp(item.Properties, WIA_IPS_XRES, request.Dpi);
        SetProp(item.Properties, WIA_IPS_YRES, request.Dpi);
        SetProp(item.Properties, WIA_IPS_CUR_INTENT, request.ColorMode switch
        {
            "bw" => 4,
            "grayscale" => 2,
            _ => 1
        });
        SetProp(item.Properties, WIA_IPS_BRIGHTNESS, request.Brightness);
        SetProp(item.Properties, WIA_IPS_CONTRAST, request.Contrast);

        var size = request.PaperSize switch
        {
            "a4" => (width: 8.27, height: 11.69),
            "legal" => (width: 8.5, height: 14.0),
            "letter" => (width: 8.5, height: 11.0),
            _ => (width: 0.0, height: 0.0)
        };
        if (size.width > 0)
        {
            SetProp(item.Properties, WIA_IPS_XEXTENT, (int)Math.Round(size.width * request.Dpi));
            SetProp(item.Properties, WIA_IPS_YEXTENT, (int)Math.Round(size.height * request.Dpi));
        }
    }

    private static object CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException($"{progId} is not registered. Install the Windows scanner components and HP scanner driver.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Unable to create {progId}.");
    }

    private static void SetProp(dynamic props, object name, object value)
    {
        try
        {
            dynamic prop = props.Item(ref name);
            prop.Value = value;
        }
        catch { /* Driver may not support the property. */ }
    }

    private static string? SafeGet(dynamic props, object name)
    {
        try
        {
            dynamic prop = props.Item(ref name);
            return prop.Value?.ToString();
        }
        catch { return null; }
    }

    private static bool IsNoMorePages(COMException ex)
    {
        // Common WIA feeder empty / no documents patterns vary per HP driver.
        return ex.ErrorCode == unchecked((int)0x80210003) || ex.ErrorCode == unchecked((int)0x8021000C) || ex.Message.Contains("feeder", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldScanAnotherFlatbedPage(ScanRequest request, int page)
    {
        if (!request.PromptForMorePages || page >= request.MaxPages) return false;
        if (!Environment.UserInteractive) return false;

        try
        {
            var result = MessageBox.Show(
                "Scan another page from the flatbed?",
                "RKSS Scanner Agent",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            return result == DialogResult.Yes;
        }
        catch
        {
            return false;
        }
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
