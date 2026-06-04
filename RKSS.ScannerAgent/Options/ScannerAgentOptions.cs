namespace RKSS.ScannerAgent.Options;

public sealed class ScannerAgentOptions
{
    public string BindUrl { get; set; } = "http://127.0.0.1:8890";
    public string DesktopBindUrl { get; set; } = "http://127.0.0.1:8891";
    public string[] AllowedOrigins { get; set; } = [];
    public string StoragePath { get; set; } = @"C:\RKSS\ScannerAgent\Scans";
    public string DefaultBackendUploadUrl { get; set; } = "http://localhost/rkss-api/scanned-documents/upload";
    public string DefaultOcrTriggerUrl { get; set; } = "http://localhost/rkss-api/scanned-documents/trigger-ocr";
}
