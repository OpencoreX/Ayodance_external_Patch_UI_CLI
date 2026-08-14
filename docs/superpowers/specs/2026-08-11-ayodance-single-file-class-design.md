# Ayodance Single-File Class Design

## Goal

Add a standalone C# source file named `AyodanceClient.cs` and merge it directly into the existing WinForms project at `E:\BACKUP_FILE_CUSTOMER\Thipnaphat & Taninsak\AUDITION[AYODANCE]`. The original AyodanceID project, WinForms resources, and bundled AyodanceID executable/DLL remain intact.

## Public API

`AyodanceClient` is a public sealed `IDisposable` class in the `AUDITION_AYODANCE_` namespace. Its constructor accepts a process ID and opens the target process once. Disposal closes the native process handle.

The high-level API exposes:

- `IReadOnlyList<AyodanceFeatureInfo> Features` to discover supported feature keys and descriptions.
- `AyodancePatchResult SetFeature(string key, bool enabled)` to apply or restore one feature.
- `IReadOnlyList<AyodancePatchResult> ApplyAll()` to enable every code patch.
- `IReadOnlyList<AyodancePatchResult> RestoreAll()` to restore every code patch.
- `AyodanceGradeResult SetMaxGrade()` to scan User structures and write grade 2610.

Feature keys remain `lockperfect`, `unlockep`, `ismission`, `beatup`, `bubble`, and `autokey` so behavior matches the current CLI.

## File Isolation

`AyodanceClient.cs` contains all required native interop, memory-region scanning, AOB matching, protected writes, feature definitions, User-structure heuristics, and result types. It must compile without references to `MemoryReader.cs`, `GamePatcher.cs`, `PatchFeature.cs`, `UserStructScanner.cs`, or `Program.cs`.

Internal helper types are nested inside `AyodanceClient` or use distinct private names to avoid conflicts if the file is added back to this repository. Only the client and immutable public result/data types form the supported API.

## Result Model

Patch calls return structured data rather than writing to the console. `AyodancePatchResult` includes the feature key, requested state, applied count, restored count, already-in-state count, failed count, and addresses successfully written.

`AyodanceGradeResult` includes candidate count, successful write count, failed write count, and successfully written addresses.

Returned address collections are read-only from the caller's perspective.

## Errors and Lifetime

- Invalid or non-positive PIDs throw `ArgumentOutOfRangeException`.
- Failure to open the process throws a `Win32Exception` containing the Windows error code.
- Unknown feature keys throw `ArgumentException` and comparison is case-insensitive.
- Calls after disposal throw `ObjectDisposedException`.
- Scan misses are valid results with zero counts, not exceptions.
- Per-address write failures are counted in results so one failure does not stop other matches.

The caller must have sufficient privileges, normally Administrator rights, and the implementation remains Windows-only.

## Data Flow

The constructor opens the process and retains its safe lifetime. A feature request selects a built-in byte pattern, scans all committed readable regions for original and patched forms, then writes the requested form to every matching address. Protected code writes temporarily change page protection and restore it afterward.

Max Grade scans writable private regions for the existing 192-byte User-structure heuristic, validates each candidate again, and writes 2610 at offset 64.

## WinForms Integration

The standalone file is placed at `Class\AyodanceClient.cs` and explicitly added to the legacy project file as a `Compile` item. `Form1.probtn_Click` becomes an `async void` event handler that:

1. Rejects a missing `GLOBAL.PID_Ld9VBoxHeadless` with a MessageBox.
2. Disables the PRO button before background work begins.
3. Uses `Task.Run` to construct the client, apply all six patches, and set Max Grade without blocking the UI thread.
4. Shows a concise MessageBox with aggregate applied/already/failed counts and grade write counts.
5. Catches errors and shows their messages.
6. Re-enables the button in `finally`.

