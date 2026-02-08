using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Installments;

public record UpcomingInstallmentPayment(
    Guid Id,
    string Description,
    string CreditCardName,
    decimal MonthlyPayment
);

public record UpcomingPaymentMonthSummary(
    DateTime Month,
    decimal TotalPayment,
    IReadOnlyList<UpcomingInstallmentPayment> Installments
);

/// <summary>
/// Get upcoming installment payments grouped by month.
/// </summary>
public class GetUpcomingPaymentsUseCase(
    IInstallmentRepository installmentRepository,
    ICreditCardRepository creditCardRepository,
    ICurrentUserService currentUserService)
{
    public async Task<IReadOnlyList<UpcomingPaymentMonthSummary>> ExecuteAsync(
        int months = 3,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        if (months < 1)
            months = 1;

        var installments = await installmentRepository.GetAllByUserIdAsync(userId, cancellationToken);
        var activeInstallments = installments
            .Where(i => i.Status == InstallmentStatus.Active)
            .ToList();

        if (activeInstallments.Count == 0)
            return [];

        var creditCards = await creditCardRepository.GetAllByUserIdAsync(userId, cancellationToken);
        var creditCardNameMap = creditCards.ToDictionary(c => c.Id, c => c.CardName);

        var utcNow = DateTime.UtcNow;
        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endExclusive = monthStart.AddMonths(months);

        var grouped = new Dictionary<DateTime, List<UpcomingInstallmentPayment>>();

        foreach (var installment in activeInstallments)
        {
            var creditCardName = creditCardNameMap.TryGetValue(installment.CreditCardId, out var cardName)
                ? cardName
                : string.Empty;

            var paidInstallments = installment.NumberOfInstallments - installment.RemainingInstallments;
            var scheduleStartMonth = new DateTime(
                installment.StartDate.Year,
                installment.StartDate.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc);

            for (var i = 0; i < installment.RemainingInstallments; i++)
            {
                var dueMonth = scheduleStartMonth.AddMonths(paidInstallments + i);

                if (dueMonth < monthStart || dueMonth >= endExclusive)
                    continue;

                if (!grouped.TryGetValue(dueMonth, out var monthInstallments))
                {
                    monthInstallments = [];
                    grouped[dueMonth] = monthInstallments;
                }

                monthInstallments.Add(new UpcomingInstallmentPayment(
                    installment.Id,
                    installment.Description,
                    creditCardName,
                    installment.MonthlyPayment));
            }
        }

        return grouped
            .OrderBy(x => x.Key)
            .Select(x => new UpcomingPaymentMonthSummary(
                x.Key,
                Math.Round(x.Value.Sum(i => i.MonthlyPayment), 2),
                x.Value
                    .OrderBy(i => i.CreditCardName)
                    .ThenBy(i => i.Description)
                    .ToList()))
            .ToList();
    }
}
