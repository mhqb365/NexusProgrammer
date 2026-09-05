# XGecu T48 SDK

.NET SDK to control XGecu T48 programmer SPI-NOR through WinUSB.

This is an unofficial, community-maintained project. It is not affiliated with
or endorsed by XGecu.

## Status

Experimental but usable for controlled hardware testing and application
integration. The SDK is useful for protocol work, but the public API and
protocol coverage may change as more flash chips are validated.

Working on real hardware:

- Detect and open the T48 over WinUSB.
- Query USB bulk endpoints.
- Read SPI25 JEDEC ID.
- Read SPI25 flash ranges.
- Blank-check SPI25 flash ranges.
- Erase SPI25 chip.
- Write SPI25 flash pages.
- Optional sparse write that skips blank `0xFF` pages.
- 1.8V and large-flash protocol profiles exposed through API flags.
- Refuse destructive operations when the initial probe does not confirm the
  selected SPI flash ID.

Validated workflow:

```text
Read ID -> Read full chip -> Erase -> Write -> Read back -> Compare
```

Tested JEDEC ID:

```text
EF4018
```

## Device Facts

Read from `drv/Xgprowinusb.inf`:

- USB VID: `0xA466`
- USB PID: `0x0A53`
- Driver: WinUSB
- Interface GUID: `{E7E8BA13-2A81-446E-A11E-72398FBDA82F}`
- Device manager class: `XGecu USB Devices`

The SDK uses this GUID to enumerate and open the programmer.

## Requirements

- Windows.
- XGecu WinUSB driver installed.
- .NET SDK/runtime matching the project target. The library currently targets
  `net8.0-windows` and `net10.0-windows`; the `T48Probe` sample currently
  targets `net10.0-windows`.
- Close `Xgpro.exe`, Wireshark, and `dumpcap.exe` before using the SDK. They can
  hold the USB device and cause `Access is denied`.

The SDK has no external NuGet dependencies.

## Project Layout

```text
T48.SDK/
  src/                     Reusable SDK library
  src/samples/T48Probe/    CLI probe and test tool
  tools/                   USBPcap parsing helpers
  PROTOCOL_NOTES.md        Reverse-engineering notes
```

## Build

From this directory:

```powershell
dotnet build .\XGecuT48SDK.sln
```

From the Nexus Programmer repository root:

```powershell
dotnet build .\SDK\T48.SDK\XGecuT48SDK.sln
```

Built DLLs:

```text
T48.SDK\src\bin\Debug\net8.0-windows\T48.SDK.dll
T48.SDK\src\bin\Debug\net10.0-windows\T48.SDK.dll
```

## Add To Another .NET App

Preferred: add a project reference to:

```text
T48.SDK\src\T48.SDK.csproj
```

Example `.csproj` reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\T48.SDK\src\T48.SDK.csproj" />
</ItemGroup>
```

Or reference the built `T48.SDK.dll` directly.

## Basic API Usage

Read JEDEC ID:

```csharp
using T48Sdk;
using T48Sdk.Spi25;

using var device = T48UsbDevice.OpenFirst();
var spi25 = new T48Spi25Client(device);

var id = spi25.ReadJedecId();
Console.WriteLine(id.JedecHex); // EF4018
```

Read flash bytes:

```csharp
var progress = new Progress<T48Progress>(p =>
{
    Console.WriteLine($"{p.Operation}: {p.Percent:F1}% {p.Message}");
});

byte[] data = spi25.ReadFlash(offset: 0, length: 256, progress);
File.WriteAllBytes("read-000000.bin", data);
```

Blank-check a range:

```csharp
var blank = spi25.BlankCheck(offset: 0, length: 16 * 1024 * 1024, progress);
Console.WriteLine(blank.IsBlank);
```

Erase and write:

```csharp
spi25.EraseChip(progress);

