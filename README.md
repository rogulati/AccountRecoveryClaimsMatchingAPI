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

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Frogulati%2FAccountRecoveryClaimsMatchingAPI%2Fmain%2FARMTemplate%2Ftemplate.json/createUIDefinitionUri/https%3A%2F%2Fraw.githubusercontent.com%2Frogulati%2FAccountRecoveryClaimsMatchingAPI%2Fmain%2FARMTemplate%2FcreateUiDefinition.json)

> [!IMPORTANT]
> Before you click **Deploy to Azure**, confirm the following two things — they cause >90% of failed first-time deployments:
>
> 1. **You can assign roles in the target resource group.** The template creates RBAC role assignments (Storage Blob Data Owner on storage, Website Contributor on the function app) for two managed identities. **Contributor is not enough** — you need **Owner** or **User Access Administrator** on the resource group. This is the most common enterprise blocker.
> 2. **The region supports Flex Consumption.** Flex Consumption isn't in every region yet. If you pick an unsupported region, the deployment fails with a runtime-creation error. The portal UI above pre-filters where possible; otherwise check the [current Flex Consumption region list](https://learn.microsoft.com/azure/azure-functions/flex-consumption-how-to#view-currently-supported-regions) before deploying.

#### What the template provisions

The template is a true **one-click deploy** — both infrastructure **and** the function code:

- **Azure Function App** on a **Flex Consumption** plan (FC1), Linux, .NET isolated worker
  - `alwaysReadyInstances = 1` by default → no cold-start CAE errors
  - `instanceMemoryMB = 2048` (configurable 512 / 2048 / 4096)
  - `maximumInstanceCount = 40` (configurable up to 1000)
