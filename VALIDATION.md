# Validation

Validated on 2026-11-24 on Windows x64 with .NET SDK 10.0.404 and the pinned self-contained runtime 10.0.15.

- Release build: zero warnings and errors, with warnings treated as errors.
- All 18 offline checks passed. These include partial/null readings, device filtering, cancellation, an empty event stream, missing heartbeats, rate limits, and monitor placement.
- A fresh source copy at a different absolute directory restored with locked inputs and produced a byte-identical published executable.
- The per-user installer completed. Installed executable hash, Start menu shortcut, and current-user startup registration were verified.
- A sample-data process was launched; further visual inspection was skipped at the user's request.

SHA-256 of the published and installed `SensiboWidget.exe`:

```text
43349D467A400F33FFCB0201CDD7B8DCA80792851E8BDAB980D52CB4E78443DD
```

Still to validate with the user: live readings using their key, dragging across the actual three-monitor arrangement, restoration after monitor disconnection, and a real Windows sign-in cycle. No real API key was used in automated tests. The fixture values are samples, not evidence of a live connection.
