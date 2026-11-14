using PCPulse.Contracts;
using PCPulse.Core.Elevation;
using PCPulse.Core.Modules;
using Xunit;

namespace PCPulse.Tests;

public sealed class ElevationSecurityTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pcpulse-elev-tests-" + Guid.NewGuid().ToString("N"));

    public ElevationSecurityTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task RunElevatedHelperAsync_WhenHelperNotDeclaredInManifest_ThrowsInvalidOperationException()
    {
        var manifest = ModuleManifest.Parse("""
        {
          "id": "nohelper",
          "name": "NoHelper",
          "description": "Test",
          "version": "1.0.0",
          "contractVersion": "1.0",
          "platforms": ["windows", "linux"],
          "entryAssembly": "NoHelper.dll",
          "entryType": "NoHelper.Mod"
        }
        """);

        var service = new ProcessElevationService(_tempDir, manifest);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunElevatedHelperAsync(["--test"]));
    }

    [Fact]
    public async Task RunElevatedHelperAsync_WhenDeclaredHelperMissingOnDisk_ThrowsFileNotFoundException()
    {
        var manifest = ModuleManifest.Parse("""
        {
          "id": "withhelper",
          "name": "WithHelper",
          "description": "Test",
          "version": "1.0.0",
          "contractVersion": "1.0",
          "platforms": ["windows", "linux"],
          "entryAssembly": "WithHelper.dll",
          "entryType": "WithHelper.Mod",
          "helperExecutable": {
            "windows": "WithHelper.Helper.exe",
            "linux": "WithHelper.Helper"
          }
        }
        """);

        var service = new ProcessElevationService(_tempDir, manifest);
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.RunElevatedHelperAsync(["--test"]));
    }

    [Fact]
    public void BuildStartInfo_UsesConfiguredHelperAndArgumentListWithoutShellExecuteOnLinux()
    {
        var helperName = OperatingSystem.IsWindows() ? "Test.Helper.exe" : "Test.Helper";
        var helperPath = Path.Combine(_tempDir, helperName);
        File.WriteAllText(helperPath, string.Empty);

        var manifest = ModuleManifest.Parse($$"""
        {
          "id": "testmod",
          "name": "TestMod",
          "description": "Test",
          "version": "1.0.0",
          "contractVersion": "1.0",
          "platforms": ["windows", "linux"],
          "entryAssembly": "Test.dll",
          "entryType": "Test.Mod",
          "helperExecutable": {
            "windows": "Test.Helper.exe",
            "linux": "Test.Helper"
          }
        }
        """);

        var service = new ProcessElevationService(_tempDir, manifest);
        var psi = service.BuildStartInfo(["--clean", "--out", "C:\\temp\\a b.json"], PulseContractVersion.GetCurrentPlatform());

        if (OperatingSystem.IsWindows())
        {
            Assert.True(psi.UseShellExecute);
            Assert.Equal(service.IsElevated ? string.Empty : "runas", psi.Verb);
            Assert.Equal(Path.GetFullPath(helperPath), psi.FileName);
            Assert.Contains("--clean", psi.Arguments);
        }
        else
        {
            Assert.False(psi.UseShellExecute);
            if (service.IsElevated)
            {
                Assert.Equal(Path.GetFullPath(helperPath), psi.FileName);
            }
            else
            {
                Assert.Equal("pkexec", psi.FileName);
                Assert.Equal(Path.GetFullPath(helperPath), psi.ArgumentList[0]);
            }
        }
    }
}
