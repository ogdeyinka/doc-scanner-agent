using System.Net;

namespace RKSS.ScannerAgent.Security;

public sealed class LocalTokenMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        if (!IsLoopbackRequest(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "Scanner agent only accepts local machine requests." });
            return;
        }

        await next(context);
    }

    private static bool IsLoopbackRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null) return true;
        if (IPAddress.IsLoopback(remote)) return true;

        if (remote.IsIPv4MappedToIPv6)
        {
            return IPAddress.IsLoopback(remote.MapToIPv4());
        }

        return false;
    }
}
