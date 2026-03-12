# Money Shot — Implementation & Security Hardening Plan

## Overview

This plan identifies improvements for the Money Shot screenshot tool, focusing on MSI installer hardening, secure configuration, and CI/CD security best practices.

---

## 1. MSI Installer Improvements (`Installer/Product.wxs`)

| Item | Description | Priority |
|------|-------------|----------|
| Per-machine install scope | Add `Scope="perMachine"` and `ALLUSERS=1` to ensure proper per-machine installation under Program Files | High |
| Launch conditions | Add OS version check and .NET 8 runtime prerequisite detection | High |
| MajorUpgrade scheduling | Set `Schedule="afterInstallValidate"` for reliable upgrade handling | High |
| Secure install directory permissions | Use WiX Util extension to lock down installed folder ACLs | High |
| Uninstall cleanup | Add `RemoveFolder` elements to clean up directories on uninstall | Medium |
| REINSTALLMODE property | Set `REINSTALLMODE=amus` for complete file replacement on repair/upgrade | Medium |
| Disable modify in ARP | Add `ARPNOMODIFY=1` to prevent partial modification from Add/Remove Programs | Low |
| Component GUIDs | Add stable GUIDs to components for reliable servicing | Medium |

## 2. Application Security Hardening

| Item | Description | Priority |
|------|-------------|----------|
| app.manifest hardening | Add heap termination on corruption, long path awareness, and UTF-8 code page support | High |
| SECURITY.md | Create a vulnerability disclosure policy following industry standards | High |
| Dependabot configuration | Add `.github/dependabot.yml` for automated dependency vulnerability alerts | High |

## 3. CI/CD Security

| Item | Description | Priority |
|------|-------------|----------|
| Pin Actions to SHAs | Replace mutable version tags with immutable commit SHAs to prevent supply chain attacks | High |
| Minimal permissions | Add explicit `permissions` blocks to all workflows with least-privilege scoping | High |
| Replace deprecated action | Replace `actions/upload-release-asset@v1` (deprecated) with `softprops/action-gh-release@v2` | Medium |

## 4. Code Quality

| Item | Description | Priority |
|------|-------------|----------|
| Code review | Run automated code review on entire codebase | High |
| CodeQL scan | Run static analysis security scan on all changes | High |

---

## Detailed Implementation Notes

### MSI Installer — Secure Configuration Requirements

Industry-standard MSI installer requirements (per Microsoft best practices and NIST guidelines):

1. **Per-machine installation**: Install under `%ProgramFiles%` with proper ACLs; standard users cannot modify installed binaries.
2. **Elevation requirement**: The installer must request elevation (`InstallPrivileges="elevated"`) to write to protected directories.
3. **Launch conditions**: Verify the target OS version and required runtimes before installation proceeds.
4. **Upgrade strategy**: Use `MajorUpgrade` with `Schedule="afterInstallValidate"` to handle upgrades atomically and prevent downgrade attacks.
5. **Clean uninstall**: Remove all installed files, folders, registry keys, and shortcuts on uninstall.
6. **No world-writable install directories**: The install folder should only be writable by administrators.

### Application Manifest Hardening

- **HeapTerminateOnCorruption**: Terminates the process if heap corruption is detected, preventing exploitation.
- **LongPathAware**: Enables support for paths longer than MAX_PATH (260 characters).
- **ActiveCodePage UTF-8**: Ensures consistent text encoding behavior.

### CI/CD Supply Chain Security

- **SHA pinning**: Mutable tags (e.g., `@v4`) can be overwritten by action maintainers (or attackers who compromise the repo). Pinning to an immutable commit SHA ensures the exact code that was audited is always used.
- **Dependabot**: Automatically opens PRs when dependencies have known vulnerabilities.
- **Least-privilege permissions**: Each workflow should only request the permissions it actually needs.