The click is an enable-only action. It does not restore patches. Existing `Resources\AyodanceID.exe`, `.dll`, `.deps.json`, and `.runtimeconfig.json` are not removed.

## PRO Progress UI

The form adds a `ProgressBar` and status `Label` below the PRO button. Progress has seven determinate steps: the six built-in patches followed by Max Grade. Before each step, the label displays `กำลังสแกน <feature> (n/7)...`; after completion, the progress bar advances by one.

The click handler executes each feature sequentially through `Task.Run`, returning to the UI thread after each result so controls update immediately. A patch step is `ผ่าน` when at least one address was newly patched, `เปิดอยู่แล้ว` when at least one patched signature already exists, and `ไม่ผ่าน` when no signature is found or any write fails. Max Grade is `ผ่าน` when at least one candidate is written and otherwise `ไม่ผ่าน`.

The PRO button remains disabled for the entire operation. At completion the bar remains at 100%, the label shows `สำเร็จ` only when every step passed or was already enabled, otherwise `มีบางรายการไม่ผ่าน`, and the existing result MessageBox remains. Exceptions set the label to `เกิดข้อผิดพลาด`, retain the last completed progress value, show the error MessageBox, and re-enable the button.

## Fast Memory Scan

PRO continues to execute all six AOB patches and Max Grade. Patch scanning reads each eligible memory region once using 16 MB blocks. It scans only globally four-byte-aligned addresses, matching ARM32 instruction alignment, and uses a four-byte prefix lookup to select possible original/patched signatures before comparing the complete 16-byte pattern. All matches are retained; the optimization must not stop after the first address.

Eligible patch regions are committed, readable, non-guard `MEM_PRIVATE` or `MEM_MAPPED` regions. Max Grade remains restricted to writable `MEM_PRIVATE` regions. Its User-structure heuristic is reordered into staged rejection: inexpensive level, percentage, experience, and range checks run before pointer and remaining-field checks so most four-byte-aligned candidates exit early.

Both scans report percentage, scanned megabytes, and current MB/s. Patch scanning occupies UI progress 0–80% and Max Grade occupies 80–100%. The implementation remains single-worker to avoid competing with the emulator for CPU and memory bandwidth.

## Neon Gaming UI

The form is restyled as a compact Neon Gaming control panel using a near-black background, blue-purple header accents, cyan primary text, and high-contrast status colors. It contains a branded header, LDPlayer selector with PID/connection state, progress and scan-speed cards, a neon progress bar, current byte-count/status text, separate `START ENGINE` and `STOP SCAN` buttons, and seven status rows for the six patches plus Max Grade.

Designer-owned controls remain in `Form1.Designer.cs`; behavior and state transitions remain in `Form1.cs`. Existing selection and refresh behavior is preserved. Status rows use `WAITING`, `SCANNING`, `PASS`, `ALREADY ON`, `FAILED`, or `STOPPED` and matching neutral/cyan/green/yellow/red colors.

## Cancellation and Thread Safety

Each START creates a new `CancellationTokenSource`. START is disabled and STOP enabled while work is active. STOP only calls `Cancel()`; it does not restore patches already written. The scanner checks the token before every memory region, every block read, every pattern-application loop, and every Max Grade candidate write, producing prompt cooperative cancellation without aborting threads.

All worker operations run through `Task.Run`. Worker code never reads or writes WinForms controls. Progress, throughput, byte counts, current phase, and per-feature results are sent through `IProgress<AyodanceProgress>`, created on the UI thread so callbacks marshal through the WinForms synchronization context. Completion, cancellation, and exceptions restore START/STOP state in `finally`.

Closing the form requests cancellation and prevents subsequent progress callbacks from updating disposed controls. The active token source is disposed when the operation completes or the form closes.

## UI State Flow

`Idle` enables START and disables STOP. `Running` disables selector, refresh, and START while enabling STOP. `Cancelling` keeps controls locked and displays `STOPPING...`. `Completed`, `Cancelled`, and `Failed` return to `Idle`, re-enable selector/refresh/START, disable STOP, and retain the final progress and row statuses for inspection.

