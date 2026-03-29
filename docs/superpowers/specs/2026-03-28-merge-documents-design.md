# Merge Documents + Acrobat UI Redesign — Design Spec

**Date:** 2026-03-28
**Status:** Approved

---

## Overview

Two parallel concerns addressed in this spec:

1. **Merge Documents** — A standalone PDF utility tab that lets users load multiple existing PDF files, review and reorder their pages as thumbnails, and merge them into a single output PDF.
2. **Acrobat-Inspired UI Redesign** — A consistent visual theme applied across all app views: dark toolbar, dark sidebar, light content canvas, accent red — matching the Adobe Acrobat aesthetic.

---

## Part 1: Merge Documents Feature

### Goals

- User can select one or more existing PDF files and add them to a file queue.
- User can reorder files in the queue before loading pages.
- After clicking "Load Pages", all pages across all queued files are rendered as thumbnails in a page editor view.
- User can reorder individual pages (drag-drop, right-click menu).
- User can preview any page full-screen by clicking its thumbnail.
- User can remove individual pages.
- User clicks "Merge" to save a new combined PDF.

### Non-Goals

- No page cropping, rotation editing, or annotation.
- No splitting PDFs into separate files.
- No importing from scanner in this view (scanning is in ScanDoubleSided tab).

---

### PDF Import Strategy

**Chosen approach: NAPS2.Pdf**

Use the `NAPS2.Pdf` NuGet package to render each PDF page to a temporary PNG image via PDFium. This is consistent with the existing NAPS2.Images usage in the scanning backend and avoids adding a second PDF rendering dependency.

The rendered images are stored in a per-session temp directory (deleted on app exit or when the user discards the session). These PNGs are then fed into `PdfSharpPdfBuilder` for the final merge, exactly the same path as scanned pages.

---

### Architecture

#### New Interfaces and Models (`PdfUtility.Core`)

```
IPdfImporter
  Task<IReadOnlyList<ImportedPage>> ImportAsync(string pdfPath, string outputDirectory, CancellationToken ct)
```

Contract: `ImportAsync` throws `PdfImportException` (new exception in Core) on corrupt/unreadable PDFs. It never returns a partial list — either all pages are rendered and returned, or an exception is thrown. The caller (ViewModel) catches per-file and continues with remaining files.

```csharp
public class PdfImportException : Exception
{
    public PdfImportException(string message) : base(message) { }
    public PdfImportException(string message, Exception inner) : base(message, inner) { }
}
```

`ImportedPage` class (not record — requires mutable `Rotation`):
```csharp
public class ImportedPage : IPageSource
{
    public string ImagePath { get; }
    public int SourcePageIndex { get; }
    public string SourceFileName { get; }
    public PageRotation Rotation { get; set; } = PageRotation.None;
    public ImportedPage(string imagePath, int sourcePageIndex, string sourceFileName)
    { ImagePath = imagePath; SourcePageIndex = sourcePageIndex; SourceFileName = sourceFileName; }
}
```

#### Implementation (`PdfUtility.Pdf` project)

`Naps2PdfImporter : IPdfImporter`
- Uses `NAPS2.Pdf.PdfImporter` to render each page to a PNG in `outputDirectory`.
- Returns one `ImportedPage` per rendered page.
- File naming: `{sanitizedSourceFileName}_{guid8}_page_{index:D4}.png` — the 8-char GUID prefix prevents collisions when two files share the same name.
- Throws `PdfImportException` wrapping the underlying exception on failure.

#### ViewModel (`PdfUtility.App`)

`MergeFileViewModel` (per-file in queue):
- `string FilePath`
- `string FileName` (display name)
- `bool IsLoading`
- `RemoveCommand`

