# Detect Device

## Programmer Detection

`ProgrammerDetectionService.DetectAvailable()` checks each supported programmer independently:

```csharp
new ProgrammerDetection(
    T48SDKProgrammer.CanOpenDevice(),
    RT809FSDKProgrammer.CanOpenDevice(),
    RT809HSDKProgrammer.CanOpenDevice(),
    Ch347NativeProgrammer.IsAvailable && Ch347NativeProgrammer.CanOpenDevice(),
    ChNativeProgrammer.IsAvailable && ChNativeProgrammer.CanOpenDevice());
```

## RT809F vs RT809H

RT809F and RT809H are both detected through FTDI enumeration.

Both SDKs check the same FTDI device ID:

```text
0x04036010
```

They are distinguished by FTDI serial prefix:

```text
RT809F: gggggggg
RT809H: byCHUNJI
```

Detection flow:

1. Load the FTDI native library
2. Call `FT_CreateDeviceInfoList`
3. Loop devices with `FT_GetDeviceInfoDetail`
4. Require `id == 0x04036010`
5. Match serial prefix for the target model

## CH341 vs CH347

CH341 and CH347 are both WCH USB devices, but they use different USB PID values and different native DLL APIs.

Both use the same WCH VID:

```text
VID_1A86
```

They are distinguished by PID:

```text
CH341: PID_5512
CH347: PID_55DA or PID_55DB
```

The app also requires the matching native DLL before trying to open each device:

```text
CH341: CH341DLLA64.DLL
CH347: CH347DLLA64.DLL
```

CH341 detection flow:

1. Check `CH341DLLA64.DLL` exists in `System32` or app folder
2. Check USB device `VID_1A86`, `PID_5512`
3. Call `CHOpenDevice(0)`
4. Call `CHSetStream(0, 0x81)`
5. Close the device with `CHCloseDevice(0)`

CH347 detection flow:

1. Check `CH347DLLA64.DLL` exists in `System32` or app folder
2. Check USB device `VID_1A86`, `PID_55DA` or `PID_55DB`
3. Call `CH347OpenDevice(0)`
4. Call `CH347SPI_Init(0, SpiConfig.Default)`
5. Close the device with `CH347CloseDevice(0)`

## Auto Selection Order

When the programmer selector is set to `Auto`, `MainWindow.ApplyProgrammerDetection()` prefers devices in this order:

```text
CH341 -> CH347 -> RT809F -> RT809H -> T48
```

So if both RT809F and RT809H are reported as present, Auto selects RT809H first.

## Manual Selection

Manual selector behavior:

```text
RT809F selected -> only uses Rt809fDetected
RT809H selected -> only uses Rt809hDetected
```

If the selected programmer is not detected, the app reports that selected programmer as disconnected.
