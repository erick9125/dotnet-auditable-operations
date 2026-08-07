using AuditableOperations.Models;

namespace AuditableOperations.Abstractions;

public interface IAuditContextAccessor
{
    AuditContext GetCurrent();
}
