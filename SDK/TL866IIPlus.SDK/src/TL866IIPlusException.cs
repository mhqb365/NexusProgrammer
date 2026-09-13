using System.ComponentModel;

namespace TL866IIPlusSdk;

public sealed class TL866IIPlusException : Exception
{
    public TL866IIPlusException(string message) : base(message)
    {
    }

    public TL866IIPlusException(string message, int nativeError)
        : base($"{message} Win32 error {nativeError}: {new Win32Exception(nativeError).Message}")
    {
        NativeError = nativeError;
    }

    public int? NativeError { get; }
}
