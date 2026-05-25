using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using account_recovery_claim_matching;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register Entra ID Bearer token validation (OAuth 2.0 client credentials flow).
// Configure EntraId:TenantId and EntraId:ClientId to enable.
// When not configured, token validation is skipped (function key auth only).
builder.Services.AddSingleton<TokenValidationService>();

// Pre-warm OIDC metadata / JWKS at startup so the first CAE request does not
// pay the network round-trip. Required to stay within the ~2s CAE budget.
builder.Services.AddHostedService<TokenValidationWarmupService>();

// Shared primary handler config for outbound HTTP: HTTP/2 + bounded pooled connection
// lifetime so DNS / SNI changes are picked up, and multiple HTTP/2 connections per host
// to avoid head-of-line blocking under burst load.
static SocketsHttpHandler CreatePrimaryHandler() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
    EnableMultipleHttp2Connections = true,
    AutomaticDecompression = DecompressionMethods.All
};

// Register the claims validator based on configuration.
// Set "ClaimsValidator:Provider" to "hrapi" for production HR API integration,
// or "excel" (default) for HTTP-hosted Excel file validation.
var provider = builder.Configuration["ClaimsValidator:Provider"] ?? "excel";

if (string.Equals(provider, "excel", StringComparison.OrdinalIgnoreCase))
{
    // Excel validator — downloads .xlsx from any HTTP(S) URL (OneDrive, Azure Blob, custom host, etc.)
    builder.Services.AddHttpClient<HttpExcelClaimsValidator>(c =>
        {
            c.DefaultRequestVersion = HttpVersion.Version20;
            c.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        })
        .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler);
    builder.Services.AddSingleton<IClaimsValidator, HttpExcelClaimsValidator>();
}
else
{
    // HR API validator — production default
    builder.Services.AddHttpClient<HrApiClaimsValidator>(c =>
        {
            c.DefaultRequestVersion = HttpVersion.Version20;
            c.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        })
        .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler);
    builder.Services.AddSingleton<IClaimsValidator, HrApiClaimsValidator>();
}

builder.Build().Run();

/// <summary>
/// Hosted service that pre-fetches OIDC metadata at startup so the first
/// CAE request does not pay the JWKS network round-trip.
/// </summary>
internal sealed class TokenValidationWarmupService : IHostedService
{
    private readonly TokenValidationService _tokenValidator;
    public TokenValidationWarmupService(TokenValidationService tokenValidator) => _tokenValidator = tokenValidator;
    public Task StartAsync(CancellationToken cancellationToken) => _tokenValidator.WarmupAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
