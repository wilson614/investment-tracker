namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Stores a snapshot of CAPE (Cyclically Adjusted P/E) data from Research Affiliates
/// This is global data, not user-specific
/// </summary>
public class CapeDataSnapshot
{
    public int Id { get; set; }

    /// <summary>
    /// The data date from the source (e.g., "2026-01-02")
    /// </summary>
    public required string DataDate { get; set; }

    /// <summary>
    /// JSON-serialized list of CAPE items
    /// </summary>
    public required string ItemsJson { get; set; }

    /// <summary>
    /// When this data was fetched from the API
    /// </summary>
    public DateTime FetchedAt { get; set; }

    /// <summary>
    /// When this record was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
