# Scan UX, Page Reorder & PDF Compression Redesign

**Goal:** Simplify the scan view to context-aware state-driven buttons, move scan settings to the sidebar, add scan mode (single/double/auto-detect), enable page reordering before merge, fix per-page PDF dimensions, and apply JPEG compression to reduce PDF file size.

**Architecture:** Three parallel tracks: (1) UI/ViewModel changes for the scan view state machine and sidebar layout; (2) scan mode and blank-page detection logic in the ViewModel; (3) JPEG compression in PdfSharpPdfBuilder via WPF's JpegBitmapEncoder.

**Tech Stack:** WPF + CommunityToolkit.Mvvm, PdfSharp 6.2.4, NAPS2.Sdk 1.2.1, .NET 10 Windows

---

## File Map

| Action | File |
|--------|------|
| Create | `src/PdfUtility.Core/Models/ScanMode.cs` |
| Modify | `src/PdfUtility.App/ViewModels/MainViewModel.cs` — remove ScanDpi, ColorMode, PaperSize, PdfFormat |
| Modify | `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs` — add ScanMode + scan settings properties, page reorder commands, blank detection, AutoDetect paper size; keep IUserSettings for DefaultSaveFolder |
| Modify | `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml` — full sidebar redesign; context-aware buttons; page reorder WrapPanel for MergeReady |
| Modify | `src/PdfUtility.App/MainWindow.xaml` — remove TitleBar.TrailingContent settings block |
| Modify | `src/PdfUtility.Pdf/PdfSharpPdfBuilder.cs` — JPEG temp-file compression |
| Modify | `src/PdfUtility.Pdf/PdfUtility.Pdf.csproj` — add `<UseWPF>true</UseWPF>` |

---

## Task 1: Add ScanMode enum

**Files:**
- Create: `src/PdfUtility.Core/Models/ScanMode.cs`

- [ ] Write the enum:

```csharp
namespace PdfUtility.Core.Models;

public enum ScanMode { DoubleSided, SingleSided, AutoDetect }
```

- [ ] Build: `dotnet build src/PdfUtility.Core/PdfUtility.Core.csproj`
- [ ] Commit: `git commit -m "feat(core): add ScanMode enum"`

---

## Task 2: Remove scan settings from MainViewModel and toolbar

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/MainViewModel.cs`
- Modify: `src/PdfUtility.App/MainWindow.xaml`

Remove `ScanDpi`, `ColorMode`, `PaperSize`, `PdfFormat` and their corresponding `*Options` arrays from `MainViewModel`. Keep `SelectedTabIndex`. No changes to `UserPreferences.cs` — it already has no `ScanMode` property.

`MainViewModel.cs` after:
```csharp
// src/PdfUtility.App/ViewModels/MainViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfUtility.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _selectedTabIndex = 0;
}
```

In `MainWindow.xaml`, remove the entire `<ui:TitleBar.TrailingContent>` block (the StackPanel with DPI, Color, PDF, Size ComboBoxes and the Settings button). The TitleBar becomes:
```xml
<ui:TitleBar Grid.Row="0" Title="PDF Utility"/>
```

- [ ] Make the changes to both files
- [ ] Build: `dotnet build src/PdfUtility.App/PdfUtility.App.csproj`
- [ ] Commit: `git commit -m "refactor(app): remove scan settings from toolbar and MainViewModel"`

---

## Task 3: Add scan settings + ScanMode to ScanDoubleSidedViewModel

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`

Remove the `MainViewModel` constructor parameter (no longer needed for settings). Add observable properties for all scan settings directly on the ViewModel. Keep `IUserSettings` — it is still used for `DefaultSaveFolder` in `MergeDocument()`.

Add these fields and properties:

```csharp
// Scan settings (previously in MainViewModel toolbar)
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

Remove the `MainViewModel _mainViewModel` field and constructor parameter. Constructor stays as 3 parameters: `IScannerBackend`, `IPdfBuilder`, `IUserSettings`. Update `BuildCurrentScanOptions()`:
```csharp
private ScanOptions BuildCurrentScanOptions() => new()
{
    Dpi = ScanDpi,
    ColorMode = ColorMode,
    PaperSize = PaperSize
};
```

Update `MergeDocument()` to use the ViewModel's own `PdfFormat`, read `JpegQuality = 85` as a constant (no prefs needed for that), and keep `_userSettings.Load()` only for `DefaultSaveFolder`:
```csharp
var prefs = _userSettings.Load();
// ...
var options = new PdfBuildOptions
{
    Format = PdfFormat,          // from ViewModel property
    JpegQuality = 85,
    PaperSize = PaperSize.AutoDetect   // each page keeps its own physical dimensions
};
```

- [ ] Make the changes
- [ ] Build: `dotnet build src/PdfUtility.App/PdfUtility.App.csproj`
- [ ] Commit: `git commit -m "feat(scan): add scan settings properties to ScanDoubleSidedViewModel"`

---

## Task 4: Context-aware state machine + SingleSided + page reorder

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`

### 4a: SingleSided mode — DoneBatch1 goes to MergeReady

**AutoDetect mode behaviour:** AutoDetect scans both sides (same as DoubleSided) but silently drops blank pages from each batch. `DoneBatch1()` in AutoDetect mode follows the same path as DoubleSided (transitions to `Batch1Complete`, prompting a batch 2 scan). Only `SingleSided` skips batch 2 entirely.

In `DoneBatch1()`, check `ScanMode`:
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
    else  // DoubleSided or AutoDetect — proceed to batch 2
    {
        SessionState = ScanSessionState.Batch1Complete;
        StatusMessage = $"Batch 1 complete ({_session.Batch1.Count} pages). Now scan the other side.";
    }
    return Task.CompletedTask;
}
```

**Note for test authors:** Any existing test that calls `DoneBatch1` and expects `Batch1Complete` must set `ScanMode = ScanMode.DoubleSided` (or `AutoDetect`) first, since the default is `DoubleSided` and the behaviour is unchanged for that mode.

### 4b: DoneBatch2 → BuildMergedThumbnails before MergeReady

In `DoneBatch2()`, call `BuildMergedThumbnails()` before setting state:
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
        MismatchWarningText = $"⚠ Batch mismatch: {_session.Batch1.Count} vs {_session.Batch2.Count} pages — extra pages will be appended at the end.";
    }
    return Task.CompletedTask;
}
```

### 4c: BuildMergedThumbnails helper

This replaces `Thumbnails` with the merged/interleaved order for display and reordering:

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

### 4d: Page reorder commands (same contract as MergeDocumentsViewModel)

Add three commands that operate on `Thumbnails` in MergeReady state. These are named `MovePageToBeginning`, `MovePageToEnd`, `RemoveScanPage` — the last differs from `MergeDocumentsViewModel.RemovePage` to avoid a naming conflict since the scan ViewModel also has a `ReplacePage` command taking the same parameter type. The XAML binds to `RemoveScanPageCommand` consistently.

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

### 4e: MergeDocument uses current Thumbnails order

Replace `var mergedPages = GetMergedPages();` with:
```csharp
var mergedPages = Thumbnails
    .Where(t => t.ScannedPage != null)
    .Select(t => t.ScannedPage!)
    .ToList();
```

### 4f: VisibilityHelper computed properties for XAML binding

The existing `IsBatch1Complete` property stays but is redeclared to match the new pattern (same semantics, just cleaned up). Remove its existing `[NotifyPropertyChangedFor(nameof(IsBatch1Complete))]` decoration from `_sessionState` and re-add it to the consolidated list below. Any `[NotifyCanExecuteChangedFor(nameof(ScanOtherSideCommand))]` stays — `CanScanOtherSide()` already checks `SessionState` directly.

Add the following computed properties alongside the existing ones:

```csharp
// Show scanner/settings section only when Idle
// Settings are intentionally hidden (read-only locked) during an active session.
public bool IsIdle => SessionState == ScanSessionState.Idle;

// Show Batch1 action buttons (Continue / Done Scanning Front)
public bool IsBatch1Actionable =>
    SessionState is ScanSessionState.Batch1Paused
                 or ScanSessionState.Batch1Error;

// IsBatch1Complete already exists — keep it, same semantics.
// public bool IsBatch1Complete => SessionState == ScanSessionState.Batch1Complete;

// Show Batch2 action buttons (Continue / Done Scanning Back)
public bool IsBatch2Actionable =>
    SessionState is ScanSessionState.Batch2Paused
                 or ScanSessionState.Batch2Error;

// Show Merge button
public bool IsMergeReady => SessionState == ScanSessionState.MergeReady;
```

