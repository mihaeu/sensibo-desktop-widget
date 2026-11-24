# Validation

## .NET 11 upgrade

Validated on 2026-11-24 on Windows x64 with .NET SDK 11.0.100 and the pinned self-contained runtime 11.0.0.

- `scripts/build.ps1` completed with locked restore, zero build warnings and errors, all 18 offline checks passing, and a successful publish.
- A fresh source copy in another directory produced a byte-identical executable and all three accompanying runtime DLLs.
- The published executable started in sample-data mode and remained running for five seconds with no standard-error output. This checks native WPF startup, not live connectivity or visual behavior.
- The initial single-file publish crashed before opening WPF. The project now keeps `DirectWriteForwarder.dll`, `System.Printing.dll`, and `ijwhost.dll` beside the executable to work around [dotnet/wpf#11842](https://github.com/dotnet/wpf/issues/11842). Keep these files with the executable; the installer copies the whole publish folder.
- The upgrade has not been installed over the existing desktop installation. Quit the installed widget and rerun `scripts/install.ps1 -Launch` after building to update it.

SHA-256 values verified against the independent rebuild:

```text
6687D8ACB9D37418A47F5E2D3D971CAAD1DAE456930BE9DAF7C86E662685F2CD  SensiboWidget.exe
17F7BE7D3C64C2C6AA5501FDD7470975B7D1E231FAD9CA8978FD15CB2C2153D9  DirectWriteForwarder.dll
DB3AA8A0DAAB89BECD05C29850CD5B386528B79CBC84558173A3B67AD59B1C7A  System.Printing.dll
C8AFE2135389D5DEC5DBBD1BD96C8ED2F543631D48090D15BD2FDBCFDD1F282F  ijwhost.dll
```

## Initial .NET 10 installation

The original build used SDK 10.0.404 and runtime 10.0.15. Its Release build and 18 offline checks passed, and an independent rebuild produced an identical executable. The per-user installer, installed executable hash, Start menu shortcut, and current-user startup registration were verified. The user subsequently confirmed that the widget was running with their key.

SHA-256 of the original installed executable:

```text
43349D467A400F33FFCB0201CDD7B8DCA80792851E8BDAB980D52CB4E78443DD
```

Still to validate with the user: dragging across the actual three-monitor arrangement, restoration after monitor disconnection, and a real Windows sign-in cycle. No real API key was used in automated tests. The fixture and README demo values are samples, not evidence of a live connection.
