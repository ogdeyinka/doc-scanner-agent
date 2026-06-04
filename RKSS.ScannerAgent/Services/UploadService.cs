using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RKSS.ScannerAgent.Models;
using RKSS.ScannerAgent.Options;

namespace RKSS.ScannerAgent.Services;

public sealed class UploadService(HttpClient http, IOptions<ScannerAgentOptions> options)
{
    public async Task<UploadResponse> UploadAsync(string filePath, ScanRequest request, CancellationToken ct)
    {
        var url = request.BackendUploadUrl ?? options.Value.DefaultBackendUploadUrl;
        using var form = new MultipartFormDataContent();
        await using var fs = File.OpenRead(filePath);
        var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(filePath.EndsWith(".pdf") ? "application/pdf" : "image/jpeg");
        form.Add(fileContent, "file", Path.GetFileName(filePath));
        form.Add(new StringContent(request.DocumentType ?? "audit_document"), "document_type");
        form.Add(new StringContent(request.RelatedModule ?? "general"), "related_module");
        form.Add(new StringContent(request.RelatedRecordId ?? ""), "related_record_id");
        form.Add(new StringContent(request.UploadedBy ?? "scanner-agent"), "uploaded_by");

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        var authToken = request.RkssAuthToken;
        if (!string.IsNullOrWhiteSpace(authToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        var res = await http.SendAsync(req, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);
        string? docId = null;
        try
        {
            using var json = JsonDocument.Parse(raw);
            if (json.RootElement.TryGetProperty("document_id", out var p)) docId = p.GetString();
            if (json.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("document_id", out var p2)) docId = p2.GetString();
        }
        catch { }

        return new UploadResponse { Success = res.IsSuccessStatusCode, DocumentId = docId, Message = res.ReasonPhrase, Raw = raw };
    }

    public async Task<OcrResponse> TriggerOcrAsync(string documentId, ScanRequest request, CancellationToken ct)
    {
        var url = request.OcrTriggerUrl ?? options.Value.DefaultOcrTriggerUrl;
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = JsonContent.Create(new { document_id = documentId });
        var authToken = request.RkssAuthToken;
        if (!string.IsNullOrWhiteSpace(authToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        var res = await http.SendAsync(req, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);
        return new OcrResponse { Success = res.IsSuccessStatusCode, Message = res.ReasonPhrase, Raw = raw };
    }
}
