# Ayodance Single-File Class Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a copyable .NET Framework 4.8 `AyodanceClient.cs` class API, merge it into the target WinForms project, and invoke all patches plus Max Grade from the PRO button while leaving existing files intact.

**Architecture:** One new source file under the target project's `Class` folder contains the public facade, immutable result classes, and private Windows memory/scanning helpers. `Form1.probtn_Click` dispatches enable-all and Max Grade work through `Task.Run`, then reports aggregate results on the UI thread.

**Tech Stack:** C# 7.3-compatible syntax, .NET Framework 4.8, Windows Forms, Windows P/Invoke, legacy MSBuild project

## Global Constraints

- Create `Class\AyodanceClient.cs` as a standalone source file with no dependency on the original AyodanceID source files.
- Keep all existing target resources and original AyodanceID project files unchanged.
- Provide no `Main`, prompts, or console output.
- Use namespace `AUDITION_AYODANCE_` and target .NET Framework 4.8.
- Use `IntPtr` for addresses and avoid records, `nint`, `init`, and nullable-reference syntax.
- Use no NuGet dependencies.
- Do not create Git commits unless the user explicitly requests one.

---

### Task 1: Public API and Validation

**Files:**
- Create: `AyodanceClient.cs`
- Create temporarily for verification: `%TEMP%\AyodanceClientSmoke\AyodanceClientSmoke.csproj`
- Create temporarily for verification: `%TEMP%\AyodanceClientSmoke\ClientApiTests.cs`

**Interfaces:**
- Consumes: Windows process ID as `int` and feature key as `string`.
- Produces: `AyodanceClient(int processId)`, `Features`, `SetFeature`, `ApplyAll`, `RestoreAll`, `SetMaxGrade`, and `Dispose`.

- [ ] **Step 1: Write the API smoke test**

```csharp
using AyodanceID;

if (AyodanceClient.AvailableFeatures.Count != 6)
    throw new Exception("Expected six features.");

try
{
    _ = new AyodanceClient(0);
    throw new Exception("Expected invalid PID failure.");
}
catch (ArgumentOutOfRangeException)
{
}
```

- [ ] **Step 2: Compile to verify the API is initially absent**

Run: `dotnet build "$env:TEMP\AyodanceClientSmoke\AyodanceClientSmoke.csproj"`

Expected: FAIL because `AyodanceClient` is not defined.

- [ ] **Step 3: Add public immutable models and facade signatures**

```csharp
namespace AyodanceID;

public sealed record AyodanceFeatureInfo(string Key, string Name, string Description);

public sealed record AyodancePatchResult(
    string FeatureKey,
    bool Enabled,
    int Applied,
    int Restored,
    int AlreadyInState,
    int Failed,
    IReadOnlyList<nint> Addresses);

public sealed record AyodanceGradeResult(
    int Candidates,
    int Written,
    int Failed,
    IReadOnlyList<nint> Addresses);

public sealed class AyodanceClient : IDisposable
{
    public static IReadOnlyList<AyodanceFeatureInfo> AvailableFeatures { get; }
    public int ProcessId { get; }
    public AyodanceClient(int processId);
    public AyodancePatchResult SetFeature(string key, bool enabled);
    public IReadOnlyList<AyodancePatchResult> ApplyAll();
    public IReadOnlyList<AyodancePatchResult> RestoreAll();
    public AyodanceGradeResult SetMaxGrade();
    public void Dispose();
}
```

Implement constructor validation before calling native APIs. Feature lookup uses `StringComparer.OrdinalIgnoreCase`; unknown keys throw `ArgumentException`. Every public operation calls a private `ThrowIfDisposed()` guard.

- [ ] **Step 4: Build the standalone smoke project**

Run: `dotnet build "$env:TEMP\AyodanceClientSmoke\AyodanceClientSmoke.csproj" -c Release`

Expected: PASS with 0 warnings and 0 errors.

### Task 2: Standalone Memory Patching Engine

