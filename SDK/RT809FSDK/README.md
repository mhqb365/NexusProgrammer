# RT809F SDK

.NET SDK to control the RT809F programmer SPI-NOR interface through FTDI D2XX.

This is an unofficial, community-maintained project. It is not affiliated with
or endorsed by ifix.net or the RT809F vendor.

## Status

Experimental but usable for controlled hardware testing and application
integration. The public API is intentionally small, but protocol behavior may
change as more hardware captures and flash chips are validated.

Working on real hardware:

- Detect whether an RT809F FTDI interface is connected.
- Open and initialize the programmer.
- Read SPI-NOR JEDEC ID.
- Read SPI-NOR ranges.
- Blank-check SPI-NOR ranges.
- Erase the whole chip.
- Program SPI-NOR pages in batches.
- Verify by streaming readback against the expected buffer.
- Optionally skip `0xFF` pages during program.
- Report progress, honor cancellation tokens, and clean up both FTDI channels.

The SDK supports 3-byte and 4-byte SPI-NOR addressing. Range arguments are still
`int` lengths, so a single operation is limited to a .NET array-sized buffer.

## Requirements

- Windows.
- .NET 8 SDK/runtime.
- FTDI D2XX driver installed.
- Matching `ftd2xx64.dll` or `ftd2xx.dll` available to the process.

Driver binaries are not distributed with this repository. Install or obtain them
from FTDI or your device vendor, then make sure the matching 32-bit or 64-bit DLL
can be found by the running process.

Close vendor programmer software before using the SDK. Another process holding
the FTDI device can prevent the SDK from opening the programmer.

## Project Layout

```text
RT809FSDK/
  src/RT809FProgrammer.cs        Reusable SDK library
  src/RT809F.SDK.csproj          Library project
  src/samples/RT809F.Cli/        CLI sample and smoke-test tool
```

## Build

From this directory:

```powershell
dotnet build .\src\RT809F.SDK.csproj
dotnet build .\src\samples\RT809F.Cli\RT809F.Cli.csproj
```

From the Nexus Programmer repository root:

```powershell
dotnet build .\SDK\RT809FSDK\src\RT809F.SDK.csproj
dotnet build .\SDK\RT809FSDK\src\samples\RT809F.Cli\RT809F.Cli.csproj
```

## Add To Another .NET App

Add a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="path\to\RT809FSDK\src\RT809F.SDK.csproj" />
</ItemGroup>
```

Or reference the built `RT809F.SDK.dll` directly.

## Basic API Usage

Detect and read JEDEC ID:

```csharp
using RT809F.SDK;

if (!RT809FProgrammer.IsConnected())
{
    Console.WriteLine("RT809F not found");
    return;
}

using var programmer = RT809FProgrammer.Open();
var id = programmer.ReadId();
Console.WriteLine(id); // C8 40 19
```

Read a full 32 MiB flash:

```csharp
var progress = new Progress<int>(percent => Console.WriteLine($"{percent}%"));

using var programmer = RT809FProgrammer.Open();
var data = await programmer.ReadAsync(0, 32 * 1024 * 1024, progress);
await File.WriteAllBytesAsync("backup.bin", data);
```

Blank-check, erase, write, and verify:

```csharp
using var programmer = RT809FProgrammer.Open();

await programmer.BlankCheckAsync(0, 32 * 1024 * 1024, progress);
await programmer.EraseAsync(TimeSpan.FromMinutes(3), progress);

var image = await File.ReadAllBytesAsync("image.bin");
await programmer.ProgramAsync(0, image, skipBlankPages: true, progress);
await programmer.VerifyAsync(0, image, progress);
```

Public API:

```text
RT809FProgrammer.IsConnected()
RT809FProgrammer.Open()
ReadId()
ReadAsync(uint address, int length, IProgress<int>? progress, CancellationToken token)
BlankCheckAsync(uint address, int length, IProgress<int>? progress, CancellationToken token)
EraseAsync(TimeSpan timeout, IProgress<int>? progress, CancellationToken token)
ProgramAsync(uint address, ReadOnlyMemory<byte> data, bool skipBlankPages, IProgress<int>? progress, CancellationToken token)
VerifyAsync(uint address, ReadOnlyMemory<byte> expected, IProgress<int>? progress, CancellationToken token)
```

## CLI Usage

Run the CLI without arguments to print all commands.

```powershell
dotnet run --project .\src\samples\RT809F.Cli -- detect
dotnet run --project .\src\samples\RT809F.Cli -- id
dotnet run --project .\src\samples\RT809F.Cli -- read backup.bin 0x2000000
dotnet run --project .\src\samples\RT809F.Cli -- read backup-region.bin 4096 0x100000
dotnet run --project .\src\samples\RT809F.Cli -- blank 0x2000000
dotnet run --project .\src\samples\RT809F.Cli -- verify backup.bin
dotnet run --project .\src\samples\RT809F.Cli -- erase --yes
dotnet run --project .\src\samples\RT809F.Cli -- write image.bin --skip-ff --yes
```

Numbers accept decimal or `0x`-prefixed hexadecimal notation. Erase and write
require `--yes`.

## Progress And Cancellation

Read and verify progress is based on bytes streamed from the chip. Program
progress is based on bytes queued and flushed in page-program batches. Erase
progress is time-estimated until the flash status register reports ready.

Pass a `CancellationToken` to stop long operations. The SDK attempts
deterministic cleanup of the FTDI SPI and control interfaces on dispose.

## Troubleshooting

`RT809F not found.`

Check:

- The programmer is plugged in.
- The FTDI D2XX driver is installed.
- Device Manager shows the RT809F FTDI interfaces.
- No vendor programmer software is holding the device.

`Cannot load ftd2xx64.dll` or `Cannot load ftd2xx.dll`.

Make sure the matching FTDI D2XX DLL is installed globally or copied beside the
application executable.

Read ID returns `00 00 00` or `FF FF FF`.

Check chip orientation, clip contact, adapter wiring, voltage, and whether the
chip is supported by the current SPI-NOR flow.

## Safety

Erase and write operations modify the attached flash device. Always keep a
verified backup before destructive commands, and test new integrations with a
sacrificial flash chip first.

Do not commit proprietary driver binaries, firmware dumps, or hardware capture
files that you do not have permission to publish.

## Contributing

Issues and pull requests are welcome. Please include the RT809F model, Windows
version, .NET version, flash chip part number, JEDEC ID, exact CLI/API command,
and whether the workflow used destructive operations.

See `CONTRIBUTING.md` for development notes.

## License

MIT. See [LICENSE](LICENSE).
