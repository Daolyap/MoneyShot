# MSI Installer

This directory contains the WiX Toolset configuration for building the Money Shot MSI installer.

## Overview

The MSI installer provides a proper Windows installation experience with:
- Per-machine installation to `Program Files\Money Shot` (requires elevation)
- Desktop and Start Menu shortcuts
- Add/Remove Programs integration
- Proper upgrade/uninstall support with cleanup
- Launch condition verifying Windows 10 or later

## Security

The installer follows industry-standard secure configuration practices:
- **Per-machine scope**: Installs to `%ProgramFiles%` which is ACL-protected; standard users cannot modify installed binaries
- **Elevation required**: The `Scope="perMachine"` attribute ensures UAC elevation is requested
- **Stable component GUIDs**: All components have explicit GUIDs for reliable servicing and upgrades
- **MajorUpgrade scheduling**: Uses `afterInstallValidate` to prevent downgrade attacks
- **Clean uninstall**: `RemoveFolder` and `RemoveRegistryKey` elements ensure complete removal
- **Quoted registry paths**: The startup registry value quotes the executable path to prevent path injection
- **REINSTALLMODE=amus**: Forces complete file replacement on repair/upgrade

## Building Locally

To build the MSI installer locally on Windows:

1. Install .NET 8 SDK
2. Install WiX Toolset v5:
   ```powershell
   dotnet tool install --global wix
   wix extension add -g WixToolset.UI.wixext
   wix extension add -g WixToolset.Util.wixext
   ```

3. Publish the application:
   ```powershell
   dotnet publish ../MoneyShot/MoneyShot.csproj --configuration Release --output ./publish --self-contained false
   ```

4. Build the MSI:
   ```powershell
   wix build -arch x64 -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
     -d PublishDir="./publish" `
     -out MoneyShot.msi `
     Product.wxs
   ```

## Automatic Builds

The MSI is automatically built by the GitHub Actions workflow (`.github/workflows/build-msi.yml`) on:
- Pull requests to main/master
- Release creation
- Manual workflow dispatch

The MSI artifact is uploaded and available for download from the Actions tab.

## Configuration

The installer configuration is defined in `Product.wxs`:
- **UpgradeCode**: `A1B2C3D4-E5F6-7890-ABCD-EF1234567890` (must remain constant for upgrades)
- **Version**: Controlled by the `Version` property in `MoneyShot/MoneyShot.csproj`
- **Installation Directory**: `C:\Program Files\Money Shot`
- **Shortcuts**: Desktop (optional) and Start Menu
- **Startup**: Optional Windows startup integration (disabled by default)

## Notes

- The installer is 64-bit only (x64 architecture)
- Framework-dependent deployment (requires .NET 8 Runtime to be installed)
- Uses embedded CAB files for simpler distribution
- Supports major upgrades (newer versions can upgrade older ones)
- Requires Windows Installer 5.0 or later (`InstallerVersion="500"`)