**Files:**
- Modify: `AyodanceClient.cs`
- Modify temporarily: `%TEMP%\AyodanceClientSmoke\ClientApiTests.cs`

**Interfaces:**
- Consumes: the facade methods and built-in ARM32 original/patched byte patterns.
- Produces: protected cross-process reads/writes, region enumeration, AOB matching, and structured patch results.

- [ ] **Step 1: Extend the smoke test for invalid feature and disposal**

```csharp
try
{
    using var client = new AyodanceClient(Environment.ProcessId);
    client.SetFeature("missing", true);
    throw new Exception("Expected unknown feature failure.");
}
catch (ArgumentException)
{
}

var disposed = new AyodanceClient(Environment.ProcessId);
disposed.Dispose();
try
{
    disposed.ApplyAll();
    throw new Exception("Expected disposed failure.");
}
catch (ObjectDisposedException)
{
}
```

- [ ] **Step 2: Run the smoke test to expose missing behavior**

Run: `dotnet run --project "$env:TEMP\AyodanceClientSmoke\AyodanceClientSmoke.csproj" -c Release`

Expected: FAIL until process lifetime, lookup validation, and disposal are implemented.

- [ ] **Step 3: Implement the private native engine**

Add private nested `FeatureDefinition`, `MemoryRegion`, and `ProcessMemory` types. Copy the six exact feature patterns from `Program.cs`. Port the `OpenProcess`, `CloseHandle`, `VirtualQueryEx`, `ReadProcessMemory`, `WriteProcessMemory`, `VirtualProtectEx`, and `FlushInstructionCache` declarations and constants from `MemoryReader.cs` without public exposure.

Use the existing scanning contract:

```csharp
private (List<nint> Original, List<nint> Patched) FindPatterns(
    ReadOnlySpan<byte> original,
    ReadOnlySpan<byte> patched);

private AyodancePatchResult Apply(FeatureDefinition feature, bool enabled);
```

Scan committed readable regions in overlapping chunks so signatures crossing chunk boundaries are included. For enable, write patched bytes to original hits and count patched hits as already in state. For disable, reverse those roles. Continue after individual write failures and include only successful write addresses.

- [ ] **Step 4: Run validation smoke tests**

Run: `dotnet run --project "$env:TEMP\AyodanceClientSmoke\AyodanceClientSmoke.csproj" -c Release`

Expected: PASS with exit code 0 and no console output.

### Task 3: Max Grade and Repository Verification

**Files:**
- Modify: `AyodanceClient.cs`
- Modify: `README.md`
- Modify temporarily: `%TEMP%\AyodanceClientSmoke\ClientApiTests.cs`

**Interfaces:**
- Consumes: writable private memory regions and the existing 192-byte User-structure heuristic.
- Produces: `AyodanceGradeResult SetMaxGrade()` and copy/paste usage documentation.

- [ ] **Step 1: Add a compile-time consumer example**

```csharp
static void Example(int gamePid)
{
    using var client = new AyodanceClient(gamePid);
    AyodancePatchResult patch = client.SetFeature("lockperfect", true);
    IReadOnlyList<AyodancePatchResult> all = client.ApplyAll();
    AyodanceGradeResult grade = client.SetMaxGrade();
}
```

- [ ] **Step 2: Implement User-structure scan and grade write**

Port the exact validation rules from `UserStructScanner.Heuristic`. Scan only `MEM_PRIVATE` writable regions at four-byte alignment, preserve the 192-byte overlap between chunks, validate every candidate immediately before writing, and write little-endian integer `2610` at candidate address plus offset `64`.

Return:

```csharp
return new AyodanceGradeResult(
    candidates.Count,
    writtenAddresses.Count,
    candidates.Count - writtenAddresses.Count,
    writtenAddresses.AsReadOnly());
```

- [ ] **Step 3: Document direct class usage**

Add a README section showing that callers copy `AyodanceClient.cs` into a .NET 8 project, construct it with the game PID, use `SetFeature`, `ApplyAll`, `RestoreAll`, and `SetMaxGrade`, then dispose it with `using`.

