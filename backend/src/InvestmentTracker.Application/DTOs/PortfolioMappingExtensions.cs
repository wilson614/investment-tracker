using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// Portfolio Entity 轉換為 DTO 的擴展方法。
/// </summary>
public static class PortfolioMappingExtensions
{
    public static PortfolioDto ToDto(this Portfolio portfolio)
    {
        return new PortfolioDto
        {
            Id = portfolio.Id,
            Description = portfolio.Description,
            BaseCurrency = portfolio.BaseCurrency,
            HomeCurrency = portfolio.HomeCurrency,
            IsActive = portfolio.IsActive,
            BoundCurrencyLedgerId = portfolio.BoundCurrencyLedgerId,
            DisplayName = portfolio.DisplayName,
            CreatedAt = portfolio.CreatedAt,
            UpdatedAt = portfolio.UpdatedAt
        };
    }

    public static IEnumerable<PortfolioDto> ToDtos(this IEnumerable<Portfolio> portfolios)
    {
        return portfolios.Select(p => p.ToDto());
    }
}
