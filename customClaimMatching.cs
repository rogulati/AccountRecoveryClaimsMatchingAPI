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

    public CustomClaimMatching(ILogger<CustomClaimMatching> logger)
    {
        _logger = logger;
    }

    [Function("CustomClaimMatching")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        
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

        // Extract Verified Credential claims
        var vcClaims = request.Data.VerifiedIdClaimsContext.Claims;
        string? fullName = vcClaims?.FullName;
        string? firstName = vcClaims?.FirstName;
        string? lastName = vcClaims?.LastName;
        string? dateOfBirth = vcClaims?.DateOfBirth;
        string? documentType = vcClaims?.DocumentType;
        string? documentId = vcClaims?.DocumentId;
        string? documentExpiryDate = vcClaims?.DocumentExpiryDate;
        string? photo = vcClaims?.Photo;

        // Extract authentication context
        string? correlationId = request.Data.AuthenticationContext?.CorrelationId;
        string? clientIp = request.Data.AuthenticationContext?.Client?.Ip;
        string? tenantId = request.Data.TenantId;

        _logger.LogInformation("Processing claim validation for UPN: {Upn}, CorrelationId: {CorrelationId}", upn, correlationId);

        // TODO: Call external HR API to validate claims against HR data
        // Example: var hrValidationResult = await _hrApiClient.ValidateEmployeeClaimsAsync(
        //     upn: upn,
        //     employeeId: employeeId,
        //     fullName: fullName,
        //     firstName: firstName,
        //     lastName: lastName,
        //     dateOfBirth: dateOfBirth,
        //     documentType: documentType,
        //     documentId: documentId,
        //     documentExpiryDate: documentExpiryDate,
        //     photo: photo
        // );

        // Mock validation result - replace with actual HR API call result
        string validationResult = "pass";
        List<string>? failedClaims = null;

        // Example of a failed validation:
        // validationResult = "fail";
        // failedClaims = new List<string> { "dateOfBirth", "documentExpiryDate" };

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
    public VcClaims? Claims { get; set; }
}

public class EntraAccount
{
    [JsonProperty("upn")]
    public string? Upn { get; set; }

    [JsonProperty("employeeId")]
    public string? EmployeeId { get; set; }
}

public class VcClaims
{
    [JsonProperty("fullName")]
    public string? FullName { get; set; }

    [JsonProperty("firstName")]
    public string? FirstName { get; set; }

    [JsonProperty("lastName")]
    public string? LastName { get; set; }

    [JsonProperty("dateOfBirth")]
    public string? DateOfBirth { get; set; }

    [JsonProperty("documentType")]
    public string? DocumentType { get; set; }

    [JsonProperty("documentId")]
    public string? DocumentId { get; set; }

    [JsonProperty("documentExpiryDate")]
    public string? DocumentExpiryDate { get; set; }

    [JsonProperty("photo")]
    public string? Photo { get; set; }
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