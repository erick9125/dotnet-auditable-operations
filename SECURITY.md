# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 0.1.x   | Yes       |

## Reporting a vulnerability

If you discover a security issue in erick9125.AuditableOperations, please open a private security advisory on GitHub or contact the maintainer directly. Do not open a public issue for vulnerabilities that could expose sensitive data.

## Security principles

This library handles potentially sensitive application data. Consumers must treat audit storage as a sensitive system.

The library will:

- Redact properties marked with `[AuditRedact]` before records reach any sink
- Never capture full HTTP request bodies
- Never serialize entire EF entity graphs or navigations by default
- Never persist full claim sets, tokens, or passwords intentionally
- Ignore properties and types marked with `[AuditIgnore]`
- Prefer independent audit persistence to reduce accidental recursion into audited business entities

The library will not:

- Guarantee that consumers mark every sensitive property
- Encrypt audit payloads at rest (consumer responsibility)
- Provide SIEM integration or retention enforcement in 0.1.0
- Automatically redact common secret property names (planned for 0.2.0)

## Consumer obligations

1. Mark sensitive properties with `[AuditRedact]` or `[AuditIgnore]`.
2. Protect access to the audit store with the same rigor as production data.
3. Avoid logging `AuditRecord` payloads to unrestricted sinks.
4. Review owned entities, value converters, and custom formatters for leakage.
5. Do not store audit databases in publicly accessible locations.

## Redaction behavior

`[AuditRedact]` is always honored — there is no configuration flag that can disable it. Only the
placeholder is configurable (`RedactedPlaceholder`). To exclude a sensitive property from the audit
trail entirely, use `[AuditIgnore]`.

```json
{
  "property": "InternalComment",
  "previousValue": "***",
  "currentValue": "***",
  "isRedacted": true
}
```

Redaction occurs in-memory during capture. Sensitive CLR values are never handed to `IAuditSink`.
