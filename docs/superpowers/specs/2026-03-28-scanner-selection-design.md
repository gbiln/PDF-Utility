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
- While enumerating: ComboBox disabled, shows "Scanning for devices…"
- No devices found: ComboBox shows "No scanners found", disabled
- "Start Scanning Batch 1" remains disabled until a device is selected from the dropdown

## Architecture

### IScannerBackend (new methods)

```csharp
Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken = default);
void SelectDevice(string deviceName);
```

`GetDevicesAsync` re-enumerates the WIA device list each time it is called (supports network scanner appearing after startup). `SelectDevice` sets the active device by name; subsequent scans use that device.

### Naps2ScannerBackend

- `GetDevicesAsync`: calls `_controller.GetDeviceList(...)`, returns `d.Name` for each device; stores full `ScanDevice` list internally for lookup by name
- `SelectDevice(string name)`: finds the matching `ScanDevice` by name and sets `_selectedDevice`; no-op if name not found

### FakeScannerBackend

- New property `List<string> SimulatedDevices { get; }` (defaults to `["Test Scanner"]`)
- `GetDevicesAsync` returns `SimulatedDevices`
- `SelectDevice` stores the name; no scan behavior change

### ScanDoubleSidedViewModel (new members)

| Member | Type | Purpose |
|--------|------|---------|
| `AvailableDevices` | `ObservableCollection<string>` | Bound to ComboBox |
| `SelectedDevice` | `string?` | Bound to ComboBox selection; setter calls `_scanner.SelectDevice()` |
| `IsLoadingDevices` | `bool` | True while `GetDevicesAsync` is running |
| `RefreshDevicesCommand` | `[RelayCommand]` | Re-enumerates; clears `SelectedDevice` if it no longer appears in new list |

`CanStartBatch1` gains an additional guard: `SelectedDevice != null`.

`[NotifyCanExecuteChangedFor(nameof(StartBatch1Command))]` added to `_selectedDevice` field (or `SelectedDevice` property setter).

### Startup behaviour

When `ScanDoubleSidedView` is loaded (via the `Loaded` event in code-behind, or the ViewModel's initialisation), `RefreshDevicesCommand` is executed automatically. This happens on a background thread; the UI shows "Scanning for devices…" while it runs.

## Error Handling

- If `GetDevicesAsync` throws (network timeout, WIA error): `AvailableDevices` is cleared, `SelectedDevice` set to null, status message shows "Could not enumerate scanners — check network connection and refresh."
- Scanner disappears between enumeration and scan: the existing `ScannerException` path in `RunScanBatchAsync` handles this (paper jam recovery flow).

## Testing

- `GetDevices_ReturnsSimulatedList`: `FakeScannerBackend` returns `SimulatedDevices`
- `SelectDevice_UpdatesSelectedDevice`: after `SelectDevice("X")`, `SelectedDevice == "X"`
- `CanStartBatch1_FalseWhenNoDeviceSelected`: `CanStartBatch1` returns false before device selected
- `CanStartBatch1_TrueAfterDeviceSelected`: `CanStartBatch1` returns true after `SelectedDevice` set
- `RefreshDevices_ClearsSelectionIfDeviceDisappears`: if previously selected device not in new list, `SelectedDevice` becomes null
