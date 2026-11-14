# PCPulse Base Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the foundational cross-platform (`Windows` + `Linux`) modular host `PCPulse`, the independently versioned `PCPulse.Contracts` (`1.0.0`) NuGet package, an in-repo reference `PCPulse.SampleModule`, automated tests, installers/update scripts, and CI/Release GitHub Actions workflows.

**Architecture:** `PCPulse` is a single-window `.NET 10` + `Avalonia 12.1.6` application (`Dark` Fluent theme, Russian UI) running without elevated privileges. Modules live in per-user storage, are pre-checked via standalone `.manifest.json` assets and `SHA-256` hashes before/after downloading from GitHub Releases, and load into isolated `AssemblyLoadContext` instances that share `PCPulse.Contracts` and `Avalonia.*` from the host default context while delegating dialogs, settings, logging, and declared elevated helpers back through `IPulseHostContext`.

**Tech Stack:** .NET 10 (`net10.0`), C# 14 / nullable + implicit usings, Avalonia `12.1.6` (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`), xUnit, PowerShell / Bash update & install scripts, Inno Setup 6, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-14-pcpulse-base-design.md`

## Global Constraints

- Target framework across all projects: `net10.0`.
- UI framework: `Avalonia` `12.1.6` (`ThemeVariant.Dark`, Russian UI).
- `PCPulse.Contracts` uses independent `<Version>1.0.0</Version>` in `src/PCPulse.Contracts/PCPulse.Contracts.csproj`; app projects use `<Version>0.1.0</Version>` in `src/Directory.Build.props`.
- Single-window rule: zero secondary `Window` instances or OS `MessageBox`; all confirmations/errors go through `DialogOverlay` / `IPulseDialogService`.
- Per-user installation and data storage without admin/`root` (`%LOCALAPPDATA%\PCPulse` & `%LOCALAPPDATA%\Programs\PCPulse` on Windows; `${XDG_DATA_HOME:-~/.local/share}/pcpulse` on Linux).
- Elevated helpers may ONLY execute the `helperExecutable` declared in `module.json` inside `ModuleInstallDirectory` (no arbitrary paths, no path traversal).
- Work directly on `main` without feature branches or PRs.

## Review Focus

1. **Path traversal / ZipSlip in module ZIP extraction and `helperExecutable` resolution** — a crafted `module.json` (`../../cmd.exe`) or ZIP entry (`../../evil.dll`) must be rejected before writing/executing anything outside the target root.
2. **Contract/Avalonia assembly identity mismatch across `AssemblyLoadContext`** — loading `PCPulse.SampleModule` in its own `AssemblyLoadContext` must still produce `IPulseModule` and `Avalonia.Controls.Control` instances assignable to the host's types.
3. **Incompatible module manifest handling (`contractVersion: "2.0"` or unsupported OS)** — catalog/installer must detect incompatibility from `.manifest.json` before downloading the `.zip` and fall back to an older compatible release if available.
4. **Corrupted or tampered `.zip` / `.sha256` verification** — mismatched hash, invalid hex, or missing `.sha256` must fail cleanly without touching existing installed versions.
5. **Module runtime exception isolation & pending delete on Windows** — a throwing module (`InitializeAsync`/`CreateView`/`GetSummaryAsync`) must not crash `PCPulse` or other modules, and locked directories marked in `pending-deletes.json` must be purged on startup before module loading.

---

### Task 1: Solution Scaffolding & `PCPulse.Contracts` (`1.0.0`)

**Files:**
- Create: `.gitignore`
- Create: `src/Directory.Build.props`
- Create: `src/PCPulse.Contracts/PCPulse.Contracts.csproj`
- Create: `src/PCPulse.Contracts/PulsePlatform.cs`
- Create: `src/PCPulse.Contracts/PulseModuleDescriptor.cs`
- Create: `src/PCPulse.Contracts/PulseStatusSummary.cs`
- Create: `src/PCPulse.Contracts/IPulseDialogService.cs`
- Create: `src/PCPulse.Contracts/IPulseElevationService.cs`
- Create: `src/PCPulse.Contracts/IPulseLogger.cs`
- Create: `src/PCPulse.Contracts/IPulseSettingsStore.cs`
- Create: `src/PCPulse.Contracts/IPulseHostContext.cs`
- Create: `src/PCPulse.Contracts/IPulseModule.cs`
- Create: `src/PCPulse.Contracts/PulseContractVersion.cs`
- Create: `PCPulse.sln`
- Create: `tests/PCPulse.Tests/PCPulse.Tests.csproj`
- Test: `tests/PCPulse.Tests/ContractCompatibilityTests.cs`

