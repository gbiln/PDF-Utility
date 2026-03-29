# Scanner Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a scanner selector dropdown + refresh button to the Scan Double Sided view so the user can pick their network-attached scanner before scanning.

**Architecture:** Rename `GetAvailableDevicesAsync` → `GetDevicesAsync` and add `SelectDevice(string?)` to `IScannerBackend`. `ScanDoubleSidedViewModel` gains `AvailableDevices`, `SelectedDevice`, `IsLoadingDevices`, and `RefreshDevicesCommand`. The view adds a SCANNER section at the top of the left panel; the `Loaded` event triggers initial enumeration. `CanStartBatch1` gains a `SelectedDevice != null` guard.

**Tech Stack:** C# / .NET 10-windows, CommunityToolkit.Mvvm, NAPS2.Sdk (WIA), WPF + WPF-UI, xUnit

**Spec:** `docs/superpowers/specs/2026-03-28-scanner-selection-design.md`

---

## File Map

```
src/
  PdfUtility.Core/
    Interfaces/IScannerBackend.cs              ← rename GetAvailableDevicesAsync; add SelectDevice
  PdfUtility.Scanning/
    Naps2ScannerBackend.cs                     ← implement GetDevicesAsync + SelectDevice
  PdfUtility.App/
    ViewModels/ScanDoubleSidedViewModel.cs     ← add scanner selection members + CanStartBatch1 guard
    Views/ScanDoubleSidedView.xaml             ← add SCANNER section to left panel
    Views/ScanDoubleSidedView.xaml.cs          ← add Loaded event → RefreshDevicesCommand
tests/
  PdfUtility.App.Tests/
    Fakes/FakeScannerBackend.cs                ← add SimulatedDevices, SelectedDeviceName, GetDevicesGate
    ViewModels/ScanDoubleSidedViewModelTests.cs ← fix InitialState_IsIdle; add 9 new tests
```

---

## Task 1: Update IScannerBackend + FakeScannerBackend

**Files:**
- Modify: `src/PdfUtility.Core/Interfaces/IScannerBackend.cs`
- Modify: `tests/PdfUtility.App.Tests/Fakes/FakeScannerBackend.cs`
- Modify: `tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs`

- [ ] **Step 1: Write failing tests for FakeScannerBackend new behavior**

Add to `tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs` (inside the class, before the closing brace):

```csharp
[Fact]
public async Task GetDevices_ReturnsSimulatedList()
{
    var fake = new FakeScannerBackend();
    var devices = await fake.GetDevicesAsync();
    Assert.Equal(new[] { "Fake Scanner" }, devices);
}

[Fact]
public void SelectDevice_StoresDeviceName()
{
    var fake = new FakeScannerBackend();
    fake.SelectDevice("Fake Scanner");
    Assert.Equal("Fake Scanner", fake.SelectedDeviceName);
}

[Fact]
public void SelectDevice_Null_ClearsDeviceName()
{
    var fake = new FakeScannerBackend();
    fake.SelectDevice("Fake Scanner");
    fake.SelectDevice(null);
    Assert.Null(fake.SelectedDeviceName);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:/Projects/PDF-Utility/.worktrees/plan1-scan-double-sided
dotnet test tests/PdfUtility.App.Tests/ --filter "GetDevices|SelectDevice" -v n
```

Expected: compile error — `GetDevicesAsync` and `SelectDevice` not defined.

- [ ] **Step 3: Update IScannerBackend**

Replace `IScannerBackend.cs` content:

```csharp
// src/PdfUtility.Core/Interfaces/IScannerBackend.cs
using System.Collections.Generic;
using System.Threading;
using PdfUtility.Core.Models;

namespace PdfUtility.Core.Interfaces;

public interface IScannerBackend
{
    /// <summary>
    /// Streams pages from the ADF one at a time as they are scanned.
    /// The IAsyncEnumerable completes when the ADF tray is empty.
    /// Throws ScannerException on feeder error or jam.
    /// </summary>
    IAsyncEnumerable<ScannedPage> ScanBatchAsync(
        ScanOptions options,
        int batchNumber,
        string sessionDirectory,
        int startingPageIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a single page from the flatbed glass.
    /// Throws ScannerException on failure.
    /// </summary>
    Task<ScannedPage> ScanSingleFlatbedAsync(
        ScanOptions options,
        int batchNumber,
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all available scanner device names (re-enumerates each call).
    /// Replaces GetAvailableDevicesAsync.
    /// </summary>
    Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the active device by name. Pass null to clear the selection.
    /// Callers must only pass names sourced from the most recent GetDevicesAsync result.
    /// </summary>
    void SelectDevice(string? deviceName);

    /// <summary>Pre-warms the scanner context (call at app startup).</summary>
    Task InitialiseAsync();
}
```

