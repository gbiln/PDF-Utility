# PDF Utility — Design Spec
**Date:** 2026-03-28
**Status:** Approved

---

## Overview

A Windows desktop application for scanning double-sided documents and performing PDF utilities. Targets users with an Epson ET-4850 printer/scanner who need ScanSnap-like speed and reliability from a single-sided ADF.

---

## Platform & Stack

| Component | Choice | Rationale |
|-----------|--------|-----------|
| Language | C# (.NET 8) | Native Windows, best scanner integration |
| UI Framework | WPF + WPF-UI (Fluent) | Modern Windows 11 look, self-contained EXE |
| MVVM | CommunityToolkit.Mvvm | Reduces boilerplate |
| Scanner | NAPS2.Sdk | Battle-tested ADF/flatbed, async page streaming |
| PDF (Standard) | PDFsharp (MIT) | Simple, zero-cost, well maintained |
| PDF (PDF/A) | iText7 Community (AGPL) | Industry-standard archival PDF |
| Installer | Inno Setup | Single installable EXE, no runtime required |

---

## Solution Structure

```
PdfUtility.sln
├── PdfUtility.App         ← WPF UI: views, viewmodels, MVVM
├── PdfUtility.Scanning    ← IScannerBackend + NAPS2 implementation
├── PdfUtility.Pdf         ← IPdfBuilder: assembly, compression, PDF/A
└── PdfUtility.Core        ← Shared models, settings, interfaces
```

### Key Interfaces

```csharp
// Scanner abstraction — swappable backend
interface IScannerBackend
{
    IAsyncEnumerable<ScannedPage> ScanBatchAsync(ScanOptions options);
    Task<ScannedPage> ScanSingleFlatbedAsync(ScanOptions options);  // throws ScannerException on failure
}

// Rotation values — enforced by this enum across all IPageSource implementations
enum PageRotation { None = 0, CW90 = 90, CW180 = 180, CW270 = 270 }

// Unified page source for PDF assembly (covers both ScannedPage and imported files)
interface IPageSource
{
    string ImagePath { get; }
    PageRotation Rotation { get; set; }
}

// PDF assembly — accepts any IPageSource (scanned pages or imported image/PDF pages)
interface IPdfBuilder
{
    Task BuildAsync(IEnumerable<IPageSource> pages, PdfBuildOptions options, string outputPath);
}

// Settings persistence
interface IUserSettings
{
    UserPreferences Load();
    void Save(UserPreferences prefs);
}
```

---

## Features

### 1. Scan Double Sided (Tab 1)

Scans all front-side pages through the ADF (Batch 1), then all back-side pages (Batch 2), and interleaves them into a correctly-ordered document.

#### Session State Machine

```csharp
enum ScanSessionState
{
    Idle,
    Batch1Scanning,
    Batch1Paused,       // mid-scan pause (user chose Continue Scanning)
    Batch1Error,        // feeder/jam error
    Batch1Complete,
    Batch2Scanning,
    Batch2Paused,
    Batch2Error,
    Batch2Complete,
    MergeReady,
    Saved
}
```

#### Workflow

```
Idle
 └─ [Start Batch 1] ──► Batch1Scanning
                              │
                    page-by-page preview updates
                              │
              ┌───────────────┼────────────────┐
     [Continue Scanning]  [Done Batch 1]  [Feeder Error]
              │                │                │
       Batch1Paused      Batch1Complete    Batch1Error
       [new ScanBatchAsync,    │            [see Error Recovery]
        appends to Batch1]     │
                        [Scan Other Side]
                              │
                        Batch2Scanning
                              │
                    page-by-page preview updates
                              │
              ┌───────────────┼────────────────┐
     [Continue Scanning]  [Done Batch 2]  [Feeder Error]
              │                │                │
       Batch2Paused      Batch2Complete    Batch2Error
       [new ScanBatchAsync,    │            [see Error Recovery]
        appends to Batch2]   MergeReady
                              │
                       [Merge Document]
                              │
                           Saved
```

