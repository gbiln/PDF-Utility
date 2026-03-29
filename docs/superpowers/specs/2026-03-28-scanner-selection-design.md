# Scanner Selection Design

## Goal

Allow the user to pick which scanner to use from a dropdown in the Scan Double Sided view, with a refresh button to re-enumerate — required because the scanner is network-attached and may not be present at startup.

## Context

The app uses NAPS2.Sdk via `IScannerBackend`. The backend already has `GetAvailableDevicesAsync()` returning device names, and `_selectedDevice` is currently auto-set to the first device found at startup. There is no UI to choose or refresh the device list.

## UI

A "SCANNER" section is added at the top of the left control panel in `ScanDoubleSidedView`, above the existing "BATCH 1 — Front Sides" section:

```
SCANNER
[EPSON ET-4850 Series      ▼] [↻]
```

- Full-width ComboBox displaying available device names, with a small inline Refresh (`↻`) button
- While enumerating: ComboBox disabled and shows "Scanning for devices…", Refresh button disabled
- No devices found: ComboBox shows "No scanners found", disabled
- "Start Scanning Batch 1" remains disabled until a device is selected from the dropdown
- If exactly one device is found during enumeration, it is automatically selected

## Architecture

### IScannerBackend — breaking change: rename `GetAvailableDevicesAsync`

`GetAvailableDevicesAsync()` is renamed to `GetDevicesAsync()`. All three change sites:
- `IScannerBackend.cs` — rename the method declaration
- `Naps2ScannerBackend.cs` — rename the method implementation
- `FakeScannerBackend.cs` — rename the method implementation

New interface methods:

```csharp
Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken = default);
void SelectDevice(string deviceName);
```

`GetDevicesAsync` re-enumerates the WIA device list each time it is called (supports network scanner appearing after startup). `SelectDevice` sets the active device by name.

**Note:** NAPS2's `GetDeviceList` does not accept a `CancellationToken`. The parameter is present on the interface for future compatibility but is not threaded through to NAPS2 in this implementation.

### Naps2ScannerBackend

- `GetDevicesAsync`: calls `_controller!.GetDeviceList(new NAPS2.Scan.ScanOptions { Driver = Driver.Wia })`, stores the full `IList<ScanDevice>` internally, returns `devices.Select(d => d.Name).ToList()`
- `SelectDevice(string name)`: finds matching `ScanDevice` by name in the stored list and sets `_selectedDevice`. Callers must only pass names sourced from the most recent `GetDevicesAsync` result. If name is not found (stale call), `_selectedDevice` is set to `null`.

### FakeScannerBackend

- New property: `List<string> SimulatedDevices { get; } = new() { "Fake Scanner" }` (keeps existing name for backward compatibility)
- `GetDevicesAsync`: returns `SimulatedDevices`
- `SelectDevice(string name)`: stores the name in a new `string? SelectedDeviceName` property (for test assertions); no scan behavior change
- New property: `TaskCompletionSource? GetDevicesGate` — if non-null, `GetDevicesAsync` awaits it before returning, allowing tests to pause enumeration mid-flight and assert `IsLoadingDevices == true` before releasing

### ScanDoubleSidedViewModel — new members

| Member | Type | Purpose |
|--------|------|---------|
| `AvailableDevices` | `ObservableCollection<string>` | Bound to ComboBox |
| `SelectedDevice` | `string?` `[ObservableProperty]` | Bound to ComboBox; partial `OnSelectedDeviceChanged` calls `_scanner.SelectDevice()` |
| `IsLoadingDevices` | `bool` `[ObservableProperty]` | True while `GetDevicesAsync` is running; disables ComboBox and Refresh button |
| `RefreshDevicesCommand` | `[RelayCommand(CanExecute = nameof(CanRefreshDevices))]` | Re-enumerates; disabled while `IsLoadingDevices` |

`CanStartBatch1` gains an additional guard: `SelectedDevice != null`.

`[NotifyCanExecuteChangedFor(nameof(StartBatch1Command))]` is added to `_selectedDevice`.

`[NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand))]` is added to `_isLoadingDevices`.

**Auto-select**: after `GetDevicesAsync` returns, if the list has exactly one device, `SelectedDevice` is set to that device automatically.

**Refresh clears stale selection**: if `SelectedDevice` is not null but no longer appears in the new device list, `SelectedDevice` is set to null.

**Error handling**: if `GetDevicesAsync` throws, `AvailableDevices` is cleared, `SelectedDevice` is set to null, `StatusMessage` is set to "Could not enumerate scanners — check network connection and click ↻ to retry."

### Startup behaviour

`ScanDoubleSidedView.xaml.cs` subscribes to the `Loaded` event and calls `vm.RefreshDevicesCommand.Execute(null)` once. This keeps lifecycle handling in the View and avoids ViewModel-to-View coupling.

## Existing Test Impact

`InitialState_IsIdle` in `ScanDoubleSidedViewModelTests.cs` currently asserts `StartBatch1Command.CanExecute(null) == true`. After this change, `CanStartBatch1` requires `SelectedDevice != null`, so this assertion will fail. The test must be updated: either set `vm.SelectedDevice = "Fake Scanner"` before asserting CanExecute, or split into two tests (`CanStartBatch1_FalseWhenNoDeviceSelected` and `CanStartBatch1_TrueAfterDeviceSelected` — which are already listed below).

## Tests

- `GetDevices_ReturnsSimulatedList`: `FakeScannerBackend.GetDevicesAsync()` returns `SimulatedDevices`
- `SelectDevice_StoresDeviceName`: after `fake.SelectDevice("Fake Scanner")`, `fake.SelectedDeviceName == "Fake Scanner"`
- `SelectedDevice_PropertySetter_CallsBackendSelectDevice`: setting `vm.SelectedDevice = "Fake Scanner"` results in `fake.SelectedDeviceName == "Fake Scanner"`
- `CanStartBatch1_FalseWhenNoDeviceSelected`: `CanStartBatch1` returns false when `SelectedDevice` is null
- `CanStartBatch1_TrueAfterDeviceSelected`: `CanStartBatch1` returns true after `SelectedDevice` is set and `SessionState == Idle`
- `RefreshDevices_ClearsSelectionIfDeviceDisappears`: if previously selected device not in new list, `SelectedDevice` becomes null
- `RefreshDevices_AutoSelectsIfSingleDevice`: if exactly one device found, `SelectedDevice` is set automatically
- `RefreshDevices_SetsIsLoadingDevicesDuringEnumeration`: set `fake.GetDevicesGate = new TaskCompletionSource()`, call `RefreshDevicesCommand.ExecuteAsync(null)` (do not await), assert `IsLoadingDevices == true`, then `gate.SetResult()`, await the command, assert `IsLoadingDevices == false`
- `RefreshDevices_ShowsErrorMessageOnFailure`: if `GetDevicesAsync` throws, `StatusMessage` contains error text and `SelectedDevice` is null
- `InitialState_IsIdle` (existing test update): add `vm.SelectedDevice = "Fake Scanner"` before asserting `CanStartBatch1`