- [ ] **Step 4: Update FakeScannerBackend**

Replace `FakeScannerBackend.cs` content:

```csharp
// tests/PdfUtility.App.Tests/Fakes/FakeScannerBackend.cs
using System.Runtime.CompilerServices;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;

namespace PdfUtility.App.Tests.Fakes;

public class FakeScannerBackend : IScannerBackend
{
    /// <summary>Pages to yield from the next ScanBatchAsync call.</summary>
    public Queue<List<string>> BatchQueue { get; } = new();

    /// <summary>If set, ScanBatchAsync throws this after yielding any queued pages.</summary>
    public Exception? NextScanError { get; set; }

    /// <summary>Path returned by the next ScanSingleFlatbedAsync call.</summary>
    public string? NextFlatbedImagePath { get; set; }

    /// <summary>If true, ScanSingleFlatbedAsync throws ScannerException.</summary>
    public bool FlatbedShouldFail { get; set; }

    public bool InitialiseCalled { get; private set; }

    /// <summary>Device names returned by GetDevicesAsync.</summary>
    public List<string> SimulatedDevices { get; } = new() { "Fake Scanner" };

    /// <summary>Name passed to the last SelectDevice call (null if cleared).</summary>
    public string? SelectedDeviceName { get; private set; }

    /// <summary>
    /// If set, GetDevicesAsync awaits this before returning — lets tests observe
    /// IsLoadingDevices == true before releasing enumeration.
    /// </summary>
    public TaskCompletionSource? GetDevicesGate { get; set; }

    /// <summary>If true, GetDevicesAsync throws InvalidOperationException.</summary>
    public bool GetDevicesShouldFail { get; set; }

    public async IAsyncEnumerable<ScannedPage> ScanBatchAsync(
        ScanOptions options,
        int batchNumber,
        string sessionDirectory,
        int startingPageIndex,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var paths = BatchQueue.Count > 0 ? BatchQueue.Dequeue() : new List<string>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ScannedPage(path, sourceBatch: batchNumber);
        }
        if (NextScanError is not null)
        {
            var ex = NextScanError;
            NextScanError = null;
            throw ex;
        }
    }

    public Task<ScannedPage> ScanSingleFlatbedAsync(
        ScanOptions options,
        int batchNumber,
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        if (FlatbedShouldFail)
            throw new ScannerException("Fake flatbed failure");
        var path = NextFlatbedImagePath ?? "fake_flatbed.png";
        return Task.FromResult(new ScannedPage(path, sourceBatch: batchNumber));
    }

    public async Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (GetDevicesGate is { } gate)
            await gate.Task;
        if (GetDevicesShouldFail)
            throw new InvalidOperationException("Simulated device enumeration failure");
        return SimulatedDevices.AsReadOnly();
    }

    public void SelectDevice(string? deviceName) => SelectedDeviceName = deviceName;

    public Task InitialiseAsync()
    {
        InitialiseCalled = true;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/PdfUtility.App.Tests/ --filter "GetDevices|SelectDevice" -v n
```

Expected: 3 tests PASS.

- [ ] **Step 6: Run App.Tests only (Naps2ScannerBackend will not compile yet — that is expected)**

```bash
dotnet test tests/PdfUtility.App.Tests/ -v q
```

Expected: all 27 tests pass (10 original App + 3 new fake tests). `PdfUtility.Scanning` will fail to build because `Naps2ScannerBackend` still has `GetAvailableDevicesAsync` — this is intentional and is fixed in Task 2. Do NOT run `dotnet test PdfUtility.slnx` until Task 2 is complete.

- [ ] **Step 7: Commit**

