# Redaction

Redaction is applied while building `AuditPropertyChange` instances.

```csharp
[AuditRedact]
public string ApiSecret { get; set; } = string.Empty;
```

Result:

```json
{
  "property": "ApiSecret",
  "previousValue": "***",
  "currentValue": "***",
  "isRedacted": true
}
```

Null values remain null even when redacted, so absence of a value is preserved without leaking content.

Configure the placeholder:

```csharp
services.AddAuditableOperations(options =>
{
    options.RedactedPlaceholder = "***";
});
```

## Redaction cannot be switched off

There is no option to disable redaction. `[AuditRedact]` is a security decision taken on a specific
property, so no global configuration flag may override it — an earlier design had one, and setting it
wrote secrets to the audit store in clear text.

To stop auditing a sensitive property entirely, use `[AuditIgnore]` instead.

## Owned types

Value objects mapped as owned types are redacted with the same rules, and their changes are reported
on the owning entity's record under a qualified name:

```json
{
  "property": "Address.Street",
  "previousValue": "***",
  "currentValue": "***",
  "isRedacted": true
}
```
