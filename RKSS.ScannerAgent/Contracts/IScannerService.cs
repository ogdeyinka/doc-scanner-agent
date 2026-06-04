using RKSS.ScannerAgent.Models;

namespace RKSS.ScannerAgent.Contracts;

public interface IScannerService
{
    IReadOnlyList<ScannerInfoDto> ListScanners();
    Task<ScanResult> ScanAsync(ScanRequest request, CancellationToken ct);
}
