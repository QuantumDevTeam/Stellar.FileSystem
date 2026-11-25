using Stellar.Kernel.Configuration;
using Stellar.Kernel.FileSystem;

namespace Stellar.FileSystem.Tests;

public sealed class FoldersTests
    : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "Stellar.FileSystem.Tests", Guid.NewGuid().ToString("N"));

    private readonly VirtualFileSystem _files = VirtualFileSystem.CreateDefault();

    public void Dispose()
    {
        _files.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static string SourceOf(RuntimeConfiguration configuration, FilePath root)
    {
        return EngineMounts.Of(configuration).Single(mount => mount.Name == root.Domain).Source;
    }

    private void MountLazy(string directory)
    {
        _files.Mount(new DomainMount("disk", DomainScheme.Directory, directory,
            new Dictionary<string, string> { ["lazy"] = "true" }));
    }

    [Fact]
    public void SystemFolders_AreFullPaths()
    {
        string[] folders =
        [
            SystemFolders.App, SystemFolders.Temp, SystemFolders.Home, SystemFolders.Desktop, SystemFolders.Documents,
            SystemFolders.Downloads, SystemFolders.Pictures, SystemFolders.Music, SystemFolders.Videos,
            SystemFolders.SavedGames, SystemFolders.Shared, SystemFolders.AppData.Roaming, SystemFolders.AppData.Local,
            SystemFolders.AppData.LocalLow
        ];

        folders.Should().OnlyContain(folder => Path.IsPathFullyQualified(folder));
        if (OperatingSystem.IsWindows()) SystemFolders.AppData.LocalLow.Should().EndWith("LocalLow");
    }

    [Fact]
    public void Folders_ArePathsOfDomains()
    {
        Folders.AppData.Roaming.Combine("saves/a.json").Should().Be(FilePath.Parse("user://saves/a.json"));
        Folders.Assets.Combine("hero.png").Should().Be(FilePath.Parse("assets://hero.png"));
    }

    [Theory]
    [InlineData("..", "..")]
    [InlineData("a/../..", "b\\..")]
    [InlineData("C:", "/etc")]
    public void Names_NeverLeadOutOfTheFolderOfTheUser(string company, string project)
    {
        var configuration = new RuntimeConfiguration { CompanyName = company, ProjectName = project };

        foreach (DomainMount mount in EngineMounts.Of(configuration).Where(mount => mount.Name == "user"))
            Path.GetFullPath(mount.Source)
                .Should().StartWith(SystemFolders.AppData.Roaming + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void Assets_FollowTheSetting()
    {
        var configuration = new RuntimeConfiguration();
        EngineMounts.Of(configuration).Select(mount => mount.Name).Should().Equal("app");

        configuration.Settings[EngineMounts.AssetsSetting] = "Content";
        SourceOf(configuration, Folders.Assets).Should().Be(Path.Combine(SystemFolders.App, "Content"));

        configuration.Settings[EngineMounts.AssetsSetting] = _root;
        SourceOf(configuration, Folders.Assets).Should().Be(_root);
    }

    [Fact]
    public void ALazyDirectory_IsCreatedByTheFirstWrite()
    {
        MountLazy(_root);
        FilePath file = FilePath.Parse("disk://a/b.txt");

        _files.Exists(file).Should().BeFalse();
        _files.Enumerate(FilePath.Root("disk")).Should().BeEmpty();
        Directory.Exists(_root).Should().BeFalse();

        _files.WriteAllText(file, "text", true);
        _files.ReadAllText(file).Should().Be("text");
    }

    [Fact]
    public async Task Text_IsWrittenAndReadAsync()
    {
        MountLazy(_root);
        FilePath file = FilePath.Parse("disk://a.txt");

        await _files.WriteAllTextAsync(file, "привет", true);
        (await _files.ReadAllTextAsync(file)).Should().Be("привет");
    }

    [Theory]
    [InlineData("dir")]
    [InlineData("mem")]
    public async Task ACancelledAtomicWrite_LeavesTheFile(string scheme)
    {
        _files.Mount(new DomainMount("disk", new DomainScheme(scheme), _root));
        FilePath file = FilePath.Parse("disk://a.txt");
        await _files.WriteAllTextAsync(file, "old");

        using var cancellation = new CancellationTokenSource();
        Func<Task> write = async () =>
        {
            // ReSharper disable once MethodSupportsCancellation
            await using Stream stream = await _files.OpenAsync(file, FileOpenOptions.Replace);
            // ReSharper disable once MethodSupportsCancellation
            await stream.WriteAsync(new byte[10]);
            // ReSharper disable once AccessToDisposedClosure
            await cancellation.CancelAsync();
            // ReSharper disable once AccessToDisposedClosure
            await stream.WriteAsync(new byte[10], cancellation.Token);
        };

        await write.Should().ThrowAsync<OperationCanceledException>();
        // ReSharper disable once MethodSupportsCancellation
        (await _files.ReadAllTextAsync(file)).Should().Be("old");
        if (scheme == "dir") Directory.GetFiles(_root).Should().HaveCount(1);
    }

    [Fact]
    public async Task AnOverlay_CopiesAFileUpOnce_FromManyThreads()
    {
        _files.Mount(new DomainMount("base", DomainScheme.Memory, ""));
        _files.Mount(new DomainMount("mods", DomainScheme.Memory, ""));
        await _files.WriteAllTextAsync(FilePath.Parse("base://a.txt"), "base");
        int copies = 0;
        using IDisposable watch = _files.Watch(FilePath.Root("mods"), true, _ => Interlocked.Increment(ref copies));
        _files.Mount(new DomainMount("all", DomainScheme.Overlay, "mods,base",
            new Dictionary<string, string> { ["write"] = "mods" }));


        Stream[] streams = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
            await _files.OpenAsync(FilePath.Parse("all://a.txt"),
                new FileOpenOptions(FileMode.Open, FileAccess.ReadWrite)))));


        // Opened, not closed yet: the only change is the copy
        copies.Should().Be(1);
        foreach (Stream stream in streams) await stream.DisposeAsync();
    }

    [Fact]
    public void MountAndDispose_FromManyThreads_LeaveNoDomain()
    {
        for (int round = 0; round < 50; round++)
        {
            var files = VirtualFileSystem.CreateDefault();
            Parallel.For(0, 16, i =>
            {
                if (i == 8)
                {
                    files.Dispose();
                    return;
                }

                try
                {
                    files.Mount(new DomainMount($"d{i}", DomainScheme.Memory, ""));
                }
                catch (ObjectDisposedException)
                {
                    // Too late to mount
                }
            });

            files.Domains.Keys.Should().BeEmpty();
        }
    }
}
