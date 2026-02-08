using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using InvestmentTracker.API.Dtos;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.BankAccount;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// Bank accounts CRUD API.
/// </summary>
/// <remarks>
/// Exceptions are handled by ExceptionHandlingMiddleware:
/// - EntityNotFoundException -> 404 Not Found
/// - AccessDeniedException -> 403 Forbidden
/// - BusinessRuleException -> 400 Bad Request
/// </remarks>
[Authorize]
[ApiController]
[Route("api/bank-accounts")]
public class BankAccountsController(
    GetBankAccountsUseCase getBankAccountsUseCase,
    GetBankAccountUseCase getBankAccountUseCase,
    CreateBankAccountUseCase createBankAccountUseCase,
    UpdateBankAccountUseCase updateBankAccountUseCase,
    DeleteBankAccountUseCase deleteBankAccountUseCase,
    IBankAccountRepository bankAccountRepository,
    ICurrentUserService currentUserService) : ControllerBase
{
    private static readonly string[] SupportedCurrencies = ["TWD", "USD", "EUR", "JPY", "CNY", "GBP", "AUD"];
    private static readonly HashSet<string> SupportedCurrencySet = new(SupportedCurrencies, StringComparer.OrdinalIgnoreCase);
    private static readonly string[] ExportHeaders = ["BankName", "TotalAssets", "InterestRate", "InterestCap", "Currency", "Note", "IsActive"];

    /// <summary>
    /// Get all bank accounts for current user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BankAccountResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<BankAccountResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var accounts = await getBankAccountsUseCase.ExecuteAsync(cancellationToken);
        return Ok(accounts);
    }

    /// <summary>
    /// Get bank account by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BankAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BankAccountResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var account = await getBankAccountUseCase.ExecuteAsync(id, cancellationToken);
        return Ok(account);
    }

    /// <summary>
    /// Create a bank account.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(BankAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BankAccountResponse>> Create(
        [FromBody] CreateBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await createBankAccountUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = account.Id }, account);
    }

    /// <summary>
    /// Update a bank account.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(BankAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BankAccountResponse>> Update(
        Guid id,
        [FromBody] UpdateBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await updateBankAccountUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(account);
    }

    /// <summary>
    /// Delete (soft delete) a bank account.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteBankAccountUseCase.ExecuteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Export current user's bank accounts to CSV.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var accounts = await bankAccountRepository.GetByUserIdAsync(userId, cancellationToken);
        var csvContent = BuildExportCsv(accounts);
        var csvBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csvContent);
        var fileName = $"bank-accounts-export-{DateTime.UtcNow:yyyyMMdd}.csv";

        return File(csvBytes, "text/csv; charset=utf-8", fileName);
    }

    /// <summary>
    /// Import bank accounts from CSV content with preview/execute modes.
    /// </summary>
    [HttpPost("import")]
    [ProducesResponseType(typeof(ImportPreviewResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ImportExecuteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Import(
        [FromBody] BankAccountImportRequest request,
        CancellationToken cancellationToken)
    {
        var mode = request.Mode.Trim().ToLowerInvariant();
        if (mode is not ("preview" or "execute"))
        {
            return BadRequest("Mode must be either 'preview' or 'execute'.");
        }

        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var existingAccounts = await bankAccountRepository.GetByUserIdAsync(userId, cancellationToken);
        var analysis = AnalyzeImport(request.CsvContent, existingAccounts);

        if (mode == "preview")
        {
            var previewResult = new ImportPreviewResultDto
            {
                Items = analysis.Items
                    .Select(item => new ImportPreviewItemResultDto
                    {
                        BankName = item.BankName,
                        Action = item.Action,
                        ChangeDetails = item.ChangeDetails
                    })
                    .ToList(),
                ValidationErrors = analysis.ValidationErrors
            };

            return Ok(previewResult);
        }

        var executeItems = new List<ImportExecuteItemResultDto>();
        var createdCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;

        foreach (var item in analysis.Items)
        {
            if (item.HasErrors)
            {
                skippedCount++;
                executeItems.Add(new ImportExecuteItemResultDto
                {
                    BankName = item.BankName,
                    Action = "Skip",
                    Success = false,
                    Message = string.Join("; ", item.RowErrors)
                });
                continue;
            }

            if (item.Action.Equals("Create", StringComparison.OrdinalIgnoreCase) && item.ImportItem is not null)
            {
                var entity = new BankAccount(
                    userId,
                    item.ImportItem.BankName,
                    item.ImportItem.TotalAssets,
                    item.ImportItem.InterestRate,
                    item.ImportItem.InterestCap,
                    item.ImportItem.Note,
                    NormalizeCurrency(item.ImportItem.Currency));

                if (!item.ImportItem.IsActive)
                {
                    entity.Deactivate();
                }

                await bankAccountRepository.AddAsync(entity, cancellationToken);

                createdCount++;
                executeItems.Add(new ImportExecuteItemResultDto
                {
                    BankName = item.BankName,
                    Action = "Create",
                    Success = true,
                    Message = "Created"
                });
                continue;
            }

            if (item.Action.Equals("Update", StringComparison.OrdinalIgnoreCase) &&
                item.ExistingAccount is not null &&
                item.ImportItem is not null)
            {
                ApplyImportItem(item.ExistingAccount, item.ImportItem);
                await bankAccountRepository.UpdateAsync(item.ExistingAccount, cancellationToken);

                updatedCount++;
                executeItems.Add(new ImportExecuteItemResultDto
                {
                    BankName = item.BankName,
                    Action = "Update",
                    Success = true,
                    Message = "Updated"
                });
                continue;
            }

            skippedCount++;
            executeItems.Add(new ImportExecuteItemResultDto
            {
                BankName = item.BankName,
                Action = "Skip",
                Success = true,
                Message = "No changes detected"
            });
        }

        var result = new ImportExecuteResultDto
        {
            CreatedCount = createdCount,
            UpdatedCount = updatedCount,
            SkippedCount = skippedCount,
            Items = executeItems
        };

        return Ok(result);
    }

    private static string BuildExportCsv(IReadOnlyList<BankAccount> accounts)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", ExportHeaders));

        foreach (var account in accounts.OrderBy(a => a.BankName, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(ToCsvValue(account.BankName));
            builder.Append(',');
            builder.Append(account.TotalAssets.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(account.InterestRate.ToString("0.####", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(account.InterestCap.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(ToCsvValue(account.Currency));
            builder.Append(',');
            builder.Append(ToCsvValue(account.Note));
            builder.Append(',');
            builder.Append(account.IsActive ? "true" : "false");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string ToCsvValue(string? value)
    {
        var normalized = value ?? string.Empty;

        if (normalized.Contains('"'))
        {
            normalized = normalized.Replace("\"", "\"\"");
        }

        if (normalized.Contains(',') || normalized.Contains('\n') || normalized.Contains('\r') || normalized.Contains('"'))
        {
            return $"\"{normalized}\"";
        }

        return normalized;
    }

    private static ImportAnalysisResult AnalyzeImport(string csvContent, IReadOnlyList<BankAccount> existingAccounts)
    {
        var rows = ParseCsvRows(csvContent);
        var validationErrors = new List<string>();
        var items = new List<ImportAnalysisItem>();

        if (rows.Count == 0)
        {
            validationErrors.Add("CSV content is empty.");
            return new ImportAnalysisResult(items, validationErrors);
        }

        var existingLookup = existingAccounts
            .GroupBy(a => a.BankName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(a => a.UpdatedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        var seenBankNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (row.Count > 0 && row[0].TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (IsHeaderRow(row))
            {
                continue;
            }

            var parsed = ParseImportRow(row, rowIndex + 1);

            if (parsed.ImportItem is not null)
            {
                if (!seenBankNames.Add(parsed.ImportItem.BankName))
                {
                    parsed.RowErrors.Add($"Duplicate BankName '{parsed.ImportItem.BankName}' found in import content.");
                }

                if (existingLookup.TryGetValue(parsed.ImportItem.BankName, out var existing))
                {
                    parsed.ExistingAccount = existing;
                    parsed.ChangeDetails.AddRange(GetChangeDetails(existing, parsed.ImportItem));
                    parsed.Action = parsed.ChangeDetails.Count > 0 ? "Update" : "Skip";
                }
                else
                {
                    parsed.Action = "Create";
                }
            }

            if (parsed.HasErrors)
            {
                parsed.Action = "Skip";
                foreach (var error in parsed.RowErrors)
                {
                    validationErrors.Add($"Row {parsed.RowNumber}: {error}");
                }
            }

            items.Add(parsed);
        }

        if (items.Count == 0 && validationErrors.Count == 0)
        {
            validationErrors.Add("CSV content does not contain any importable rows.");
        }

        return new ImportAnalysisResult(items, validationErrors);
    }

    private static ImportAnalysisItem ParseImportRow(IReadOnlyList<string> columns, int rowNumber)
    {
        var result = new ImportAnalysisItem
        {
            RowNumber = rowNumber,
            Action = "Skip"
        };

        if (columns.Count != ExportHeaders.Length)
        {
            result.BankName = columns.Count > 0 && !string.IsNullOrWhiteSpace(columns[0])
                ? columns[0].Trim()
                : $"(Row {rowNumber})";
            result.RowErrors.Add($"Expected {ExportHeaders.Length} columns but got {columns.Count}.");
            return result;
        }

        var bankName = columns[0].Trim();
        result.BankName = string.IsNullOrWhiteSpace(bankName) ? $"(Row {rowNumber})" : bankName;

        if (!decimal.TryParse(columns[1].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var totalAssets))
        {
            result.RowErrors.Add("TotalAssets must be a valid decimal number.");
        }

        if (!decimal.TryParse(columns[2].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var interestRate))
        {
            result.RowErrors.Add("InterestRate must be a valid decimal number.");
        }

        if (!decimal.TryParse(columns[3].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var interestCap))
        {
            result.RowErrors.Add("InterestCap must be a valid decimal number.");
        }

        var currency = NormalizeCurrency(columns[4]);
        var note = string.IsNullOrWhiteSpace(columns[5]) ? null : columns[5].Trim();

        if (!TryParseBoolean(columns[6], out var isActive))
        {
            result.RowErrors.Add("IsActive must be true/false (or 1/0).");
        }

        if (result.RowErrors.Count > 0)
        {
            return result;
        }

        var importItem = new BankAccountImportItemDto
        {
            BankName = bankName,
            TotalAssets = totalAssets,
            InterestRate = interestRate,
            InterestCap = interestCap,
            Currency = currency,
            Note = note,
            IsActive = isActive
        };

        var itemValidationErrors = ValidateImportItem(importItem);
        result.RowErrors.AddRange(itemValidationErrors);
        result.ImportItem = importItem;

        return result;
    }

    private static IReadOnlyList<string> ValidateImportItem(BankAccountImportItemDto item)
    {
        var validationContext = new ValidationContext(item);
        var validationResults = new List<ValidationResult>();

        Validator.TryValidateObject(item, validationContext, validationResults, validateAllProperties: true);

        var errors = validationResults
            .Select(r => r.ErrorMessage ?? "Validation error.")
            .ToList();

        if (!SupportedCurrencySet.Contains(item.Currency))
        {
            errors.Add($"Currency must be one of: {string.Join(", ", SupportedCurrencies)}.");
        }

        return errors;
    }

    private static List<string> GetChangeDetails(BankAccount existing, BankAccountImportItemDto imported)
    {
        var changes = new List<string>();

        if (!string.Equals(existing.BankName, imported.BankName, StringComparison.Ordinal))
        {
            changes.Add($"BankName: {existing.BankName} -> {imported.BankName}");
        }

        var normalizedTotalAssets = Math.Round(imported.TotalAssets, 2);
        if (existing.TotalAssets != normalizedTotalAssets)
        {
            changes.Add($"TotalAssets: {existing.TotalAssets.ToString("0.00", CultureInfo.InvariantCulture)} -> {normalizedTotalAssets.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        var normalizedInterestRate = Math.Round(imported.InterestRate, 4);
        if (existing.InterestRate != normalizedInterestRate)
        {
            changes.Add($"InterestRate: {existing.InterestRate.ToString("0.####", CultureInfo.InvariantCulture)} -> {normalizedInterestRate.ToString("0.####", CultureInfo.InvariantCulture)}");
        }

        var normalizedInterestCap = Math.Round(imported.InterestCap, 2);
        if (existing.InterestCap != normalizedInterestCap)
        {
            changes.Add($"InterestCap: {existing.InterestCap.ToString("0.00", CultureInfo.InvariantCulture)} -> {normalizedInterestCap.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        var normalizedCurrency = NormalizeCurrency(imported.Currency);
        if (!string.Equals(existing.Currency, normalizedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"Currency: {existing.Currency} -> {normalizedCurrency}");
        }

        var existingNote = NormalizeNullableText(existing.Note);
        var importedNote = NormalizeNullableText(imported.Note);
        if (!string.Equals(existingNote, importedNote, StringComparison.Ordinal))
        {
            changes.Add($"Note: {ToDisplayText(existingNote)} -> {ToDisplayText(importedNote)}");
        }

        if (existing.IsActive != imported.IsActive)
        {
            changes.Add($"IsActive: {existing.IsActive.ToString().ToLowerInvariant()} -> {imported.IsActive.ToString().ToLowerInvariant()}");
        }

        return changes;
    }

    private static void ApplyImportItem(BankAccount existing, BankAccountImportItemDto imported)
    {
        existing.SetBankName(imported.BankName);
        existing.SetTotalAssets(imported.TotalAssets);
        existing.SetInterestSettings(imported.InterestRate, imported.InterestCap);
        existing.SetCurrency(NormalizeCurrency(imported.Currency));
        existing.SetNote(imported.Note);

        if (imported.IsActive)
        {
            existing.Activate();
        }
        else
        {
            existing.Deactivate();
        }
    }

    private static List<List<string>> ParseCsvRows(string csvContent)
    {
        var content = (csvContent ?? string.Empty).TrimStart('\uFEFF');
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentField = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var current = content[i];

            if (current == '"')
            {
                if (inQuotes && i + 1 < content.Length && content[i + 1] == '"')
                {
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if ((current == '\n' || current == '\r') && !inQuotes)
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();

                rows.Add(currentRow);
                currentRow = new List<string>();

                if (current == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            if (current == ',' && !inQuotes)
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();
                continue;
            }

            currentField.Append(current);
        }

        currentRow.Add(currentField.ToString());

        if (currentRow.Any(value => !string.IsNullOrWhiteSpace(value)) || currentRow.Count > 1)
        {
            rows.Add(currentRow);
        }

        return rows;
    }

    private static bool IsHeaderRow(IReadOnlyList<string> row)
    {
        if (row.Count != ExportHeaders.Length)
        {
            return false;
        }

        for (var i = 0; i < ExportHeaders.Length; i++)
        {
            if (!string.Equals(row[i].Trim(), ExportHeaders[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseBoolean(string rawValue, out bool value)
    {
        var normalized = rawValue.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "true":
            case "1":
            case "yes":
            case "y":
                value = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "n":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static string NormalizeCurrency(string currency)
    {
        return (currency ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string? NormalizeNullableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string ToDisplayText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(empty)" : value;
    }

    private sealed class ImportAnalysisResult(
        IReadOnlyList<ImportAnalysisItem> items,
        IReadOnlyList<string> validationErrors)
    {
        public IReadOnlyList<ImportAnalysisItem> Items { get; } = items;
        public IReadOnlyList<string> ValidationErrors { get; } = validationErrors;
    }

    private sealed class ImportAnalysisItem
    {
        public int RowNumber { get; init; }
        public string BankName { get; set; } = string.Empty;
        public string Action { get; set; } = "Skip";
        public BankAccountImportItemDto? ImportItem { get; set; }
        public BankAccount? ExistingAccount { get; set; }
        public List<string> ChangeDetails { get; } = [];
        public List<string> RowErrors { get; } = [];
        public bool HasErrors => RowErrors.Count > 0;
    }
}
