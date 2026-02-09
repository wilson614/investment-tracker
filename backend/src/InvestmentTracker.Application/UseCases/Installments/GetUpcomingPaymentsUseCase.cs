using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
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
        var creditCards = await creditCardRepository.GetAllByUserIdAsync(userId, cancellationToken);
        var creditCardMap = creditCards.ToDictionary(c => c.Id, c => c);

        var utcNow = DateTime.UtcNow;

        var activeInstallmentViews = installments
            .Select(installment =>
            {
                if (!creditCardMap.TryGetValue(installment.CreditCardId, out var creditCard))
                    return null;

                var remainingInstallments = installment.GetRemainingInstallments(creditCard.PaymentDueDay, utcNow);
                var effectiveStatus = installment.GetEffectiveStatus(creditCard.PaymentDueDay, utcNow);

                return new
                {
                    Installment = installment,
                    CreditCard = creditCard,
                    RemainingInstallments = remainingInstallments,
                    EffectiveStatus = effectiveStatus
                };
            })
            .Where(view => view is not null && view.EffectiveStatus == InstallmentStatus.Active && view.RemainingInstallments > 0)
            .ToList();

        if (activeInstallmentViews.Count == 0)
            return [];

        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endExclusive = monthStart.AddMonths(months);

        var grouped = new Dictionary<DateTime, List<UpcomingInstallmentPayment>>();

        foreach (var view in activeInstallmentViews)
        {
            var installment = view!.Installment;
            var creditCard = view.CreditCard;
            var creditCardName = creditCard.CardName;

            var paidInstallments = installment.GetPaidInstallments(creditCard.PaymentDueDay, utcNow);
            var scheduleStartMonth = new DateTime(
                installment.StartDate.Year,
                installment.StartDate.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc);

            for (var i = 0; i < view.RemainingInstallments; i++)
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
