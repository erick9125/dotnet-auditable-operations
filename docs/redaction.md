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
    options.RedactSensitiveValues = true;
    options.RedactedPlaceholder = "***";
});
```
