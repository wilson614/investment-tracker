namespace InvestmentTracker.Domain.Exceptions;

/// <summary>
/// 當請求的實體不存在時拋出的異常。
/// </summary>
public class EntityNotFoundException : Exception
{
    public string EntityName { get; }
    public object? EntityId { get; }

    public EntityNotFoundException(string entityName, object? entityId = null)
        : base($"{entityName} not found" + (entityId != null ? $" (ID: {entityId})" : ""))
    {
        EntityName = entityName;
        EntityId = entityId;
    }

    public EntityNotFoundException(string message) : base(message)
    {
        EntityName = "Entity";
    }
}

/// <summary>
/// 當使用者無權存取資源時拋出的異常。
/// </summary>
public class AccessDeniedException : Exception
{
    public AccessDeniedException() : base("Access denied")
    {
    }

    public AccessDeniedException(string message) : base(message)
    {
    }
}

/// <summary>
/// 當業務規則驗證失敗時拋出的異常。
/// </summary>
public class BusinessRuleException(string message) : Exception(message);
