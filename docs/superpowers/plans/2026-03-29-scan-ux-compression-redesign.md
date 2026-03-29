# Scan UX, Page Reorder & PDF Compression Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Simplify the scan view to context-aware state-driven buttons, move scan settings into the sidebar, add scan mode (double/single/auto-detect blank pages), enable page reordering before merge, auto-detect per-page PDF dimensions, and apply JPEG compression for smaller PDFs.

**Architecture:** Seven sequential tasks — each builds on the previous and compiles cleanly: (1) new ScanMode enum; (2) strip toolbar settings from MainViewModel; (3) add those settings to ScanDoubleSidedViewModel; (4) update state machine + page reorder commands; (5) blank-page auto-detect in the scan loop; (6) full XAML rewrite of the scan sidebar; (7) JPEG compression in PdfSharpPdfBuilder.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm, PdfSharp 6.2.4, NAPS2.Sdk 1.2.1, xUnit

---

## File Map

| Action | File |
|--------|------|
| **Create** | `src/PdfUtility.Core/Models/ScanMode.cs` |
| **Modify** | `src/PdfUtility.App/ViewModels/MainViewModel.cs` |
| **Modify** | `src/PdfUtility.App/MainWindow.xaml` |
| **Modify** | `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs` |
| **Modify** | `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml` |
| **Modify** | `src/PdfUtility.Pdf/PdfUtility.Pdf.csproj` |
| **Modify** | `src/PdfUtility.Pdf/PdfSharpPdfBuilder.cs` |
| **Modify** | `tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs` |

---

## Task 1: Add ScanMode enum

**Files:**
- Create: `src/PdfUtility.Core/Models/ScanMode.cs`

- [ ] Create the file:

```csharp
namespace PdfUtility.Core.Models;

public enum ScanMode { DoubleSided, SingleSided, AutoDetect }
```

- [ ] Build to confirm no issues:

```
dotnet build src/PdfUtility.Core/PdfUtility.Core.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] Commit:

```
git add src/PdfUtility.Core/Models/ScanMode.cs
git commit -m "feat(core): add ScanMode enum (DoubleSided, SingleSided, AutoDetect)"
```

---

## Task 2: Strip toolbar settings from MainViewModel and MainWindow

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/MainViewModel.cs`
- Modify: `src/PdfUtility.App/MainWindow.xaml`

The toolbar currently has ComboBoxes for DPI, Color, PDF format, paper size, and a Settings button that does nothing. These move to the scan sidebar in Task 6. `MainViewModel` shrinks to just `SelectedTabIndex`.

- [ ] Replace `MainViewModel.cs` entirely with:

```csharp
// src/PdfUtility.App/ViewModels/MainViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfUtility.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _selectedTabIndex = 0;
}
```

- [ ] In `MainWindow.xaml`, replace the entire `<ui:TitleBar>` element (lines 17–38) with:

```xml
<ui:TitleBar Grid.Row="0" Title="PDF Utility"/>
```

The full TitleBar block being removed is everything between `<ui:TitleBar Grid.Row="0" Title="PDF Utility">` and its closing `</ui:TitleBar>` tag — the StackPanel containing the DPI/Color/PDF/Size ComboBoxes and the ⚙ Settings button.

- [ ] Build to confirm no compilation errors:

```
dotnet build src/PdfUtility.App/PdfUtility.App.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] Commit:

```
git add src/PdfUtility.App/ViewModels/MainViewModel.cs src/PdfUtility.App/MainWindow.xaml
git commit -m "refactor(app): remove scan settings from toolbar; strip MainViewModel to tab index only"
```

---

## Task 3: Move scan settings into ScanDoubleSidedViewModel

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`

The ViewModel currently reads settings from `MainViewModel` (injected in the constructor) via `BuildCurrentScanOptions()`. Remove that dependency. Add observable properties for each setting directly on the ViewModel. Keep `IUserSettings` — still used for `DefaultSaveFolder` in `MergeDocument()`.

- [ ] **Remove** the `MainViewModel` constructor parameter and backing field. The constructor signature becomes:

```csharp
public ScanDoubleSidedViewModel(
    IScannerBackend scanner,
    IPdfBuilder pdfBuilder,
    IUserSettings userSettings)
{
    _scanner = scanner;
    _pdfBuilder = pdfBuilder;
    _userSettings = userSettings;
}
```

- [ ] **Add** the following observable fields after the `_sessionDirectory` field declaration:

```csharp
// ── Scan settings (moved from MainViewModel toolbar) ─────────────────
[ObservableProperty] private int _scanDpi = 300;
[ObservableProperty] private ColorMode _colorMode = ColorMode.Color;
[ObservableProperty] private PaperSize _paperSize = PaperSize.Letter;
[ObservableProperty] private PdfFormat _pdfFormat = PdfFormat.Standard;
[ObservableProperty] private ScanMode _scanMode = ScanMode.DoubleSided;

public int[] DpiOptions { get; } = [150, 300, 600];
public ColorMode[] ColorModeOptions { get; } = Enum.GetValues<ColorMode>();
public PaperSize[] PaperSizeOptions { get; } = [PaperSize.Letter, PaperSize.Legal];
public PdfFormat[] PdfFormatOptions { get; } = Enum.GetValues<PdfFormat>();
public ScanMode[] ScanModeOptions { get; } = Enum.GetValues<ScanMode>();
```

