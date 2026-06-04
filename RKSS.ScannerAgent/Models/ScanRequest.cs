namespace RKSS.ScannerAgent.Models;

public sealed class ScanRequest
{
    public string Provider { get; set; } = "auto"; // auto, wia, twain
    public string? ScannerId { get; set; }
    public int Dpi { get; set; } = 300;
    public string ColorMode { get; set; } = "color"; // color, grayscale, bw
    public string PaperSource { get; set; } = "feeder"; // auto, flatbed, feeder
    public string PaperSize { get; set; } = "default"; // default, a4, letter, legal
    public int Brightness { get; set; } = 0; // WIA range is commonly -1000..1000
    public int Contrast { get; set; } = 0; // WIA range is commonly -1000..1000
    public bool UseAdf { get; set; } = true;
    public bool Duplex { get; set; } = false;
    public bool PromptForMorePages { get; set; } = false;
    public bool RemoveBlankPages { get; set; } = false;
    public int MaxPages { get; set; } = 25;
    public string OutputFormat { get; set; } = "pdf"; // pdf, jpg
    public bool ShowScannerUi { get; set; } = false;
    public bool AutoUpload { get; set; } = true;
    public bool TriggerOcr { get; set; } = true;
    public string? BackendUploadUrl { get; set; }
    public string? OcrTriggerUrl { get; set; }
    public string? RkssAuthToken { get; set; }
    public string? DocumentType { get; set; }
    public string? RelatedModule { get; set; }
    public string? RelatedRecordId { get; set; }
    public string? UploadedBy { get; set; }

    public void Normalize()
    {
        Dpi = Dpi is < 100 or > 600 ? 300 : Dpi;
        MaxPages = MaxPages is < 1 or > 100 ? 25 : MaxPages;
        Brightness = Math.Clamp(Brightness, -1000, 1000);
        Contrast = Math.Clamp(Contrast, -1000, 1000);
        Provider = Provider.ToLowerInvariant() switch { "wia" => "wia", "twain" => "twain", _ => "auto" };
        ColorMode = ColorMode.ToLowerInvariant() switch { "bw" or "blackwhite" => "bw", "grayscale" or "gray" => "grayscale", _ => "color" };
        PaperSource = PaperSource.ToLowerInvariant() switch { "flatbed" => "flatbed", "auto" => "auto", _ => UseAdf ? "feeder" : "flatbed" };
        PaperSize = PaperSize.ToLowerInvariant() switch { "a4" => "a4", "letter" => "letter", "legal" => "legal", _ => "default" };
        UseAdf = PaperSource == "feeder" || PaperSource == "auto";
        OutputFormat = OutputFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "pdf";
    }
}
