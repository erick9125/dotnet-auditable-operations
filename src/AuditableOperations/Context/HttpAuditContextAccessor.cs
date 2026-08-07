using System.Security.Claims;
using AuditableOperations.Abstractions;
using AuditableOperations.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AuditableOperations.Context;

public sealed class HttpAuditContextAccessor : IAuditContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuditableOperationsOptions _options;

    public HttpAuditContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        IOptions<AuditableOperationsOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    public AuditContext GetCurrent()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return new AuditContext();
        }

        return new AuditContext
        {
            UserId = _options.CaptureUser ? ResolveUserId(httpContext.User) : null,
            TenantId = _options.CaptureTenant ? ResolveTenantId(httpContext.User) : null,
            CorrelationId = httpContext.TraceIdentifier,
            Source = BuildSource(httpContext)
        };
    }

    private static string? ResolveUserId(ClaimsPrincipal user)
    {
        return user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.Identity?.Name;
    }

    private static string? ResolveTenantId(ClaimsPrincipal user)
    {
        return user.FindFirst("tenant_id")?.Value
            ?? user.FindFirst("tenant")?.Value;
    }

    private static string? BuildSource(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value;
        if (string.IsNullOrWhiteSpace(path))
        {
            return method;
        }

        return $"{method} {path}";
    }
}