- [ ] **Step 4: Verify standalone and repository builds**

Run: `dotnet build "$env:TEMP\AyodanceClientSmoke\AyodanceClientSmoke.csproj" -c Release`

Expected: PASS with 0 errors.

Run: `dotnet build "C:\Users\KALIUNAI_PC\Desktop\AyodanceID\AyodanceID.csproj" -c Release`

Expected: PASS with 0 errors and no duplicate type definitions.

- [ ] **Step 5: Inspect the final working-tree scope**

Run: `git -C "C:\Users\KALIUNAI_PC\Desktop\AyodanceID" status --short`

Expected: new `AyodanceClient.cs`, README/spec/plan changes, preserved pre-existing build artifact changes, and no deletion or modification of existing CLI source files.

### Task 4: Merge Into WinForms PRO Button

**Files:**
- Create: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Class\AyodanceClient.cs`
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE].csproj`
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Form1.cs`

**Interfaces:**
- Consumes: `GLOBAL.PID_Ld9VBoxHeadless` and the existing `probtn` control.
- Produces: a non-blocking click action that enables all six patches, writes Max Grade, and displays aggregate results.

- [ ] **Step 1: Add standalone class to the legacy project**

Add this exact compile item beside the existing `Class` entries:

```xml
<Compile Include="Class\AyodanceClient.cs" />
```

- [ ] **Step 2: Wire the PRO click handler**

```csharp
private async void probtn_Click(object sender, EventArgs e)
{
    int processId = GLOBAL.PID_Ld9VBoxHeadless;
    if (processId <= 0)
    {
        MessageBox.Show("กรุณาเลือกหน้าต่าง LDPlayer ก่อน", "Ayodance", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
    }

    probtn.Enabled = false;
    try
    {
        AyodanceRunResult result = await Task.Run(() =>
        {
            using (var client = new AyodanceClient(processId))
            {
                return new AyodanceRunResult(client.ApplyAll(), client.SetMaxGrade());
            }
        });

        MessageBox.Show(result.ToSummary(), "Ayodance", MessageBoxButtons.OK,
            result.Failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }
    catch (Exception ex)
    {
        MessageBox.Show(ex.Message, "Ayodance Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
        probtn.Enabled = true;
    }
}
```

Define `AyodanceRunResult` in the standalone file with aggregate `Failed` and `ToSummary()` so UI formatting does not duplicate patch logic.

- [ ] **Step 3: Build the target project**

Run: `msbuild "E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE].csproj" /t:Build /p:Configuration=Debug /m`

Expected: PASS with 0 compile errors.

- [ ] **Step 4: Verify preservation and diff scope**

Confirm `Resources\AyodanceID.exe`, `Resources\AyodanceID.dll`, `Resources\AyodanceID.deps.json`, and `Resources\AyodanceID.runtimeconfig.json` still exist. Confirm only `Class\AyodanceClient.cs`, the `.csproj`, and `Form1.cs` changed in the target project.

### Task 5: Add Determinate PRO Progress

**Files:**
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Form1.cs`
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Form1.Designer.cs`

**Interfaces:**
- Consumes: `AyodanceClient.AvailableFeatures`, `SetFeature`, `SetMaxGrade`, and existing `probtn`.
- Produces: `proProgressBar` with range 0..7 and `proStatusLabel` with per-step and final status.

- [ ] **Step 1: Make the handler consume the intended controls**

Reset the bar to zero, update the label before and after each of six sequential feature calls, run Max Grade as step seven, and set the final label from aggregate success. Build before adding designer declarations and confirm compile failure for missing `proProgressBar` and `proStatusLabel`.

- [ ] **Step 2: Add designer controls**

Add designer-owned `ProgressBar proProgressBar` and `Label proStatusLabel`, place them below the top buttons, set `Maximum = 7`, and add them to the form controls collection.

- [ ] **Step 3: Verify behavior classification**

A feature passes when `Applied > 0`, is already enabled when `AlreadyInState > 0`, and fails when neither is true or `Failed > 0`. Max Grade passes only when `Written > 0` and `Failed == 0`.

- [ ] **Step 4: Build and preserve resources**

Run the Debug build, confirm `PE32+` with `32BITPREF=0`, and confirm all four existing AyodanceID resource files remain present.

### Task 6: Fast Scan, Cancellation, and Neon UI

**Files:**
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Class\AyodanceClient.cs`
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Form1.cs`
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Form1.Designer.cs`

**Interfaces:**
- Consumes: `CancellationToken`, `IProgress<AyodanceProgress>`, selected LDPlayer PID, and existing refresh/select behavior.
- Produces: cancellable `ApplyAll` and `SetMaxGrade`, throughput metrics, Neon Gaming START/STOP UI, and seven task statuses.

- [ ] **Step 1: Compile-test cancellation API before implementation**

Require `ApplyAll(IProgress<AyodanceProgress>, CancellationToken)` and `SetMaxGrade(IProgress<AyodanceProgress>, CancellationToken)`, plus `ScannedBytes`, `TotalBytes`, and `MegabytesPerSecond` progress properties. Confirm compile failure before adding them.

- [ ] **Step 2: Implement fast cancellable scanner**

Increase blocks to 16 MB, restrict patch regions to readable `MEM_PRIVATE`/`MEM_MAPPED`, scan globally four-byte-aligned addresses, dispatch candidates through a four-byte prefix table, retain all full-pattern matches, and check cancellation at region/block/write boundaries.

- [ ] **Step 3: Report measurable progress**

Use `Stopwatch` to report percentage, bytes scanned, total bytes, and MB/s from both Patch and Max Grade scans. Reorder User-structure validation so cheap scalar checks reject candidates before pointer checks.

- [ ] **Step 4: Build Neon Gaming designer**

Replace the sparse legacy layout with branded dark panels, selector/PID state, progress and MB/s metrics, progress visualization, seven-row status list, and separate START/STOP buttons while preserving existing form events.

- [ ] **Step 5: Implement safe UI state machine**

Create and dispose one `CancellationTokenSource` per run, use `Task.Run` with the token, update controls only through UI-created `Progress<AyodanceProgress>`, keep written patches on STOP, cancel on form close, and restore control state in `finally`.

- [ ] **Step 6: Verify build and preservation**

Build Debug x64, confirm `PE32+` and `32BITPREF=0`, verify cancellation API and UI controls exist, and confirm all four bundled AyodanceID resources remain present.

### Task 7: Clarify PATCH and REFRESH Actions

**Files:**
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Form1.Designer.cs`

- [ ] **Step 1:** Assert the primary labels are exactly `PATCH` and `STOP` and observe failure.
- [ ] **Step 2:** Rename START ENGINE to PATCH and STOP SCAN to STOP while leaving REFRESH beside the selector.
- [ ] **Step 3:** Build Debug x64 and verify enabled-state logic remains PATCH/REFRESH off and STOP on while running.

### Task 8: Compact Five-Action Workflow

**Files:**
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Class\AyodanceClient.cs`
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Form1.cs`
- Modify: `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]\AUDITION[AYODANCE]\AUDITION[AYODANCE]\Form1.Designer.cs`

- [ ] **Step 1:** Compile-test `RestoreAll(IProgress<AyodanceProgress>, CancellationToken)` and observe failure.
- [ ] **Step 2:** Reuse the one-pass fast scanner for cancellable restore operations.
- [ ] **Step 3:** Add BackgroundWorker START loop with progress/cancellation events and an isolated single-iteration extension method.
- [ ] **Step 4:** Implement one STOP action that cancels the active BackgroundWorker or memory token.
- [ ] **Step 5:** Reflow Neon UI to approximately 400×500 with START/STOP/RE-PROCESS and PATCH/REVERT rows.
- [ ] **Step 6:** Build x64 and verify no overlapping bounds, mutual exclusion, event wiring, and resource preservation.
