using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Stellar.Core.Quantization;
using Stellar.FileSystem.Drivers;
using Stellar.Kernel.FileSystem;

namespace Stellar.FileSystem;

/// <summary>
/// The file system of one engine instance: its drivers, the domains they mounted and the content types.
/// </summary>
/// <remarks>
/// Nothing of it is static: make one per engine instance. <see cref="CreateDefault"/> gives one with the drivers of
/// this module (<c>dir</c>, <c>asm</c>, <c>mem</c>, <c>overlay</c>) and the common content types; mounting the
/// domains is up to the runtime.
/// </remarks>
public sealed class VirtualFileSystem
    : Quant<MetaQuant>, IFileSystem
{
    private readonly ConcurrentDictionary<DomainScheme, IFileDriver> _drivers = new();
    private readonly ConcurrentDictionary<string, IDomain> _domains = new(StringComparer.Ordinal);
    private readonly Lock _mounting = new();
    private volatile bool _disposed;

    /// <summary>
    /// Makes a file system with no drivers and no domains.
    /// </summary>
    /// <param name="contentTypes">The content types; an empty registry if <c>null</c>.</param>
    public VirtualFileSystem(IContentTypeRegistry? contentTypes = null)
        : base(new MetaQuant())
    {
        ContentTypes = contentTypes ?? new ContentTypeRegistry();
    }

    /// <summary>
    /// Makes a file system with the drivers of this module and the common content types.
    /// </summary>
    /// <returns>The file system, with no domain mounted.</returns>
    public static VirtualFileSystem CreateDefault()
    {
        var fileSystem = new VirtualFileSystem(ContentTypeRegistry.CreateDefault());
        fileSystem.RegisterDriver(new DirectoryDriver());
        fileSystem.RegisterDriver(new AssemblyDriver());
        fileSystem.RegisterDriver(new MemoryDriver());
        fileSystem.RegisterDriver(new OverlayDriver());
        return fileSystem;
    }

    /// <inheritdoc/>
    public IContentTypeRegistry ContentTypes { get; }

    /// <inheritdoc/>
    public IReadOnlyCollection<IDomain> Domains => _domains.Values.ToArray();

    /// <inheritdoc/>
    public event Action<IDomain>? Mounted;

    /// <inheritdoc/>
    public event Action<IDomain>? Unmounting;

    /// <inheritdoc/>
    public void RegisterDriver(IFileDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(driver.Scheme.Name)) throw new ArgumentException("The driver has no scheme", nameof(driver));

        if (!_drivers.TryAdd(driver.Scheme, driver))
            throw new InvalidOperationException($"A driver of the scheme '{driver.Scheme}' is registered already");
    }

    /// <inheritdoc/>
    public bool TryGetDriver(DomainScheme scheme, [NotNullWhen(true)] out IFileDriver? driver) =>
        _drivers.TryGetValue(scheme, out driver);

    /// <inheritdoc/>
    public IDomain Mount(DomainMount mount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var name = FileSystemNames.NormalizeName(mount.Name, nameof(mount));
        if (!_drivers.TryGetValue(mount.Scheme, out var driver))
            throw new InvalidOperationException($"No driver of the scheme '{mount.Scheme}' is registered, for the domain '{name}'");

        IDomain domain;
        lock (_mounting)
        {
            if (_domains.ContainsKey(name)) throw new InvalidOperationException($"A domain '{name}' is mounted already");

            domain = driver.Mount(mount with { Name = name }, this);
            if (domain.Name != name)
            {
                domain.Dispose();
                throw new InvalidOperationException($"The driver of '{mount.Scheme}' mounted '{domain.Name}' instead of '{name}'");
            }

            _domains[name] = domain;
        }

        Mounted?.Invoke(domain);
        return domain;
    }

    /// <inheritdoc/>
    public bool Unmount(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!FileSystemNames.TryNormalizeName(name, out var normalized)) return false;

        IDomain? domain;
        lock (_mounting)
        {
            if (!_domains.TryRemove(normalized, out domain)) return false;
        }

        // Out of the file system, still working: whoever holds data of it lets go
        try { Unmounting?.Invoke(domain); }
        finally { domain.Dispose(); }
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetDomain(string name, [NotNullWhen(true)] out IDomain? domain)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_domains.TryGetValue(name, out domain)) return true;
        return FileSystemNames.TryNormalizeName(name, out var normalized) && _domains.TryGetValue(normalized, out domain);
    }

    /// <inheritdoc/>
    public IDomain GetDomain(string name) =>
        TryGetDomain(name, out var domain) ? domain : throw new DomainNotFoundException(name);

    /// <summary>
    /// Unmounts every domain.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var name in _domains.Keys.ToArray()) Unmount(name);
    }
}
