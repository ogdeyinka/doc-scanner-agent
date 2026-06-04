using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RKSS.ScannerAgent.Contracts;
using RKSS.ScannerAgent.Models;
using RKSS.ScannerAgent.Options;
using RKSS.ScannerAgent.Security;
using RKSS.ScannerAgent.Services;
using Serilog;
using System.ServiceProcess;

var runAsDesktop = args.Any(arg =>
    arg.Equals("--desktop", StringComparison.OrdinalIgnoreCase) ||
    arg.Equals("--interactive", StringComparison.OrdinalIgnoreCase));

if (args.Any(arg => arg.Equals("--start-service", StringComparison.OrdinalIgnoreCase)))
{
    const string serviceName = "RKSSScannerAgent";
    try
    {
        using var controller = new ServiceController(serviceName);
        if (controller.Status != ServiceControllerStatus.Running &&
            controller.Status != ServiceControllerStatus.StartPending)
        {
            controller.Start();
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Unable to start {serviceName}: {ex.Message}");
        Environment.ExitCode = 1;
    }

    return;
}

var webArgs = args
    .Where(arg =>
        !arg.Equals("--desktop", StringComparison.OrdinalIgnoreCase) &&
        !arg.Equals("--interactive", StringComparison.OrdinalIgnoreCase) &&
        !arg.StartsWith("rkss-scanner-agent:", StringComparison.OrdinalIgnoreCase))
    .ToArray();

var builder = WebApplication.CreateBuilder(webArgs);

if (!runAsDesktop)
{
    builder.Host.UseWindowsService();
}

builder.Host.UseSerilog((ctx, cfg) => cfg
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "scanner-agent-.log"), rollingInterval: RollingInterval.Day));

builder.Services.Configure<ScannerAgentOptions>(builder.Configuration.GetSection("ScannerAgent"));
builder.Services.AddSingleton<WiaScannerService>();
builder.Services.AddSingleton<TwainScannerService>();
builder.Services.AddSingleton<IScannerService>(sp => runAsDesktop
    ? sp.GetRequiredService<TwainScannerService>()
    : sp.GetRequiredService<WiaScannerService>());
builder.Services.AddSingleton<PdfService>();
builder.Services.AddHttpClient<UploadService>();

builder.Services.AddCors(options =>
{
    var allowed = builder.Configuration.GetSection("ScannerAgent:AllowedOrigins").Get<string[]>() ?? [];
    var allowedSet = allowed
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    options.AddPolicy("RkssReact", p => p
        .SetIsOriginAllowed(origin =>
        {
            if (allowedSet.Contains(origin)) return true;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("[::1]", StringComparison.OrdinalIgnoreCase) ||
                   uri.IsLoopback;
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Content-Disposition", "X-RKSS-Page-Count", "X-RKSS-Scanner-Name"));
});

var app = builder.Build();
var scannerAgentOptions = app.Services.GetRequiredService<IOptions<ScannerAgentOptions>>().Value;

app.Use(async (context, next) =>
{
    var origin = context.Request.Headers.Origin.ToString();
    if (IsTrustedWebOrigin(origin, scannerAgentOptions.AllowedOrigins))
    {
        context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        context.Response.Headers["Vary"] = "Origin";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
        var requestedHeaders = context.Request.Headers.AccessControlRequestHeaders.ToString();
        context.Response.Headers["Access-Control-Allow-Headers"] =
            string.IsNullOrWhiteSpace(requestedHeaders) ? "Content-Type,Accept" : requestedHeaders;
        context.Response.Headers["Access-Control-Expose-Headers"] =
            "Content-Disposition,X-RKSS-Page-Count,X-RKSS-Scanner-Name";
        context.Response.Headers["Access-Control-Max-Age"] = "86400";
    }

    if (HttpMethods.IsOptions(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    await next();
});

app.UseCors("RkssReact");
app.UseMiddleware<LocalTokenMiddleware>();

var agentMode = runAsDesktop ? "desktop-twain" : "service-wia";
var bindUrl = runAsDesktop ? scannerAgentOptions.DesktopBindUrl : scannerAgentOptions.BindUrl;

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "RKSS Scanner Agent",
    mode = agentMode,
    interactive = Environment.UserInteractive,
    time = DateTimeOffset.Now
}));

app.MapGet("/scanners", ([FromServices] IScannerService scanner) => Results.Ok(scanner.ListScanners()));

app.MapPost("/scan", async ([FromBody] ScanRequest request,
    [FromServices] IScannerService scanner,
    [FromServices] PdfService pdf,
    [FromServices] UploadService uploader,
    [FromServices] IOptions<ScannerAgentOptions> options,
    CancellationToken ct) =>
{
    request.Normalize();
    var result = await scanner.ScanAsync(request, ct);

    if (request.OutputFormat.Equals("pdf", StringComparison.OrdinalIgnoreCase))
    {
        result.PdfPath = pdf.CreatePdf(result.ImagePaths, result.JobId);
    }

    if (request.AutoUpload)
    {
        var uploadPath = result.PdfPath ?? result.ImagePaths.First();
        result.UploadResponse = await uploader.UploadAsync(uploadPath, request, ct);
    }

    if (request.TriggerOcr && !string.IsNullOrWhiteSpace(result.UploadResponse?.DocumentId))
    {
        result.OcrResponse = await uploader.TriggerOcrAsync(result.UploadResponse.DocumentId!, request, ct);
    }

    return Results.Ok(result);
});

app.MapPost("/scan/download", async ([FromBody] ScanRequest request,
    [FromServices] IScannerService scanner,
    [FromServices] PdfService pdf,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    try
    {
        request.AutoUpload = false;
        request.TriggerOcr = false;
        request.Normalize();
        var result = await scanner.ScanAsync(request, ct);
        var path = request.OutputFormat.Equals("pdf", StringComparison.OrdinalIgnoreCase)
            ? pdf.CreatePdf(result.ImagePaths, result.JobId)
            : result.ImagePaths.First();
        httpContext.Response.Headers["X-RKSS-Page-Count"] = result.ImagePaths.Count.ToString();
        httpContext.Response.Headers["X-RKSS-Scanner-Name"] = result.ScannerName;
        return Results.File(
            await File.ReadAllBytesAsync(path, ct),
            request.OutputFormat == "pdf" ? "application/pdf" : "image/jpeg",
            Path.GetFileName(path)
        );
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Scan download failed");
        return Results.Json(new { success = false, message = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.Run(bindUrl);

static bool IsTrustedWebOrigin(string origin, string[] configuredOrigins)
{
    if (string.IsNullOrWhiteSpace(origin)) return false;

    if (configuredOrigins.Any(configuredOrigin =>
            string.Equals(configuredOrigin, origin, StringComparison.OrdinalIgnoreCase)))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

    return uri.IsLoopback ||
           uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
           uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
           uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
           uri.Host.Equals("[::1]", StringComparison.OrdinalIgnoreCase);
}