- [ ] **Update** `BuildCurrentScanOptions()` — remove `_mainViewModel` references:

```csharp
private ScanOptions BuildCurrentScanOptions() => new()
{
    Dpi = ScanDpi,
    ColorMode = ColorMode,
    PaperSize = PaperSize
};
```

- [ ] **Update** `MergeDocument()` — replace `prefs.PdfFormat` and `prefs.PaperSize` with ViewModel properties. Keep `prefs.DefaultSaveFolder`. The relevant block becomes:

```csharp
var prefs = _userSettings.Load();
if (!string.IsNullOrEmpty(prefs.DefaultSaveFolder))
    dialog.InitialDirectory = prefs.DefaultSaveFolder;

// ... (ShowDialog check) ...

var mergedPages = GetMergedPages();
var options = new PdfBuildOptions
{
    Format = PdfFormat,               // from ViewModel property
    JpegQuality = 85,
    PaperSize = PaperSize.AutoDetect  // each page keeps its own physical dimensions
};
```

- [ ] Add `using PdfUtility.Core.Models;` at the top if `ScanMode` is not already resolved (it should be via the existing `using PdfUtility.Core.Models;` directive).

- [ ] Build:

```
dotnet build src/PdfUtility.App/PdfUtility.App.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] Commit:

```
git add src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs
git commit -m "feat(scan): move scan settings (DPI/color/size/format/mode) into ScanDoubleSidedViewModel"
```

---

## Task 4: Update state machine, add page reorder, add computed visibility properties

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`
- Modify: `tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs`

### 4a: Write the new tests first

The following tests cover the new SingleSided mode and page reorder commands. Add them to `ScanDoubleSidedViewModelTests.cs` at the end of the class:

```csharp
[Fact]
public async Task DoneBatch1_SingleSidedMode_TransitionsDirectlyToMergeReady()
{
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(["p1.png", "p2.png"]);
    var vm = CreateVm(fake);
    vm.SelectedDevice = "Fake Scanner";
    vm.ScanMode = ScanMode.SingleSided;
    await vm.StartBatch1Command.ExecuteAsync(null);

    await vm.DoneBatch1Command.ExecuteAsync(null);

    Assert.Equal(ScanSessionState.MergeReady, vm.SessionState);
    // Thumbnails rebuilt in merged order (just batch 1 pages for single-sided)
    Assert.Equal(2, vm.Thumbnails.Count);
}

[Fact]
public async Task DoneBatch1_DoubleSidedMode_TransitionsToBatch1Complete()
{
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(["p1.png"]);
    var vm = CreateVm(fake);
    vm.SelectedDevice = "Fake Scanner";
    vm.ScanMode = ScanMode.DoubleSided;
    await vm.StartBatch1Command.ExecuteAsync(null);

    await vm.DoneBatch1Command.ExecuteAsync(null);

    Assert.Equal(ScanSessionState.Batch1Complete, vm.SessionState);
}

[Fact]
public async Task DoneBatch1_AutoDetectMode_TransitionsToBatch1Complete()
{
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(["p1.png"]);
    var vm = CreateVm(fake);
    vm.SelectedDevice = "Fake Scanner";
    vm.ScanMode = ScanMode.AutoDetect;
    await vm.StartBatch1Command.ExecuteAsync(null);

    await vm.DoneBatch1Command.ExecuteAsync(null);

    Assert.Equal(ScanSessionState.Batch1Complete, vm.SessionState);
}

[Fact]
public async Task MovePageToBeginning_MovesLastPageToFirst()
{
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(["p1.png"]);
    fake.BatchQueue.Enqueue(["p2.png"]);
    var vm = CreateVm(fake);
    vm.SelectedDevice = "Fake Scanner";
    vm.ScanMode = ScanMode.DoubleSided;
    await vm.StartBatch1Command.ExecuteAsync(null);
    await vm.DoneBatch1Command.ExecuteAsync(null);
    await vm.ScanOtherSideCommand.ExecuteAsync(null);
    await vm.DoneBatch2Command.ExecuteAsync(null);
    // Now in MergeReady with 2 thumbnails
    Assert.Equal(ScanSessionState.MergeReady, vm.SessionState);
    var last = vm.Thumbnails[^1];

    vm.MovePageToBeginningCommand.Execute(last);

    Assert.Equal(last, vm.Thumbnails[0]);
    Assert.Equal(1, vm.Thumbnails[0].PageNumber);
}

[Fact]
public async Task MovePageToEnd_MovesFirstPageToLast()
{
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(["p1.png"]);
    fake.BatchQueue.Enqueue(["p2.png"]);
    var vm = CreateVm(fake);
    vm.SelectedDevice = "Fake Scanner";
    vm.ScanMode = ScanMode.DoubleSided;
    await vm.StartBatch1Command.ExecuteAsync(null);
    await vm.DoneBatch1Command.ExecuteAsync(null);
    await vm.ScanOtherSideCommand.ExecuteAsync(null);
    await vm.DoneBatch2Command.ExecuteAsync(null);
    var first = vm.Thumbnails[0];

    vm.MovePageToEndCommand.Execute(first);

    Assert.Equal(first, vm.Thumbnails[^1]);
    Assert.Equal(vm.Thumbnails.Count, vm.Thumbnails[^1].PageNumber);
}

[Fact]
public async Task RemoveScanPage_RemovesPageAndRenumbers()
{
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(["p1.png"]);
    fake.BatchQueue.Enqueue(["p2.png"]);
    var vm = CreateVm(fake);
    vm.SelectedDevice = "Fake Scanner";
    vm.ScanMode = ScanMode.DoubleSided;
    await vm.StartBatch1Command.ExecuteAsync(null);
    await vm.DoneBatch1Command.ExecuteAsync(null);
    await vm.ScanOtherSideCommand.ExecuteAsync(null);
    await vm.DoneBatch2Command.ExecuteAsync(null);
    int originalCount = vm.Thumbnails.Count;
    var toRemove = vm.Thumbnails[0];

    vm.RemoveScanPageCommand.Execute(toRemove);

    Assert.Equal(originalCount - 1, vm.Thumbnails.Count);
    Assert.Equal(1, vm.Thumbnails[0].PageNumber); // renumbered
}
```