**Tab switching during an active scan:** NAPS2's `ScanBatchAsync` is a streaming ADF loop that cannot be externally paused mid-feed — the ADF will run until it is empty or an error occurs. Tab switching does **not** physically stop the hardware. Instead: if the user switches to Merge Documents while `Batch1Scanning` or `Batch2Scanning` is active, the scan continues in the background, thumbnails continue to accumulate in the Scan tab, and a persistent info banner is shown on the Merge tab: "A scan is in progress — switch to Scan Double Sided to monitor it." All Merge Documents controls remain usable. The Scan tab state machine is unaffected by the tab switch.

**Session discard:** A **Discard Session** button appears whenever the session is not Idle. Clicking it shows a confirmation dialog: "Discard all scanned pages and start over?" On confirm, all temp files for the session are deleted and state returns to Idle.

#### Scan Controls (left panel, ~220px)

**Batch 1 section:**
- **Start Scanning Batch 1** — initiates ADF scan, enables live preview (calls `ScanBatchAsync`, appends pages to Batch1)
- **Continue Scanning** — feeds next stack into ADF; issues a new `ScanBatchAsync` call, appending results to existing Batch1 list
- **Done with Batch 1** — locks Batch 1, transitions to `Batch1Complete`, unlocks Batch 2 controls

**Batch 2 section** (disabled until Batch1Complete):
- **Scan Other Side** — initiates ADF scan for back sides (calls `ScanBatchAsync`, appends to Batch2)
- **Continue Scanning** — issues a new `ScanBatchAsync` call, appending to Batch2
- **Done with Batch 2** — locks Batch 2, transitions to `Batch2Complete` → `MergeReady`

**Final action** (disabled until MergeReady):
- **Merge Document** — interleaves batches, prompts Save As dialog

#### Page Count Mismatch
If `Batch1.Count != Batch2.Count`, show a warning banner at merge time:
> "⚠ Batch mismatch: X vs Y pages — extra pages will be appended at the end."
User can proceed or cancel to fix.

#### Live Preview (right panel, 3/4 width)
- Thumbnails appear as each page completes scanning
- Each thumbnail shows: page number badge, source label ("Front" for Batch 1, "Back" for Batch 2), **Replace** link
- Status bar shows current scan progress: "Scanning page 4… (ADF feeding)"
- Partial/damaged pages flagged with ⚠ badge. A page is considered partial/damaged when NAPS2 delivers a `ScanErrorException` mid-batch — the last image received before the error is flagged. Additionally, if the delivered image height is less than 80% of the expected height for the selected paper size at the selected DPI, it is flagged automatically.

#### Page Replacement (Flatbed)
1. User clicks **Replace** on any thumbnail
2. App calls `IScannerBackend.ScanSingleFlatbedAsync`
3. On success: `ScannedPage.ReplaceImage(newPath)` updates the image path and clears any warning flag; thumbnail refreshes immediately
4. On failure (scanner offline, timeout, etc.): error dialog shown — "Could not scan replacement page: {reason}. Try again or cancel." The original page is unchanged.

#### Interleaving Logic
When the user clicks **Merge Document**, Batch 2 is **automatically reversed** before interleaving. This corrects for the physical reality that when the user flips the stack and re-feeds it through the ADF, the back sides come out in reverse order (last sheet first).

```
batch2Reversed = Reverse(batch2)
merged = []
for i in range(max(len(batch1), len(batch2Reversed))):
    if i < len(batch1):        merged.append(batch1[i])
    if i < len(batch2Reversed): merged.append(batch2Reversed[i])
```

Result for a 4-page document: F1, B1, F2, B2, F3, B3, F4, B4.

---

### 2. Merge Documents (Tab 2)

Combines multiple PDFs and/or image files into a single PDF with full page-level reordering.

#### Layout
- **Left panel (1/4):** File list with add/remove. Accepts PDF, JPG, PNG, TIFF, BMP via drag-drop or file browser.
- **Right panel (3/4):** Draggable page thumbnail grid. Each thumbnail shows page number badge, source filename label, and **Delete** button.
- **Merge & Save PDF** button at bottom of left panel.

