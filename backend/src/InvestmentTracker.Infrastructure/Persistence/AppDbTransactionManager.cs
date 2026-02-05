using InvestmentTracker.Application.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace InvestmentTracker.Infrastructure.Persistence;

/// <summary>
/// EF Core transaction manager backed by <see cref="AppDbContext.Database" />.
/// </summary>
public sealed class AppDbTransactionManager(AppDbContext context) : IAppDbTransactionManager
{
    public async Task<IAppDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        // EF Core InMemory provider does not support transactions.
        // Integration tests use InMemory DbContext, so we fall back to a no-op transaction.
        // For relational providers (SQLite/PostgreSQL), BeginTransactionAsync provides the required atomicity.
        if (string.Equals(context.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal))
        {
            return NoOpAppDbTransaction.Instance;
        }

        // Requirement: use DbContext.Database.BeginTransactionAsync()
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        return new EfCoreAppDbTransaction(transaction);
    }

    private sealed class EfCoreAppDbTransaction(IDbContextTransaction transaction) : IAppDbTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default)
            => transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync()
            => transaction.DisposeAsync();
    }

    private sealed class NoOpAppDbTransaction : IAppDbTransaction
    {
        public static readonly NoOpAppDbTransaction Instance = new();

        private NoOpAppDbTransaction()
        {
        }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
