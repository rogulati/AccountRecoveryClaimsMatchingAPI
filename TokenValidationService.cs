using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace account_recovery_claim_matching;

/// <summary>
/// Validates JWT Bearer tokens issued by Entra ID via the OAuth 2.0 client credentials flow.
/// Used when the function is called by an Entra custom authentication extension.
/// When AzureAd settings are not configured, token validation is skipped (testing mode).
/// </summary>
public class TokenValidationService
{
    private readonly ILogger<TokenValidationService> _logger;
    private readonly bool _isEnabled;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;
    private readonly TokenValidationParameters? _validationParameters;

    public TokenValidationService(IConfiguration configuration, ILogger<TokenValidationService> logger)
    {
        _logger = logger;

        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];

        _isEnabled = !string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId);

        if (!_isEnabled)
        {
            _logger.LogInformation("AzureAd settings not configured — Bearer token validation is disabled.");
            return;
        }

        var authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";

        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        _validationParameters = new TokenValidationParameters
        {
            ValidAudiences = new[] { clientId, $"api://{clientId}" },
            ValidIssuers = new[]
            {
                $"https://login.microsoftonline.com/{tenantId}/v2.0",
                $"https://sts.windows.net/{tenantId}/"
            },
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    }

    public bool IsEnabled => _isEnabled;

    public async Task<(bool IsValid, string? ErrorMessage)> ValidateTokenAsync(string token)
    {
        if (!_isEnabled)
        {
            return (true, null);
        }

        try
        {
            var config = await _configManager!.GetConfigurationAsync(CancellationToken.None);
            _validationParameters!.IssuerSigningKeys = config.SigningKeys;

            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(token, _validationParameters, out _);

            _logger.LogInformation("Bearer token validated successfully.");
            return (true, null);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Bearer token validation failed: {Message}", ex.Message);
            return (false, ex.Message);
        }
    }
}