- [ ] Run the new tests to confirm they fail (ViewModel doesn't have these members yet):

```
dotnet test tests/PdfUtility.App.Tests/ --filter "DoneBatch1_SingleSidedMode|MovePageToBeginning|MovePageToEnd|RemoveScanPage|DoubleSidedMode|AutoDetectMode"
```

Expected: compilation error or test failures — that's correct, the methods don't exist yet.

### 4b: Update existing tests that rely on DoneBatch1 → Batch1Complete

These tests assume `ScanMode == DoubleSided` (the default). Add an explicit assignment to document intent. Modify these three tests:

- `DoneBatch1_TransitionsToBatch1Complete` (line ~54): add `vm.ScanMode = ScanMode.DoubleSided;` before the `await vm.StartBatch1Command` call.
- `DoneCurrentBatch_InBatch1Error_TransitionsToBatch1Complete` (line ~155): same, add after `vm.SelectedDevice = "Fake Scanner";`.
- `MergedPages_Batch1AndBatch2_HaveDistinctSubdirectories` (line ~171): add after `vm.SelectedDevice = "Fake Scanner";`.
- `MergedPages_TwoSheets_DoesNotDuplicateImages` (line ~197): add after `vm.SelectedDevice = "Fake Scanner";`.

### 4c: Implement DoneBatch1 changes

Replace the existing `DoneBatch1()` method body:

```csharp
[RelayCommand(CanExecute = nameof(CanDoneBatch1))]
private Task DoneBatch1()
{
    if (ScanMode == ScanMode.SingleSided)
    {
        BuildMergedThumbnails();
        SessionState = ScanSessionState.MergeReady;
        StatusMessage = $"Single-sided scan complete ({_session.Batch1.Count} pages). Ready to merge.";
    }
    else  // DoubleSided or AutoDetect — both require batch 2
    {
        SessionState = ScanSessionState.Batch1Complete;
        StatusMessage = $"Batch 1 complete ({_session.Batch1.Count} pages). Now scan the other side.";
    }
    return Task.CompletedTask;
}
// CanDoneBatch1 stays unchanged
```

### 4d: Implement DoneBatch2 changes

Replace the existing `DoneBatch2()` method body — call `BuildMergedThumbnails()` before the state transition. Remove the intermediate `Batch2Complete` state (it goes straight to `MergeReady`):

```csharp
[RelayCommand(CanExecute = nameof(CanDoneBatch2))]
private Task DoneBatch2()
{
    BuildMergedThumbnails();
    SessionState = ScanSessionState.MergeReady;
    StatusMessage = $"Batch 2 complete ({_session.Batch2.Count} pages). Ready to merge.";

    if (_session.HasPageCountMismatch)
    {
        ShowMismatchWarning = true;
        MismatchWarningText =
            $"⚠ Batch mismatch: {_session.Batch1.Count} vs {_session.Batch2.Count} pages — " +
            "extra pages will be appended at the end.";
    }
    return Task.CompletedTask;
}
// CanDoneBatch2 stays unchanged
```

### 4e: Add BuildMergedThumbnails helper

Add this private method after `GetMergedPages()`:

```csharp
private void BuildMergedThumbnails()
{
    var merged = _session.BuildMergedPages();
    Thumbnails.Clear();
    for (int i = 0; i < merged.Count; i++)
    {
        Thumbnails.Add(new PageThumbnailViewModel
        {
            ImagePath = merged[i].ImagePath,
            PageNumber = i + 1,
            SourceLabel = merged[i].SourceBatch == 1 ? "Front" : "Back",
            ScannedPage = merged[i]
        });
    }
}
```

### 4f: Add page reorder commands and renumber helper

Add these after the existing `ReplacePage` command:

```csharp
[RelayCommand]
private void MovePageToBeginning(PageThumbnailViewModel thumb)
{
    int idx = Thumbnails.IndexOf(thumb);
    if (idx <= 0) return;
    Thumbnails.Move(idx, 0);
    RenumberThumbnails();
}

[RelayCommand]
private void MovePageToEnd(PageThumbnailViewModel thumb)
{
    int idx = Thumbnails.IndexOf(thumb);
    if (idx < 0 || idx == Thumbnails.Count - 1) return;
    Thumbnails.Move(idx, Thumbnails.Count - 1);
    RenumberThumbnails();
}

[RelayCommand]
private void RemoveScanPage(PageThumbnailViewModel thumb)
{
    int idx = Thumbnails.IndexOf(thumb);
    if (idx < 0) return;
    Thumbnails.RemoveAt(idx);
    RenumberThumbnails();
}

private void RenumberThumbnails()
{
    for (int i = 0; i < Thumbnails.Count; i++)
        Thumbnails[i].PageNumber = i + 1;
}
```

### 4g: Update MergeDocument to use Thumbnails order

In `MergeDocument()`, replace:
```csharp
var mergedPages = GetMergedPages();
```
with:
```csharp
var mergedPages = Thumbnails
    .Where(t => t.ScannedPage != null)
    .Select(t => t.ScannedPage!)
    .ToList();
```

Add `using System.Linq;` at the top if not already present.

### 4h: Add computed visibility properties + wire NotifyPropertyChangedFor

The `_sessionState` field already has a list of `[NotifyPropertyChangedFor]` and `[NotifyCanExecuteChangedFor]` attributes. Add the new properties to that list.

**New computed properties** — add after `IsBatch1Complete`:

```csharp
// ── XAML visibility helpers ───────────────────────────────────────────
// Settings section: only shown when Idle (locked during an active session)
public bool IsIdle => SessionState == ScanSessionState.Idle;

// Batch 1 action buttons (Continue + Done Scanning Front)
public bool IsBatch1Actionable =>
    SessionState is ScanSessionState.Batch1Paused
                 or ScanSessionState.Batch1Error;

// Batch 2 action buttons (Continue + Done Scanning Back)
public bool IsBatch2Actionable =>
    SessionState is ScanSessionState.Batch2Paused
                 or ScanSessionState.Batch2Error;

// Merge button
public bool IsMergeReady => SessionState == ScanSessionState.MergeReady;
```

**Update the `[NotifyPropertyChangedFor]` list on `_sessionState`** to include all new properties. The complete attribute block on `_sessionState` should be:

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(StartBatch1Command))]
[NotifyCanExecuteChangedFor(nameof(ContinueScanningCommand))]
[NotifyCanExecuteChangedFor(nameof(DoneBatch1Command))]
[NotifyCanExecuteChangedFor(nameof(ScanOtherSideCommand))]
[NotifyCanExecuteChangedFor(nameof(DoneBatch2Command))]
[NotifyCanExecuteChangedFor(nameof(MergeDocumentCommand))]
[NotifyCanExecuteChangedFor(nameof(DiscardSessionCommand))]
[NotifyCanExecuteChangedFor(nameof(RescanLastPageCommand))]
[NotifyCanExecuteChangedFor(nameof(DoneCurrentBatchCommand))]
[NotifyPropertyChangedFor(nameof(IsBatch1Complete))]
[NotifyPropertyChangedFor(nameof(IsIdle))]
[NotifyPropertyChangedFor(nameof(IsBatch1Actionable))]
[NotifyPropertyChangedFor(nameof(IsBatch2Actionable))]
[NotifyPropertyChangedFor(nameof(IsMergeReady))]
private ScanSessionState _sessionState = ScanSessionState.Idle;
```

- [ ] Build:

```
dotnet build src/PdfUtility.App/PdfUtility.App.csproj
```

Expected: `Build succeeded.`

- [ ] Run all scan ViewModel tests:

```
dotnet test tests/PdfUtility.App.Tests/ --filter "ScanDoubleSidedViewModelTests"
```

Expected: all pass, including the 6 new tests.

- [ ] Commit:

```
git add src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs
git add tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs
git commit -m "feat(scan): single-sided mode, page reorder commands, merged thumbnail order, visibility properties"
```

---

## Task 5: Blank page auto-detect

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`

