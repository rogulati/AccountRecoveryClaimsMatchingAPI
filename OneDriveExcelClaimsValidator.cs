using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace account_recovery_claim_matching;

public class OneDriveExcelClaimsValidator : IClaimsValidator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OneDriveExcelClaimsValidator> _logger;
    private readonly string _downloadUrl;
    private readonly string _sheetName;
    private readonly TimeSpan _cacheDuration;

    // In-memory cache of parsed employee data
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);
    private static List<Dictionary<string, string>>? _cachedRows;
    private static DateTime _cacheExpiry = DateTime.MinValue;

    public OneDriveExcelClaimsValidator(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OneDriveExcelClaimsValidator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _downloadUrl = configuration["Excel:ShareUrl"]
            ?? throw new InvalidOperationException("Excel:ShareUrl configuration is required. Use a OneDrive sharing link.");
        _sheetName = configuration["Excel:SheetName"] ?? "Sheet1";

        var cacheMinutes = int.TryParse(configuration["Excel:CacheMinutes"], out var m) ? m : 5;
        _cacheDuration = TimeSpan.FromMinutes(cacheMinutes);
    }

    public async Task<ClaimMatchResult> ValidateClaimsAsync(
        string? upn,
        string? employeeId,
        Dictionary<string, string> claims)
    {
        var rows = await GetCachedRowsAsync();
        if (rows == null)
        {
            return new ClaimMatchResult { Result = "fail", FailedClaims = new List<string> { "downloadFailed" } };
        }

        // Look up the employee row by UPN or EmployeeId
        Dictionary<string, string>? matchedRow = null;
        foreach (var row in rows)
        {
            row.TryGetValue("UPN", out var rowUpn);
            row.TryGetValue("EMPLOYEEID", out var rowEmpId);

            if ((!string.IsNullOrEmpty(upn) && string.Equals(rowUpn, upn, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(employeeId) && string.Equals(rowEmpId, employeeId, StringComparison.OrdinalIgnoreCase)))
            {
                matchedRow = row;
                _logger.LogInformation("Found matching row for UPN={Upn}, EmployeeId={EmpId}", upn, employeeId);
                break;
            }
        }

        if (matchedRow == null)
        {
            _logger.LogWarning("No matching employee row found for UPN={Upn}, EmployeeId={EmpId}", upn, employeeId);
            return new ClaimMatchResult { Result = "fail", FailedClaims = new List<string> { "employeeNotFound" } };
        }

        // Compare each claim dynamically against the Excel row
        var failedClaims = new List<string>();

        foreach (var claim in claims)
        {
            var claimName = claim.Key;
            var claimValue = claim.Value;

            // Look up the corresponding column in the Excel row (case-insensitive)
            if (matchedRow.TryGetValue(claimName.ToUpperInvariant(), out var excelValue))
            {
                CompareClaim(failedClaims, claimName, claimValue, excelValue);
            }
            else
            {
                _logger.LogWarning("Claim '{ClaimName}' has no matching column in Excel — skipping.", claimName);
            }
        }

        if (failedClaims.Count > 0)
        {
            _logger.LogWarning("Claim validation failed. FailedClaims: {FailedClaims}", string.Join(", ", failedClaims));
            return new ClaimMatchResult { Result = "fail", FailedClaims = failedClaims };
        }

        _logger.LogInformation("All claims matched successfully.");
        return new ClaimMatchResult { Result = "pass" };
    }

    private async Task<List<Dictionary<string, string>>?> GetCachedRowsAsync()
    {
        // Fast path: cache is still valid
        if (_cachedRows != null && DateTime.UtcNow < _cacheExpiry)
        {
            _logger.LogInformation("Using cached Excel data (expires in {Seconds}s).",
                (_cacheExpiry - DateTime.UtcNow).TotalSeconds.ToString("F0"));
            return _cachedRows;
        }

        await _cacheLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cachedRows != null && DateTime.UtcNow < _cacheExpiry)
            {
                return _cachedRows;
            }

            _logger.LogInformation("Downloading and parsing Excel file from shared link.");
            var directUrl = ConvertToDirectDownloadUrl(_downloadUrl);

            using var response = await _httpClient.GetAsync(directUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to download Excel file. Status: {Status}", response.StatusCode);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var workbook = new XLWorkbook(stream);

            var worksheet = workbook.Worksheets.TryGetWorksheet(_sheetName, out var ws)
                ? ws
                : workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                _logger.LogWarning("No worksheet found.");
                return null;
            }

            var usedRange = worksheet.RangeUsed();
            if (usedRange == null)
            {
                _logger.LogWarning("Worksheet is empty.");
                return null;
            }

            // Build column map from header row
            var headerRow = usedRange.Row(1);
            var columns = new List<(string Name, int Index)>();
            for (int col = 1; col <= usedRange.ColumnCount(); col++)
            {
                var colName = headerRow.Cell(col).GetString()?.Trim();
                if (!string.IsNullOrEmpty(colName))
                    columns.Add((colName.ToUpperInvariant(), col));
            }

            // Parse all data rows into a list of dictionaries
            var rows = new List<Dictionary<string, string>>();
            for (int row = 2; row <= usedRange.RowCount(); row++)
            {
                var dataRow = usedRange.Row(row);
                var rowDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, index) in columns)
                {
                    var value = dataRow.Cell(index).GetString()?.Trim();
                    if (!string.IsNullOrEmpty(value))
                        rowDict[name] = value;
                }
                if (rowDict.Count > 0)
                    rows.Add(rowDict);
            }

            _cachedRows = rows;
            _cacheExpiry = DateTime.UtcNow.Add(_cacheDuration);

            _logger.LogInformation("Excel data cached: {RowCount} rows, expires at {Expiry}",
                rows.Count, _cacheExpiry.ToString("HH:mm:ss"));

            return _cachedRows;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static string ConvertToDirectDownloadUrl(string shareUrl)
    {
        // OneDrive Personal: replace "redir?" or "e=" with download=1
        if (shareUrl.Contains("1drv.ms") || shareUrl.Contains("onedrive.live.com"))
        {
            return shareUrl.Contains("?") ? shareUrl + "&download=1" : shareUrl + "?download=1";
        }

        // SharePoint / OneDrive for Business: replace sharing link format to download
        // e.g., /:x:/g/personal/... → /personal/.../download.aspx
        if (shareUrl.Contains("sharepoint.com"))
        {
            return shareUrl.Contains("?") ? shareUrl + "&download=1" : shareUrl + "?download=1";
        }

        return shareUrl;
    }

    private static void CompareClaim(List<string> failedClaims, string claimName, string? vidValue, string? excelValue)
    {
        if (string.IsNullOrEmpty(vidValue))
            return;

        if (!string.Equals(vidValue, excelValue, StringComparison.OrdinalIgnoreCase))
        {
            failedClaims.Add(claimName);
        }
    }
}