`MergeDocumentsViewModel`:
- `ObservableCollection<MergeFileViewModel> FileQueue`
- `ObservableCollection<PageThumbnailViewModel> Pages`
- `MergeFileViewModel? SelectedFile`
- `PageThumbnailViewModel? PreviewPage` (non-null = preview overlay visible; overlay is modal — page grid interaction and file queue modification are both disabled while preview is open)
- `bool IsLoadingPages`
- `string? StatusMessage`
- `string? ErrorMessage`
- `string? LastSaveDirectory` — stored in `IUserSettings` (existing service); defaults to `Environment.GetFolderPath(SpecialFolder.MyDocuments)` on first use; persists across restarts
- Commands: `AddFilesCommand`, `RemoveFileCommand`, `MoveFileUpCommand`, `MoveFileDownCommand`, `LoadPagesCommand`, `MovePageToBeginningCommand`, `MovePageToEndCommand`, `RemovePageCommand`, `MergeCommand`, `PreviewPageCommand`, `ClosePreviewCommand`, `DiscardSessionCommand`

State machine:
- `Idle` → AddFiles → `FilesQueued` → LoadPages → `LoadingPages` → `PagesLoaded` → Merge → `Merging` → `PagesLoaded`
- From any state with files: RemoveFile → if FileQueue becomes empty → back to `Idle`
- File queue is **locked during `LoadingPages` and `Merging`** — AddFiles and RemoveFile commands are disabled in these states, preventing concurrent modification.
- After a successful merge, state returns to `PagesLoaded` (pages remain, user can merge again to a different file).
- `DiscardSession` command (available in `FilesQueued` / `PagesLoaded`) clears FileQueue and Pages, deletes temp files, returns to `Idle`.

`StatusMessage` and `ErrorMessage` clearing:
- `StatusMessage` is cleared at the start of any command execution.
- `ErrorMessage` is cleared at the start of any command execution.
- Only one of the two is set after each operation completes.

Reuses existing `PageThumbnailViewModel` (already has `ImagePath`, `HasWarning`, `SourceLabel`).

#### View (`PdfUtility.App`)

`MergeDocumentsView.xaml` replaces the current placeholder tab content.

Two-panel layout:

**Left sidebar (dark, ~240 px):**
- "Add Files" button (opens OpenFileDialog, multi-select, filter `*.pdf`); disabled during `LoadingPages`/`Merging`
- File list (`ListBox` of `MergeFileViewModel`): shows filename, remove button per row; list locked during `LoadingPages`/`Merging`
- Up/Down arrow buttons for file reorder; disabled during `LoadingPages`/`Merging`
- "Load Pages" button (enabled in `FilesQueued` state only)
- "Discard" button (enabled in `FilesQueued`/`PagesLoaded`): clears everything, returns to `Idle`

**Right content area (light canvas):**
- Thumbnail grid (`WrapPanel` in `ScrollViewer`) reusing `PageThumbnailControl`
- Right-click context menu on each thumbnail: "Move to Beginning", "Move to End", "Remove Page"
  - Context menu is **disabled** while preview overlay is open
- Click on thumbnail → opens **modal full-page preview overlay** (blocks the thumbnail grid interaction; same visual as ScanDoubleSided). Clicking outside the image or pressing Escape closes the overlay.
- "Merge" button in toolbar (enabled in `PagesLoaded` state only)

**SaveFileDialog specification:**
- Title: "Save Merged PDF"
- Default filename: `merged.pdf`
- Default directory: last-used save directory (or `Documents` folder on first use)
- Filter: `PDF Files (*.pdf)|*.pdf`

**Merge retry:** after a failed merge (e.g. file locked, disk full), `ErrorMessage` is shown and state stays in `PagesLoaded`. The user can immediately retry "Merge" or choose a different output file. No state reset is required.

---

### Data Flow

```
User selects PDFs
  → FileQueue populated (MergeFileViewModel per file)

User clicks "Load Pages"
  → foreach file in queue:
      IPdfImporter.ImportAsync(file, tempDir)  → List<ImportedPage>
  → Pages ObservableCollection populated with PageThumbnailViewModel per ImportedPage

User reorders pages (drag-drop or context menu)
  → Pages collection reordered in-place

User clicks "Merge"
  → SaveFileDialog → outputPath
  → IPdfBuilder.BuildAsync(Pages (as IPageSource), PdfBuildOptions, outputPath)
  → Success: StatusMessage = "Merged to <filename>"
```

---

### Error Handling