## Action Layout

The primary action is labeled `PATCH`, replacing `START ENGINE`, with `STOP` beside it as the paired run control. `REFRESH` remains a separate secondary button inside the LDPlayer selector card beside the ComboBox. During a run PATCH and REFRESH are disabled while STOP is enabled; idle state reverses those enabled states. Fast Scan, patch behavior, cancellation semantics, and progress reporting are unchanged.

## Compact Five-Action Layout

The Neon Gaming form is reduced from 430×608 to approximately 400×500. Header, selector, metric cards, progress text, and task list use tighter but consistent spacing. Actions are arranged in two rows with fixed bounds so translated labels cannot overlap: `START`, `STOP`, and `RE-PROCESS` on the first row; `PATCH` and `REVERT` on the second row.

`START` runs a `BackgroundWorker` image-search loop. The loop repeatedly invokes a dedicated single-iteration method intended for the user's later image-search/comparison implementation, reports status through `ReportProgress`, and checks `CancellationPending` every iteration. A short wait prevents an empty placeholder loop from consuming a CPU core. `STOP` calls `CancelAsync` for this loop or cancels the active memory operation, whichever is running.

`RE-PROCESS` clears and reacquires LDPlayer handles and PIDs. `PATCH` applies all six AOB patches and Max Grade. `REVERT` restores the six AOB patches and does not alter Max Grade data. Image-loop work and PATCH/REVERT memory work are mutually exclusive; while any operation runs, START, RE-PROCESS, PATCH, and REVERT are disabled and STOP is enabled.

BackgroundWorker code never accesses WinForms controls in `DoWork`. UI changes occur only in `ProgressChanged` and `RunWorkerCompleted`. PATCH/REVERT continue using `Task.Run`, `CancellationToken`, and UI-created `Progress<T>` callbacks.

## LAN Room Synchronization

The application supports multiple machines on one LAN and multiple LDPlayer instances per machine. A HOST instance owns one TCP synchronization server and advertises it through UDP broadcast. CLIENT instances discover advertisements matching their persistent Group PIN, connect over TCP, authenticate into the current RoomID, and reconnect automatically after transient disconnects.

Group PIN is stored locally and identifies a trusted set. RoomID is always created by HOST for each application session and is returned only after PIN validation. Stable instance identity is `MachineName + LDPlayer instance name`; PID is runtime metadata and never identity because it changes after restart. Expected quorum is 2, 3, or 5 total instances including HOST.

## Sync Protocol

Every message is newline-delimited JSON and includes `ProtocolVersion`, `MessageType`, `GroupPinHash`, `RoomId`, `InstanceId`, `RoundId`, `Sequence`, and `SentUtc`. Messages larger than 16 KB, malformed JSON, incompatible versions, incorrect PIN hashes, stale RoomIDs, stale RoundIDs, or non-increasing sequences are rejected.

Core messages are `DISCOVER_HOST`, `HOST_ADVERTISEMENT`, `JOIN`, `JOIN_ACCEPTED`, `HEARTBEAT`, `ARM_ROUND`, `ROUND_ARMED`, `READY_TO_PRESS`, `READY_REVOKED`, `QUORUM_READY`, `PRESS_PLAY`, `PRESSED`, `PRESS_FAILED`, `ROUND_CANCELLED`, and `ROUND_COMPLETE`. Only HOST may send ARM, PRESS_PLAY, cancellation, or completion authority messages.

Heartbeat is sent every two seconds. Six seconds without a valid heartbeat marks an instance disconnected, revokes readiness, and cancels an active round. Reconnecting CLIENTS must join the current RoomID and report fresh state; readiness from an earlier connection or RoundID is never reused.

## Round State Machine

