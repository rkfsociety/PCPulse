using System.IO.Compression;
using PCPulse.Contracts;
using PCPulse.Core.Logging;
using PCPulse.Core.Modules;
using PCPulse.Core.Platform;
using Xunit;

namespace PCPulse.Tests;

public sealed class ModuleLoadingTests : IDisposable
{
    private readonly string _rootDir = Path.Combine(Path.GetTempPath(), "pcpulse-mod-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PulsePaths _paths;

    public ModuleLoadingTests()
    {
        _paths = new PulsePaths(_rootDir);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (Directory.Exists(_rootDir))
        {
            try
            {
                Directory.Delete(_rootDir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task InstallAndLoadSampleModule_LoadsInIsolatedContext_CachesView_AndDisposesOnDisable()
    {
        var zipPath = CreateSampleModuleZip();
        var logger = new PulseFileLogger(_paths.LogsDirectory, "host");
        await using var manager = new ModuleManager(
            _paths,
            new Version(0, 1, 0),
            new StubDialogService(),
            logger);

        var installed = await manager.InstallFromZipAsync(zipPath);
        Assert.Equal("sample-module", installed.Id);
        Assert.True(installed.Enabled);

        await manager.InitializeAndLoadAllAsync();

        var handle = manager.GetLoadedModule("sample-module");
        Assert.NotNull(handle);
        Assert.Equal(ModuleRuntimeState.Loaded, handle!.State);
        Assert.NotNull(handle.Module);
        Assert.NotEqual(typeof(IPulseModule).Assembly, handle.Module!.GetType().Assembly);

        var summary = await handle.Module.GetSummaryAsync();
        Assert.NotNull(summary);
        Assert.Contains("Эталонный модуль", summary!.Headline);

        var view1 = manager.GetOrCreateView("sample-module");
        var view2 = manager.GetOrCreateView("sample-module");
        Assert.NotNull(view1);
        Assert.Same(view1, view2);

        await manager.SetModuleEnabledAsync("sample-module", enabled: false);
        var disabledHandle = manager.GetLoadedModule("sample-module");
        Assert.NotNull(disabledHandle);
        Assert.Equal(ModuleRuntimeState.Disabled, disabledHandle!.State);
    }

    [Fact]
    public async Task InitializeAndLoadAllAsync_IsolatesBrokenModuleFailure_AndCleansPendingDeletes()
    {
        var pendingDir = Path.Combine(_paths.ModulesDirectory, "old-mod", "1.0.0");
        Directory.CreateDirectory(pendingDir);
        await File.WriteAllTextAsync(Path.Combine(pendingDir, "stale.dll"), "stale");
        var store = new InstalledModuleStore(_paths);
        await store.AddPendingDeleteAsync(pendingDir);

        var brokenZip = CreateBrokenModuleZip();
        var goodZip = CreateSampleModuleZip();
        var logger = new PulseFileLogger(_paths.LogsDirectory, "host");
        await using var manager = new ModuleManager(_paths, new Version(0, 1, 0), new StubDialogService(), logger);

        await manager.InstallFromZipAsync(brokenZip);
        await manager.InstallFromZipAsync(goodZip);

        await manager.InitializeAndLoadAllAsync();

        Assert.False(Directory.Exists(pendingDir));
        var broken = manager.GetLoadedModule("broken-mod");
        var good = manager.GetLoadedModule("sample-module");
        Assert.NotNull(broken);
        Assert.Equal(ModuleRuntimeState.Faulted, broken!.State);
        Assert.False(string.IsNullOrWhiteSpace(broken.ErrorMessage));
        Assert.NotNull(good);
        Assert.Equal(ModuleRuntimeState.Loaded, good!.State);
    }

    [Fact]
    public async Task UninstallModuleAsync_WhenDirectoryLocked_PersistsPendingDeleteBeforeReturning()
    {
        var zipPath = CreateSampleModuleZip();
        var logger = new PulseFileLogger(_paths.LogsDirectory, "host");
        await using var manager = new ModuleManager(_paths, new Version(0, 1, 0), new StubDialogService(), logger);
        var record = await manager.InstallFromZipAsync(zipPath);

        var lockFilePath = Path.Combine(record.InstallDirectory, "lock.tmp");
        await File.WriteAllTextAsync(lockFilePath, "locked");
        using var lockStream = new FileStream(lockFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(record.InstallDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        try
        {
            await manager.UninstallModuleAsync(record.Id);

            if (Directory.Exists(record.InstallDirectory))
            {
                Assert.True(File.Exists(_paths.PendingDeletesFile));
                var pendingJson = await File.ReadAllTextAsync(_paths.PendingDeletesFile);
                Assert.Contains(record.Id, pendingJson);
            }
        }
        finally
        {
            if (!OperatingSystem.IsWindows() && Directory.Exists(record.InstallDirectory))
            {
                File.SetUnixFileMode(
                    record.InstallDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    private string CreateSampleModuleZip()
    {
        var sampleBinDir = FindSampleModuleOutputDir();
        var zipPath = Path.Combine(_rootDir, "sample-module-1.0.0-any.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.GetFiles(sampleBinDir))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("PCPulse.SampleModule.dll", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("module.json", StringComparison.OrdinalIgnoreCase))
            {
                archive.CreateEntryFromFile(file, name);
            }
        }
        return zipPath;
    }

    private string CreateBrokenModuleZip()
    {
        var zipPath = Path.Combine(_rootDir, "broken-mod-1.0.0-any.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("module.json");
        using (var writer = new StreamWriter(entry.Open()))
        {
            writer.Write("""
            {
              "id": "broken-mod",
              "name": "Broken",
              "description": "Missing DLL",
              "version": "1.0.0",
              "contractVersion": "1.0",
              "platforms": ["windows", "linux"],
              "entryAssembly": "DoesNotExist.dll",
              "entryType": "Broken.Missing"
            }
            """);
        }
        return zipPath;
    }

    private static string FindSampleModuleOutputDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PCPulse.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var releaseDir = Path.Combine(dir!.FullName, "src", "PCPulse.SampleModule", "bin", "Release", "net10.0");
        if (File.Exists(Path.Combine(releaseDir, "PCPulse.SampleModule.dll")))
            return releaseDir;

        var debugDir = Path.Combine(dir.FullName, "src", "PCPulse.SampleModule", "bin", "Debug", "net10.0");
        Assert.True(File.Exists(Path.Combine(debugDir, "PCPulse.SampleModule.dll")),
            $"PCPulse.SampleModule.dll not found in {releaseDir} or {debugDir}");
        return debugDir;
    }

    private sealed class StubDialogService : IPulseDialogService
    {
        public Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string confirmText = "Продолжить",
            string cancelText = "Отмена",
            bool isDestructive = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
