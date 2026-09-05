using System.IO;

namespace NexusProgrammer;

public static class LargeFileIo
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public static async Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await File.WriteAllBytesAsync(path, data, cancellationToken);
    }

    public static async Task CopyAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destinationMode = overwrite ? FileMode.Create : FileMode.CreateNew;
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using var destination = new FileStream(destinationPath, destinationMode, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await source.CopyToAsync(destination, BufferSize, cancellationToken);
    }
}
