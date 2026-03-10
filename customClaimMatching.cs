using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace account_recovery_claim_matching;

public class CustomClaimMatching
{
    private readonly ILogger<CustomClaimMatching> _logger;
    private readonly IClaimsValidator _claimsValidator;
    private readonly TokenValidationService _tokenValidator;

    public CustomClaimMatching(ILogger<CustomClaimMatching> logger, IClaimsValidator claimsValidator, TokenValidationService tokenValidator)
    {
        _logger = logger;
        _claimsValidator = claimsValidator;
        _tokenValidator = tokenValidator;
    }

    [Function("CustomClaimMatching")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");

        // Validate Bearer token when present (required for Entra custom auth extension calls)
        var authHeader = req.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader.Substring("Bearer ".Length).Trim();
            var (isValid, errorMessage) = await _tokenValidator.ValidateTokenAsync(token);
            if (!isValid)
            {
                _logger.LogWarning("Bearer token validation failed.");
                return new UnauthorizedResult();
            }
        }
        else if (_tokenValidator.IsEnabled)
        {
            _logger.LogInformation("No Bearer token in request — proceeding with function key auth only.");
        }
        
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        var request = JsonConvert.DeserializeObject<VerifiedIdClaimValidationRequest>(requestBody);

        if (request?.Data?.VerifiedIdClaimsContext == null)
        {
            _logger.LogError("Invalid request payload");
            return new BadRequestObjectResult("Invalid request payload");
        }

        // Extract Entra Account information
        string? upn = request.Data.VerifiedIdClaimsContext.EntraAccount?.Upn;
        string? employeeId = request.Data.VerifiedIdClaimsContext.EntraAccount?.EmployeeId;

        // Extract Verified Credential claims (dynamic — any key/value pairs the caller sends)
        var claims = request.Data.VerifiedIdClaimsContext.Claims ?? new Dictionary<string, string>();

        // Extract authentication context
        string? correlationId = request.Data.AuthenticationContext?.CorrelationId;
        string? clientIp = request.Data.AuthenticationContext?.Client?.Ip;
        string? tenantId = request.Data.TenantId;

        _logger.LogInformation("Processing claim validation for UPN: {Upn}, CorrelationId: {CorrelationId}, ClaimCount: {Count}",
            upn, correlationId, claims.Count);

        // Validate claims against authoritative data source
        var matchResult = await _claimsValidator.ValidateClaimsAsync(
            upn: upn,
            employeeId: employeeId,
            claims: claims
        );

        string validationResult = matchResult.Result;
        List<string>? failedClaims = matchResult.FailedClaims;

        // Build response
        var response = new VerifiedIdClaimValidationResponse(validationResult, failedClaims);

        return new OkObjectResult(response);
    }
}

#region Request Models

public class VerifiedIdClaimValidationRequest
{
    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("source")]
    public string? Source { get; set; }

    [JsonProperty("data")]
    public VerifiedIdClaimValidationData? Data { get; set; }
}

public class VerifiedIdClaimValidationData
{
    [JsonProperty("@odata.type")]
    public string? ODataType { get; set; }

    [JsonProperty("tenantId")]
    public string? TenantId { get; set; }

    [JsonProperty("verifiedIdClaimsContext")]
    public VerifiedIdClaimsContext? VerifiedIdClaimsContext { get; set; }

    [JsonProperty("authenticationContext")]
    public AuthenticationContext? AuthenticationContext { get; set; }
}

public class VerifiedIdClaimsContext
{
    [JsonProperty("entraAccount")]
    public EntraAccount? EntraAccount { get; set; }

    [JsonProperty("claims")]
    public Dictionary<string, string>? Claims { get; set; }
}

public class EntraAccount
{
    [JsonProperty("upn")]
    public string? Upn { get; set; }

    [JsonProperty("employeeId")]
    public string? EmployeeId { get; set; }
}

public class AuthenticationContext
{
    [JsonProperty("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonProperty("client")]
    public ClientInfo? Client { get; set; }
}

public class ClientInfo
{
    [JsonProperty("ip")]
    public string? Ip { get; set; }
}

#endregion

#region Response Models

public class VerifiedIdClaimValidationResponse
{
    [JsonPropertyName("data")]
    public VerifiedIdClaimValidationResponseData Data { get; set; }

    public VerifiedIdClaimValidationResponse(string result, List<string>? failedClaims = null)
    {
        Data = new VerifiedIdClaimValidationResponseData(result, failedClaims);
    }
}

public class VerifiedIdClaimValidationResponseData
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = "microsoft.graph.onVerifiedIdClaimValidationResponseData";

    [JsonPropertyName("actions")]
    public List<VerifiedIdClaimsMatchingResultAction> Actions { get; set; }

    public VerifiedIdClaimValidationResponseData(string result, List<string>? failedClaims = null)
    {
        Actions = new List<VerifiedIdClaimsMatchingResultAction>
        {
            new VerifiedIdClaimsMatchingResultAction(result, failedClaims)
        };
    }
}

public class VerifiedIdClaimsMatchingResultAction
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = "microsoft.graph.verifiedIdClaimsMatchingResult";

    [JsonPropertyName("result")]
    public string Result { get; set; }

    [JsonPropertyName("failedClaims")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? FailedClaims { get; set; }

    public VerifiedIdClaimsMatchingResultAction(string result, List<string>? failedClaims = null)
    {
        Result = result;
        FailedClaims = failedClaims;
    }
}

#endregion