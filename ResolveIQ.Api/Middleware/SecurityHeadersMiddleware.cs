namespace ResolveIQ.Api.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        if (!headers.ContainsKey("X-Content-Type-Options"))
            headers["X-Content-Type-Options"] = "nosniff";

        if (!headers.ContainsKey("X-Frame-Options"))
            headers["X-Frame-Options"] = "DENY";

        if (!headers.ContainsKey("Referrer-Policy"))
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        if (!headers.ContainsKey("Content-Security-Policy"))
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

        if (!headers.ContainsKey("Permissions-Policy"))
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        if (!headers.ContainsKey("X-Permitted-Cross-Domain-Policies"))
            headers["X-Permitted-Cross-Domain-Policies"] = "none";

        await _next(context);
    }
}