- **Storage Account** (StorageV2, shared-key disabled, OAuth-only) with an `app-package` blob container. The Functions host uses it for trigger metadata and as the code deployment source. The function app accesses it with its **system-assigned managed identity** (no connection strings).
- **Application Insights** for monitoring and logging.
- **Deployment automation:**
  - A short-lived **user-assigned managed identity** (`<funcname>-deployer`).
  - A `Microsoft.Resources/deploymentScripts` resource that downloads the **release zip** (default: this repo's latest GitHub Release) and publishes it to the function app via `az functionapp deployment source config-zip`.
  - The deployer identity is granted **Website Contributor** scoped to just the function app.

#### Parameters

| Parameter | Description |
|-----------|-------------|
| **Function App Name** | Globally unique name for the Function App. |
| **Location** | Azure region. Must be one that supports Flex Consumption. |
| **Always-Ready Instances** | Pre-warmed instances kept hot 24×7. Default `1` (eliminates cold-start CAE errors). |
| **Instance Memory** | Per-instance memory: 512 / 2048 / 4096 MB. Default `2048`. |
| **Maximum Instance Count** | Upper autoscale bound (40–1000). Default `40`. |
| **.NET Version** | `10.0` (default) or `8.0`. |
| **Storage Account Type** | `Standard_LRS` / `Standard_GRS` / `Standard_RAGRS`. |
| **Claims Validator Provider** | `excel` (default, test) or `hrapi` (production). |
| **Excel Share URL / Sheet Name / Cache Minutes** | *(Excel provider)* URL to the `.xlsx`, worksheet name, in-memory cache TTL. |
| **HR API Base URL / Auth Mode / API Key / OAuth Scope** | *(HR API provider)* HR REST endpoint and auth. |
| **Entra Tenant ID / Client ID** | Enables Bearer token validation on the function endpoint. Leave blank during initial testing. |
| **Package URL** | Public URL to the function-app release zip. Defaults to the latest GitHub Release of this repo. Override to deploy from a fork or pin a specific version. |

> **Note on region availability:** If `.NET 10` is not yet supported on Flex Consumption in your region, redeploy with `dotnetVersion = 8.0`.

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

> **Note:** The `EntraId__TenantId` and `EntraId__ClientId` app settings are no longer used. Authentication is handled entirely by EasyAuth. Keep these empty or remove them from your Function App's environment variables.

#### Verifying Authentication

Once EasyAuth is configured, verify it works:

1. **Without a token:** Call the function URL directly — you should get `401 Unauthorized`
2. **With a valid token:** Include a Bearer token with the correct audience — you should get the claims validation response

**Verify in Application Insights logs:**

```kusto
traces
| where message has "claims" or message has "validation"
| order by timestamp desc
| take 10
```

## Claims Validation Providers

The function uses a pluggable validation architecture (`IClaimsValidator`). The active provider is selected via the `ClaimsValidator:Provider` app setting.

| Value | Provider | Description |
|-------|----------|-------------|
| `excel` | **HTTP Excel** (default) | Downloads an Excel file from any HTTP(S) URL — OneDrive sharing links, Azure Blob Storage, or any web-hosted `.xlsx`. Use this for testing. |
| `hrapi` | **HR API** | Calls an external HR REST API to validate claims. Use this in production. |

> **Note:** The Excel provider only validates the `documentNumber` claim against the matching column in the spreadsheet. It is intended for testing purposes only. The HR API provider forwards all claims to the external API for validation.

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

### Excel Provider (`excel`)

Downloads an Excel file from any HTTP(S) URL and parses it locally using ClosedXML. **No authentication or Graph permissions required** — host the file on any web server with read access and provide the direct download URL.

#### Setup

1. Upload your Excel file to a web server, file share, or cloud storage service (for example, Azure Blob Storage, SharePoint, or any HTTP-accessible location)
2. Get a direct download URL for the file. The URL must be accessible without interactive sign-in
3. Set the URL as an app setting

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
  "Excel__ShareUrl": "https://your-server.com/path/to/file.xlsx",
  "Excel__SheetName": "Sheet1"
}
```

| Setting | Description |
|---------|-------------|
| `Excel__ShareUrl` | Public OneDrive sharing link to the Excel file |
| `Excel__SheetName` | Worksheet name (defaults to `Sheet1`) |

> **Note:** Use double underscores (`__`) as the separator for nested config in environment variables / App Settings. In `local.settings.json`, use colons: `"Excel:ShareUrl"`.

## Cold Start Mitigation

This Function App runs on **Flex Consumption** with `alwaysReadyInstances = 1` by default. That means at least one worker is pre-warmed 24×7, so:

- **JWKS / OIDC discovery is already cached** when the first real CAE call arrives.
- **`SocketsHttpHandler` HTTP/2 connection pools** are already established to Entra and the HR API.
- **Excel data** (when using the `excel` provider) is held in the in-process cache.

End-to-end p99 stays comfortably under the ~2s Entra CAE budget. Scale-out instances incur a small additional warm-up, but the always-ready instance absorbs the initial burst.

> The legacy `KeepAlive` timer-triggered function (previously used to mask Consumption-plan cold starts) is redundant on Flex Consumption and can be removed.

## Cutting a release

The ARM template's `packageUrl` parameter defaults to `https://github.com/<owner>/<repo>/releases/latest/download/release.zip`. To publish a new release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The `.github/workflows/release.yml` workflow then:

1. Runs `dotnet publish -c Release` against `account-recovery-claim-matching.csproj`.
2. Zips the publish output into `release.zip`.
3. Creates a GitHub Release tagged `v1.0.0` and attaches `release.zip`.

The next "Deploy to Azure" click picks up the new zip automatically (or pin a specific version by overriding `packageUrl` with `…/releases/download/v1.0.0/release.zip`).

## Technology Stack

- .NET 10.0
- Azure Functions v4 (Isolated Worker Model)
- ASP.NET Core HTTP Triggers
- ClosedXML (Excel file parsing for test provider)
- Azure.Identity (Managed Identity / DefaultAzureCredential — HR API OAuth mode)
- System.IdentityModel.Tokens.Jwt / Microsoft.IdentityModel.Protocols.OpenIdConnect (Entra JWT Bearer token validation)

## License

[Add your license here]
