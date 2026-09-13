namespace TL866IIPlusSdk;

public sealed record TL866IIPlusDeviceInfo(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    Guid InterfaceGuid,
    string ProductString)
{
    public static readonly Guid WinUsbInterfaceGuid = new("E7E8BA13-2A81-446E-A11E-72398FBDA82F");
    public const ushort XGecuVendorId = 0xA466;
    public const ushort XGecuProductId = 0x0A53;

    public bool IsTl866IIPlus =>
        ProductString.Contains("TL866", StringComparison.OrdinalIgnoreCase) &&
        ProductString.Contains("II", StringComparison.OrdinalIgnoreCase);
}