Add ALL of the following to the `[NotifyPropertyChangedFor]` attribute list on `_sessionState`:
- `nameof(IsIdle)`
- `nameof(IsBatch1Complete)`  ← must be in the list (was already there; keep it)
- `nameof(IsBatch1Actionable)`
- `nameof(IsBatch2Actionable)`
- `nameof(IsMergeReady)`

- [ ] Implement all of 4a–4f
- [ ] Build: `dotnet build src/PdfUtility.App/PdfUtility.App.csproj`
- [ ] Commit: `git commit -m "feat(scan): single-sided mode, page reorder, merged thumbnail order"`

---

## Task 5: Blank page auto-detect

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`

In `RunScanBatchAsync`, after receiving each page and saving its PNG, check for blank if mode is AutoDetect:

```csharp
// Inside the foreach loop, after adding the page to batch:
if (ScanMode == ScanMode.AutoDetect && IsBlankPage(page.ImagePath))
{
    batch.Remove(page);
    try { File.Delete(page.ImagePath); } catch { }
    // Remove the thumb we just added to the dispatcher
    var dispatcher2 = System.Windows.Application.Current?.Dispatcher;
    if (dispatcher2 != null)
        await dispatcher2.InvokeAsync(() =>
        {
            if (Thumbnails.Count > 0) Thumbnails.RemoveAt(Thumbnails.Count - 1);
        });
    else if (Thumbnails.Count > 0)
        Thumbnails.RemoveAt(Thumbnails.Count - 1);
    continue; // do NOT increment; batch.Count didn't change
}
```

Blank detection helper (mean pixel brightness threshold — 94% white):
```csharp
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

        int stride = bmp.PixelWidth * 4;
        var pixels = new byte[bmp.PixelHeight * stride];
        bmp.CopyPixels(pixels, stride, 0);

        long sum = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            sum += (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
        return sum / (pixels.Length / 4) >= threshold;
    }
    catch { return false; }
}
```

Note: The `continue` approach is wrong for `await foreach` — restructure so that we add to batch first, then check and remove if blank. The logic must use a flag, not `continue` on the outer loop.

Correct pattern:
```csharp
await foreach (var page in _scanner.ScanBatchAsync(...))
{
    cancellationToken.ThrowIfCancellationRequested();

    if (ScanMode == ScanMode.AutoDetect && IsBlankPage(page.ImagePath))
    {
        try { File.Delete(page.ImagePath); } catch { }
        continue;
    }

    batch.Add(page);
    var thumb = new PageThumbnailViewModel { ... PageNumber = batch.Count, ... };
    var dispatcher = ...
    // add thumb to Thumbnails
    StatusMessage = $"Scanning page {batch.Count}…";
}
```

- [ ] Implement blank detection
- [ ] Build and verify
- [ ] Commit: `git commit -m "feat(scan): auto-detect blank pages in AutoDetect mode"`

---

## Task 6: Redesign ScanDoubleSidedView.xaml

**Files:**
- Modify: `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml`

### Sidebar structure

The sidebar (230px left column) uses a `ScrollViewer` containing a `StackPanel`. Sections are shown/hidden via `Visibility` bound to the computed properties added in Task 4f.

```xml
<!-- SCANNER SECTION — visible only when Idle -->
<StackPanel Visibility="{Binding IsIdle, Converter={StaticResource BoolToVisibility}}">
    <TextBlock Text="SCANNER" Style="{StaticResource SidebarLabel}"/>
    <!-- scanner dropdown + refresh (existing) -->

    <TextBlock Text="SCAN MODE" Style="{StaticResource SidebarLabel}" Margin="0,8,0,0"/>
    <ComboBox ItemsSource="{Binding ScanModeOptions}"
              SelectedItem="{Binding ScanMode, Mode=TwoWay}"/>

    <TextBlock Text="DPI" Style="{StaticResource SidebarLabel}" Margin="0,8,0,0"/>
    <ComboBox ItemsSource="{Binding DpiOptions}" SelectedItem="{Binding ScanDpi, Mode=TwoWay}"/>

    <TextBlock Text="COLOR" Style="{StaticResource SidebarLabel}" Margin="0,8,0,0"/>
    <ComboBox ItemsSource="{Binding ColorModeOptions}" SelectedItem="{Binding ColorMode, Mode=TwoWay}"/>

    <TextBlock Text="SIZE" Style="{StaticResource SidebarLabel}" Margin="0,8,0,0"/>
    <ComboBox ItemsSource="{Binding PaperSizeOptions}" SelectedItem="{Binding PaperSize, Mode=TwoWay}"/>

    <TextBlock Text="PDF FORMAT" Style="{StaticResource SidebarLabel}" Margin="0,8,0,0"/>
    <ComboBox ItemsSource="{Binding PdfFormatOptions}" SelectedItem="{Binding PdfFormat, Mode=TwoWay}"/>

    <Button Content="▶  Start Scanning" Style="{StaticResource AccentButton}"
            Command="{Binding StartBatch1Command}"
            HorizontalAlignment="Stretch" Margin="0,12,0,0"/>