- **Corrupt/unreadable PDF:** `ImportAsync` throws `PdfImportException`. Scope: includes corrupt files, malformed PDFs, and zero-page PDFs. Password-protected PDFs are **out of scope** — they throw `PdfImportException` with a message noting password protection. The ViewModel catches per-file, shows `ErrorMessage` listing failed filenames, and continues loading remaining files.
- **Temp directory creation failure:** If `Directory.CreateDirectory(tempDir)` fails (permissions), `LoadPagesCommand` catches the exception and sets `ErrorMessage = "Could not create temp directory: <message>"`. State stays in `FilesQueued`.
- **No pages loaded (all imports failed):** `ErrorMessage` = "No pages could be loaded. Check that the selected files are valid, unencrypted PDFs." State stays in `FilesQueued`.
- **Some imports failed:** `ErrorMessage` = "Some files could not be imported: <filename1>, <filename2>. Loaded N pages from remaining files." Pages from successful imports are shown.
- **SaveFileDialog cancelled (user presses Cancel):** Merge is aborted silently. No `ErrorMessage` or `StatusMessage` is set. State stays in `PagesLoaded`. Pages are not cleared. `LastSaveDirectory` is **not updated** (retains previous value).
- **Merge failure (file locked, disk full, etc.):** `ErrorMessage` = "Merge failed: <message>". State stays in `PagesLoaded`. User can retry immediately.
- **Cancellation:** `LoadPagesCommand` creates and holds a `CancellationTokenSource`; the token is passed to `ImportAsync`. If cancelled (programmatically, e.g. `DiscardSession` called during load), partial pages are discarded and state returns to `FilesQueued`. No Cancel UI button is exposed in Phase 1 — cancellation only happens via `DiscardSession`.
- **Large PDFs:** No page count limit — all pages are loaded. No special warning threshold in this version. If performance is an issue that is a Phase 2 concern.

---

### Temp File Cleanup

- Temp dir: `Path.Combine(Path.GetTempPath(), "PdfUtility_Merge_<sessionGuid>")`
- Cleaned up when: user clicks "Discard" / starts a new merge session / app exits (best-effort in `Application.Exit`).
- On startup: best-effort background scan of `Path.GetTempPath()` for `PdfUtility_Merge_*` directories older than 1 day; delete them silently. Failures (permissions, in-use files) are swallowed — this is cleanup only, not correctness-critical. Implemented as `Task.Run` fire-and-forget in `App.OnStartup`.

---

### Testing

**Unit tests (`PdfUtility.App.Tests`):**
- `MergeDocumentsViewModelTests` using `FakePdfImporter` and `FakePdfBuilder`
- Key scenarios:
  - AddFiles → FileQueue populated; `AddFilesCommand` disabled during `LoadingPages`
  - LoadPages → Pages populated (one entry per page across all files)
  - MovePageToBeginning / MovePageToEnd → correct page order
  - RemovePageCommand removes entry from Pages
  - Merge calls `IPdfBuilder.BuildAsync` with correct page order
  - Merge cancelled (SaveFileDialog returns null) → pages not cleared, no error set
  - Import failure on one file → error message, other files still loaded
  - All imports fail → error message, state stays `FilesQueued`
  - DiscardSession → FileQueue empty, Pages empty, state `Idle`
  - `AddFilesCommand` and `RemoveFileCommand` disabled during `LoadingPages` and `Merging`
  - Startup cleanup: orphaned `PdfUtility_Merge_*` directories older than 1 day are deleted

**Integration tests (`PdfUtility.Pdf.Tests`):**
- `Naps2PdfImporterTests` — requires a real PDF fixture; tests:
  - `ImportAsync` returns correct page count matching the fixture
  - Each returned `ImportedPage.ImagePath` exists on disk and is a non-empty file (size > 0 bytes; readable as image by `System.Drawing.Image.FromFile`)
  - `ImportAsync` throws `PdfImportException` when given a corrupt file (fixture: a text file renamed `.pdf`)
- Fixture: a small 2-page test PDF committed to `tests/fixtures/two-page-test.pdf`

---

## Part 2: Acrobat-Inspired UI Redesign

### Color Palette

