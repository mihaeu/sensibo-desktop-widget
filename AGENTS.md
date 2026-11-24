# Working agreements

- Keep changes focused and maintainable. Use platform facilities before adding dependencies.
- Do not add assistant or tool branding to source, documentation, filenames, branches, or commits. Ask for confirmation if explicitly requested to do so.
- When there is no ticket number, use a plain descriptive commit subject without prefixes or extra markup.
- Preserve the temperature-first layout and compact footprint unless a design change is requested.

## Architecture

- Keep API parsing, streaming, and placement calculations in `SensiboWidget.Core` without WPF dependencies.
- Keep Windows integration and UI behavior in `SensiboWidget`.
- Dispatch view updates to the UI thread. Cancel network work on exit and when changing credentials or devices.
- Do not add a service, web server, database, telemetry, or background updater for this widget.

## Secrets and API use

- Never commit, print, or log real keys, account responses, or sensor histories.
- Use `X-Api-Key` headers over HTTPS. Do not send keys in URLs or follow redirects with credentials.
- Persist keys only with Windows DPAPI for the current user.
- Keep all API requests read-only. Respect rate limits, server retry delays, heartbeat timeouts, and reconnect backoff.
- Treat absent and null measurements separately from zero. Preserve partial SSE updates and measurement timestamps.

## Windows behavior

- Work in physical coordinates at Win32 boundaries and logical pixels in WPF.
- Test negative monitor coordinates, different DPI scales, taskbar work areas, and missing displays.
- Retain startup opt-out, explicit Quit, and tray-based recovery when hidden.
- Keep installation per-user. Guard deletion paths and preserve settings unless removal is explicit.

## Validation and build

- Run `scripts/build.ps1` before handing off a code change. It restores locked inputs, builds, runs offline checks, and publishes.
- Add focused tests for meaningful behavior changes, especially event parsing, reconnects, and position restoration.
- Keep the SDK and runtime pins explicit. Review lock-file changes when updating them.
- Do not claim live sensor or multi-monitor validation from fixtures or calculations alone.
- Update README instructions when setup, build, installation, or behavior changes.
