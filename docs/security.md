# Sensitive data handling

## Attributes

| Attribute | Target | Effect |
|-----------|--------|--------|
| `[Audited]` | Class | Opt-in entity for change capture when `RequireAuditedAttribute` is true |
| `[AuditRedact]` | Property | Replace values with `***` before sink write |
| `[AuditIgnore]` | Class or property | Skip capture entirely |

## Rules in 0.1.0

- Only scalar mapped properties are audited.
- Primary keys are used for `EntityId`, not listed as property changes on create.
- Concurrency tokens are ignored by default.
- Shadow properties are ignored by default.
- Navigations are never serialized as graphs.
- Redaction happens before `IAuditSink.WriteAsync`, and cannot be disabled by configuration.
- Owned types (value objects) are folded into the owning entity's record under a qualified property
  name such as `Address.Street`, and follow the same redaction and ignore rules. Changing only a value
  object still produces an `Updated` record for the aggregate.
- Record fields are truncated to the widths in `AuditFieldLimits` before reaching a sink, so a
  caller-controlled value such as a long request path cannot fail the audit write after the business
  transaction has already committed.

## Example

```csharp
[Audited]
public class Order
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;

    [AuditRedact]
    public string InternalNote { get; set; } = string.Empty;

    [AuditIgnore]
    public DateTime CacheUpdatedAt { get; set; }
}
```

## Planned (0.2.0)

Automatic redaction for common names such as `password`, `secret`, `token`, `apiKey`, `accessToken`, and `refreshToken`.