**Interfaces:**
- Produces:
  - `PulsePlatform` (`None = 0`, `Windows = 1`, `Linux = 2`, `All = Windows | Linux`)
  - `PulseModuleDescriptor(string Id, string DisplayName, string Description, Version ModuleVersion, string ContractVersion, PulsePlatform SupportedPlatforms, bool RequiresElevationForSomeActions = false)`
  - `PulseStatusSeverity` (`Normal`, `Info`, `Warning`, `Attention`), `PulseStatusSummary(string Headline, string? Details = null, PulseStatusSeverity Severity = PulseStatusSeverity.Normal)`
  - `IPulseDialogService`, `IPulseElevationService`, `IPulseLogger`, `IPulseSettingsStore`, `IPulseHostContext`, `IPulseModule`
  - `PulseContractVersion.Current == "1.0"` and `PulseContractVersion.IsCompatible(string? moduleContractVersion, out string? errorReason)`

- [ ] **Step 1: Scaffold `.gitignore`, `src/Directory.Build.props`, `PCPulse.Contracts.csproj`, `PCPulse.Tests.csproj`, and `PCPulse.sln`**
- [ ] **Step 2: Write the failing test in `tests/PCPulse.Tests/ContractCompatibilityTests.cs`**

```csharp
[Theory]
[InlineData("1.0", true)]
[InlineData("1.0.0", false)]
[InlineData("1.1", false)]
[InlineData("2.0", false)]
[InlineData("0.9", false)]
[InlineData("", false)]
[InlineData(null, false)]
public void IsCompatible_ValidatesMajorMinorRule(string? version, bool expected)
{
    var ok = PulseContractVersion.IsCompatible(version, out var reason);
    Assert.Equal(expected, ok);
    if (!expected) Assert.False(string.IsNullOrWhiteSpace(reason));
}
```
Also test `Current == "1.0"` and `CurrentPlatform()` returns `Windows` or `Linux` on supported OS.

- [ ] **Step 3: Run test to verify it fails** (`dotnet test tests/PCPulse.Tests/PCPulse.Tests.csproj`) — Expected: FAIL (`PulseContractVersion` does not exist).
- [ ] **Step 4: Implement all `PCPulse.Contracts` types and `PulseContractVersion`**
- [ ] **Step 5: Run `dotnet test` and `dotnet pack src/PCPulse.Contracts/PCPulse.Contracts.csproj -c Release`** — Expected: PASS and `PCPulse.Contracts.1.0.0.nupkg` generated.
- [ ] **Step 6: Commit** (`git add ... && git commit -m "feat: add PCPulse.Contracts 1.0.0 and test project"`)

---

### Task 2: Manifest Validation, Path Safety, SHA-256, Logger, Settings, and Elevation (`PCPulse.Core`)

**Files:**
- Create: `src/PCPulse.Core/PCPulse.Core.csproj`
- Create: `src/PCPulse.Core/Platform/PulsePaths.cs`
- Create: `src/PCPulse.Core/Security/PathGuard.cs`
- Create: `src/PCPulse.Core/Security/Sha256Verifier.cs`
- Create: `src/PCPulse.Core/Security/ZipSafeExtractor.cs`
- Create: `src/PCPulse.Core/Logging/PulseFileLogger.cs`
- Create: `src/PCPulse.Core/Settings/JsonSettingsStore.cs`
- Create: `src/PCPulse.Core/Modules/ModuleManifest.cs`
- Create: `src/PCPulse.Core/Elevation/ProcessElevationService.cs`
- Test: `tests/PCPulse.Tests/ManifestValidationTests.cs`
- Test: `tests/PCPulse.Tests/SecurityVerificationTests.cs`
- Test: `tests/PCPulse.Tests/ElevationSecurityTests.cs`

**Interfaces:**
- Consumes: `PCPulse.Contracts` (`PulsePlatform`, `IPulseLogger`, `IPulseSettingsStore`, `IPulseElevationService`, `PulseContractVersion`)
- Produces:
  - `PulsePaths(string? rootOverride = null)` (`DataRoot`, `LogsDirectory`, `ModulesDirectory`, `TempDirectory`, `InstalledModulesFile`, `PendingDeletesFile`, `HostSettingsFile`, `GetModuleDataDirectory(string moduleId)`)
  - `PathGuard.ResolveSafeChildPath(string rootDirectory, string relativePath)`
  - `Sha256Verifier.ParseExpectedHash(string sha256Content)` & `VerifyFileAsync(string filePath, string sha256Content, CancellationToken ct)`
  - `ZipSafeExtractor.ExtractToDirectoryAsync(string zipPath, string destinationDirectory, CancellationToken ct)`
  - `ModuleManifest` & `ModuleManifest.Parse(string json)`
  - `PulseFileLogger`, `JsonSettingsStore`, `ProcessElevationService`