```bash
git add src/PdfUtility.Core/Interfaces/IScannerBackend.cs tests/PdfUtility.App.Tests/
git commit -m "feat(core): rename GetAvailableDevicesAsync→GetDevicesAsync; add SelectDevice to IScannerBackend"
```

---

## Task 2: Update Naps2ScannerBackend

**Files:**
- Modify: `src/PdfUtility.Scanning/Naps2ScannerBackend.cs`

- [ ] **Step 1: Update Naps2ScannerBackend to implement the new interface**

Replace the full content of `src/PdfUtility.Scanning/Naps2ScannerBackend.cs`:

```csharp
// src/PdfUtility.Scanning/Naps2ScannerBackend.cs
using NAPS2.Images;
using NAPS2.Images.Wpf;
using NAPS2.Scan;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;
using ScanOptions = PdfUtility.Core.Models.ScanOptions;

namespace PdfUtility.Scanning;

public class Naps2ScannerBackend : IScannerBackend, IDisposable
{
    private ScanningContext? _context;
    private ScanController? _controller;
    private ScanDevice? _selectedDevice;
    private IList<ScanDevice> _knownDevices = [];
    private bool _disposed;

    public async Task InitialiseAsync()
    {
        _context = new ScanningContext(new WpfImageContext());
        _context.SetUpWin32Worker();
        _controller = new ScanController(_context);

        // Pre-warm: enumerate devices so the first GetDevicesAsync call is fast
        _knownDevices = await _controller.GetDeviceList(
            new NAPS2.Scan.ScanOptions { Driver = Driver.Wia });
        _selectedDevice = _knownDevices.FirstOrDefault();
    }

    public async Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        // CancellationToken not forwarded — NAPS2 GetDeviceList does not accept one
        _knownDevices = await _controller!.GetDeviceList(
            new NAPS2.Scan.ScanOptions { Driver = Driver.Wia });
        return _knownDevices.Select(d => d.Name).ToList();
    }

    public void SelectDevice(string? deviceName)
    {
        EnsureInitialised();
        _selectedDevice = deviceName == null
            ? null
            : _knownDevices.FirstOrDefault(d => d.Name == deviceName);
    }

    public async IAsyncEnumerable<ScannedPage> ScanBatchAsync(
        ScanOptions options,
        int batchNumber,
        string sessionDirectory,
        int startingPageIndex,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        Directory.CreateDirectory(sessionDirectory);

        var naps2Options = BuildNaps2Options(options, PaperSource.Feeder);
        var channel = System.Threading.Channels.Channel.CreateUnbounded<ScannedPage>();
        Exception? scanException = null;
        int index = startingPageIndex;

        var scanTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var image in _controller!.Scan(naps2Options, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var imagePath = Path.Combine(sessionDirectory, $"page_{index:D4}.png");
                    try { image.Save(imagePath, ImageFileFormat.Png, new ImageSaveOptions()); }
                    finally { image.Dispose(); }
                    await channel.Writer.WriteAsync(
                        new ScannedPage(imagePath, sourceBatch: batchNumber), cancellationToken);
                    index++;
                }
            }
            catch (OperationCanceledException) { }
            catch (ScannerException ex) { scanException = ex; }
            catch (Exception ex) { scanException = new ScannerException($"Scanner error: {ex.Message}", ex); }
            finally { channel.Writer.Complete(); }
        }, CancellationToken.None);

        await foreach (var page in channel.Reader.ReadAllAsync(cancellationToken))
            yield return page;

        await scanTask;
        if (scanException != null) throw scanException;
    }

    public async Task<ScannedPage> ScanSingleFlatbedAsync(
        ScanOptions options,
        int batchNumber,
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        Directory.CreateDirectory(sessionDirectory);

        var naps2Options = BuildNaps2Options(options, PaperSource.Flatbed);
        var imagePath = Path.Combine(sessionDirectory, $"replace_{pageIndex:D4}_{Guid.NewGuid():N}.png");

        try
        {
            await foreach (var image in _controller!.Scan(naps2Options, cancellationToken))
            {
                try
                {
                    image.Save(imagePath, ImageFileFormat.Png, new ImageSaveOptions());
                    return new ScannedPage(imagePath, sourceBatch: batchNumber);
                }
                finally { image.Dispose(); }
            }
            throw new ScannerException("Flatbed scan produced no image. Ensure a document is on the glass.");
        }
        catch (OperationCanceledException) { throw; }
        catch (ScannerException) { throw; }
        catch (Exception ex) { throw new ScannerException($"Scanner error: {ex.Message}", ex); }
    }

    private NAPS2.Scan.ScanOptions BuildNaps2Options(ScanOptions options, PaperSource source)
    {
        return new NAPS2.Scan.ScanOptions
        {
            Device = _selectedDevice,
            Driver = Driver.Wia,
            PaperSource = source,
            Dpi = options.Dpi,
            BitDepth = options.ColorMode switch
            {
                ColorMode.Color => BitDepth.Color,
                ColorMode.Grayscale => BitDepth.Grayscale,
                ColorMode.BlackAndWhite => BitDepth.BlackAndWhite,
                _ => BitDepth.Color
            },
            PageSize = options.PaperSize switch
            {
                PaperSize.Letter => PageSize.Letter,
                PaperSize.Legal => PageSize.Legal,
                PaperSize.AutoDetect => PageSize.Letter,
                _ => PageSize.Letter
            }
        };
    }

    private void EnsureInitialised()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Naps2ScannerBackend));
        if (_controller is null)
            throw new InvalidOperationException("Call InitialiseAsync before scanning.");
    }

    public void Dispose()
    {
        _disposed = true;
        _context?.Dispose();
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build PdfUtility.slnx -v q
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Run full test suite**

```bash
dotnet test PdfUtility.slnx -v q
```

Expected: all 27 tests pass (24 total from Task 1 + 3 new fake tests = 27).

- [ ] **Step 4: Commit**

```bash
git add src/PdfUtility.Scanning/Naps2ScannerBackend.cs
git commit -m "feat(scanning): implement GetDevicesAsync and SelectDevice in Naps2ScannerBackend"
```

---

## Task 3: Scanner Selection in ScanDoubleSidedViewModel

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`
- Modify: `tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

Add the following tests to `ScanDoubleSidedViewModelTests.cs` (inside the class, after the existing tests):

```csharp
// ── Scanner Selection Tests ───────────────────────────────────────

