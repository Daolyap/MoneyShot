# Security Policy

## Supported Versions

| Version | Supported          |
|---------|--------------------|
| 2.0.x   | :white_check_mark: |
| < 2.0   | :x:                |

## Reporting a Vulnerability

If you discover a security vulnerability in Money Shot, please report it responsibly.

**Do NOT open a public GitHub issue for security vulnerabilities.**

Instead, please email the maintainers directly or use [GitHub's private vulnerability reporting](https://github.com/Daolyap/MoneyShot/security/advisories/new).

### What to Include

- A description of the vulnerability
- Steps to reproduce the issue
- The potential impact
- Any suggested fixes (optional)

### Response Timeline

- **Acknowledgement**: Within 48 hours of the report
- **Assessment**: Within 7 days
- **Fix**: Depending on severity, within 14–30 days
- **Disclosure**: Coordinated with the reporter after the fix is released

## Security Design Principles

Money Shot follows these security principles:

1. **No telemetry or analytics** — the application makes no network requests
2. **Local-only data storage** — all settings and screenshots stay on your machine
3. **Minimal permissions** — runs as a standard user (`asInvoker`), no elevation required
4. **Secure installer** — MSI installs to `%ProgramFiles%` with proper ACLs; standard users cannot modify installed binaries
5. **Input validation** — file paths and settings are sanitized before use
6. **Atomic settings writes** — settings are written to a temp file and renamed to prevent corruption
7. **Heap hardening** — the application manifest enables `SegmentHeap` for improved memory safety
8. **Dependency pinning** — CI/CD workflows pin GitHub Actions to immutable commit SHAs