</StackPanel>

<!-- BATCH 1 ACTIONS — visible when IsBatch1Actionable -->
<StackPanel Visibility="{Binding IsBatch1Actionable, Converter={StaticResource BoolToVisibility}}">
    <TextBlock Text="BATCH 1 — FRONT SIDES" Style="{StaticResource SidebarLabel}"/>
    <Button Content="⊕  Continue Scanning" Style="{StaticResource SidebarButton}"
            Command="{Binding ContinueScanningCommand}" Margin="0,0,0,4"/>
    <Button Content="✓  Done Scanning Front" Style="{StaticResource SidebarButton}"
            Command="{Binding DoneBatch1Command}"/>
</StackPanel>

<!-- BATCH 2 START — visible when Batch1Complete -->
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
    <Button Content="▶  Scan Back Side" Style="{StaticResource AccentButton}"
            Command="{Binding ScanOtherSideCommand}"
            HorizontalAlignment="Stretch"/>
</StackPanel>

<!-- BATCH 2 ACTIONS — visible when IsBatch2Actionable -->
<StackPanel Visibility="{Binding IsBatch2Actionable, Converter={StaticResource BoolToVisibility}}">
    <TextBlock Text="BATCH 2 — BACK SIDES" Style="{StaticResource SidebarLabel}"/>
    <Button Content="⊕  Continue Scanning" Style="{StaticResource SidebarButton}"
            Command="{Binding ContinueScanningCommand}" Margin="0,0,0,4"/>
    <Button Content="✓  Done Scanning Back" Style="{StaticResource SidebarButton}"
            Command="{Binding DoneBatch2Command}"/>
</StackPanel>

<!-- MERGE — visible when IsMergeReady -->
<StackPanel Visibility="{Binding IsMergeReady, Converter={StaticResource BoolToVisibility}}">
    <Button Content="⇄  Merge Document" Style="{StaticResource AccentButton}"
            Command="{Binding MergeDocumentCommand}"
            HorizontalAlignment="Stretch"/>
</StackPanel>

<!-- ERROR BANNER — existing, no change -->
<!-- MISMATCH WARNING — existing, no change -->

<!-- DISCARD — always visible when session active (existing) -->
<Button Content="🗑  Discard Session" Style="{StaticResource DangerButton}"
        Command="{Binding DiscardSessionCommand}" Margin="0,16,0,0"/>
```

### Main canvas — page reorder in MergeReady

The WrapPanel ItemTemplate gets a ContextMenu with the three reorder commands. Use the same `Tag` pattern as `MergeDocumentsView.xaml`:

```xml
<Button ...
        Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=ItemsControl}}">
    <Button.ContextMenu>
        <ContextMenu>
            <MenuItem Header="Move to Beginning"
                      Command="{Binding PlacementTarget.Tag.MovePageToBeginningCommand,
                          RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding}"/>
            <MenuItem Header="Move to End"
                      Command="{Binding PlacementTarget.Tag.MovePageToEndCommand,
                          RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding}"/>
            <Separator/>
            <MenuItem Header="Remove Page"
                      Command="{Binding PlacementTarget.Tag.RemoveScanPageCommand,
                          RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding}"/>
        </ContextMenu>
    </Button.ContextMenu>
    <controls:PageThumbnailControl DataContext="{Binding}"/>
