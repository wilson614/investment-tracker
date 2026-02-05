namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Abstraction for database transaction management.
/// This keeps transaction handling out of the Application layer's persistence implementation.
/// </summary>
public interface IAppDbTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts a database transaction for the current request scope.
/// </summary>
public interface IAppDbTransactionManager
{
    Task<IAppDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
