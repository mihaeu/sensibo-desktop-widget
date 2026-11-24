## Table of contents

- [Screenshot or Demo](#screenshot-or-demo)
- [Requirements](#requirements)
- [Build and check](#build-and-check)
- [Install for the current Windows user](#install-for-the-current-windows-user)
- [First connection](#first-connection)
- [Use](#use)
- [Network behavior](#network-behavior)
- [Code layout](#code-layout)
- [Uninstall](#uninstall)
- [Manual acceptance checks](#manual-acceptance-checks)

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/assets/logo-dark.png">
    <img src="docs/assets/logo.png" alt="Sensibo Desktop Widget" width="480">
  </picture>
</p>

# Sensibo Desktop Widget

A small, always-on-top Windows widget showing temperature, humidity, TVOC, and estimated CO₂. The temperature-first layout is 176 × 176 device-independent pixels by default, with rounded corners and light, dark, or system appearance.

## Screenshot or Demo

<img src="docs/assets/widget-demo.png" alt="Temperature-first widget showing 20.3 degrees Celsius, 69.3 percent humidity, 519 ppb TVOC, and 967 ppm estimated CO2" width="176" height="176">

The native WPF layout with sample readings, shown at its default size.

## Requirements

- Windows 11 x64 (Windows 10 x64 may work but is not the primary test target).
- A Sensibo account and an internet connection. Air Pro identifies itself as `airq`.
- To build: **.NET SDK 11.0.100**, pinned in `global.json`. No Visual Studio installation is required.
- To run the published app: no separate .NET installation. The self-contained app includes runtime **11.0.0**.

## Build and check

Install SDK 11.0.100 from the [official .NET 11 download page](https://dotnet.microsoft.com/en-us/download/dotnet/11.0). From the repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

If the SDK is installed in a separate directory, pass its executable explicitly without changing your system PATH:

```powershell
.\scripts\build.ps1 -DotnetPath "$env:LOCALAPPDATA\Microsoft\dotnet11\dotnet.exe"
```

The script verifies the exact SDK, restores with checked-in dependency lock files, builds Release with warnings treated as errors, runs the offline checks, and publishes to `artifacts/publish`. The first restore requires access to NuGet to download the pinned Windows runtime packs. There are no third-party packages. Keep the entire publish folder together: three WPF runtime DLLs must remain beside the executable to work around a [.NET 11 startup bug](https://github.com/dotnet/wpf/issues/11842). The installer copies these files automatically.

Build inputs are pinned by `global.json`, the runtime version in the app project, `NuGet.config`, and `packages.lock.json`. Deterministic compilation and normalized source paths are enabled; automatic Git-dependent source embedding and revision attributes are disabled so a source archive builds the same as a checkout. An independent rebuild from a second directory produced an identical executable; see `VALIDATION.md`. `artifacts/SHA256SUMS.txt` records the executable hash. Do not edit generated outputs. When intentionally updating the SDK/runtime, update their pins, run `dotnet restore SensiboDesktopWidget.slnx --force-evaluate`, review the lock-file changes, then rerun the build script.

To run only the offline checks after building:

```powershell
dotnet run --project tests/SensiboWidget.Tests -c Release --no-build --no-restore
```

The checks are a small dependency-free executable: a failed assertion exits nonzero. They cover device parsing, absent versus zero readings, partial updates, SSE framing, authenticated requests, rate-limit responses, cancellation, selected-device filtering, and monitor positioning. They never contact Sensibo or change startup registration.

## Install for the current Windows user

After a successful build:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install.ps1 -Launch
```

This requires no administrator privileges. It:

1. Copies the published app to `%LOCALAPPDATA%\Programs\SensiboDesktopWidget`.
2. Creates **Sensibo Widget** in the Start menu.
3. Registers startup in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `SensiboDesktopWidget`.
4. Opens the app when `-Launch` is supplied.

Use `-NoStartup` to install without startup registration. Startup means **Windows sign-in**, when the desktop is available. You can also change this option in the app's Settings. The execution-policy option applies to that PowerShell process only. Organization policy can still prevent scripts or unsigned executables from running.

For updates, quit the widget through its tray menu, rebuild, and run the installer again. Settings and the encrypted key are kept outside the application directory. This is a locally built, unsigned application; it has no automatic updater.

## First connection

1. Open [Sensibo API keys](https://home.sensibo.com/me/api).
2. Sign in with the same email as the phone app. Passwordless sign-in is available.
3. Create a key named, for example, **Desktop readings**.
4. Paste it into the widget's masked API-key field and click **Connect and find devices**.
5. Choose your device and click **Save**.

The app makes read-only API requests. A Sensibo key itself grants account control: do not share it. It is stored using Windows DPAPI with `CurrentUser` protection in `%LOCALAPPDATA%\SensiboDesktopWidget\api-key.bin`. The JSON settings file stores only device selection, appearance, and position. No API keys, readings, or HTTP response bodies are logged. A process running as the same Windows user can decrypt the key; DPAPI is not protection from a compromised user session.

## Use

- Drag anywhere on the widget. It snaps within 16 logical pixels of a monitor's usable edge, respecting the taskbar.
- Monitor identity and relative position are saved after dragging. Moving between displays accounts for per-monitor DPI; a missing display falls back to the primary monitor.
- Right-click for Settings, Reconnect, Move to main display, Hide, and Quit.
- Double-click the tray icon to show a hidden widget. `Esc` hides it; `Shift+F10` opens its menu when it has keyboard focus.
- Settings offers 156, 176, 208, or 240 logical pixels and System, Light, or Dark appearance.
- A small amber dot indicates an unavailable connection, an offline device, or a measurement older than 15 minutes. Hover for the timestamp and connection status. Unchanged measurements may legitimately become old; the indicator is conservative, not proof of a sensor fault.
- Missing readings show `—`, never a fabricated zero. Air Pro's **eCO₂** is estimated from TVOC, not a direct CO₂ measurement.
- Always-on-top applies to ordinary desktop windows, not the Windows secure desktop or exclusive full-screen applications.

For a visual preview without an API key:

```powershell
.\artifacts\publish\SensiboWidget.exe --demo
```

Demo mode shows clearly identified sample readings in its tooltip and tray label. It does not save settings, change startup registration, or contact Sensibo.

## Network behavior

The app uses the [Sensibo REST API and event stream](https://support.sensibo.com/api/) directly through .NET `HttpClient`:

- HTTPS only; authentication is sent through `X-Api-Key`, never in URLs.
- Gzip compression is requested. Redirects are disabled to avoid forwarding credentials.
- The SSE connection supplies measurement updates. On its `hello` event the app reads an initial snapshot, and refreshes the snapshot approximately every 15–17 minutes while receiving events/heartbeats.
- Partial updates are merged. Changes for other devices and accessory sensors are ignored.
- A missing heartbeat triggers reconnection. Failures use increasing delays with jitter, respect `Retry-After`, and retain previous readings with a connection warning.
- A slow snapshot is attempted between stream retries. Invalid credentials stop retries until Settings or Reconnect is used.

## Code layout

- `src/SensiboWidget.Core`: readings, JSON parsing, REST/SSE client, pure monitor-placement calculations.
- `src/SensiboWidget`: WPF windows, tray icon, per-monitor Windows integration, encrypted storage, startup registration.
- `tests/SensiboWidget.Tests`: offline assertions using fake HTTP responses; no real credentials.
- `scripts`: reproducible build and per-user install/uninstall entry points.

UI updates run on the WPF dispatcher. Network work runs asynchronously with cancellation. The modest UI uses code-behind for window behavior; domain and network logic are independent of WPF.

## Uninstall

Quit from the tray, then run from the repository:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

Alternatively, run the copy in `%LOCALAPPDATA%\Programs\SensiboDesktopWidget\uninstall.ps1`. Settings and the encrypted key are preserved by default. Add `-RemoveSettings` to remove them too. Revoke the key separately on Sensibo's API-key page if it is no longer needed.

## Manual acceptance checks

Use a real account to verify initial readings and changes; disconnect and reconnect the network; move the widget among all monitors with their actual DPI settings; restart it to verify position; disconnect the saved monitor to verify recovery; and sign out/in to verify automatic startup. Offline checks cannot establish these environmental behaviors.