In `AutoDetect` scan mode, each scanned page PNG is checked for blankness immediately after receipt. Blank pages are silently dropped before being added to the batch.

### 5a: Add IsBlankPage helper

Add this static method to the ViewModel class (it uses `System.Windows.Media.Imaging` which is available since the project targets `net10.0-windows`):

```csharp
/// <summary>
/// Returns true if the image at <paramref name="imagePath"/> is effectively blank
/// (mean pixel brightness ≥ <paramref name="threshold"/> out of 255).
/// Threshold 240 ≈ 94% white — catches empty pages from ADF double-feed.
/// </summary>
private static bool IsBlankPage(string imagePath, byte threshold = 240)
{
    try
    {
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        int stride = bmp.PixelWidth * 4; // BGRA
        var pixels = new byte[bmp.PixelHeight * stride];
        bmp.CopyPixels(pixels, stride, 0);

        long sum = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            sum += (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;

        return (sum / (pixels.Length / 4)) >= threshold;
    }
    catch { return false; } // if image unreadable, don't skip
}
```

### 5b: Update RunScanBatchAsync to check blank pages

In `RunScanBatchAsync`, the inner scan loop currently looks like:

```csharp
await foreach (var page in _scanner.ScanBatchAsync(
    BuildCurrentScanOptions(), targetBatch, _sessionDirectory, startIndex))
{
    // page.SourceBatch is already set correctly by the scanner
    batch.Add(page);
    var thumb = new PageThumbnailViewModel { ... };
    // dispatcher add to Thumbnails
    StatusMessage = $"Scanning page {batch.Count}… (ADF feeding)";
}
```

