namespace TL866IIPlusSdk;

public sealed record UsbPipeInfo(byte PipeId, int PipeType, ushort MaximumPacketSize, byte Interval)
{
    public bool IsInput => (PipeId & 0x80) != 0;
    public bool IsOutput => !IsInput;
}
