# Validation

## Spacing refinement

Validated on 2026-11-26. The content now has equal outer padding and content-sized rows separated by equal flexible gaps, with dividers centered in each gap. The temperature row remains taller than the other two rows.

- Native WPF layout measurements at 240 pixels: 14 pixels of padding on all four sides, and two equal gaps of approximately 32.68 pixels. Equal padding and gaps were also verified at 272, 296, and 320 pixels.
- The sample image was regenerated and inspected. Release build and publish succeeded with zero warnings or errors; all 41 checks passed.
- The installed executable matches the build and remained running after restart. The startup preference was preserved. Interactive monitor movement and Windows sign-in were not retested.

SHA-256 of the published and installed executable after the spacing refinement:

```text
716E4E4FC67D3D4FB1F8BF55B052117E4E9C255A02212AD110A307A7F7E3D2C9
```

## Barcelona weather and Room first layout

Validated on 2026-11-26 on Windows x64 with SDK 11.0.100 and runtime 11.0.0.

- Release build and publish completed with zero warnings and errors. All 41 offline checks passed, including independent 15-minute/hourly schedules, rate limits, missing and malformed data, cancellation, Barcelona midnight, and both daylight-saving transitions.
- The optional `--weather-live` check returned the Barcelona outside temperature, sea surface temperature, and all three forecast periods through the actual weather client, without a Sensibo key.
- The published app started in offline demo mode and remained running for five seconds without standard-error output.
- A second build in the repository produced the same executable as the independent source copy.
- The installed widget was updated and restarted. The installed executable and all three runtime DLLs match the publish output; the existing startup preference was preserved. The updated process remained running after launch.
- The README images were regenerated from the native WPF layouts with sample data. The widget image was inspected for readable values, Celsius units, and forecast glyphs.
- Weather is model data. The live endpoint check does not validate accuracy against a Barcelona thermometer or a sea sensor. Multi-monitor and sign-in behavior still need an interactive acceptance check.

SHA-256 of the published executable:

```text
274CFB95FE109CABB3351F870B56B32F56929C1E44E97516BD0F1F07D6E1D8CB
```

## .NET 11 upgrade

Validated on 2026-11-24 on Windows x64 with .NET SDK 11.0.100 and the pinned self-contained runtime 11.0.0.

- `scripts/build.ps1` completed with locked restore, zero build warnings and errors, all 18 offline checks passing, and a successful publish.
- A fresh source copy in another directory produced a byte-identical executable and all three accompanying runtime DLLs.
- The published executable started in sample-data mode and remained running for five seconds with no standard-error output. This checks native WPF startup, not live connectivity or visual behavior.
- The initial single-file publish crashed before opening WPF. The project now keeps `DirectWriteForwarder.dll`, `System.Printing.dll`, and `ijwhost.dll` beside the executable to work around [dotnet/wpf#11842](https://github.com/dotnet/wpf/issues/11842). Keep these files with the executable; the installer copies the whole publish folder.
- At this stage the .NET 11 upgrade was built but not installed. The later Barcelona weather validation above includes installation of the updated .NET 11 app.

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