Replace it with the blank-check pattern (check BEFORE adding to batch):

```csharp
await foreach (var page in _scanner.ScanBatchAsync(
    BuildCurrentScanOptions(), targetBatch, _sessionDirectory, startIndex))
{
    cancellationToken.ThrowIfCancellationRequested();

    // AutoDetect: silently skip blank pages (mean brightness ≥ 240/255)
    if (ScanMode == ScanMode.AutoDetect && IsBlankPage(page.ImagePath))
    {
        try { File.Delete(page.ImagePath); } catch { }
        continue;
    }

    batch.Add(page);

    var thumb = new PageThumbnailViewModel
    {
        ImagePath = page.ImagePath,
        PageNumber = batch.Count,
        SourceLabel = targetBatch == 1 ? "Front" : "Back",
        ScannedPage = page
    };

    var dispatcher = System.Windows.Application.Current?.Dispatcher;
    if (dispatcher != null)
        await dispatcher.InvokeAsync(() => Thumbnails.Add(thumb));
    else
        Thumbnails.Add(thumb);

    StatusMessage = $"Scanning page {batch.Count}… (ADF feeding)";
}
```

Note: `continue` is valid inside `await foreach`. The `cancellationToken.ThrowIfCancellationRequested()` at the top of the loop replaces the one that was previously inside.

- [ ] Implement `IsBlankPage` static helper
- [ ] Update `RunScanBatchAsync` loop body

- [ ] Build:

```
dotnet build src/PdfUtility.App/PdfUtility.App.csproj
```

Expected: `Build succeeded.`

- [ ] Run tests to confirm nothing broken:

```
dotnet test tests/PdfUtility.App.Tests/ --filter "ScanDoubleSidedViewModelTests"
```

Expected: all pass.

- [ ] Commit:

```
git add src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs
git commit -m "feat(scan): auto-detect blank pages; silently skip in AutoDetect mode"
```

---

## Task 6: Rewrite ScanDoubleSidedView.xaml

**Files:**
- Modify: `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml`

The existing sidebar shows all buttons always, relying on `CanExecute` for enable/disable state. Replace it with context-aware sections that show/hide entirely based on the computed properties added in Task 4. Add the scan settings ComboBoxes (DPI, Color, Size, PDF Format, Scan Mode) to the idle section.

Replace the entire file content with:

```xml
<!-- src/PdfUtility.App/Views/ScanDoubleSidedView.xaml -->
<UserControl x:Class="PdfUtility.App.Views.ScanDoubleSidedView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             xmlns:controls="clr-namespace:PdfUtility.App.Views.Controls"
             xmlns:models="clr-namespace:PdfUtility.Core.Models;assembly=PdfUtility.Core">

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="230"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- ── Left: dark sidebar ── -->
        <Border Grid.Column="0" Background="{StaticResource AppSidebarBg}" Padding="12">
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel>

                    <!-- ── IDLE: scanner selection + settings + start ── -->
                    <StackPanel Visibility="{Binding IsIdle, Converter={StaticResource BoolToVisibility}}">

                        <TextBlock Text="SCANNER" Style="{StaticResource SidebarLabel}"/>
                        <Grid Margin="0,0,0,8">
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
                                            <DataTrigger Binding="{Binding IsLoadingDevices}" Value="True">
                                                <Setter Property="IsEnabled" Value="False"/>
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding AvailableDevices.Count}" Value="0">
                                                <Setter Property="IsEnabled" Value="False"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </ComboBox.Style>
                            </ComboBox>
                            <Button Grid.Column="1" Content="↻"
                                    Command="{Binding RefreshDevicesCommand}"
                                    Style="{StaticResource SidebarButton}"
                                    Width="32" ToolTip="Refresh scanner list"/>
                        </Grid>

                        <TextBlock Text="SCAN MODE" Style="{StaticResource SidebarLabel}"/>
                        <ComboBox ItemsSource="{Binding ScanModeOptions}"
                                  SelectedItem="{Binding ScanMode, Mode=TwoWay}"
                                  Margin="0,0,0,8"/>

                        <TextBlock Text="DPI" Style="{StaticResource SidebarLabel}"/>
                        <ComboBox ItemsSource="{Binding DpiOptions}"
                                  SelectedItem="{Binding ScanDpi, Mode=TwoWay}"
                                  Margin="0,0,0,8"/>

                        <TextBlock Text="COLOR" Style="{StaticResource SidebarLabel}"/>
                        <ComboBox ItemsSource="{Binding ColorModeOptions}"
                                  SelectedItem="{Binding ColorMode, Mode=TwoWay}"
                                  Margin="0,0,0,8"/>

                        <TextBlock Text="SIZE" Style="{StaticResource SidebarLabel}"/>
                        <ComboBox ItemsSource="{Binding PaperSizeOptions}"
                                  SelectedItem="{Binding PaperSize, Mode=TwoWay}"
                                  Margin="0,0,0,8"/>

                        <TextBlock Text="PDF FORMAT" Style="{StaticResource SidebarLabel}"/>
                        <ComboBox ItemsSource="{Binding PdfFormatOptions}"
                                  SelectedItem="{Binding PdfFormat, Mode=TwoWay}"
                                  Margin="0,0,0,12"/>

                        <Button Content="▶  Start Scanning"
                                Style="{StaticResource AccentButton}"
                                Command="{Binding StartBatch1Command}"
                                HorizontalAlignment="Stretch"/>
                    </StackPanel>

                    <!-- ── BATCH 1 ACTIONS (Paused or Error) ── -->
                    <StackPanel Visibility="{Binding IsBatch1Actionable, Converter={StaticResource BoolToVisibility}}">
                        <TextBlock Text="BATCH 1 — FRONT SIDES" Style="{StaticResource SidebarLabel}"/>
                        <Button Content="⊕  Continue Scanning"
                                Style="{StaticResource SidebarButton}"
                                Command="{Binding ContinueScanningCommand}"
                                Margin="0,0,0,4"/>
                        <Button Content="✓  Done Scanning Front"
                                Style="{StaticResource SidebarButton}"
                                Command="{Binding DoneBatch1Command}"/>
                    </StackPanel>

                    <!-- ── SCAN BACK SIDE (Batch1Complete) ── -->
                    <StackPanel>
                        <StackPanel.Style>
                            <Style TargetType="StackPanel">
                                <Setter Property="Visibility" Value="Collapsed"/>
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding SessionState}"
                                                 Value="{x:Static models:ScanSessionState.Batch1Complete}">
                                        <Setter Property="Visibility" Value="Visible"/>
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </StackPanel.Style>
                        <TextBlock Text="BATCH 2 — BACK SIDES" Style="{StaticResource SidebarLabel}"/>
                        <Button Content="▶  Scan Back Side"
                                Style="{StaticResource AccentButton}"
                                Command="{Binding ScanOtherSideCommand}"
                                HorizontalAlignment="Stretch"/>
                    </StackPanel>

                    <!-- ── BATCH 2 ACTIONS (Paused or Error) ── -->
                    <StackPanel Visibility="{Binding IsBatch2Actionable, Converter={StaticResource BoolToVisibility}}">
                        <TextBlock Text="BATCH 2 — BACK SIDES" Style="{StaticResource SidebarLabel}"/>
                        <Button Content="⊕  Continue Scanning"
                                Style="{StaticResource SidebarButton}"
                                Command="{Binding ContinueScanningCommand}"
                                Margin="0,0,0,4"/>
                        <Button Content="✓  Done Scanning Back"
                                Style="{StaticResource SidebarButton}"
                                Command="{Binding DoneBatch2Command}"/>
                    </StackPanel>

                    <!-- ── MERGE READY ── -->
                    <StackPanel Visibility="{Binding IsMergeReady, Converter={StaticResource BoolToVisibility}}">
                        <Button Content="⇄  Merge Document"
                                Style="{StaticResource AccentButton}"
                                Command="{Binding MergeDocumentCommand}"
                                HorizontalAlignment="Stretch"/>
                    </StackPanel>

                    <!-- ── ERROR RECOVERY BANNER ── -->
                    <Border Background="#3A1A00" CornerRadius="6" Padding="10" Margin="0,12,0,0"
                            Visibility="{Binding ShowErrorBanner, Converter={StaticResource BoolToVisibility}}">
                        <StackPanel>
                            <TextBlock Text="{Binding ErrorMessage}" TextWrapping="Wrap"
                                       Foreground="#FFB74D" FontSize="11" Margin="0,0,0,8"/>
                            <Button Content="▶  Continue Scanning"
                                    Style="{StaticResource SidebarButton}"
                                    Command="{Binding ContinueScanningCommand}" Margin="0,0,0,4"/>
                            <Button Content="↩  Rescan Last Page"
                                    Style="{StaticResource SidebarButton}"
                                    Command="{Binding RescanLastPageCommand}" Margin="0,0,0,4"/>
                            <Button Content="✓  Done with Batch"
                                    Style="{StaticResource SidebarButton}"
                                    Command="{Binding DoneCurrentBatchCommand}"/>
                        </StackPanel>
                    </Border>

                    <!-- ── MISMATCH WARNING ── -->
                    <Border Background="#3A2800" CornerRadius="6" Padding="10" Margin="0,8,0,0"
                            Visibility="{Binding ShowMismatchWarning, Converter={StaticResource BoolToVisibility}}">
                        <TextBlock Text="{Binding MismatchWarningText}" TextWrapping="Wrap"
                                   Foreground="#FFD54F" FontSize="11"/>
                    </Border>

                    <!-- ── DISCARD (visible whenever session is active, i.e. not Idle) ── -->
                    <Button Content="🗑  Discard Session"
                            Style="{StaticResource DangerButton}"
                            Command="{Binding DiscardSessionCommand}"
                            Margin="0,16,0,0">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource DangerButton}">
                                <Setter Property="Visibility" Value="Collapsed"/>
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsIdle}" Value="False">
                                        <Setter Property="Visibility" Value="Visible"/>
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                    </Button>

                </StackPanel>
            </ScrollViewer>
        </Border>

        <!-- ── Right: canvas + preview ── -->
        <Grid Grid.Column="1" Background="{StaticResource AppCanvasBg}">
            <Grid.RowDefinitions>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <Grid Grid.Row="0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>

                <!-- Narrow thumbnail strip (when preview is open) -->
                <Border Grid.Column="0"
                        BorderBrush="#DDDDDD" BorderThickness="0,0,1,0"
                        Visibility="{Binding IsPreviewOpen, Converter={StaticResource BoolToVisibility}}">
                    <ScrollViewer Width="110" VerticalScrollBarVisibility="Auto" Padding="4">
                        <ItemsControl ItemsSource="{Binding Thumbnails}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Button Background="Transparent" BorderThickness="0" Padding="0" Cursor="Hand"
                                            Command="{Binding DataContext.PreviewPageCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                            CommandParameter="{Binding}">
                                        <controls:PageThumbnailControl DataContext="{Binding}" ShowReplaceLink="False"/>
                                    </Button>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </Border>

                <!-- WrapPanel thumbnails with right-click reorder (no preview open) -->
                <ScrollViewer Grid.Column="1" VerticalScrollBarVisibility="Auto" Padding="12"
                              Visibility="{Binding IsNotPreviewOpen, Converter={StaticResource BoolToVisibility}}">
                    <ItemsControl ItemsSource="{Binding Thumbnails}">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Background="Transparent" BorderThickness="0" Padding="0" Cursor="Hand"
                                        Command="{Binding DataContext.PreviewPageCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                        CommandParameter="{Binding}"
                                        Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=ItemsControl}}">
                                    <Button.ContextMenu>
                                        <ContextMenu>
                                            <MenuItem Header="Move to Beginning"
                                                      Command="{Binding PlacementTarget.Tag.MovePageToBeginningCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                                                      CommandParameter="{Binding}"/>
                                            <MenuItem Header="Move to End"
                                                      Command="{Binding PlacementTarget.Tag.MovePageToEndCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                                                      CommandParameter="{Binding}"/>
                                            <Separator/>
                                            <MenuItem Header="Remove Page"
                                                      Command="{Binding PlacementTarget.Tag.RemoveScanPageCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                                                      CommandParameter="{Binding}"/>
                                        </ContextMenu>
                                    </Button.ContextMenu>
                                    <controls:PageThumbnailControl DataContext="{Binding}" ShowReplaceLink="False"/>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>

                <!-- Full-page preview -->
                <Grid Grid.Column="1"
                      Visibility="{Binding IsPreviewOpen, Converter={StaticResource BoolToVisibility}}">
                    <Button Content="✕  Close Preview"
                            Command="{Binding ClosePreviewCommand}"
                            HorizontalAlignment="Right" VerticalAlignment="Top"
                            Margin="8" Padding="8,4" Cursor="Hand"
                            Panel.ZIndex="1"/>
                    <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto"
                                  Margin="0,36,0,0">
                        <Image Source="{Binding SelectedThumbnail.ImagePath, Converter={StaticResource PathToBitmap}, ConverterParameter=1200}"
                               Stretch="Uniform" Margin="16"
                               RenderOptions.BitmapScalingMode="HighQuality"/>
                    </ScrollViewer>
                </Grid>
            </Grid>

            <!-- Status bar -->
            <Border Grid.Row="1" Background="#2D2D2D" Padding="12,6">
                <TextBlock Text="{Binding StatusMessage}" Foreground="#E0E0E0" FontSize="12"/>
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] Replace the file
- [ ] Build:

```
dotnet build src/PdfUtility.App/PdfUtility.App.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If there are XAML MC3024 or similar errors, the most likely cause is the `BasedOn` style for the Discard button — if `DangerButton` style doesn't set all needed properties, remove the `BasedOn` and just use `Style="{StaticResource DangerButton}"` directly with `Visibility` set via a separate trigger approach.

