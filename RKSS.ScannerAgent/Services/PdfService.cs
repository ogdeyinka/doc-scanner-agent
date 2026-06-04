using Microsoft.Extensions.Options;
using RKSS.ScannerAgent.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RKSS.ScannerAgent.Services;

public sealed class PdfService(IOptions<ScannerAgentOptions> options)
{
    public string CreatePdf(IReadOnlyList<string> imagePaths, string jobId)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var pdfPath = Path.Combine(options.Value.StoragePath, $"nama_scan_{jobId}.pdf");

        Document.Create(container =>
        {
            foreach (var img in imagePaths)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(10);
                    page.Content().Image(img).FitArea();
                });
            }
        }).GeneratePdf(pdfPath);

        return pdfPath;
    }
}