- [ ] **Step 1: Write failing tests for `ModuleManifest.Parse` (valid manifest, bad `id`, `..` in `entryAssembly` or `helperExecutable`, unsupported platform), `PathGuard` (rejects traversal/absolute paths), `Sha256Verifier` (raw hex + `sha256sum` format + mismatch rejection), `ZipSafeExtractor` (rejects `../../evil.txt` ZipSlip), `JsonSettingsStore` (rejects traversal keys), and `ProcessElevationService` (rejects undeclared/escaped helper path)**
- [ ] **Step 2: Run `dotnet test`** — Expected: FAIL (types do not exist).
- [ ] **Step 3: Implement `PCPulse.Core` security, manifest, logging, settings, and elevation classes**
- [ ] **Step 4: Run `dotnet test`** — Expected: PASS.
- [ ] **Step 5: Commit**

---

### Task 3: `ModuleLoadContext`, `PCPulse.SampleModule`, and Fault-Isolated `ModuleManager`

**Files:**
- Create: `src/PCPulse.Core/Modules/ModuleLoadContext.cs`
- Create: `src/PCPulse.Core/Modules/InstalledModuleRecord.cs`
- Create: `src/PCPulse.Core/Modules/InstalledModuleStore.cs`
- Create: `src/PCPulse.Core/Modules/LoadedModuleHandle.cs`
- Create: `src/PCPulse.Core/Modules/ModuleManager.cs`
- Create: `src/PCPulse.SampleModule/PCPulse.SampleModule.csproj`
- Create: `src/PCPulse.SampleModule/SamplePulseModule.cs`
- Create: `src/PCPulse.SampleModule/module.json`
- Test: `tests/PCPulse.Tests/ModuleLoadingTests.cs`

**Interfaces:**
- Consumes: `PCPulse.Contracts`, `PulsePaths`, `ModuleManifest`, `PulseFileLogger`, `JsonSettingsStore`, `ProcessElevationService`, `ZipSafeExtractor`
- Produces:
  - `ModuleManager` (`InitializeAndLoadAllAsync`, `InstallFromZipAsync`, `SetModuleEnabledAsync`, `UninstallModuleAsync`, `GetLoadedModule(string id)`, `GetOrCreateView(string id)`, `DisposeAsync()`)
  - `LoadedModuleHandle` (`Manifest`, `State`, `ErrorMessage`, `Module`, `CachedView`, `LastSummary`)

- [ ] **Step 1: Write failing tests in `ModuleLoadingTests.cs`:**
  - Installing `PCPulse.SampleModule` from a generated ZIP loads `IPulseModule` in an isolated `ModuleLoadContext` while preserving host `IPulseModule` & `Avalonia.Controls.Control` type identity;
  - `GetOrCreateView()` caches the `Control` across repeated calls;
  - Switching/disabling or disposing calls `DisposeAsync()` on the module;
  - Loading a broken module records `ErrorMessage` on that handle without crashing `ModuleManager` or other modules;
  - Pending deletion directories recorded in `pending-deletes.json` are removed at `InitializeAndLoadAllAsync` startup before loading modules.
- [ ] **Step 2: Run `dotnet test`** — Expected: FAIL.
- [ ] **Step 3: Implement `ModuleLoadContext`, `InstalledModuleStore`, `ModuleManager`, and `PCPulse.SampleModule`**
- [ ] **Step 4: Run `dotnet test`** — Expected: PASS.
- [ ] **Step 5: Commit**

---

### Task 4: Ecosystem Catalog, GitHub Releases Module Installer, and Self-Updater (`PCPulse.Core`)

**Files:**
- Create: `src/PCPulse.Core/Catalog/CatalogModuleEntry.cs`
- Create: `src/PCPulse.Core/Catalog/ModuleCatalog.cs`
- Create: `src/PCPulse.Core/Catalog/catalog.json`
- Create: `src/PCPulse.Core/GitHub/GitHubReleaseClient.cs`
- Create: `src/PCPulse.Core/Updates/AppUpdateService.cs`
- Test: `tests/PCPulse.Tests/CatalogAndReleaseTests.cs`
- Test: `tests/PCPulse.Tests/AppUpdateServiceTests.cs`

**Interfaces:**
- Consumes: `PulseContractVersion`, `ModuleManifest`, `Sha256Verifier`, `ModuleManager`
- Produces:
  - `ModuleCatalog` (`LoadBuiltIn()`, `Entries` containing `mempulse` -> `rkfsociety/MemPulse` and `diskpulse` -> `rkfsociety/DiskPulse`)
  - `GitHubReleaseClient(HttpClient httpClient)` (`FindLatestCompatibleModuleAsync`, `DownloadAndVerifyAsync`, `GetLatestAppReleaseAsync`)
  - `AppUpdateService` (`CheckForUpdateAsync`, `PrepareUpdatePackageAsync`)

