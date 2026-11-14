using PCPulse.Contracts;
using PCPulse.Core.Modules;
using Xunit;

namespace PCPulse.Tests;

public sealed class ManifestValidationTests
{
    [Fact]
    public void Parse_ValidManifest_Succeeds()
    {
        const string json = """
        {
          "id": "mempulse",
          "name": "MemPulse",
          "description": "Мониторинг и очистка памяти",
          "version": "0.3.0",
          "contractVersion": "1.0",
          "platforms": ["windows", "linux"],
          "entryAssembly": "MemPulse.PulseModule.dll",
          "entryType": "MemPulse.PulseModule.MemPulseModule",
          "helperExecutable": {
            "windows": "MemPulse.Helper.exe",
            "linux": "MemPulse.Helper"
          }
        }
        """;

        var manifest = ModuleManifest.Parse(json);

        Assert.Equal("mempulse", manifest.Id);
        Assert.Equal("MemPulse", manifest.Name);
        Assert.Equal(new Version(0, 3, 0), manifest.Version);
        Assert.Equal("1.0", manifest.ContractVersion);
        Assert.Equal(PulsePlatform.All, manifest.SupportedPlatforms);
        Assert.Equal("MemPulse.PulseModule.dll", manifest.EntryAssembly);
        Assert.Equal("MemPulse.PulseModule.MemPulseModule", manifest.EntryType);
        Assert.Equal("MemPulse.Helper.exe", manifest.GetHelperForPlatform(PulsePlatform.Windows));
        Assert.Equal("MemPulse.Helper", manifest.GetHelperForPlatform(PulsePlatform.Linux));
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData(@"..\..\cmd.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData("/usr/bin/sh")]
    public void Parse_InvalidEntryAssemblyPath_Throws(string badAssembly)
    {
        var json = $$"""
        {
          "id": "mempulse",
          "name": "MemPulse",
          "description": "Test",
          "version": "1.0.0",
          "contractVersion": "1.0",
          "platforms": ["windows", "linux"],
          "entryAssembly": "{{badAssembly.Replace("\\", "\\\\")}}",
          "entryType": "Ns.Type"
        }
        """;

        Assert.Throws<FormatException>(() => ModuleManifest.Parse(json));
    }

    [Theory]
    [InlineData("MemPulse")]
    [InlineData("bad/id")]
    [InlineData("-bad")]
    [InlineData("a")]
    public void Parse_InvalidModuleId_Throws(string badId)
    {
        var json = $$"""
        {
          "id": "{{badId}}",
          "name": "MemPulse",
          "description": "Test",
          "version": "1.0.0",
          "contractVersion": "1.0",
          "platforms": ["windows"],
          "entryAssembly": "Mod.dll",
          "entryType": "Ns.Type"
        }
        """;

        Assert.Throws<FormatException>(() => ModuleManifest.Parse(json));
    }

    [Fact]
    public void Parse_TraversalInHelperExecutable_Throws()
    {
        const string json = """
        {
          "id": "diskpulse",
          "name": "DiskPulse",
          "description": "Test",
          "version": "1.0.0",
          "contractVersion": "1.0",
          "platforms": ["windows"],
          "entryAssembly": "DiskPulse.Module.dll",
          "entryType": "Ns.Type",
          "helperExecutable": {
            "windows": "..\\..\\evil.exe"
          }
        }
        """;

        Assert.Throws<FormatException>(() => ModuleManifest.Parse(json));
    }
}
