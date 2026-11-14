using PCPulse.App.Services;
using PCPulse.Core.Platform;
using Xunit;

namespace PCPulse.Tests;

public sealed class AppSmokeTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pcpulse-app-tests-" + Guid.NewGuid().ToString("N"));

    public AppSmokeTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task HostDialogService_SerializesDialogsAndReturnsUserChoice()
    {
        var presentedTitles = new List<string>();
        var service = new HostDialogService(req =>
        {
            presentedTitles.Add(req.Title);
            return Task.FromResult(req.Title == "Первый");
        });

        var t1 = service.ConfirmAsync("Первый", "Сообщение 1");
        var t2 = service.ConfirmAsync("Второй", "Сообщение 2");
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal([true, false], results);
        Assert.Equal(["Первый", "Второй"], presentedTitles);
    }

    [Fact]
    public void HostSettings_DefaultsToLivingInTray()
    {
        var defaults = new HostSettings();
        Assert.True(defaults.ShowTrayIcon);
        Assert.True(defaults.MinimizeToTrayOnClose);
    }

    [Fact]
    public async Task HostSettingsStore_PersistsAndReloadsSettings()
    {
        var paths = new PulsePaths(_tempDir);
        var store = new HostSettingsStore(paths);

        var saved = new HostSettings(ShowTrayIcon: false, MinimizeToTrayOnClose: true);
        await store.SaveAsync(saved);

        var loaded = await store.LoadAsync();
        Assert.False(loaded.ShowTrayIcon);
        Assert.True(loaded.MinimizeToTrayOnClose);
    }

    [Fact]
    public void InstallerScript_DefinesEveryReferencedPreprocessorSymbol()
    {
        var root = FindRepoRoot();
        var issPath = Path.Combine(root, "installer", "PCPulse.iss");
        var iss = File.ReadAllText(issPath);

        Assert.DoesNotContain("MyAppName", iss, StringComparison.Ordinal);
        Assert.Contains("#define AppName", iss, StringComparison.Ordinal);
        Assert.Contains("{#StringChange(AppName", iss, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_DoesNotSynchronouslyBlockUiThreadOnModuleLoading()
    {
        var root = FindRepoRoot();
        var programCs = File.ReadAllText(Path.Combine(root, "src", "PCPulse.App", "Program.cs"));

        Assert.DoesNotContain("InitializeAndLoadAllAsync().GetAwaiter().GetResult()", programCs, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PCPulse.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