- [ ] Commit:

```
git add src/PdfUtility.App/Views/ScanDoubleSidedView.xaml
git commit -m "feat(scan): context-aware sidebar — settings in idle, state-driven action sections, page reorder context menu"
```

---

## Task 7: JPEG compression in PdfSharpPdfBuilder

**Files:**
- Modify: `src/PdfUtility.Pdf/PdfUtility.Pdf.csproj`
- Modify: `src/PdfUtility.Pdf/PdfSharpPdfBuilder.cs`

PNGs are currently embedded with FlateDecode (lossless). Converting them to JPEG before embedding gives DCTDecode (JPEG filter), which is 5–10× smaller for color scans and supported by every PDF reader since Acrobat 1.0. Quality is controlled by the existing `JpegQuality` setting in `PdfBuildOptions`.

### 7a: Enable WPF in the PDF project

In `src/PdfUtility.Pdf/PdfUtility.Pdf.csproj`, add `<UseWPF>true</UseWPF>` inside the existing `<PropertyGroup>`:

```xml
<PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
</PropertyGroup>
```

### 7b: Add ConvertPngToJpeg helper

Add this static helper method to `PdfSharpPdfBuilder`:

```csharp
using System.Windows.Media.Imaging;

private static void ConvertPngToJpeg(string pngPath, string jpegPath, int quality)
{
    var bmp = new BitmapImage();
    bmp.BeginInit();
    bmp.UriSource = new Uri(pngPath, UriKind.Absolute);
    bmp.CacheOption = BitmapCacheOption.OnLoad;
    bmp.EndInit();
    bmp.Freeze(); // releases the file handle

    var encoder = new JpegBitmapEncoder { QualityLevel = quality };
    encoder.Frames.Add(BitmapFrame.Create(bmp));
    using var fs = File.Create(jpegPath);
    encoder.Save(fs);
}
```

