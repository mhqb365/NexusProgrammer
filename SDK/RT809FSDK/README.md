# RT809F .NET SDK

Managed .NET 8 SDK used by Nexus Programmer to control RT809F SPI-NOR through FTDI D2XX.

## Supported

- Non-invasive discovery and JEDEC ID
- 24-bit SPI-NOR read, blank check and verify
- Chip erase and batched page program
- Optional blank-page skipping
- Progress, cancellation and deterministic interface cleanup

The SDK supports SPI 25xx devices up to 16 MiB. It uses a conservative 3 MHz clock and reproduces the interface-B socket-control sessions documented in `usbcap/`.

## Requirements

- Windows and .NET 8
- FTDI D2XX driver
- Matching `ftd2xx64.dll` or `ftd2xx.dll` available to the process

## Library

```xml
<ProjectReference Include="path/to/RT809FSDK/src/RT809F.SDK.csproj" />
```

```csharp
using RT809F.SDK;

using var programmer = RT809FProgrammer.Open();
var id = programmer.ReadId();
var data = await programmer.ReadAsync(0, 16 * 1024 * 1024);
```

## CLI

```powershell
dotnet run --project src/samples/RT809F.Cli -- detect
dotnet run --project src/samples/RT809F.Cli -- id
dotnet run --project src/samples/RT809F.Cli -- read backup.bin 0x1000000
dotnet run --project src/samples/RT809F.Cli -- verify backup.bin
```

Erase and write require `--yes`. Run the CLI without arguments for all commands.

The `usbcap/` directory is retained as protocol documentation and is not required at runtime.