- [ ] **Step 1: Write failing tests with mock `HttpMessageHandler` covering:**
  - Latest release with `contractVersion: "2.0"` skips `.zip` download and selects an older compatible `"1.0"` release;
  - Unsupported OS manifest yields `IncompatiblePlatform` without downloading `.zip`;
  - Exact RID asset (`win-x64`/`linux-x64`) is preferred over `any`;
  - SHA-256 mismatch aborts install/update cleanly;
  - `AppUpdateService` detects newer semantic version (e.g., `0.1.0` -> `0.2.0`), ignores older/equal versions.
- [ ] **Step 2: Run `dotnet test`** — Expected: FAIL.
- [ ] **Step 3: Implement `ModuleCatalog`, `GitHubReleaseClient`, and `AppUpdateService`**
- [ ] **Step 4: Run `dotnet test`** — Expected: PASS.
- [ ] **Step 5: Commit**

---

### Task 5: Single-Window Avalonia UI Shell (`PCPulse.App`) + `--startup-smoke-test`

**Files:**
- Create: `src/PCPulse.App/PCPulse.App.csproj`
- Create: `src/PCPulse.App/Assets/PCPulse.png` & `src/PCPulse.App/Assets/PCPulse.ico`
- Create: `src/PCPulse.App/Services/HostSettings.cs`
- Create: `src/PCPulse.App/Services/HostDialogService.cs`
- Create: `src/PCPulse.App/Services/ThemeTokens.cs`
- Create: `src/PCPulse.App/Views/MainWindow.cs`
- Create: `src/PCPulse.App/Views/OverviewPage.cs`
- Create: `src/PCPulse.App/Views/ModulesPage.cs`
- Create: `src/PCPulse.App/Views/SettingsPage.cs`
- Create: `src/PCPulse.App/Views/AboutPage.cs`
- Create: `src/PCPulse.App/Program.cs`
- Test: `tests/PCPulse.Tests/AppSmokeTests.cs`

**Interfaces:**
- Consumes: `PCPulse.Contracts`, `PCPulse.Core` (`PulsePaths`, `PulseFileLogger`, `ModuleManager`, `ModuleCatalog`, `GitHubReleaseClient`, `AppUpdateService`)
- Produces:
  - Single-window Avalonia `MainWindow` with sidebar navigation (`Обзор`, dynamic enabled modules, `Модули`, `Настройки`, `О программе`), cached module `ContentControl`, embedded `DialogOverlay` (`HostDialogService`), `TrayIcon`, and `--startup-smoke-test` exit code `0`.

- [ ] **Step 1: Write failing tests in `AppSmokeTests.cs` verifying `HostDialogService` queues/completes confirmations cleanly without creating `Window`s and `HostSettings` persists JSON cleanly**
- [ ] **Step 2: Run `dotnet test`** — Expected: FAIL.
- [ ] **Step 3: Generate crisp brand PNG/ICO assets, implement `PCPulse.App` single-window shell, `DialogOverlay`, pages, tray icon, and `--startup-smoke-test`**
- [ ] **Step 4: Run `dotnet test` AND `dotnet run --project src/PCPulse.App/PCPulse.App.csproj -c Release -- --startup-smoke-test`** — Expected: PASS, exit code `0`, startup smoke log marker recorded.
- [ ] **Step 5: Commit**

---

### Task 6: Installers, Update Scripts, CI/Release Workflows, Rules, and Documentation

**Files:**
- Create: `installer/PCPulse.iss`
- Create: `scripts/publish-windows.ps1`
- Create: `scripts/build-installer.ps1`
- Create: `scripts/apply-update.ps1`
- Create: `scripts/install-linux.sh`
- Create: `scripts/apply-update.sh`
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/release.yml`
- Create: `.project/rules.md`
- Modify: `README.md`

- [ ] **Step 1: Create `installer/PCPulse.iss` (`PrivilegesRequired=lowest`, `{localappdata}\Programs\PCPulse`) and PowerShell/Bash install & update scripts (`apply-update.ps1` saved with UTF-8 BOM for PowerShell 5.1 Cyrillic safety)**
- [ ] **Step 2: Create `.github/workflows/ci.yml` (`windows-latest` + `ubuntu-24.04` build `--warnaserror`, `dotnet test`, `dotnet pack PCPulse.Contracts`, `--startup-smoke-test` / `xvfb-run`) and `.github/workflows/release.yml` (publishes `v*` app release + `contracts-v*` NuGet release)**
- [ ] **Step 3: Write `.project/rules.md` and update `README.md` with user + module integration docs**
- [ ] **Step 4: Run full local verification:**
  - `.\scripts\publish-windows.ps1`
  - `.\publish\win-x64\PCPulse.exe --startup-smoke-test`
  - `dotnet test PCPulse.sln -c Release`
  - `dotnet pack src/PCPulse.Contracts/PCPulse.Contracts.csproj -c Release -o dist`
- [ ] **Step 5: Commit**