Idle allows room configuration. HOST chooses SOLO or SYNC ROOM, expected quorum, and countdown from 0–30 seconds. Settings are snapshotted when HOST presses ARM ROUND; edits during an armed round apply only to the next round.

In SYNC ROOM, ARM ROUND creates a new monotonically increasing RoundID and distributes a HOST UTC start time. All instances wait until that synchronized countdown expires, then their BackgroundWorker begins looking for the Play-button reference image. Finding it sends READY_TO_PRESS but does not click. Losing the image sends READY_REVOKED.

HOST reaches QUORUM_READY only when exactly the configured total is connected in the current RoomID and every one, including HOST, is ready for the same RoundID. HOST must then press CONFIRM PLAY. It sends PRESS_PLAY with a target HOST UTC time approximately 300 ms in the future. Each instance verifies RoundID and the Play image again, waits until the target time using its measured HOST clock offset, invokes `ExecuteReadyClick()`, and returns PRESSED or PRESS_FAILED.

Any disconnect, readiness revocation, timeout, stale message, or failed final image check before confirmation broadcasts ROUND_CANCELLED. The next attempt always receives a new RoundID. Partial participants are never authorized to click.

In SOLO mode no network quorum is used. START snapshots the countdown, waits, searches for the Play image, clicks immediately when found, and remains cancellable through STOP.

## Compact Room UI

The approved Neon Gaming layout retains the compact window and adds `ENGINE` and `ROOM SYNC` tabs. ENGINE keeps START, STOP, RE-PROCESS, PATCH, REVERT, scanner metrics, and patch status. ROOM SYNC contains SOLO/SYNC selection, HOST/CLIENT role, Group PIN, RoomID, quorum, countdown, RoundID/state, member readiness table, ARM ROUND, CONFIRM PLAY, STOP/CANCEL, and connection status.

CONFIRM PLAY is enabled only for HOST in QUORUM_READY. CLIENTS see it disabled. ARM is unavailable while a round is active. Network status updates are marshalled through the WinForms synchronization context; socket/background threads never access controls directly.

## Extension Handoff

Image-dependent work is isolated behind `FindReadyImage()` and `ExecuteReadyClick()` extension points. The initial implementation provides safe placeholders returning not-found/no-click and documents the expected screen coordinate, confidence, cancellation, and result contracts. Network, room state, countdown, quorum, cancellation, and UI can be tested without real image recognition through a manual readiness test action available only in Debug builds.

A handoff document is required at `docs/HANDOFF-room-sync.md`. It records architecture, protocol fields/messages, state transitions, configuration keys, extension points, build/run instructions, LAN firewall requirements, test procedure for 2/3/5 instances, known limitations, and the exact next steps for adding single-image matching and clicking.

## Room Sync Verification

Automated tests cover message serialization/validation, stale sequence rejection, host-only authority, quorum counting including HOST, disconnect cancellation, RoundID rollover, countdown snapshotting, SOLO bypass, reconnect without stale readiness, cancellation, and target-time calculation. Loopback integration tests run one HOST and multiple CLIENTS in-process for quorum sizes 2, 3, and 5. The WinForms project must build x64 with existing AyodanceID resources preserved.

## Compatibility

The merged source targets .NET Framework 4.8 and its default C# compiler. It avoids records, `nint`, `init`, nullable-reference syntax, ranges, and other modern-only syntax. Public addresses use `IntPtr`. It uses only framework APIs and Windows P/Invoke, with no new NuGet dependency, no `Main` method, no interactive prompts, and no console output.

## Verification

- Compile the standalone file through the target .NET Framework 4.8 WinForms project.
- Compile the event handler that constructs and disposes `AyodanceClient` and calls the public methods.
- Run the target solution/project build and confirm the original AyodanceID project is not modified.
- Exercise validation paths with invalid PID and unknown feature key without requiring a live game process.

Live patch effectiveness requires the matching game build and a suitably privileged process, so it is outside deterministic automated verification.
