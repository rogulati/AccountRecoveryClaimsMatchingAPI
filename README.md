# Account Recovery Claims Matching API

An Azure Function that enables tenant-owned extensibility for account recovery using Microsoft Entra Verified ID custom authentication extensions.

> **Quick Start:** This repository includes a one-click **Deploy to Azure** button for ARM-based deployment. See the [Deploy to Azure](#deploy-to-azure) section to get started.

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
    "type": "microsoft.graph.authenticationEvent.verifiedIdClaimValidation",
    "source": "/tenants/<tenant-guid>/applications/<app-id>",
    "data": {
        "@odata.type": "microsoft.graph.onVerifiedIdClaimValidationCalloutData",
        "tenantId": "<tenant-guid>",
        "authenticationEventListenerId": "<listener-guid>",
        "customAuthenticationExtensionId": "<extension-guid>",
        "authenticationContext": {
            "correlationId": "<guid>",
            "protocol": "OAUTH2",
            "client": {
                "clientIp": "127.0.0.1",
                "locale": "en-us",
                "market": "en-us"
            },
            "clientServicePrincipal": {
                "appId": "<app-id>",
                "displayName": "My App",
                "id": "<sp-guid>"
            },
            "resourceServicePrincipal": null,
            "user": {
                "id": "<user-guid>",
                "userPrincipalName": "user@contoso.com",
                "givenName": "John",
                "surname": "Doe",
                "mail": "user@contoso.com",
                "onPremisesSamAccountName": "jdoe",
                "userType": "Member",
                "createdDateTime": "2024-01-01T00:00:00"
            }
        },
        "verifiedIdClaimsContext": {
            "identities": [
                {
                    "issuer": "contoso.com",
                    "issuerAssignedId": "user@contoso.com",
                    "signInType": "userPrincipalName"
                }
            ],
            "additionalInfo": {
                "employeeId": "12345678"
            },
            "claims": {
                "firstName": "John",
                "lastName": "Doe",
                "fullName": "John Doe",
                "dateOfBirth": "1990-01-15",
                "documentType": "Passport",
                "documentId": "AB123456",
                "homeAddress": "123 Main St",
                "documentExpiryDate": "2028-01-15"
            }
        }
    }
}
```

### Input Claims

The `claims` object is **dynamic** — you can include any set of key/value pairs. The function will validate whichever claims are present against the authoritative data source.

| Field | Source | Description |
|-------|--------|-------------|
| `authenticationContext.user.userPrincipalName` | Entra ID | User's principal name (used for employee lookup) |
| `verifiedIdClaimsContext.additionalInfo.employeeId` | Entra ID | Employee identifier (used for employee lookup, optional) |
| `verifiedIdClaimsContext.identities` | Entra ID | Array of identity records (issuer, issuerAssignedId, signInType) |
| `verifiedIdClaimsContext.claims.*` | Verified ID | Any key/value pairs — the function compares each key against the matching column in the data source |

**Common claims** (add or remove as needed):

| Claim Key | Example Value | Description |
|-----------|---------------|-------------|
| `firstName` | `"John"` | First name from credential |
| `lastName` | `"Doe"` | Last name from credential |
| `fullName` | `"John Doe"` | Full name from credential |
| `dateOfBirth` | `"1990-01-15"` | Date of birth |
| `documentType` | `"Passport"` | Type of identity document |
| `documentId` | `"AB123456"` | Document identifier |
| `documentExpiryDate` | `"2028-01-15"` | Document expiration date |
| `homeAddress` | `"123 Main St"` | Home address |
| `mobileNo` | `"+1-555-0100"` | Mobile phone number |

> **Tip:** To add a new claim, simply include it in the `claims` object in the request payload and add a matching column header in the Excel file (or handle it in your HR API). No code changes required.

## Response Schema

### Successful Match (200)

```json
{
    "data": {
        "@odata.type": "microsoft.graph.onVerifiedIdClaimValidationResponseData",
        "actions": [
            {
                "@odata.type": "microsoft.graph.verifiedIdClaimValidation.pass"
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
                "@odata.type": "microsoft.graph.verifiedIdClaimValidation.failed",
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

### Deploy to Azure

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Frogulati%2FAccountRecoveryClaimsMatchingAPI%2Fmain%2FARMTemplate%2Ftemplate.json)

You will be prompted for the following parameters:

| Parameter | Description |
|-----------|-------------|
| **Function App Name** | Globally unique name for the Function App |
| **Repo URL** | GitHub repository URL for source deployment (pre-filled) |
| **Branch** | Repository branch to deploy from (defaults to `main`) |
| **Storage Account Type** | Storage SKU — `Standard_LRS`, `Standard_GRS`, or `Standard_RAGRS` |
| **Location** | Azure region (defaults to the resource group's location) |
| **Excel Share URL** | *(optional)* Public OneDrive sharing link to the Excel file |
| **Excel Sheet Name** | *(optional)* Worksheet name (defaults to `Sheet1`) |
| **Excel Cache Minutes** | *(optional)* Minutes to cache parsed Excel data (defaults to `5`) |
| **Entra ID Tenant ID** | *(optional)* Entra tenant ID for OAuth Bearer validation. Leave empty to disable |
| **Entra ID Client ID** | *(optional)* App registration client ID for OAuth. Leave empty to disable |

The template deploys:
- **Azure Function App** (Consumption plan, .NET 10 isolated worker, v4 runtime)
- **Storage Account** — Required runtime dependency for Azure Functions on the Consumption plan. The Functions host uses it for trigger management, function keys, and internal orchestration (`AzureWebJobsStorage`). On Consumption plans, it also hosts an Azure Files share that stores the deployed function code for scale-out (`WEBSITE_CONTENTSHARE`). Your application code does not interact with it directly.
- **Application Insights** for monitoring and logging
- **System-assigned Managed Identity**

### Post-Deployment

The function endpoint will be available at:
```
https://<your-function-app-name>.azurewebsites.net/api/CustomClaimMatching
```

### Authentication

The function uses `AuthorizationLevel.Anonymous` — **no function keys are required**. All authentication is via OAuth 2.0 Bearer tokens validated by `TokenValidationService`.

#### OAuth 2.0 Client Credentials Flow (Entra Custom Auth Extension)

When the function is registered as an **Entra ID custom authentication extension**, Entra calls it using the OAuth 2.0 client credentials flow:

1. Entra acquires a token from `https://login.microsoftonline.com/{tenantId}/v2.0` with the Function App's app registration as the audience
2. Entra sends the token in the `Authorization: Bearer <token>` header
3. The function validates the JWT — checking issuer, audience, signature, and expiration via OIDC discovery

**Required App Settings** (enable Bearer token validation):

| Setting | Description |
|---------|-------------|
| `EntraId__TenantId` | Your Entra ID tenant ID (GUID) |
| `EntraId__ClientId` | Application (client) ID of the Function App's app registration |

```json
{
  "EntraId__TenantId": "00000000-0000-0000-0000-000000000000",
  "EntraId__ClientId": "00000000-0000-0000-0000-000000000000"
}
```

> When `EntraId` settings are **not configured**, Bearer token validation is skipped. This lets you test locally without needing an app registration.

#### Authentication Behavior Summary

The function uses `AuthorizationLevel.Anonymous` — there are no function keys. All authentication is via OAuth 2.0 Bearer tokens validated by `TokenValidationService`.

| Scenario | Bearer Token | Result |
|----------|:---:|--------|
| EntraId not configured | — | Allowed (validation disabled — local dev only) |
| EntraId configured | ✅ valid | Allowed |
| EntraId configured | ❌ invalid | 401 |
| EntraId configured | — (absent) | 401 |

#### App Registrations

Two app registrations are involved in the OAuth 2.0 client credentials flow:

| Role | App ID | Purpose |
|------|--------|---------|
| **API app** (Function App identity) | `5528a4be-4453-44f1-82f6-2ce4130cac1b` | Configured as `EntraId__ClientId`. Tokens must have this app as the `aud` (audience) claim to be accepted. |
| **Production client** (Entra custom auth extension) | `99045fe1-7639-4a75-9d4a-577b6ca3810f` | Microsoft's built-in app ID. In production, Entra's custom authentication extension infrastructure acquires a token using this identity automatically — no setup needed. Validated via the `azp` claim. |
| **Test client** (manual testing, e.g. Insomnia) | `8144934d-9b73-4f0b-a7ac-e3958c811bac` | Optional. Used to manually acquire tokens via `client_credentials` to simulate what Entra does. Only needed for testing outside the Entra flow. |

To allow the test client, add its app ID to `EntraId:AuthorizedClientAppIds` (semicolon-separated):
```
EntraId__AuthorizedClientAppIds=99045fe1-7639-4a75-9d4a-577b6ca3810f;8144934d-9b73-4f0b-a7ac-e3958c811bac
```

#### Setting Up the App Registration

To enable OAuth 2.0 for the Entra custom authentication extension (see [Learn doc](https://learn.microsoft.com/en-us/entra/identity-platform/custom-extension-tokenissuancestart-configuration?tabs=azure-portal%2Cworkforce-tenant)):

1. **Register the custom authentication extension** in Entra ID → Enterprise applications → Custom authentication extensions
2. Select the **TokenIssuanceStart** event type
3. Set the **Target URL** to your Azure Function endpoint
4. Under **API Authentication**, create a new app registration (this becomes the API app / audience)
5. **Grant admin consent** for the `Receive custom authentication extension HTTP requests` permission
6. Set `EntraId__TenantId` and `EntraId__ClientId` (the API app's client ID) in the Function App's app settings

#### Verifying the Custom Authentication Extension Flow

The Entra ID custom authentication extension always authenticates to your Azure Function using the OAuth 2.0 `client_credentials` flow. The well-known Microsoft app ID `99045fe1-7639-4a75-9d4a-577b6ca3810f` is the authorized caller (configured via `EntraId:AuthorizedClientAppIds`).

Once `EntraId__TenantId` and `EntraId__ClientId` are configured, you can verify end-to-end by triggering the sign-in flow that invokes the custom authentication extension (see [Step 5 in the Learn doc](https://learn.microsoft.com/en-us/entra/identity-platform/custom-extension-tokenissuancestart-configuration?tabs=azure-portal%2Cworkforce-tenant#step-5-test-the-application)):

```
https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize?client_id={App_to_enrich_ID}&response_type=id_token&redirect_uri=https://jwt.ms&scope=openid&state=12345&nonce=12345
```

**Verify in Application Insights logs:**

```kusto
traces
| where message has "Bearer token"
| order by timestamp desc
| take 10
```

You should see `Bearer token validated successfully. azp=99045fe1-7639-4a75-9d4a-577b6ca3810f` for successful calls from the custom authentication extension.

## Claims Validation Providers

The function uses a pluggable validation architecture (`IClaimsValidator`). The active provider is selected via the `ClaimsValidator:Provider` app setting.

| Value | Provider | Description |
|-------|----------|-------------|
| `excel` | **HTTP Excel** (default) | Downloads an Excel file from any HTTP(S) URL — OneDrive sharing links, Azure Blob Storage, or any web-hosted `.xlsx`. Use this for testing. |
| `hrapi` | **HR API** | Calls an external HR REST API to validate claims. Use this in production. |

Set the provider in `local.settings.json`:
```json
{
  "ClaimsValidator__Provider": "excel"
}
```

---

### HR API Provider (`hrapi`)

Posts the VID claims to your HR system's REST endpoint for validation. Supports two authentication modes.

#### Authentication Modes

| `HrApi:AuthMode` | Description |
|-------------------|-------------|
| `apikey` (default) | Sends a static key in the `x-api-key` header |
| `oauth` | Acquires an OAuth 2.0 bearer token via `DefaultAzureCredential` (managed identity in Azure, VS/CLI credentials locally) |

#### Required App Settings

| Setting | Description |
|---------|-------------|
| `HrApi__BaseUrl` | Base URL of your HR API (e.g., `https://hr.contoso.com/api`) |
| `HrApi__AuthMode` | *(optional)* `apikey` (default) or `oauth` |
| `HrApi__ApiKey` | *(optional)* API key — required when AuthMode is `apikey` |
| `HrApi__OAuthScope` | *(optional)* OAuth scope (e.g., `api://hr-api-app-id/.default`) — required when AuthMode is `oauth` |

**API key example:**
```json
{
  "HrApi__BaseUrl": "https://hr.contoso.com/api",
  "HrApi__AuthMode": "apikey",
  "HrApi__ApiKey": "your-api-key"
}
```

**OAuth example (managed identity):**
```json
{
  "HrApi__BaseUrl": "https://hr.contoso.com/api",
  "HrApi__AuthMode": "oauth",
  "HrApi__OAuthScope": "api://your-hr-api-app-id/.default"
}
```

> When using `oauth`, the Function App's managed identity must be granted the appropriate app role on the HR API's app registration.

#### HR API Contract

**Request** — `POST {BaseUrl}/validate`
```json
{
  "upn": "user@contoso.com",
  "employeeId": "12345678",
  "claims": {
    "firstName": "John",
    "lastName": "Doe",
    "fullName": "John Doe",
    "dateOfBirth": "1990-01-15",
    "documentType": "Passport",
    "documentId": "AB123456",
    "documentExpiryDate": "2028-01-15"
  }
}
```

**Expected Response** — `200 OK`
```json
{
  "result": "pass"
}
```
or on failure:
```json
{
  "result": "fail",
  "failedClaims": ["dateOfBirth", "documentExpiryDate"]
}
```

---

### OneDrive Excel Provider (`excel`)

Downloads an Excel file from a public OneDrive sharing link and parses it locally using ClosedXML. **No authentication or Graph permissions required** — just share the file with "Anyone with the link" and provide the sharing URL.

#### Setup

1. Upload your Excel file to OneDrive (Personal or Business)
2. Right-click → **Share** → set access to **"Anyone with the link"**
3. Copy the sharing link
4. Set the link as an app setting

#### Excel File Format

The Excel file must have a **header row** with at least the lookup columns. Additional columns are matched dynamically against the claims in the request:

| Column | Required | Description |
|--------|----------|-------------|
| `EmployeeId` | Yes (either) | Employee identifier (used for row lookup) |
| `UPN` | Yes (either) | User principal name (used for row lookup) |
| *Any other columns* | No | Matched dynamically by column header name |

The function looks up the employee row by matching the Entra account's **UPN** or **EmployeeId**, then compares each claim key from the request against the column with the same name (case-insensitive). Claims that have no matching column are logged and skipped.

**Example Excel layout:**

| EmployeeId | UPN | FirstName | LastName | FullName | DateOfBirth | DocumentType | DocumentId | DocumentExpiryDate | HomeAddress | MobileNo |
|---|---|---|---|---|---|---|---|---|---|---|
| E001 | jdoe@contoso.com | John | Doe | John Doe | 1990-01-15 | Passport | AB123456 | 2028-01-15 | 123 Main St | +1-555-0100 |

> **To add a new claim:** just add a column to the Excel file with the claim name as header, and include the same key in the request's `claims` object.

#### Required App Settings

Add these to `local.settings.json` (local) or Function App **Configuration** (Azure):

```json
{
  "Excel__ShareUrl": "https://1drv.ms/x/s!your-share-link",
  "Excel__SheetName": "Sheet1"
}
```

| Setting | Description |
|---------|-------------|
| `Excel__ShareUrl` | Public OneDrive sharing link to the Excel file |
| `Excel__SheetName` | Worksheet name (defaults to `Sheet1`) |

> **Note:** Use double underscores (`__`) as the separator for nested config in environment variables / App Settings. In `local.settings.json`, use colons: `"Excel:ShareUrl"`.

## Technology Stack

- .NET 10.0
- Azure Functions v4 (Isolated Worker Model)
- ASP.NET Core HTTP Triggers
- ClosedXML (Excel file parsing for test provider)
- Azure.Identity (Managed Identity / DefaultAzureCredential — HR API OAuth mode)
- System.IdentityModel.Tokens.Jwt / Microsoft.IdentityModel.Protocols.OpenIdConnect (Entra JWT Bearer token validation)

## License

[Add your license here]
