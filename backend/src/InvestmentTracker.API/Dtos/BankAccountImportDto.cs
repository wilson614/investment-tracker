using System.ComponentModel.DataAnnotations;

namespace InvestmentTracker.API.Dtos;

/// <summary>
/// Bank account row for CSV export.
/// </summary>
public record BankAccountExportDto
{
    public Guid Id { get; init; }
    public string BankName { get; init; } = string.Empty;
    public decimal TotalAssets { get; init; }

    /// <summary>
    /// Annual interest rate (stored with 4 decimal places).
    /// </summary>
    public decimal InterestRate { get; init; }

    public decimal InterestCap { get; init; }
    public string Currency { get; init; } = "TWD";
    public string? Note { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// Request DTO for importing bank accounts from raw CSV content.
/// </summary>
public record BankAccountImportRequest
{
    [Required]
    [RegularExpression("^(preview|execute)$", ErrorMessage = "Mode must be either 'preview' or 'execute'.")]
    public required string Mode { get; init; }

    [Required]
    public required string CsvContent { get; init; }
}

/// <summary>
/// Single bank account item parsed from CSV.
/// </summary>
public record BankAccountImportItemDto
{
    [Required]
    [StringLength(100)]
    public required string BankName { get; init; }

    [Range(0, double.MaxValue)]
    public decimal TotalAssets { get; init; }

    /// <summary>
    /// Annual interest rate (stored with 4 decimal places).
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal InterestRate { get; init; }

    [Range(0, double.MaxValue)]
    public decimal InterestCap { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = "TWD";

    [StringLength(500)]
    public string? Note { get; init; }

    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Import preview response with per-row analysis and validation errors.
/// </summary>
public record ImportPreviewResultDto
{
    public IReadOnlyList<ImportPreviewItemResultDto> Items { get; init; } = [];
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

/// <summary>
/// Per-item preview result.
/// </summary>
public record ImportPreviewItemResultDto
{
    public string BankName { get; init; } = string.Empty;

    /// <summary>
    /// Create / Update / Skip
    /// </summary>
    public string Action { get; init; } = string.Empty;

    public IReadOnlyList<string> ChangeDetails { get; init; } = [];
}

/// <summary>
/// Import execute summary result.
/// </summary>
public record ImportExecuteResultDto
{
    public int CreatedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int SkippedCount { get; init; }
    public IReadOnlyList<ImportExecuteItemResultDto> Items { get; init; } = [];
}

/// <summary>
/// Per-item execution result.
/// </summary>
public record ImportExecuteItemResultDto
{
    public string BankName { get; init; } = string.Empty;

    /// <summary>
    /// Create / Update / Skip
    /// </summary>
    public string Action { get; init; } = string.Empty;

    public bool Success { get; init; }
    public string? Message { get; init; }
}
