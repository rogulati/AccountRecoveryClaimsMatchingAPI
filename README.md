# Account Recovery Claims Matching API

An Azure Function that enables tenant-owned extensibility for account recovery using Microsoft Entra Verified ID custom authentication extensions.

## Overview

This function allows the account recovery flow to call a customer-hosted endpoint with Verified ID (VID) claims. The service queries authoritative systems (e.g., HR databases) and applies bespoke matching logic to return a pass/fail decision.

**Key Benefits:**
- Avoids replicating sensitive HR data into Entra
- Gives customers full control over their identity verification rules
- Supports custom matching logic against any authoritative data source

## How It Works

```
┌─────────────┐     ┌──────────────────┐     ┌─────────────────┐
│   Entra     │────▶│  This Function   │────▶│  HR/CRM/Other   │
│  Recovery   │     │  (Claims Match)  │     │    Systems      │
│    Flow     │◀────│                  │◀────│                 │
└─────────────┘     └──────────────────┘     └─────────────────┘
```

1. **During recovery**: Entra passes the VID claim payload to this API endpoint
2. **Validation**: The function queries internal systems (HR, CRM, or composite sources) and applies custom logic
3. **Decision**: Returns a binary match decision
   - **Pass**: Recovery process proceeds
   - **Fail**: Recovery flow is halted

## Request Schema

The function expects a POST request with the following payload:

```json
{
    "type": "microsoft.graph.authenticationEvent.OnVerifiedIdClaimValidation",
    "source": "/tenants/<tenant-guid>/applications/<app-id>",
    "data": {
        "@odata.type": "microsoft.graph.onVerifiedIdClaimValidation",
        "tenantId": "<tenant-guid>",
        "verifiedIdClaimsContext": {
            "entraAccount": {
                "upn": "user@contoso.com",
                "employeeId": "12345678"
            },
            "claims": {
                "fullName": "John Doe",
                "firstName": "John",
                "lastName": "Doe",
                "dateOfBirth": "1990-01-15",
                "documentType": "Passport",
                "documentId": "AB123456",
                "documentExpiryDate": "2028-01-15",
                "photo": "<base64-encoded-photo>"
            }
        },
        "authenticationContext": {
            "correlationId": "<guid>",
            "client": {
                "ip": "192.168.1.1"
            }
        }
    }
}
```

### Input Claims

| Field | Source | Description |
|-------|--------|-------------|
| `upn` | Entra ID | User's principal name |
| `employeeId` | Entra ID | Employee identifier (optional) |
| `fullName` | Verified ID | Full name from credential |
| `firstName` | Verified ID | First name from credential |
| `lastName` | Verified ID | Last name from credential |
| `dateOfBirth` | Verified ID | Date of birth from credential |
| `documentType` | Verified ID | Type of identity document |
| `documentId` | Verified ID | Document identifier |
| `documentExpiryDate` | Verified ID | Document expiration date |
| `photo` | Verified ID | Photo/portrait from credential |

## Response Schema

### Successful Match (200)

```json
{
    "data": {
        "@odata.type": "microsoft.graph.onVerifiedIdClaimValidationResponseData",
        "actions": [
            {
                "@odata.type": "microsoft.graph.verifiedIdClaimsMatchingResult",
                "result": "pass"
            }
        ]
    }
}
```

### Failed Match (200)

```json
{
    "data": {
        "@odata.type": "microsoft.graph.onVerifiedIdClaimValidationResponseData",
        "actions": [
            {
                "@odata.type": "microsoft.graph.verifiedIdClaimsMatchingResult",
                "result": "fail",
                "failedClaims": ["dateOfBirth", "documentExpiryDate"]
            }
        ]
    }
}
```

## Configuration

### Local Development

1. Clone the repository
2. Copy `local.settings.json.example` to `local.settings.json` (if applicable)
3. Run `dotnet restore`
4. Run `dotnet build`
5. Start the function: `func start`

### Deployment

The function is deployed to Azure Functions at:
```
https://accountrecoveryclaimsmatch.azurewebsites.net/api/CustomClaimMatching
```

### Authentication

The function uses `AuthorizationLevel.Function`, requiring a function key to be passed via:
- Query parameter: `?code=<function-key>`
- Header: `x-functions-key: <function-key>`

## Customization

### Adding HR API Integration

Locate the TODO section in `customClaimMatching.cs` and implement your HR system integration:

```csharp
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
```

## Technology Stack

- .NET 10.0
- Azure Functions v4 (Isolated Worker Model)
- ASP.NET Core HTTP Triggers

## License

[Add your license here]
