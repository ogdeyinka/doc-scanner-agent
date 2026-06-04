namespace RKSS.ScannerAgent.Models;

public sealed class ScanResult
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");
    public string ScannerName { get; set; } = "";
    public List<string> ImagePaths { get; set; } = [];
    public string? PdfPath { get; set; }
    public UploadResponse? UploadResponse { get; set; }
    public OcrResponse? OcrResponse { get; set; }
}

public sealed class UploadResponse
{
    public bool Success { get; set; }
    public string? DocumentId { get; set; }
    public string? Message { get; set; }
    public string? Raw { get; set; }
}

public sealed class OcrResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Raw { get; set; }
}
