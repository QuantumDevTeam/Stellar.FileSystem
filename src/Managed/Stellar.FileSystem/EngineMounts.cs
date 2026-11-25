using Stellar.Kernel.Configuration;
using Stellar.Kernel.FileSystem;

namespace Stellar.FileSystem;

/// <summary>
/// Tells where the domains of <see cref="Folders"/> lie for an application.
/// </summary>
/// <remarks>
/// The settings <c>AssetsFolder</c> and <c>ResourcesFolder</c> (the <c>Runtime</c> section of the
/// <c>.stellar.project</c>, or <c>--stellar:AssetsFolder=...</c>) move the assets and the resources: relative to the
/// application, or a full path.
/// </remarks>
public static class EngineMounts
{
    /// <summary>The setting of the folder of the assets, <c>Assets</c> by default.</summary>
    public const string AssetsSetting = "AssetsFolder";

    /// <summary>The setting of the folder of the resources, <c>Resources</c> by default.</summary>
    public const string ResourcesSetting = "ResourcesFolder";

    /// <summary>The folder of applications that name no company.</summary>
    public const string DefaultCompany = "Starlet";

    private static DomainMount ReadOnly(FilePath root, string directory)
    {
        return new DomainMount(root.Domain, DomainScheme.Directory, directory,
            new Dictionary<string, string> { ["readonly"] = "true" });
    }

    // A folder nobody asked for may be missing; one asked for by name must be there (Mount throws)
    private static void AddContent(
        List<DomainMount> mounts, FilePath root, RuntimeConfiguration configuration,
        string setting, string byDefault
    )
    {
        bool asked = configuration.Settings.TryGetValue(setting, out string? folder)
                     && !string.IsNullOrWhiteSpace(folder);
        string full = Path.GetFullPath(asked ? folder! : byDefault, SystemFolders.App);

        if (asked || Directory.Exists(full)) mounts.Add(ReadOnly(root, full));
    }

    // One folder, never a path: "..", "a/b" and "C:" must not lead anywhere else
    private static string FolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        char[] characters = name.ToCharArray();
        for (int i = 0; i < characters.Length; i++)
            if (characters[i] < ' ' || "\\/:*?\"<>|".Contains(characters[i]))
                characters[i] = '_';

        return new string(characters).Trim(' ', '.');
    }

    private static DomainMount Lazy(FilePath root, string directory)
    {
        return new DomainMount(root.Domain, DomainScheme.Directory, directory,
            new Dictionary<string, string> { ["lazy"] = "true" });
    }

    /// <summary>
    /// Gets the domains to mount for an application.
    /// </summary>
    /// <param name="configuration">The configuration of the application.</param>
    /// <returns>The domains, in the order they are mounted.</returns>
    public static IReadOnlyList<DomainMount> Of(RuntimeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        List<DomainMount> mounts = [ReadOnly(Folders.App, SystemFolders.App)];
        AddContent(mounts, Folders.Assets, configuration, AssetsSetting, "Assets");
        AddContent(mounts, Folders.Resources, configuration, ResourcesSetting, "Resources");

        // Without a name all applications would share one folder, or get the whole folder of the user
        string project = FolderName(configuration.ProjectName);
        if (project.Length == 0) return mounts;

        string company = FolderName(configuration.CompanyName);
        if (company.Length == 0 || company == "Unknown") company = DefaultCompany;
        string own = Path.Combine(company, project);

        mounts.Add(Lazy(Folders.AppData.Roaming, Path.Combine(SystemFolders.AppData.Roaming, own)));
        mounts.Add(Lazy(Folders.AppData.Local, Path.Combine(SystemFolders.AppData.Local, own)));
        mounts.Add(Lazy(Folders.AppData.LocalLow, Path.Combine(SystemFolders.AppData.LocalLow, own)));
        mounts.Add(Lazy(Folders.Temp, Path.Combine(SystemFolders.Temp, own)));
        return mounts;
    }
}