#### Page Reordering
Pages are reordered by dragging thumbnails in the grid. The merge output respects the visual order shown, regardless of source file order.

#### Supported Input Formats
PDF, JPG, JPEG, PNG, TIFF, BMP

#### Session Lifecycle
- Work in progress (file list, page order) is preserved across tab switches within the same app session
- State is **not** persisted to disk — closing and reopening the app resets Merge Documents to empty
- A **Clear** button on the tab discards all files and temp images and resets to empty
- After a successful "Merge & Save PDF", the file list and page grid are cleared automatically

#### Error Handling
| Error | Behaviour |
|-------|-----------|
| Password-protected PDF | Skip file, show banner: "'{filename}' is password-protected and cannot be opened." |
| Corrupt / truncated PDF | Skip file, show banner: "'{filename}' could not be read and was skipped." |
| Unreadable image file | Skip file, show banner with filename and reason |
| Out of disk space during merge | Abort merge, delete partial output, show error dialog |
| Output path not writable | Show error dialog prompting user to choose a different save location |

Skipped files are highlighted in red in the file list. The user can remove them and retry.

---

## Data Model

### ScanSession (in-memory)
```csharp
class ScanSession
{
    List<ScannedPage> Batch1 { get; }     // front sides, in scan order
    List<ScannedPage> Batch2 { get; }     // back sides, in scan order (reversed at merge time)
    List<ScannedPage> Merged { get; }     // interleaved result, populated on merge
    ScanSessionState State { get; set; }
}

class ScannedPage : IPageSource
{
    string ImagePath { get; private set; }  // temp PNG in scan session directory
    PageRotation Rotation { get; set; }
    int SourceBatch { get; }                // 1 or 2
    bool HasWarning { get; set; }           // partial/damaged flag

    void ReplaceImage(string newPath)       // updates ImagePath, clears HasWarning
    {
        ImagePath = newPath;
        HasWarning = false;
    }
}

// Used by Merge Documents for imported files
class ImportedPage : IPageSource
{
    string ImagePath { get; }       // rasterised PNG in merge session temp directory
    PageRotation Rotation { get; set; }
    string SourceFileName { get; }  // original filename shown as thumbnail label
}
```

### Temp Storage

