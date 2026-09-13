using System.Runtime.InteropServices;

namespace TL866IIPlusSdk;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UsbDeviceDescriptor
{
    public byte Length;
    public byte DescriptorType;
    public ushort BcdUsb;
    public byte DeviceClass;
    public byte DeviceSubClass;
    public byte DeviceProtocol;
    public byte MaxPacketSize0;
    public ushort VendorId;
    public ushort ProductId;
    public ushort BcdDevice;
    public byte ManufacturerIndex;
    public byte ProductIndex;
    public byte SerialNumberIndex;
    public byte NumConfigurations;
}
