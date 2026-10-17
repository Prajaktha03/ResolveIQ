using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.Health;
using ResolveIQ.Api.Hubs;
using ResolveIQ.Api.Middleware;
using ResolveIQ.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind to Render's assigned port if PORT environment variable is set
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(renderPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{renderPort}");
}

// PostgreSQL database
var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var connectionString = ConnectionStringHelper.Normalize(rawConnectionString);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("Database", tags: ["ready"]);

// Application services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<RoleSetupService>();
builder.Services.AddSingleton<SlaService>();
builder.Services.AddScoped<ISlaMonitoringService, SlaMonitoringService>();
builder.Services.AddHostedService<SlaMonitoringBackgroundService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSignalR();
builder.Services.AddHttpClient("AiService");
builder.Services.AddScoped<IKnowledgeRetrievalService, KnowledgeRetrievalService>();
builder.Services.AddScoped<IAiIntelligenceService, AiIntelligenceService>();

// JWT configuration & validation
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("Jwt:Key is missing. Please configure a 256-bit base64-encoded secret.");
}

byte[] signingKeyBytes;
try
{
    signingKeyBytes = Convert.FromBase64String(jwtKey);
}
catch (FormatException)
{
    throw new InvalidOperationException("Jwt:Key is not a valid base64-encoded string.");
}

if (signingKeyBytes.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 256 bits (32 bytes).");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "ResolveIQ";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "ResolveIQ.Clients";
var signingKey = new SymmetricSecurityKey(signingKeyBytes);

// Authentication
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,

            ValidateAudience = true,
            ValidAudience = jwtAudience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Support passing JWT token via query string for WebSocket connections to the SignalR hub
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notifications"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// Authorization
builder.Services.AddAuthorization();

// Explicit, configurable CORS policy
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (builder.Environment.IsDevelopment()
        ? ["http://localhost:3000", "https://localhost:3000", "http://localhost:5173", "https://localhost:5173"]
        : []);

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else
        {
            policy.SetIsOriginAllowed(_ => false);
        }
    });
});

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Type = "https://httpstatuses.com/429",
            Title = "Too Many Requests",
            Detail = "Rate limit exceeded. Please try again later.",
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: token);
    };

    var authPermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthPermitLimit", 10);
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    var generalPermitLimit = builder.Configuration.GetValue<int>("RateLimiting:GeneralPermitLimit", 100);
    options.AddPolicy("general", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = generalPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    var aiPermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AiPermitLimit", 20);
    options.AddPolicy("ai", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = aiPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Forwarded headers for reverse proxies (ALB, Nginx, Cloudflare, Envoy, Ingress)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (builder.Configuration.GetValue<bool>("ForwardedHeaders:ForwardAllProxies", false))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

// Controllers and Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste your JWT token here."
    });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
});

var app = builder.Build();

// Safe diagnostic log of effective PostgreSQL configuration (no credentials or connection strings logged)
if (!string.IsNullOrWhiteSpace(connectionString))
{
    try
    {
        var diag = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        app.Logger.LogInformation(
            "PostgreSQL configured: Host={Host}, Port={Port}, Database={Database}, SslMode={SslMode}, TrustServerCertificate={TrustServerCertificate}, GssEncryptionMode={GssEncryptionMode}, Timeout={Timeout}s",
            diag.Host,
            diag.Port,
            diag.Database,
            diag.SslMode,
#pragma warning disable CS0618
            diag.TrustServerCertificate,
#pragma warning restore CS0618
            diag.GssEncryptionMode,
            diag.Timeout);
    }
    catch
    {
        // Suppress diagnostic errors
    }
}

// One-time command-line Admin setup
if (Environment.GetEnvironmentVariable("RESOLVEIQ_SETUP_ADMIN") == "true")
{
    var fullName = Environment.GetEnvironmentVariable("RESOLVEIQ_ADMIN_NAME");
    var email = Environment.GetEnvironmentVariable("RESOLVEIQ_ADMIN_EMAIL");
    var password = Environment.GetEnvironmentVariable("RESOLVEIQ_ADMIN_PASSWORD");

    if (string.IsNullOrWhiteSpace(fullName) ||
        string.IsNullOrWhiteSpace(email) ||
        string.IsNullOrWhiteSpace(password) ||
        password.Length < 8)
    {
        throw new InvalidOperationException(
            "Provide an Admin name, email, and password of at least 8 characters.");
    }

    using var scope = app.Services.CreateScope();

    var setupService = scope.ServiceProvider
        .GetRequiredService<RoleSetupService>();

    await setupService.CreateInitialAdminAsync(
        fullName,
        email,
        password);

    Console.WriteLine("Initial Admin created successfully.");
    return;
}

// 0. Forwarded headers for reverse proxies (must run first to populate RemoteIpAddress & Scheme)
app.UseForwardedHeaders();

// 1. Correlation ID tracking (first so all downstream operations include trace ID)
app.UseMiddleware<CorrelationIdMiddleware>();

// 2. Centralized exception handling (RFC 7807 ProblemDetails, traceId included)
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// 3. Security headers (HSTS/nosniff/X-Frame-Options/CSP)
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Configuration.GetValue<bool>("HttpsRedirection:Enabled", false))
{
    app.UseHttpsRedirection();
}

// 4. CORS
app.UseCors("DefaultCorsPolicy");

// 5. Rate limiting
app.UseRateLimiter();

// 6. Swagger (controlled by environment or explicit Swagger:Enabled configuration)
var enableSwagger = app.Environment.IsDevelopment() ||
                    app.Configuration.GetValue<bool>("Swagger:Enabled", false);
if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 7. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 8. Health check endpoints (safe responses without exposing credentials or schema)
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = async (context, _) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { status = "Healthy" });
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var isHealthy = report.Status == HealthStatus.Healthy;
        context.Response.StatusCode = isHealthy ? (int)HttpStatusCode.OK : (int)HttpStatusCode.ServiceUnavailable;

        var result = new
        {
            status = report.Status.ToString(),
            database = report.Entries.TryGetValue("Database", out var entry) ? entry.Status.ToString() : "Unknown"
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});

// 9. Application routes
app.MapGet("/", () => "ResolveIQ API is running!");
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();