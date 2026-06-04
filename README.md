# RKSS Scanner Agent

Local Windows scanner agent for RKSS DIAS. Version 2.0.0 runs in two local modes: a Windows Service for WIA on `http://127.0.0.1:8890`, and a signed-in-user desktop helper for TWAIN on `http://127.0.0.1:8891`. The React audit registration page checks both local endpoints, scans JPG/PDF output, and returns the scanned file to the browser so it can be uploaded with the audit documents.

The current web integration is in:

```text
src/components/register-audit/Step3Documents.tsx
```

It provides an enterprise scan console with scanner detection, paper source, paper size, color mode, DPI, brightness, contrast, blank-page removal, optional scanner driver UI, scan preview, page count, and OK/Cancel attachment flow.

## Scanner provider support

The agent now supports two scanner providers behind the same HTTP API shape.

| Provider | Best for | Notes |
| --- | --- | --- |
| WIA | Normal workstation service deployment | Runs as the `RKSSScannerAgent` Windows Service on port `8890`. Recommended default for HP ScanJet and Windows scanner devices. |
| TWAIN | TWAIN-only scanner drivers, virtual TWAIN scanners, or advanced vendor drivers | Runs as the signed-in user's desktop helper on port `8891`. TWAIN drivers normally require a message loop and cannot reliably display UI from a Windows Service. |

The web app calls both endpoints and merges the scanner list. Scanner IDs are tagged by the browser by mode, for example:

```text
service-wia:{device-id}
desktop-twain:{source-name}
```

When a scanner is selected, `POST /scan/download` is sent back to the endpoint that owns that scanner.

## Build

```powershell
dotnet restore .\RKSS.ScannerAgent\RKSS.ScannerAgent.csproj
dotnet build .\RKSS.ScannerAgent\RKSS.ScannerAgent.csproj -c Release
dotnet publish .\RKSS.ScannerAgent\RKSS.ScannerAgent.csproj -c Release -r win-x64 --self-contained true -o .\publish\rkss-win-x64
```

The project references the local `NTwain` library from:

```text
..\ScanAppForWeb-master\packages\NTwain.3.4.1\lib\net40-client\NTwain.dll
```

Keep that package available when building from this workspace, or replace the reference with a NuGet `NTwain` package reference in environments that have package feed access.

## Run modes

### WIA production service mode

Use this mode for normal enterprise rollout.

```powershell
.\publish\rkss-win-x64\RKSS.ScannerAgent.exe
```

Install as a Windows Service:

```powershell
sc.exe create RKSSScannerAgent binPath= "C:\RKSS\ScannerAgent\app\RKSS.ScannerAgent.exe" start= auto
sc.exe description RKSSScannerAgent "Local RKSS DIAS scanner bridge for WIA/TWAIN-compatible desktop scanners."
sc.exe start RKSSScannerAgent
```

In service mode, use WIA scanners. TWAIN discovery is intentionally not used from the Windows Service because Windows service desktop isolation prevents reliable TWAIN UI and virtual-driver discovery.

### TWAIN interactive mode

Use this mode when a workstation scanner is TWAIN-only or the vendor's TWAIN driver is required.

1. Install the scanner vendor's TWAIN driver.
2. Start the agent while signed in as the desktop user:

```powershell
.\publish\rkss-win-x64\RKSS.ScannerAgent.exe --desktop
```

The desktop helper listens on `http://127.0.0.1:8891`. Open the web app, click `Scan`, and select a scanner marked `[TWAIN]`. Use `Display scanner's UI` if the driver requires its own TWAIN window.

The Inno Setup installer creates a Startup shortcut that launches this desktop helper at user logon. Keep WIA as the service default where possible.

## Installer script for workstations

Use `install-rkss-scanner-agent.ps1` or `install-rkss-scanner-agent.cmd` to deploy the service build on Windows 8, 10, and 11 desktops.

Recommended package layout:

```text
RKSSScannerAgentDeployment\
  install-rkss-scanner-agent.ps1
  install-rkss-scanner-agent.cmd
  publish\
    rkss-win-x64\
      RKSS.ScannerAgent.exe
      appsettings.json
      NTwain.dll
      *.dll
      runtime files...
```

Install with defaults by right-clicking `install-rkss-scanner-agent.cmd` and choosing **Run as administrator**, or run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\install-rkss-scanner-agent.ps1
```

Install with a production origin:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\install-rkss-scanner-agent.ps1 `
  -BindUrl "http://127.0.0.1:8890" `
  -AllowedOrigins "https://your-production-domain,http://localhost:3000"
```

Other actions:

```powershell
.\install-rkss-scanner-agent.ps1 -Action Status
.\install-rkss-scanner-agent.ps1 -Action Repair
.\install-rkss-scanner-agent.ps1 -Action Stop
.\install-rkss-scanner-agent.ps1 -Action Start
.\install-rkss-scanner-agent.ps1 -Action Uninstall
```

## EXE or MSI packaging

For normal workstation deployment, use the Inno Setup installer script:

```powershell
ISCC.exe .\installer\RKSSScannerAgent.iss
```

This produces:

```text
installer\output\RKSS-Scanner-Agent-Setup-2.0.0.exe
```

The EXE installer copies the published agent files, removes any previous installation before copying, installs `RKSSScannerAgent` as a Windows Service, starts it automatically, registers `rkss-scanner-agent://` to launch the desktop TWAIN helper, and creates a user Startup shortcut for desktop TWAIN mode.

During setup, the installer prompts for:

- `Service WIA Bind URL`: the localhost URL the Windows service listens on. Use `http://127.0.0.1:8890` unless another local process already uses that port.
- `Desktop TWAIN Bind URL`: the localhost URL the desktop helper listens on. Use `http://127.0.0.1:8891` unless another local process already uses that port.
- `Allowed Origins`: comma-separated browser origins allowed to call the local scanner agent, for example `https://audit.example.gov,http://localhost:3000`.

For enterprise MSI deployment through Group Policy, Intune, SCCM, or similar tools, use `installer\RKSSScannerAgent.wxs` as the WiX starter definition. MSI packaging requires WiX Toolset and should harvest all files from `publish\rkss-win-x64` into the MSI.

## Sign the RKSS installer

Windows Smart App Control and Microsoft Defender SmartScreen commonly block unsigned or unknown-reputation installers. Do not distribute the unsigned installer for production. Build a signed installer with an OV or EV Authenticode code-signing certificate issued to RK Systems & Services.

Use a code-signing certificate issued to RK Systems & Services, then sign the generated Inno Setup EXE:

```powershell
$env:SIGN_CERT_PASSWORD = "<certificate-password>"
$env:SIGN_CERT_PATH = "C:\Path\To\rkss-code-signing.pfx"
.\build-rkss-installer-signed.ps1
```

If the RKSS certificate is already installed in the Windows certificate store with its private key, omit `SIGN_CERT_PATH`; the script will sign using the certificate subject `RK Systems & Services`.

For Smart App Control, EV code signing is preferred because reputation builds faster. If using an OV certificate, Microsoft reputation may still take time after first distribution even though the installer is validly signed.

Verify the signature before distribution:

```powershell
signtool verify /pa /v .\installer\output\RKSS-Scanner-Agent-Setup-2.0.0.exe
```

The expected signature status is `Valid`. If `Get-AuthenticodeSignature` reports `NotSigned`, Smart App Control can block the installer.

## Configuration

The agent reads `ScannerAgent` settings from `appsettings.json`.

```json
{
  "ScannerAgent": {
    "BindUrl": "http://127.0.0.1:8890",
    "DesktopBindUrl": "http://127.0.0.1:8891",
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://127.0.0.1:3000",
      "http://localhost:5173",
      "http://127.0.0.1:5173"
    ],
    "StoragePath": "C:\\RKSS\\ScannerAgent\\Scans"
  }
}
```

For production, keep `BindUrl` and `DesktopBindUrl` on `http://127.0.0.1:<port>` and add the production web origin to `AllowedOrigins`. The agent rejects non-loopback network requests and the web app connects by local port only.

## Browser scan flow

The audit registration screen uses `POST /scan/download`. The scanned output becomes a normal browser `File`, previews in the scan console, and is attached to the registration only after the user clicks `OK`.

The scan request supports:

```json
{
  "scannerId": "selected WIA device ID or TWAIN source name",
  "provider": "auto",
  "dpi": 300,
  "colorMode": "color",
  "paperSource": "flatbed",
  "paperSize": "default",
  "brightness": 0,
  "contrast": 0,
  "duplex": false,
  "maxPages": 25,
  "outputFormat": "pdf",
  "showScannerUi": false,
  "promptForMorePages": false,
  "removeBlankPages": false,
  "autoUpload": false,
  "triggerOcr": false
}
```

The response includes:

```text
Content-Disposition: attachment; filename=...
X-RKSS-Page-Count: 1
X-RKSS-Scanner-Name: HP TWAIN USB
```

Example:

```powershell
curl -X POST http://127.0.0.1:8890/scan/download `
  -H "Content-Type: application/json" `
  -o scanned.pdf `
  -d "{\"scannerId\":\"wia:YOUR_DEVICE_ID\",\"outputFormat\":\"pdf\",\"dpi\":300,\"colorMode\":\"color\",\"paperSource\":\"flatbed\",\"paperSize\":\"default\",\"brightness\":0,\"contrast\":0,\"removeBlankPages\":false}"
```

For TWAIN, use the desktop helper endpoint:

```powershell
curl -X POST http://127.0.0.1:8891/scan/download `
  -H "Content-Type: application/json" `
  -o scanned.pdf `
  -d "{\"scannerId\":\"TWAIN_SOURCE_NAME\",\"outputFormat\":\"pdf\",\"dpi\":300,\"colorMode\":\"color\",\"paperSource\":\"flatbed\",\"paperSize\":\"default\",\"brightness\":0,\"contrast\":0,\"removeBlankPages\":false}"
```

## Health and diagnostics

Check that the agent is running:

```powershell
curl http://127.0.0.1:8890/health
curl http://127.0.0.1:8891/health
```

List scanners:

```powershell
curl http://127.0.0.1:8890/scanners
curl http://127.0.0.1:8891/scanners
```

Logs are written under:

```text
C:\RKSS\ScannerAgent\app\logs
```

Scanned working files are written under:

```text
C:\RKSS\ScannerAgent\Scans
```

## Troubleshooting

- No scanners found: install the full scanner driver package, not only the printer driver. Confirm Windows Scan can see the device for WIA.
- TWAIN scanner not listed: confirm `RKSS.ScannerAgent.exe --desktop` is running as the signed-in user, then check `http://127.0.0.1:8891/scanners`.
- TWAIN scan fails in service mode: use the desktop helper on port `8891`; TWAIN is not expected to work from the service on port `8890`.
- Browser cannot connect: confirm `http://127.0.0.1:8890/health`, the configured local port, and `AllowedOrigins`.
- Driver UI does not appear: driver UI can only appear in an interactive desktop session. It will not reliably display from a Windows Service.
- Blank-page removal removes too much or too little: disable `Remove blank pages`; the agent uses a conservative brightness sampling heuristic.
