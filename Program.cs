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
// Configure AzureAd:TenantId and AzureAd:ClientId to enable.
// When not configured, token validation is skipped (function key auth only).
builder.Services.AddSingleton<TokenValidationService>();

// Register the claims validator based on configuration.
// Set "ClaimsValidator:Provider" to "hrapi" for production HR API integration,
// or "excel" (default) for OneDrive Excel testing.
var provider = builder.Configuration["ClaimsValidator:Provider"] ?? "excel";

if (string.Equals(provider, "excel", StringComparison.OrdinalIgnoreCase))
{
    // Excel validator — downloads file from a public OneDrive sharing link (no auth)
    builder.Services.AddHttpClient<OneDriveExcelClaimsValidator>();
    builder.Services.AddSingleton<IClaimsValidator, OneDriveExcelClaimsValidator>();
}
else
{
    // HR API validator — production default
    builder.Services.AddHttpClient<HrApiClaimsValidator>();
    builder.Services.AddSingleton<IClaimsValidator, HrApiClaimsValidator>();
}

builder.Build().Run();