byte[] image = File.ReadAllBytes("image.bin");
spi25.WriteFlash(offset: 0, image, progress);
```

Write constraints:

- Offset must be aligned to `256` bytes.
- Data length must be a multiple of `256` bytes.
- Set `useLargeFlashProfile: true` for chips that need the large-flash command
  profile.
- Set `use1V8Profile: true` only when the chip and adapter setup require the
  1.8V profile.
- Use a sacrificial/test chip until your app has its own confirmation flow.

Public API highlights:

```text
T48DeviceDiscovery.FindConnectedDevices()
T48UsbDevice.OpenFirst(IUsbTransferLogger? logger)
T48UsbDevice.Open(T48DeviceInfo info, IUsbTransferLogger? logger)
T48Spi25Client.ReadJedecId(bool use1V8Profile)
T48Spi25Client.ReadFlash(uint offset, int length, IProgress<T48Progress>? progress, bool useLargeFlashProfile, bool use1V8Profile)
T48Spi25Client.BlankCheck(uint offset, int length, IProgress<T48Progress>? progress, bool useLargeFlashProfile, bool use1V8Profile)
T48Spi25Client.EraseChip(IProgress<T48Progress>? progress, TimeSpan? progressEstimate, bool useLargeFlashProfile, bool use1V8Profile)
T48Spi25Client.WriteFlash(uint offset, ReadOnlySpan<byte> data, IProgress<T48Progress>? progress, bool useLargeFlashProfile, bool use1V8Profile)
T48Spi25Client.WriteFlashSparse(uint offset, ReadOnlySpan<byte> data, IProgress<T48Progress>? progress, bool useLargeFlashProfile, bool use1V8Profile)
```

## CLI Usage

The sample CLI is useful for testing the programmer before integrating the SDK.

List devices:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" list
```

Show endpoints:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" pipes
```

Read ID:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" read-id
```

Read 256 bytes:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" read-flash 0 256 "T48.SDK\read-000000.bin"
```

Read full W25Q128, 16 MiB:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" read-flash 0 16777216 "T48.SDK\w25q128-full.bin"
```

Blank-check full W25Q128:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" blank-check 0 16777216 "T48.SDK\blank.log"
```

Erase chip:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" erase-chip "T48.SDK\erase.log"
```

Optional smooth erase progress estimate, in seconds:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" erase-chip "T48.SDK\erase.log" 45
```

Write image:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" write-flash 0 "T48.SDK\w25q128-full.bin" "T48.SDK\write.log"
```

Verify by readback and binary compare:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" read-flash 0 16777216 "T48.SDK\w25q128-verify-read.bin"
cmd /c fc /b "T48.SDK\w25q128-full.bin" "T48.SDK\w25q128-verify-read.bin"
```

Expected successful compare:

```text
FC: no differences encountered
```

Raw transfer for protocol work:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" raw "0501030000000000" 32 t48-usb.log
```

## Logs

Most CLI commands accept an optional log file as the last argument. The log
records USB direction, pipe, byte count, elapsed time, and payload hex.

Example:

```powershell
dotnet ".\T48.SDK\src\samples\T48Probe\bin\Debug\net10.0-windows\T48Probe.dll" read-id "T48.SDK\read-id.log"
```

## Progress

The SDK exposes fallback progress with `IProgress<T48Progress>`.

- Read: based on 16 KiB blocks received from endpoint `0x82`.
- Blank check: based on bytes scanned.
- Write: based on 256-byte pages sent and acknowledged.
- Erase: smooth elapsed-time fallback from 20% to 95% while waiting for the T48
  erase response, then 100% when the real response arrives. The current protocol
  capture gives done/error style responses, not a reliable real chip percentage.
  Erase success is accepted only when the response matches `0E00<JEDEC...>`.

For UI integration, treat `T48Progress.Percent` as display progress and
`T48Progress.Message` as optional status text.

## Troubleshooting

`Unable to open XGecu T48 device. Win32 error 5: Access is denied.`

Close any program holding the device:

- `Xgpro.exe`
- Wireshark
- `dumpcap.exe`

Then unplug/replug the T48 and retry.

`No XGecu T48 WinUSB device was found.`

Check:

- T48 is plugged in.
- Driver is installed.
- Device Manager shows `XGecu WinUSB Device`.
- VID/PID is `A466:0A53`.

PowerShell `fc` problem:

PowerShell aliases `fc` to `Format-Custom`. Use:

```powershell
cmd /c fc /b file1.bin file2.bin
```

Or compare hashes:

```powershell
(Get-FileHash file1.bin).Hash -eq (Get-FileHash file2.bin).Hash
```

## Protocol Notes

Low-level command frames and capture analysis live in:

```text
T48.SDK\PROTOCOL_NOTES.md
```

USBPcap helper tools:

```powershell
python ".\T48.SDK\tools\parse-usbpcap.py" C:\Users\Windows\Desktop\t48.pcap
python ".\T48.SDK\tools\summarize-captures.py"
```

## Safety

Erase and write are destructive. Build UI-level confirmations around them.
For early testing, use a sacrificial chip and always keep a readback backup.

## Contributing

Issues and pull requests are welcome. Please include the T48 driver version,
chip model, JEDEC ID, command used, and whether the workflow touched destructive
operations.

## License

MIT. See `LICENSE`.
