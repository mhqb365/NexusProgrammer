# NexusProgrammer

NexusProgrammer is a Windows utility app for CH34x and XGecu T48.
It focuses on SPI NOR flash chips. It can detect a programmer, search/select SPI NOR chips, read/write/verify buffers and erase chips

## Features

- CH341, CH347, and XGecu T48 programmer detection.
- SPI NOR catalog search with JEDEC ID matching.
- Separate SPI NOR catalogs for CH34x and XGecu T48.
- Hex buffer preview/editor.
- Read, write, verify, erase, and script workflows.
- Light/dark mode.

## Requirements

- Windows.
- .NET 8 SDK to build from source.
- WCH CH341/CH347 driver and native DLLs for programmer hardware access.
- XGecu WinUSB driver for T48 hardware access.

The app looks for the native WCH DLLs next to the EXE or in the Windows system
directory. Without hardware/DLL access, programmer-dependent actions are disabled.

## Build

```powershell
dotnet build "NexusProgrammer.csproj"
```

Output is written to `bin/Debug/net8.0-windows/`.

## Publish

```powershell
dotnet publish "NexusProgrammer.csproj" -c Release -r win-x64 --self-contained false
```

Copy any required WCH native DLLs next to the published EXE if they are not
installed system-wide.

## License

Application source code is licensed under the MIT License. See `LICENSE`.

Parts of the CH34x SPI NOR catalog are generated from flashrom chip definitions and remain
subject to the flashrom GPL-2.0-or-later license. See `THIRD_PARTY_NOTICES.md`
and `flashrom-data/COPYING.rst`.
