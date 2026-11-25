using Stellar.Core.EntryPoints;
using Stellar.Core.Services;
using Stellar.Kernel.Configuration;
using Stellar.Kernel.EntryPoint;
using Stellar.Kernel.FileSystem;
using Stellar.Kernel.Services;

namespace Stellar.FileSystem.Tests;

public sealed class EntryPointTests
    : IDisposable
{
    private static readonly string AppData =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static readonly string[] Roots =
    [
        SystemFolders.AppData.Roaming, SystemFolders.AppData.Local, SystemFolders.AppData.LocalLow, SystemFolders.Temp
    ];

    // Names nobody else has: what the tests leave in the real folder of the user is deleted whole
    private readonly string _company = "StellarTests-" + Guid.NewGuid().ToString("N");
    private readonly string _project = "Project-" + Guid.NewGuid().ToString("N");
    private readonly ServiceCatalog _services = new();

    public void Dispose()
    {
        foreach (string root in Roots)
        {
            Delete(Path.Combine(root, _company));
            Delete(Path.Combine(root, _project));
            Delete(Path.Combine(root, "Starlet", _project));
            Delete(Path.Combine(root, "Unknown", _project));

            // Only if it is ours: a folder of somebody else is not empty
            string shared = Path.Combine(root, "Starlet");
            if (Directory.Exists(shared) && !Directory.EnumerateFileSystemEntries(shared).Any())
                Directory.Delete(shared);
        }
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private static FileSystemEntryPoint Make(string company, string project)
    {
        return new FileSystemEntryPoint(new RuntimeConfiguration { CompanyName = company, ProjectName = project });
    }

    private static int Run(FileSystemEntryPoint entryPoint, IServiceCatalog? services)
    {
        var context = new RunContext(null, services: services);
        return entryPoint.Run(in context);
    }

    private static void RequestStop(FileSystemEntryPoint entryPoint)
    {
        var context = new StopContext(null, StopReason.Regular);
        entryPoint.RequestStop(in context);
    }

    private static void Stop(FileSystemEntryPoint entryPoint)
    {
        RequestStop(entryPoint);
        entryPoint.Dispose();
    }

    [Fact]
    public void Run_PublishesTheFileSystem_AndStopTakesItBack()
    {
        FileSystemEntryPoint entryPoint = Make(_company, _project);
        Run(entryPoint, _services).Should().Be(0);

        var files = _services.Find<IFileSystem>()!;
        files.Should().BeOfType<VirtualFileSystem>();
        files.Domains.Keys.Should().BeEquivalentTo("app", "user", "local", "locallow", "temp");

        Stop(entryPoint);
        _services.Find<IFileSystem>().Should().BeNull();
        files.Domains.Keys.Should().BeEmpty();
    }

    [Fact]
    public void User_IsTheFolderOfTheProject_OfTheCompany()
    {
        FileSystemEntryPoint entryPoint = Make(_company, _project);
        Run(entryPoint, _services);

        _services.Find<IFileSystem>()!.WriteAllText(FilePath.Parse("user://a.txt"), "text");
        File.ReadAllText(Path.Combine(AppData, _company, _project, "a.txt")).Should().Be("text");

        Stop(entryPoint);
        File.Exists(Path.Combine(AppData, _company, _project, "a.txt")).Should().BeTrue("the files outlive the engine");
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void User_OfNoCompany_IsInStarlet(string? company)
    {
        FileSystemEntryPoint entryPoint = Make(company!, _project);
        Run(entryPoint, _services);
        _services.Find<IFileSystem>()!.WriteAllText(FilePath.Parse("user://a.txt"), "text");
        Stop(entryPoint);

        Directory.Exists(Path.Combine(AppData, "Starlet", _project)).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void User_OfNoProjectName_IsNotMounted(string? project)
    {
        FileSystemEntryPoint entryPoint = Make(_company, project!);
        Run(entryPoint, _services);
        Stop(entryPoint);

        Directory.Exists(Path.Combine(AppData, _company)).Should().BeFalse();
    }

    [Fact]
    public void User_OfNoProject_IsNotMounted()
    {
        FileSystemEntryPoint entryPoint = Make(_company, "");
        Run(entryPoint, _services).Should().Be(0);

        _services.Find<IFileSystem>()!.Domains.Keys.Should().BeEquivalentTo("app");

        Stop(entryPoint);
        Directory.Exists(Path.Combine(AppData, _company)).Should().BeFalse();
    }

    [Fact]
    public void App_CannotBeWritten()
    {
        FileSystemEntryPoint entryPoint = Make(_company, _project);
        Run(entryPoint, _services);

        var files = _services.Find<IFileSystem>()!;
        files.Invoking(it => it.WriteAllText(FilePath.Parse("app://a.txt"), "text"))
            .Should().Throw<NotSupportedException>();
        File.Exists(Path.Combine(AppContext.BaseDirectory, "a.txt")).Should().BeFalse();

        Stop(entryPoint);
    }

    [Fact]
    public void WithoutServices_DoesNotStart()
    {
        FileSystemEntryPoint entryPoint = Make(_company, _project);
        Run(entryPoint, null).Should().NotBe(0);

        entryPoint.Invoking(Stop).Should().NotThrow();
        Directory.Exists(Path.Combine(AppData, _company)).Should().BeFalse();
    }

    [Fact]
    public void Stop_LeavesTheFileSystemOfAnotherModule()
    {
        FileSystemEntryPoint entryPoint = Make(_company, _project);
        Run(entryPoint, _services);

        using var other = new VirtualFileSystem();
        _services.Set<IFileSystem>(other);

        Stop(entryPoint);
        _services.Find<IFileSystem>().Should().BeSameAs(other);
    }

    [Fact]
    public void TwoEngines_HaveTheirOwnFileSystems()
    {
        var otherServices = new ServiceCatalog();
        FileSystemEntryPoint first = Make(_company, _project);
        FileSystemEntryPoint second = Make(_company, _project);
        Run(first, _services);
        Run(second, otherServices);

        Stop(first);
        otherServices.Find<IFileSystem>()!.Domains.Keys.Should()
            .BeEquivalentTo("app", "user", "local", "locallow", "temp");

        Stop(second);
    }

    [Fact]
    public void Dispose_MayBeRepeated_AndCalledWithoutRun()
    {
        FileSystemEntryPoint entryPoint = Make(_company, _project);
        entryPoint.Invoking(it => it.Dispose()).Should().NotThrow();

        Run(entryPoint, _services);
        Stop(entryPoint);
        entryPoint.Invoking(Stop).Should().NotThrow();
    }
}
