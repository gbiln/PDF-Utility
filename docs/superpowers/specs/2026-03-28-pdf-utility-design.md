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
    Task<ScannedPage> ScanSingleFlatbedAsync(ScanOptions options);
}

// PDF assembly
interface IPdfBuilder
{
    Task BuildAsync(IEnumerable<ScannedPage> pages, PdfBuildOptions options, string outputPath);
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
       Batch1Scanning    Batch1Complete    BatchPaused(Error)
                              │                 │
                       [Scan Other Side]  [Continue/Rescan/Done]
                              │
                        Batch2Scanning
                              │
                    page-by-page preview updates
                              │
              ┌───────────────┼────────────────┐
     [Continue Scanning]  [Done Batch 2]  [Feeder Error]
                              │
                        Batch2Complete
                              │
                       [Merge Document]
                              │
                           Saved
```

#### Scan Controls (left panel, ~220px)

**Batch 1 section:**
- **Start Scanning Batch 1** — initiates ADF scan, enables live preview
- **Continue Scanning** — feeds next stack into ADF, appends to Batch 1
- **Done with Batch 1** — locks Batch 1, unlocks Batch 2 controls

**Batch 2 section** (disabled until Batch 1 complete):
- **Scan Other Side** — initiates ADF scan for back sides
- **Continue Scanning** — appends more back-side pages
- **Done with Batch 2** — locks Batch 2, unlocks Merge

**Final action** (disabled until both batches complete):
- **Merge Document** — interleaves batches, prompts Save As dialog

#### Page Count Mismatch
If `Batch1.Count != Batch2.Count`, show a warning banner at merge time:
> "⚠ Batch mismatch: X vs Y pages — extra pages will be appended at the end."
User can proceed or cancel to fix.

#### Live Preview (right panel, 3/4 width)
- Thumbnails appear as each page completes scanning
- Each thumbnail shows: page number badge, source label, **Replace** link
- Status bar shows current scan progress: "Scanning page 4… (ADF feeding)"
- Partial/damaged pages flagged with ⚠ badge

#### Page Replacement (Flatbed)
1. User clicks **Replace** on any thumbnail
2. App triggers single flatbed scan via `IScannerBackend.ScanSingleFlatbedAsync`
3. Replaced `ScannedPage` image updated in-place
4. Thumbnail refreshes immediately

#### Interleaving Logic
```
merged = []
for i in range(max(len(batch1), len(batch2))):
    if i < len(batch1): merged.append(batch1[i])
    if i < len(batch2): merged.append(batch2[i])
```

---

### 2. Merge Documents (Tab 2)

Combines multiple PDFs and/or image files into a single PDF with full page-level reordering.

#### Layout
- **Left panel (1/4):** File list with add/remove. Accepts PDF, JPG, PNG, TIFF via drag-drop or file browser.
- **Right panel (3/4):** Draggable page thumbnail grid. Each thumbnail shows page number badge, source file label, and **Delete** button.
- **Merge & Save PDF** button at bottom of left panel.

#### Page Reordering
Pages are reordered by dragging thumbnails in the grid. The merge output respects the visual order shown, regardless of source file order.

#### Supported Input Formats
PDF, JPG, JPEG, PNG, TIFF, BMP

---

## Data Model

### ScanSession (in-memory)
```csharp
class ScanSession
{
    List<ScannedPage> Batch1 { get; }   // front sides
    List<ScannedPage> Batch2 { get; }   // back sides
    List<ScannedPage> Merged { get; }   // interleaved result
    ScanSessionState State { get; }
}

class ScannedPage
{
    string ImagePath { get; }   // temp PNG on disk
    int Rotation { get; set; }  // 0 / 90 / 180 / 270
    int SourceBatch { get; }    // 1 or 2
    bool HasWarning { get; set; } // partial/damaged flag
}
```

### Temp Storage
- Location: `%TEMP%\PdfUtility\<session-guid>\`
- Format: PNG (lossless, fast write during scan)
- Cleanup: deleted on app exit or explicit session discard
- JPEG compression applied only at final PDF assembly

---

## Settings

Persisted to `%AppData%\PdfUtility\settings.json`:

```json
{
  "scanDpi": 300,
  "colorMode": "Color",
  "pdfFormat": "Standard",
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

DPI, Color Mode, PDF Format, and Paper Size are exposed as dropdowns in the main toolbar for quick access. All changes persist immediately.

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

1. Scanning pauses immediately
2. Pages scanned so far are preserved
3. Error banner displayed: **"Paper jam or feeder error — fix the jam, then choose how to continue"**
4. Any partial/incomplete last page flagged with ⚠ badge

**Recovery options:**
| Option | Behaviour |
|--------|-----------|
| Continue Scanning | User fixes jam, ADF resumes from next sheet |
| Rescan Last Page | Discards last (potentially damaged) page, rescans it |
| Done with Batch | Accepts pages scanned so far, ends batch |

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
- Toolbar (top right): DPI dropdown, Color Mode dropdown, PDF Format dropdown, Settings button

### Settings Dialog
- JPEG Quality slider
- Default Save Folder picker
- Scanner Backend dropdown
- About / version info

---

## Installer

- Built with Inno Setup
- Single `.exe` installer, no separate runtime required (.NET 8 bundled via self-contained publish)
- Installs to `%ProgramFiles%\PdfUtility\`
- Creates Start Menu shortcut and optional desktop shortcut
- Uninstaller included