**Scan sessions:** `%TEMP%\PdfUtility\scan-<session-guid>\`
- Format: PNG per scanned page
- Cleanup: on app exit, on explicit session discard, or on app startup for scan session directories older than 7 days

**Merge sessions:** `%TEMP%\PdfUtility\merge-<session-guid>\`
- Created when the user adds the first file to Merge Documents
- Contains rasterised PNG images extracted from each PDF page via PDFsharp rendering; original image files (JPG, PNG, etc.) are referenced directly and not copied
- A `MergeSessionService` (in `PdfUtility.Pdf`) owns extraction and cleanup — the Merge Documents ViewModel calls it, not the other way around
- Cleanup: when the user clicks "Merge & Save PDF" (success or failure), when the Merge Documents tab is explicitly cleared, on app exit, or on app startup for merge session directories older than 7 days

**Both session types:** JPEG compression applied only at final PDF assembly, never during scanning or import.

---

## Settings

Persisted to `%AppData%\PdfUtility\settings.json`:

```json
{
  "scanDpi": 300,
  "colorMode": "Color",
  "pdfFormat": "Standard",
  "paperSize": "Letter",
  "jpegQuality": 85,
  "defaultSaveFolder": "",
  "scannerBackend": "Naps2"
}
```

| Setting | Default | Options |
|---------|---------|---------|
| Scan DPI | 300 | 150, 300, 600 |
| Color Mode | Color | Color, Grayscale, BlackAndWhite |
| PDF Format | Standard | Standard, PdfA |
| Paper Size | Letter | Letter (8.5×11"), Legal (8.5×14"), Auto-detect |
| JPEG Quality | 85 | 1–100 slider |
| Default Save Folder | (last used) | Folder picker |
| Scanner Backend | Naps2 | Naps2, EpsonNative (future) |

DPI, Color Mode, PDF Format, and Paper Size are exposed as dropdowns in the main toolbar for quick access. All toolbar changes persist to `settings.json` immediately.

The Settings Dialog (modal, OK/Cancel) controls JPEG Quality, Default Save Folder, and Scanner Backend. Changes take effect only on OK.

### Paper Size Handling
- **Letter** and **Legal** sizes are passed to NAPS2.Sdk's `ScanOptions.PaperSize` so the ADF feeds at the correct length
- **Auto-detect** uses the scanner's hardware detection where supported by the Epson ET-4850
- PDF page dimensions are set to match the selected paper size — no cropping or scaling applied
- Mixed paper sizes in a single batch are not supported; user selects one size per scan session

---

## Performance

### ScanSnap-like Speed Targets
- First thumbnail appears within 1 second of first page completing
- No UI freeze at any point during scanning
- No per-page delay between ADF feeds

### Implementation

1. **Async page streaming** — NAPS2 `ScanningContext` fires per-page callback; each page dispatched to UI via `Dispatcher.InvokeAsync` immediately
2. **Background scanning thread** — scan loop never blocks the UI thread
3. **PNG temp write** — fast lossless write during scan; JPEG compression deferred to save time
4. **Pre-warmed scanner** — NAPS2 context initialised and devices enumerated on app startup, not on first scan click
5. **ADF continuous feed** — NAPS2 native ADF loop runs at hardware max feed rate, no polling between pages

---

## Error Recovery — Paper Jams & Feeder Errors

On `ScanErrorException` from NAPS2:

1. Scanning stops; state transitions to `Batch1Error` or `Batch2Error`
2. Pages scanned so far are preserved
3. Error banner displayed: **"Paper jam or feeder error — fix the jam, then choose how to continue"**
4. The last page delivered before the error is flagged with ⚠ badge (may be partial)

**Recovery options:**
| Option | Behaviour |
|--------|-----------|
| Continue Scanning | User fixes jam; app issues a new `ScanBatchAsync` call appending to the current batch from the next sheet. State returns to `Batch1Scanning` / `Batch2Scanning`. |
| Rescan Last Page | Discards the last page in the current batch (the potentially damaged one); issues a new `ScanBatchAsync` starting from that sheet. |
| Done with Batch | Accepts pages scanned so far; transitions to `Batch1Complete` or `Batch2Complete`. |

**Granularity of "Rescan Last Page":** Always refers to the last `ScannedPage` object appended to the current batch list before the error — regardless of how many `Continue Scanning` cycles have occurred. If the error occurred on the very first page of a new `Continue Scanning` invocation (i.e., no new pages were added before the error), "Rescan Last Page" is disabled and only "Continue Scanning" and "Done with Batch" are offered.

If a damaged page can't re-feed via ADF, user can use **Replace** (flatbed scan) on the flagged thumbnail.

---

## Scanner Backend Abstraction

`IScannerBackend` decouples scanner logic from the UI. The Settings dialog exposes a **Scanner Backend** dropdown:

- **NAPS2 (default)** — `Naps2ScannerBackend` wraps NAPS2.Sdk, handles WIA/TWAIN automatically
- **Epson Native (future)** — `EpsonScannerBackend` will wrap Epson ESCI/ESC2 SDK, selectable per-user if NAPS2 has reliability issues on their hardware

Adding the Epson backend requires only implementing `IScannerBackend` — no changes to UI or PDF logic.

---

## UI Structure

### Main Window
- Tab bar: **Scan Double Sided** | **Merge Documents**
- Toolbar (top right): DPI dropdown, Color Mode dropdown, PDF Format dropdown, Paper Size dropdown, Settings button

### Settings Dialog (modal)
- JPEG Quality slider
- Default Save Folder picker
- Scanner Backend dropdown
- About / version info
- OK / Cancel buttons — changes apply only on OK

---

## Installer

- Built with Inno Setup
- Single `.exe` installer, no separate runtime required (.NET 8 bundled via self-contained publish)
- Installs to `%ProgramFiles%\PdfUtility\`
- Creates Start Menu shortcut and optional desktop shortcut
- Uninstaller included