[Fact]
public void CanStartBatch1_FalseWhenNoDeviceSelected()
{
    var vm = CreateVm();
    Assert.Null(vm.SelectedDevice);
    Assert.False(vm.StartBatch1Command.CanExecute(null));
}

[Fact]
public void CanStartBatch1_TrueAfterDeviceSelected()
{
    var vm = CreateVm();
    vm.SelectedDevice = "Fake Scanner";
    Assert.True(vm.StartBatch1Command.CanExecute(null));
}

[Fact]
public void SelectedDevice_PropertySetter_CallsBackendSelectDevice()
{
    var fake = new FakeScannerBackend();
    var vm = CreateVm(fake);
    vm.SelectedDevice = "Fake Scanner";
    Assert.Equal("Fake Scanner", fake.SelectedDeviceName);
}

[Fact]
public async Task RefreshDevices_PopulatesAvailableDevices()
{
    var fake = new FakeScannerBackend();
    var vm = CreateVm(fake);
    await vm.RefreshDevicesCommand.ExecuteAsync(null);
    Assert.Equal(new[] { "Fake Scanner" }, vm.AvailableDevices);
}

[Fact]
public async Task RefreshDevices_AutoSelectsIfSingleDevice()
{
    var fake = new FakeScannerBackend();
    var vm = CreateVm(fake);
    await vm.RefreshDevicesCommand.ExecuteAsync(null);
    Assert.Equal("Fake Scanner", vm.SelectedDevice);
}

[Fact]
public async Task RefreshDevices_ClearsSelectionIfDeviceDisappears()
{
    var fake = new FakeScannerBackend();
    var vm = CreateVm(fake);
    vm.SelectedDevice = "Fake Scanner";
    fake.SimulatedDevices.Clear();
    await vm.RefreshDevicesCommand.ExecuteAsync(null);
    Assert.Null(vm.SelectedDevice);
}

