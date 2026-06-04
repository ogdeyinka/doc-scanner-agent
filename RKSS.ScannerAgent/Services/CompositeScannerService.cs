using RKSS.ScannerAgent.Contracts;
using RKSS.ScannerAgent.Models;

namespace RKSS.ScannerAgent.Services;

public sealed class CompositeScannerService(
    WiaScannerService wia,
    TwainScannerService twain,
    ILogger<CompositeScannerService> logger) : IScannerService
{
    public IReadOnlyList<ScannerInfoDto> ListScanners()
    {
        var scanners = new List<ScannerInfoDto>();
        AddScanners(scanners, "wia", () => wia.ListScanners());
        AddScanners(scanners, "twain", () => twain.ListScanners());
        return scanners;
    }

    public Task<ScanResult> ScanAsync(ScanRequest request, CancellationToken ct)
    {
        var provider = ResolveProvider(request);
        request.ScannerId = StripProviderPrefix(request.ScannerId);

        return provider switch
        {
            "twain" => twain.ScanAsync(request, ct),
            "wia" => wia.ScanAsync(request, ct),
            _ => wia.ScanAsync(request, ct),
        };
    }

    private void AddScanners(List<ScannerInfoDto> target, string provider, Func<IReadOnlyList<ScannerInfoDto>> scan)
    {
        try
        {
            target.AddRange(scan().Select(s => s with { Id = $"{provider}:{s.Id}" }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Provider} scanner discovery failed", provider.ToUpperInvariant());
        }
    }

    private static string ResolveProvider(ScanRequest request)
    {
        if (request.ScannerId?.StartsWith("twain:", StringComparison.OrdinalIgnoreCase) == true) return "twain";
        if (request.ScannerId?.StartsWith("wia:", StringComparison.OrdinalIgnoreCase) == true) return "wia";
        return request.Provider;
    }

    private static string? StripProviderPrefix(string? scannerId)
    {
        if (string.IsNullOrWhiteSpace(scannerId)) return scannerId;
        var separator = scannerId.IndexOf(':');
        return separator > 0 ? scannerId[(separator + 1)..] : scannerId;
    }
}
