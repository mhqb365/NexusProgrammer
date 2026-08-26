# RT809F SDK

SDK to control RT809F Programmer SPI-NOR through FTDI D2XX.

This is an unofficial, community-maintained project. It is not affiliated with
or endorsed by ifix.net.

## Supported

- Non-invasive discovery and JEDEC ID
- 24-bit SPI-NOR read, blank check and verify
- Chip erase and batched page program
- Optional blank-page skipping
- Progress, cancellation and deterministic interface cleanup

## Requirements

- Windows and .NET 8
- FTDI D2XX driver
- Matching `ftd2xx64.dll` or `ftd2xx.dll` available to the process

Driver binaries are not distributed with this repository. Install or obtain them from FTDI or your device vendor and make sure the matching 32-bit or 64-bit DLL can be found by the running process.

## Build

```powershell
dotnet build src/RT809F.SDK.csproj
dotnet build src/samples/RT809F.Cli/RT809F.Cli.csproj
```

## Library

```xml
<ProjectReference Include="path/to/RT809FSDK/src/RT809F.SDK.csproj" />
```

```csharp
using RT809F.SDK;

using var programmer = RT809FProgrammer.Open();
var id = programmer.ReadId();
var data = await programmer.ReadAsync(0, 32 * 1024 * 1024);
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

## Safety

Erase and write operations modify the attached flash device. Always keep a verified backup before destructive commands.

## License

MIT. See [LICENSE](LICENSE).