[Fact]
public async Task RefreshDevices_SetsIsLoadingDevicesDuringEnumeration()
{
    var fake = new FakeScannerBackend();
    var gate = new TaskCompletionSource();
    fake.GetDevicesGate = gate;
    var vm = CreateVm(fake);

    var task = vm.RefreshDevicesCommand.ExecuteAsync(null);
    Assert.True(vm.IsLoadingDevices);
    Assert.False(vm.RefreshDevicesCommand.CanExecute(null));

    gate.SetResult();
    await task;
    Assert.False(vm.IsLoadingDevices);
}

[Fact]
public async Task RefreshDevices_ShowsErrorMessageOnFailure()
{
    var fake = new FakeScannerBackend { GetDevicesShouldFail = true };
    var vm = CreateVm(fake);
    await vm.RefreshDevicesCommand.ExecuteAsync(null);
    Assert.Null(vm.SelectedDevice);
    Assert.Contains("Could not enumerate scanners", vm.StatusMessage);
}
```

Also update the existing `InitialState_IsIdle` test — add `vm.SelectedDevice = "Fake Scanner";` before checking `CanExecute`:

```csharp
[Fact]
public void InitialState_IsIdle()
{
    var vm = CreateVm();
    Assert.Equal(ScanSessionState.Idle, vm.SessionState);
    // CanStartBatch1 requires a device to be selected
    Assert.False(vm.StartBatch1Command.CanExecute(null));
    vm.SelectedDevice = "Fake Scanner";
    Assert.True(vm.StartBatch1Command.CanExecute(null));
    Assert.False(vm.ContinueScanningCommand.CanExecute(null));
    Assert.False(vm.DoneBatch1Command.CanExecute(null));
    Assert.False(vm.ScanOtherSideCommand.CanExecute(null));
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/PdfUtility.App.Tests/ --filter "CanStartBatch1|RefreshDevices|SelectedDevice_PropertySetter" -v n
```

Expected: compile errors — `SelectedDevice`, `AvailableDevices`, `IsLoadingDevices`, `RefreshDevicesCommand` not defined yet.

- [ ] **Step 3: Update ScanDoubleSidedViewModel**

Add the following fields and property after the existing `[ObservableProperty] private ScanSessionState _sessionState` block, and update `CanStartBatch1`:

**New fields to add** (after the existing `[ObservableProperty] private bool _showMismatchWarning;` line):

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(StartBatch1Command))]
private string? _selectedDevice;

[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand))]
private bool _isLoadingDevices;

public ObservableCollection<string> AvailableDevices { get; } = new();
```

**Add partial method** for `OnSelectedDeviceChanged` (after the constructor):

```csharp
partial void OnSelectedDeviceChanged(string? value) => _scanner.SelectDevice(value);
```

**Update `CanStartBatch1`** — add `SelectedDevice != null` guard:

```csharp
private bool CanStartBatch1() => SessionState == ScanSessionState.Idle && SelectedDevice != null;
```

**Add `RefreshDevicesCommand`** (after `DoneCurrentBatch`):

```csharp
[RelayCommand(CanExecute = nameof(CanRefreshDevices))]
private async Task RefreshDevices()
{
    IsLoadingDevices = true;
    StatusMessage = "Scanning for devices…";
    AvailableDevices.Clear();
    try
    {
        var devices = await _scanner.GetDevicesAsync();
        foreach (var d in devices)
            AvailableDevices.Add(d);

        // Clear stale selection if previous device no longer found
        if (SelectedDevice != null && !devices.Contains(SelectedDevice))
            SelectedDevice = null;

        // Auto-select when exactly one device found
        if (devices.Count == 1)
            SelectedDevice = devices[0];

        StatusMessage = devices.Count == 0
            ? "No scanners found — check network connection and click ↻ to retry."
            : "Ready to scan.";
    }
    catch (Exception)
    {
        AvailableDevices.Clear();
        SelectedDevice = null;
        StatusMessage = "Could not enumerate scanners — check network connection and click ↻ to retry.";
    }
    finally
    {
        IsLoadingDevices = false;
    }
}
private bool CanRefreshDevices() => !IsLoadingDevices;
```

- [ ] **Step 4: Run the new tests**

```bash
dotnet test tests/PdfUtility.App.Tests/ --filter "CanStartBatch1|RefreshDevices|SelectedDevice_PropertySetter" -v n
```

Expected: all 8 new tests PASS.

- [ ] **Step 5: Run full test suite**

```bash
dotnet test PdfUtility.slnx -v q
```

Expected: all 35 tests pass (27 from Task 2 + 8 new = 35 total). `InitialState_IsIdle` should now pass with its updated assertion.

- [ ] **Step 6: Commit**

```bash
git add src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs
git commit -m "feat(app): add scanner selection — AvailableDevices, SelectedDevice, RefreshDevicesCommand"
```

---

## Task 4: Scanner Section in ScanDoubleSidedView XAML

**Files:**
- Modify: `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml`
- Modify: `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml.cs`

- [ ] **Step 1: Add SCANNER section to the left panel in ScanDoubleSidedView.xaml**

In `ScanDoubleSidedView.xaml`, insert the following block inside the left-panel `<StackPanel>`, **before** the `<!-- Batch 1 -->` comment:

```xml
<!-- Scanner Selection -->
<TextBlock Text="SCANNER" FontSize="11" FontWeight="SemiBold"
           Foreground="#0078D4" Margin="0,0,0,8"/>
<Grid Margin="0,0,0,16">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <ComboBox Grid.Column="0"
              ItemsSource="{Binding AvailableDevices}"
              SelectedItem="{Binding SelectedDevice, Mode=TwoWay}"
              Margin="0,0,4,0">
        <ComboBox.Style>
            <Style TargetType="ComboBox">
                <Setter Property="IsEnabled" Value="True"/>
                <Style.Triggers>
                    <!-- Disable while enumerating -->
                    <DataTrigger Binding="{Binding IsLoadingDevices}" Value="True">
                        <Setter Property="IsEnabled" Value="False"/>
                    </DataTrigger>
                    <!-- Disable when no scanners found (status bar shows explanation) -->
                    <DataTrigger Binding="{Binding AvailableDevices.Count}" Value="0">
                        <Setter Property="IsEnabled" Value="False"/>
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </ComboBox.Style>
    </ComboBox>
    <ui:Button Grid.Column="1" Content="↻"
               Command="{Binding RefreshDevicesCommand}"
               Width="32" ToolTip="Refresh scanner list"/>
</Grid>
```

- [ ] **Step 2: Add Loaded event to ScanDoubleSidedView.xaml.cs**

Replace the contents of `ScanDoubleSidedView.xaml.cs`:

```csharp
// src/PdfUtility.App/Views/ScanDoubleSidedView.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using PdfUtility.App.ViewModels;

namespace PdfUtility.App.Views;

public partial class ScanDoubleSidedView : System.Windows.Controls.UserControl
{
    public ScanDoubleSidedView()
    {
        InitializeComponent();
        if (System.Windows.Application.Current is App app)
        {
            var vm = app.Services.GetRequiredService<ScanDoubleSidedViewModel>();
            DataContext = vm;
            Loaded += (_, _) => vm.RefreshDevicesCommand.Execute(null);
        }
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build PdfUtility.slnx -v q
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Run full test suite**

```bash
dotnet test PdfUtility.slnx -v q
```

Expected: all 35 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PdfUtility.App/Views/ScanDoubleSidedView.xaml src/PdfUtility.App/Views/ScanDoubleSidedView.xaml.cs
git commit -m "feat(app): add SCANNER dropdown and refresh button to ScanDoubleSidedView"
```

---

## Task 5: Final Build + Publish

- [ ] **Step 1: Run full test suite**

```bash
dotnet test PdfUtility.slnx -v n
```

Expected: all 35 tests pass (10 Core + 4 Pdf + 21 App), 0 failures.

- [ ] **Step 2: Build release self-contained executable**

```bash
dotnet publish src/PdfUtility.App/PdfUtility.App.csproj -c Release -r win-x64 --self-contained
```

Expected: `Publish succeeded.`

- [ ] **Step 3: Final commit**

```bash
git add src/PdfUtility.App/ src/PdfUtility.Core/ src/PdfUtility.Scanning/ tests/
git commit -m "feat: scanner selection — pick network scanner from dropdown before scanning"
```

---

## What's Next

- Merge `feature/plan1-scan-double-sided` into `main`
- **Plan 2:** Merge Documents tab + Settings persistence (settings.json) + iText7 PDF/A + Windows.Data.Pdf rasterisation
- **Plan 3:** Inno Setup installer + startup orphan temp-file cleanup