### 7c: Update BuildAsync to use JPEG conversion

In `BuildAsync`, the current inner loop body has:
```csharp
using var xImage = XImage.FromFile(source.ImagePath);
```

Replace the entire inner loop body with:

```csharp
string? tempJpeg = null;
try
{
    tempJpeg = Path.Combine(
        Path.GetTempPath(),
        $"pdfutility_jpeg_{Guid.NewGuid():N}.jpg");
    ConvertPngToJpeg(source.ImagePath, tempJpeg, options.JpegQuality);

    using var xImage = XImage.FromFile(tempJpeg);

    double drawW = options.PaperSize == PaperSize.AutoDetect
        ? xImage.PointWidth
        : PaperWidthPt(options.PaperSize);
    double drawH = options.PaperSize == PaperSize.AutoDetect
        ? xImage.PointHeight
        : PaperHeightPt(options.PaperSize);

    var page = document.AddPage();

    bool isLandscape = source.Rotation == PageRotation.CW90 || source.Rotation == PageRotation.CW270;
    page.Width  = XUnit.FromPoint(isLandscape ? drawH : drawW);
    page.Height = XUnit.FromPoint(isLandscape ? drawW : drawH);

    using var gfx = XGraphics.FromPdfPage(page);
    switch (source.Rotation)
    {
        case PageRotation.None:
            gfx.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
            break;
        case PageRotation.CW90:
            gfx.TranslateTransform(page.Width.Point, 0);
            gfx.RotateAtTransform(90, new XPoint(0, 0));
            gfx.DrawImage(xImage, 0, 0, drawW, drawH);
            break;
        case PageRotation.CW180:
            gfx.TranslateTransform(page.Width.Point, page.Height.Point);
            gfx.RotateAtTransform(180, new XPoint(0, 0));
            gfx.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
            break;
        case PageRotation.CW270:
            gfx.TranslateTransform(0, page.Height.Point);
            gfx.RotateAtTransform(270, new XPoint(0, 0));
            gfx.DrawImage(xImage, 0, 0, drawW, drawH);
            break;
    }
}
finally
{
    if (tempJpeg != null)
        try { File.Delete(tempJpeg); } catch { }
}
```

Also remove the old TODO comment:
```
// TODO: Apply options.JpegQuality — PdfSharp 6.2.4's PdfDocumentOptions does not
// expose a JpegQuality property. When a future version adds it, set it here:
// document.Options.JpegQuality = options.JpegQuality;
```

- [ ] Edit `PdfUtility.Pdf.csproj` to add `<UseWPF>true</UseWPF>`
- [ ] Add `ConvertPngToJpeg` static helper
- [ ] Update `BuildAsync` loop body

- [ ] Build:

```
dotnet build src/PdfUtility.Pdf/PdfUtility.Pdf.csproj
```

Expected: `Build succeeded.`

- [ ] Run PDF builder tests:

```
dotnet test tests/PdfUtility.Pdf.Tests/
```

Expected: all pass.

- [ ] Commit:

```
git add src/PdfUtility.Pdf/PdfUtility.Pdf.csproj src/PdfUtility.Pdf/PdfSharpPdfBuilder.cs
git commit -m "feat(pdf): JPEG DCTDecode compression via WPF JpegBitmapEncoder; uses JpegQuality setting"
```

---

## Task 8: Run full test suite + fix any regressions

- [ ] Run all tests:

```
dotnet test
```

Expected: all pass. Common failure modes and fixes:

- **`MergedPages_Batch1AndBatch2_HaveDistinctSubdirectories` calls `vm.GetMergedPages()`** — `GetMergedPages()` still delegates to `_session.BuildMergedPages()` which is unchanged. This should pass.
- **`MergedPages_TwoSheets_DoesNotDuplicateImages` asserts specific interleave order** — same as above, `_session.BuildMergedPages()` interleaving is unchanged.
- **Any `DoneCurrentBatch` test** — verify it sets `ScanMode = DoubleSided` explicitly before scanning if it asserts `Batch1Complete`.
- **XAML compile errors** — if the `BasedOn` on the Discard button causes `MC3024 Style already set`, use a plain trigger instead of `BasedOn` (see the `Style.Triggers` DataTrigger approach in the XAML above — it uses inline style with `BasedOn` which avoids the `Style=` attribute + `<Button.Style>` conflict).

- [ ] Fix any failures
- [ ] Re-run until clean:

```
dotnet test
```

- [ ] Commit if any test fixes were needed:

```
git add -A
git commit -m "test: fix regressions after scan UX + compression refactor"
```

---

## Task 9: Publish

- [ ] Full publish:

```
dotnet publish src/PdfUtility.App/PdfUtility.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

Expected: `PdfUtility.App -> C:\projects\PDF-Utility\publish\`

- [ ] Verify `publish/PdfUtility.App.exe` exists and `publish/_win64/pdfium.dll` is present

- [ ] Final commit + push:

```
git push origin main
```
