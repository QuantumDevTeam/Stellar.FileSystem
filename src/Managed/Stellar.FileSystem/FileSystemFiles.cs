using System.Text;

using Stellar.Kernel.FileSystem;

namespace Stellar.FileSystem;

/// <summary>
/// Whole files at once: bytes and text, read and written through any domain.
/// </summary>
/// <remarks>
/// Text is read in the charset of the content type of the file (<see cref="ContentType.Charset"/>), UTF-8 by
/// default, and a byte order mark wins over both. For large files, or data shared between owners, open a stream or
/// lease a resource instead.
/// </remarks>
public static class FileSystemFiles
{
    private static byte[] ReadToEnd(Stream stream)
    {
        if (stream is MemoryStream memory && memory.TryGetBuffer(out ArraySegment<byte> segment))
            return segment.AsSpan((int)memory.Position).ToArray();

        var buffer = new MemoryStream(stream.CanSeek ? (int)Math.Min(stream.Length, int.MaxValue) : 0);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static Encoding EncodingOf(IFileSystem fileSystem, FilePath path)
    {
        string? charset = fileSystem.ContentTypes.Resolve(path).Charset;
        if (charset is null or "utf-8" or "utf8") return new UTF8Encoding(false);

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return new UTF8Encoding(false);
        }
    }

    private static string Decode(ReadOnlySpan<byte> data, Encoding encoding)
    {
        // A byte order mark wins over the charset, as StreamReader does
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return Encoding.UTF8.GetString(data[3..]);
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])) return Encoding.Unicode.GetString(data[2..]);
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) return Encoding.BigEndianUnicode.GetString(data[2..]);
        return encoding.GetString(data);
    }

    /// <summary>
    /// Reads the bytes of a file.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="path">The file.</param>
    /// <returns>A copy of its bytes.</returns>
    public static byte[] ReadAllBytes(this IFileSystem fileSystem, FilePath path)
    {
        IDomain domain = fileSystem.GetDomain(path);
        if (domain.TryGetMemory(path.Value, out ReadOnlyMemory<byte> memory)) return memory.ToArray();

        using Stream stream = domain.Open(path.Value,
            FileOpenOptions.Read with { Flags = FileOpenFlags.Sequential });
        return ReadToEnd(stream);
    }

    /// <summary>
    /// Reads the bytes of a file without blocking on a slow source.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>A copy of its bytes.</returns>
    public static async ValueTask<byte[]> ReadAllBytesAsync(
        this IFileSystem fileSystem, FilePath path,
        CancellationToken cancellationToken = default
    )
    {
        IDomain domain = fileSystem.GetDomain(path);
        if (domain.TryGetMemory(path.Value, out ReadOnlyMemory<byte> memory)) return memory.ToArray();

        await using Stream stream = await domain.OpenAsync(path.Value,
                FileOpenOptions.Read with { Flags = FileOpenFlags.Sequential }, cancellationToken)
            .ConfigureAwait(false);
        var buffer = new MemoryStream(stream.CanSeek ? (int)Math.Min(stream.Length, int.MaxValue) : 0);
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>
    /// Reads a file as text.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="path">The file.</param>
    /// <returns>Its text.</returns>
    public static string ReadAllText(this IFileSystem fileSystem, FilePath path)
    {
        Encoding encoding = EncodingOf(fileSystem, path);
        IDomain domain = fileSystem.GetDomain(path);
        if (domain.TryGetMemory(path.Value, out ReadOnlyMemory<byte> memory)) return Decode(memory.Span, encoding);

        using Stream stream = domain.Open(path.Value,
            FileOpenOptions.Read with { Flags = FileOpenFlags.Sequential });
        using var reader = new StreamReader(stream, encoding, true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads a file as text without blocking on a slow source.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>Its text.</returns>
    public static async ValueTask<string> ReadAllTextAsync(
        this IFileSystem fileSystem, FilePath path,
        CancellationToken cancellationToken = default
    )
    {
        byte[] data = await fileSystem.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Decode(data, EncodingOf(fileSystem, path));
    }

    /// <summary>
    /// Writes the bytes of a file, replacing it, without blocking on a slow source.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="path">The file.</param>
    /// <param name="data">The bytes; they must not change until the write ends.</param>
    /// <param name="atomic">Whether the file is the old one or the new one, never a part of it, a cancelled write included; needs <see cref="DomainCapabilities.AtomicWrite"/>.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>Ends when the file is written.</returns>
    public static async ValueTask WriteAllBytesAsync(
        this IFileSystem fileSystem, FilePath path,
        ReadOnlyMemory<byte> data, bool atomic = false, CancellationToken cancellationToken = default
    )
    {
        FileOpenOptions options = atomic ? FileOpenOptions.Replace : FileOpenOptions.Write;
        await using Stream stream = await fileSystem.OpenAsync(path, options, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a file as text, replacing it, without blocking on a slow source; the charset as in <see cref="WriteAllText"/>.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="path">The file.</param>
    /// <param name="text">The text.</param>
    /// <param name="atomic">Whether the file is the old one or the new one, never a part of it, a cancelled write included; needs <see cref="DomainCapabilities.AtomicWrite"/>.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>Ends when the file is written.</returns>
    public static ValueTask WriteAllTextAsync(
        this IFileSystem fileSystem, FilePath path,
        string text, bool atomic = false, CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        return fileSystem.WriteAllBytesAsync(path, EncodingOf(fileSystem, path).GetBytes(text), atomic,
            cancellationToken);
    }

    /// <summary>
    /// Writes the bytes of a file, replacing it.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="path">The file.</param>
    /// <param name="data">The bytes.</param>
    /// <param name="atomic">Whether the file is the old one or the new one, never a part of it; needs <see cref="DomainCapabilities.AtomicWrite"/>.</param>
    public static void WriteAllBytes(
        this IFileSystem fileSystem, FilePath path,
        ReadOnlySpan<byte> data, bool atomic = false
    )
    {
        using Stream stream = fileSystem.Open(path, atomic ? FileOpenOptions.Replace : FileOpenOptions.Write);
        stream.Write(data);
    }

    /// <summary>
    /// Writes a file as text, replacing it, in the charset of its content type; UTF-8 with no byte order mark by default.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="path">The file.</param>
    /// <param name="text">The text.</param>
    /// <param name="atomic">Whether the file is the old one or the new one, never a part of it; needs <see cref="DomainCapabilities.AtomicWrite"/>.</param>
    public static void WriteAllText(
        this IFileSystem fileSystem, FilePath path,
        string text, bool atomic = false
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        fileSystem.WriteAllBytes(path, EncodingOf(fileSystem, path).GetBytes(text), atomic);
    }
}
