# Double-Sided Scan Merge Fix — Design Spec

**Date:** 2026-03-28
**Status:** Approved

## Problem

When scanning double-sided documents, Batch 1 (front sides) and Batch 2 (back sides) both save
image files using the same naming scheme (`page_0000.png`, `page_0001.png`, …) in the same
session directory. Batch 2 silently overwrites Batch 1's files. By the time `BuildMergedPages()`
runs, the `ScannedPage` objects from Batch 1 still point to those paths — but the files now
contain the back-side images. Both batches reference the same set of files, producing a
symmetric duplicate pattern in the output PDF (e.g. "4, 2, 2, 4" for a 2-sheet scan).

The merge/interleave algorithm in `ScanSession.BuildMergedPages()` is correct and requires no
changes.

## Root Cause

`Naps2ScannerBackend.ScanBatchAsync` (line 73):

```csharp
var imagePath = Path.Combine(sessionDirectory, $"page_{index:D4}.png");
```

`index` resets to `startingPageIndex` (always 0 for a fresh batch) on each batch call, so both
batches collide on the same filenames.

## Fix

Use a per-batch subdirectory so filenames are structurally isolated:

```csharp
var batchDir = Path.Combine(sessionDirectory, $"batch{batchNumber}");
Directory.CreateDirectory(batchDir);
var imagePath = Path.Combine(batchDir, $"page_{index:D4}.png");
```

Session directory layout after the fix:

```
scan-<guid>/
  batch1/
    page_0000.png   ← front of sheet 1
    page_0001.png   ← front of sheet 2
    …
  batch2/
    page_0000.png   ← back of last sheet (ADF-reversed)
    page_0001.png   ← back of second-to-last sheet
    …
```

## Scope

| File | Change |
|------|--------|
| `src/PdfUtility.Scanning/Naps2ScannerBackend.cs` | Derive `batchDir` from `sessionDirectory + batchNumber`; use it when building `imagePath` |

No other files require changes:
- `IScannerBackend` interface — unchanged (signature unaffected)
- `ScanDoubleSidedViewModel` — unchanged (passes `sessionDirectory` as-is)
- `ScanSession.BuildMergedPages()` — unchanged (already correct)
- `ScanSession.DiscardTempFiles()` — unchanged (iterates `ScannedPage.ImagePath` values directly)
- `ScanSingleFlatbedAsync` — unchanged (uses GUID suffix, already collision-safe)

## Expected Result

For a 10-page document (5 sheets, double-sided):

| Scan order | File | Content |
|------------|------|---------|
| Batch 1, scan 1 | `batch1/page_0000.png` | Front of sheet 1 (doc p.1) |
| Batch 1, scan 2 | `batch1/page_0001.png` | Front of sheet 2 (doc p.3) |
| … | … | … |
| Batch 2, scan 1 | `batch2/page_0000.png` | Back of sheet 5 (doc p.10) |
| Batch 2, scan 2 | `batch2/page_0001.png` | Back of sheet 4 (doc p.8) |
| … | … | … |

`BuildMergedPages()` reverses Batch 2 and interleaves, producing:

```
doc p.1, doc p.2, doc p.3, doc p.4, … doc p.10
```