</Button>
```

Add `xmlns:models="clr-namespace:PdfUtility.Core.Models;assembly=PdfUtility.Core"` to the UserControl for the `ScanSessionState` x:Static reference.

- [ ] Rewrite the XAML
- [ ] Build: `dotnet build src/PdfUtility.App/PdfUtility.App.csproj`
- [ ] Commit: `git commit -m "feat(scan): context-aware sidebar, settings moved from toolbar, page reorder"`

---

## Task 7: JPEG compression in PdfSharpPdfBuilder

**Files:**
- Modify: `src/PdfUtility.Pdf/PdfUtility.Pdf.csproj`
- Modify: `src/PdfUtility.Pdf/PdfSharpPdfBuilder.cs`

### 7a: Enable WPF in the PDF project

In `PdfUtility.Pdf.csproj`:
```xml
<PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
</PropertyGroup>
```

### 7b: JPEG conversion helper + wire up in BuildAsync

Replace the `XImage.FromFile(source.ImagePath)` call with a JPEG-converted version. Convert each source PNG to a temp JPEG file (same temp dir), load with `XImage.FromFile` (PDFsharp detects JPEG and embeds as DCTDecode), then delete the temp file.

```csharp
using System.Windows.Media.Imaging;

// In BuildAsync, replace:
//   using var xImage = XImage.FromFile(source.ImagePath);
// with:
string? tempJpeg = null;
try
{
    tempJpeg = Path.Combine(Path.GetTempPath(),
        $"pdfutility_jpeg_{Guid.NewGuid():N}.jpg");
    ConvertPngToJpeg(source.ImagePath, tempJpeg, options.JpegQuality);
    using var xImage = XImage.FromFile(tempJpeg);
    // ... existing DrawImage logic unchanged ...
}
finally
{
    if (tempJpeg != null)
        try { File.Delete(tempJpeg); } catch { }
}

// Helper:
private static void ConvertPngToJpeg(string pngPath, string jpegPath, int quality)
{
    var bmp = new BitmapImage();
    bmp.BeginInit();
    bmp.UriSource = new Uri(pngPath, UriKind.Absolute);
    bmp.CacheOption = BitmapCacheOption.OnLoad;
    bmp.EndInit();
    bmp.Freeze();

    var encoder = new JpegBitmapEncoder { QualityLevel = quality };
    encoder.Frames.Add(BitmapFrame.Create(bmp));
    using var fs = File.Create(jpegPath);
    encoder.Save(fs);
}
```

Remove the old TODO comment about `document.Options.JpegQuality`.

- [ ] Add `<UseWPF>true</UseWPF>` to csproj
- [ ] Implement `ConvertPngToJpeg` and update `BuildAsync`
- [ ] Build: `dotnet build src/PdfUtility.Pdf/PdfUtility.Pdf.csproj`
- [ ] Commit: `git commit -m "feat(pdf): JPEG compression via WPF encoder, DCTDecode embedding"`

---

## Task 8: Verify and update tests

**Files:**
- Modify: `tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs`

The existing test `CreateVm` helper already passes 3 parameters (`IScannerBackend`, `IPdfBuilder`, `IUserSettings`) — no constructor changes needed in tests.

However, any test that calls `DoneBatch1Command` and asserts `SessionState == Batch1Complete` needs to verify that `ScanMode` is left at its default `DoubleSided` value (it is, by default). Add explicit `vm.ScanMode = ScanMode.DoubleSided` to those tests as documentation of intent.

- [ ] Run: `dotnet test tests/PdfUtility.App.Tests/`
- [ ] Fix any failures from the state machine changes
- [ ] Commit: `git commit -m "test: verify scan ViewModel tests after state machine refactor"`

---

## Task 9: Publish

- [ ] `dotnet publish src/PdfUtility.App/PdfUtility.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/`
- [ ] Verify `publish/` contains the updated binary
- [ ] Commit publish if any assets need updating
