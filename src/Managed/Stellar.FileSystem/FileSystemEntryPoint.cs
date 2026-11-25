using Stellar.Kernel.Configuration;
using Stellar.Kernel.EntryPoint;
using Stellar.Kernel.FileSystem;
using Stellar.Kernel.Services;

namespace Stellar.FileSystem;

/// <summary>
/// Publishes the file system of the engine:
/// app:// is the folder of the application, read-only,
/// user:// the folder of the user.
/// </summary>
[StellarEntryPoint]
public sealed class FileSystemEntryPoint(RuntimeConfiguration configuration)
    : StellarEntryPoint(FileSystemModule.Id, configuration)
{
    private IServiceCatalog? _services;
    private VirtualFileSystem? _fileSystem;

    /// <inheritdoc />
    public override int Run<TContext>(ref readonly TContext context)
    {
        if (context.Services is not { } services) return 1;

        _services = services;
        _fileSystem = VirtualFileSystem.CreateDefault();

        _fileSystem.Mount(new DomainMount(
            "app", DomainScheme.Directory, AppContext.BaseDirectory,
            new Dictionary<string, string> { ["readonly"] = "true" }
        ));

        if (!string.IsNullOrWhiteSpace(RuntimeConfiguration.ProjectName))
        {
            string company = RuntimeConfiguration.CompanyName;
            if (string.IsNullOrWhiteSpace(company) || company == "Unknown") company = "Starlet";
            _fileSystem.Mount(new DomainMount(
                "user", DomainScheme.Directory, Path.Combine(Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData),
                    company, RuntimeConfiguration.ProjectName)
            ));
        }

        services.Set<IFileSystem>(_fileSystem);

        return 0;
    }

    /// <inheritdoc />
    public override void RequestStop<TContext>(ref readonly TContext context)
    {
        if (_services is not { } services) return;
        _services = null;

        if (ReferenceEquals(services.Find<IFileSystem>(), _fileSystem)) services.Remove<IFileSystem>();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _fileSystem?.Dispose();
    }
}
