# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 1.x     | ✅ |

## Reporting a vulnerability

Please **do not** open a public issue for security vulnerabilities.

Instead, report privately via one of:

- **GitHub private vulnerability reporting** (preferred):
  go to the repository's **Security** tab → **Report a vulnerability**
- **Email**: lttruc1402@gmail.com

Include a description of the issue, steps to reproduce, and the affected
version. You can expect an initial response within a few days. Once the issue
is confirmed and fixed, a patched release will be published and the report
credited (unless you prefer to stay anonymous).

## Scope notes

SeekKit serializes keyset values into Base64 tokens. By default tokens are
**opaque but not signed or encrypted**:

- To reject tampered/forged tokens, enable built-in HMAC-SHA256 signing:
  `config.UseHmacSigning(key)` (see README → *Signed tokens*).
- If keyset values themselves are sensitive, plug in a custom `ISeekSerializer`
  that encrypts the payload — signing alone does not hide the values.