| Token | Hex | Usage |
|---|---|---|
| `AppToolbarBg` | `#1E1E1E` | Top toolbar background |
| `AppSidebarBg` | `#2D2D2D` | Left sidebar background (both views) |
| `AppCanvasBg` | `#F5F5F5` | Main content area (thumbnail grid) |
| `AppAccent` | `#D42B2B` | Primary action buttons, active state indicators |
| `AppAccentHover` | `#B02020` | Button hover |
| `AppSidebarText` | `#E0E0E0` | Sidebar label text |
| `AppToolbarText` | `#FFFFFF` | Toolbar button text/icons |
| `AppThumbnailBorder` | `#CCCCCC` | Thumbnail card border (light canvas) |

### Scope

Applies to:
- `MainWindow.xaml` — tab bar, toolbar strip
- `ScanDoubleSidedView.xaml` — existing scan view restyled
- `MergeDocumentsView.xaml` — new view, built with theme from the start
- `PageThumbnailControl.xaml` — thumbnail card updated for light-canvas context

### Component Structure

**Toolbar strip (top):**
- Dark (`#1E1E1E`) horizontal band, ~40 px tall
- Primary action button (e.g. "Start Scan", "Merge") in accent red `#D42B2B`
- Secondary actions as flat white-icon buttons

**Left sidebar:**
- Dark (`#2D2D2D`), ~240 px, white/light-gray text
- Contains scan controls / file list depending on view
- Bottom-aligned status / device selector

**Content canvas:**
- Light gray `#F5F5F5`, fills remaining width
- Thumbnail grid with white thumbnail cards, subtle drop shadow, `#CCCCCC` border

**Full-page preview overlay:**
- Same semi-transparent dark overlay as current implementation
- Image centered, close button top-right

### Implementation Approach

- Define all colors as `StaticResource` in `App.xaml` resource dictionary
- Define named `Style` resources for: `ToolbarButton`, `SidebarButton`, `AccentButton`, `SidebarLabel`
- Apply styles throughout XAML rather than inline colors

---

## Affected Files Summary

| File | Change |
|---|---|
| `src/PdfUtility.Core/Interfaces/IPdfImporter.cs` | New |
| `src/PdfUtility.Core/Models/ImportedPage.cs` | New |
| `src/PdfUtility.Pdf/Naps2PdfImporter.cs` | New |
| `src/PdfUtility.App/ViewModels/MergeFileViewModel.cs` | New |
| `src/PdfUtility.App/ViewModels/MergeDocumentsViewModel.cs` | New |
| `src/PdfUtility.App/Views/MergeDocumentsView.xaml` | New |
| `src/PdfUtility.App/Views/MergeDocumentsView.xaml.cs` | New |
| `src/PdfUtility.App/App.xaml` | Add color/style resources |
| `src/PdfUtility.App/MainWindow.xaml` | Wire up MergeDocumentsView tab; apply toolbar styling |
| `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml` | Apply Acrobat theme |
| `src/PdfUtility.App/Controls/PageThumbnailControl.xaml` | Update for light canvas |
| `src/PdfUtility.Pdf/PdfUtility.Pdf.csproj` | Add NAPS2.Pdf package reference |
| `tests/PdfUtility.App.Tests/ViewModels/MergeDocumentsViewModelTests.cs` | New |
| `tests/PdfUtility.Pdf.Tests/Naps2PdfImporterTests.cs` | New |
| `tests/fixtures/two-page-test.pdf` | New test fixture |

---

## Open Questions / Deferred

- **Page rotation in merge UI:** `ImportedPage.Rotation` exists because `IPageSource` requires it; it always stays `PageRotation.None` in Phase 1. No rotation UI is exposed. `PdfSharpPdfBuilder` already handles rotation correctly if set. Rotation editing is Phase 2.
- Drag-and-drop reordering of pages in the thumbnail grid (Phase 2): initial implementation uses right-click context menu only.
- Multi-select (Ctrl/Shift+click) for bulk remove (Phase 2).
- Cancel button for in-progress "Load Pages" (Phase 2). `CancellationToken` plumbing is Phase 1.
- Drag-and-drop to add files to the file queue (Phase 2).
