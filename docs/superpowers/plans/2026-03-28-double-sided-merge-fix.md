# Double-Sided Scan Merge Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the file-name collision that causes Batch 2 to overwrite Batch 1's scanned images, producing a garbled/duplicated PDF.

**Architecture:** Add one local variable in `Naps2ScannerBackend.ScanBatchAsync` to route each batch's images into its own subdirectory (`batch1/`, `batch2/`). The fix is isolated to the scanner backend; no interface, ViewModel, or merge-algorithm changes are needed.

**Tech Stack:** C# / .NET 10, xUnit, dotnet CLI

---

## File Map

| File | Change |
|------|--------|
| `src/PdfUtility.Scanning/Naps2ScannerBackend.cs` | Derive `batchDir`; create it; use it for `imagePath` |
| `tests/PdfUtility.Scanning.Tests/` | New test project — tests that verify filenames land in the correct subdirectory |

> There is no existing `PdfUtility.Scanning.Tests` project. It must be created in Task 1.

---

### Task 1: Create the Scanning test project

**Files:**
- Create: `tests/PdfUtility.Scanning.Tests/PdfUtility.Scanning.Tests.csproj`
- Modify: `PdfUtility.slnx` (add project)

- [ ] **Step 1: Scaffold the test project**

```bash
cd C:/projects/PDF-Utility
dotnet new xunit -n PdfUtility.Scanning.Tests \
  --output tests/PdfUtility.Scanning.Tests \
  --framework net10.0-windows
```

Expected: `tests/PdfUtility.Scanning.Tests/` folder created.

- [ ] **Step 2: Add project reference to Core and Scanning**

```bash
cd C:/projects/PDF-Utility
dotnet add tests/PdfUtility.Scanning.Tests/PdfUtility.Scanning.Tests.csproj \
  reference src/PdfUtility.Core/PdfUtility.Core.csproj
dotnet add tests/PdfUtility.Scanning.Tests/PdfUtility.Scanning.Tests.csproj \
  reference src/PdfUtility.Scanning/PdfUtility.Scanning.csproj
```

- [ ] **Step 3: Add the test project to the solution**

```bash
cd C:/projects/PDF-Utility
dotnet sln PdfUtility.slnx add tests/PdfUtility.Scanning.Tests/PdfUtility.Scanning.Tests.csproj
```

- [ ] **Step 4: Delete the placeholder UnitTest1.cs**

```bash
rm tests/PdfUtility.Scanning.Tests/UnitTest1.cs
```

- [ ] **Step 5: Verify the project builds**

```bash
cd C:/projects/PDF-Utility
dotnet build tests/PdfUtility.Scanning.Tests/PdfUtility.Scanning.Tests.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add tests/PdfUtility.Scanning.Tests/ PdfUtility.slnx
git commit -m "test: scaffold PdfUtility.Scanning.Tests project"
```

---

### Task 2: Write the failing test for per-batch subdirectories

**Context:** `Naps2ScannerBackend` requires a real WIA/NAPS2 scanner, so we can't test it directly in unit tests without a device. Instead, write a **focused integration-style test against `FakeScannerBackend`** in `PdfUtility.App.Tests` that asserts the `ScannedPage.ImagePath` values are in distinct subdirectories for each batch. This does not require a real scanner and proves the ViewModel+session plumbing passes the right paths through.

**Files:**
- Modify: `tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs`

- [ ] **Step 1: Read the existing ViewModel tests to understand the test setup pattern**

Read: `tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs`

Look for: how `FakeScannerBackend` is wired up, how `StartBatch1` and `ScanOtherSide` are invoked, and what assertions already exist.

- [ ] **Step 2: Add the failing test**

Add this test to `ScanDoubleSidedViewModelTests.cs`. Follow the same local-variable pattern used throughout that file (`var fake = new FakeScannerBackend(); var vm = CreateVm(fake); vm.SelectedDevice = "Fake Scanner";`):

```csharp
[Fact]
public async Task MergedPages_Batch1AndBatch2_HaveDistinctSubdirectories()
{
    // Arrange — fake scanner yields one front page then one back page
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(new List<string> { "batch1/page_0000.png" });
    fake.BatchQueue.Enqueue(new List<string> { "batch2/page_0000.png" });
    var vm = CreateVm(fake);
    vm.SelectedDevice = "Fake Scanner";  // required: CanStartBatch1 is false without this

    // Act
    await vm.StartBatch1Command.ExecuteAsync(null);
    await vm.DoneCurrentBatchCommand.ExecuteAsync(null);
    await vm.ScanOtherSideCommand.ExecuteAsync(null);
    await vm.DoneCurrentBatchCommand.ExecuteAsync(null);

    var merged = vm.GetMergedPages();

    // Assert — both pages exist and are in distinct subdirectories
    Assert.Equal(2, merged.Count);
    Assert.Contains("batch1", merged[0].ImagePath, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("batch2", merged[1].ImagePath, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("batch2", merged[0].ImagePath, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("batch1", merged[1].ImagePath, StringComparison.OrdinalIgnoreCase);
}
```

> **Note:** `FakeScannerBackend` returns paths from `BatchQueue` as-is (line 52 in `FakeScannerBackend.cs`). The fake paths contain `batch1/` and `batch2/` by convention; the real backend will produce them after the fix in Task 3. This test validates the ViewModel+merge plumbing is correct end-to-end.

- [ ] **Step 3: Run the test to verify it passes (it should — FakeScannerBackend returns queued paths verbatim)**

```bash
cd C:/projects/PDF-Utility
dotnet test tests/PdfUtility.App.Tests/ --filter "MergedPages_Batch1AndBatch2_HaveDistinctSubdirectories" -v
```

Expected: PASS.

- [ ] **Step 4: Add a regression test that would have caught the original bug**

Add this test to `ScanDoubleSidedViewModelTests.cs`:

```csharp
[Fact]
public async Task MergedPages_TwoSheets_DoesNotDuplicateImages()
{
    // Arrange — two fronts, two backs (ADF-reversed order)
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(new List<string>
    {
        "batch1/page_0000.png",  // front of sheet 1
        "batch1/page_0001.png",  // front of sheet 2
    });
    fake.BatchQueue.Enqueue(new List<string>
    {
        "batch2/page_0000.png",  // back of sheet 2 (ADF reversal — last sheet scanned first)
        "batch2/page_0001.png",  // back of sheet 1
    });
    var vm = CreateVm(fake);
    vm.SelectedDevice = "Fake Scanner";

    await vm.StartBatch1Command.ExecuteAsync(null);
    await vm.DoneCurrentBatchCommand.ExecuteAsync(null);
    await vm.ScanOtherSideCommand.ExecuteAsync(null);
    await vm.DoneCurrentBatchCommand.ExecuteAsync(null);

    var merged = vm.GetMergedPages();

    // 4 distinct image paths — no duplicates
    Assert.Equal(4, merged.Count);
    Assert.Equal(4, merged.Select(p => p.ImagePath).Distinct().Count());

    // Correct interleave order: F1, B1, F2, B2
    Assert.Equal("batch1/page_0000.png", merged[0].ImagePath); // front sheet 1
    Assert.Equal("batch2/page_0001.png", merged[1].ImagePath); // back sheet 1 (reversed)
    Assert.Equal("batch1/page_0001.png", merged[2].ImagePath); // front sheet 2
    Assert.Equal("batch2/page_0000.png", merged[3].ImagePath); // back sheet 2 (reversed)
}
```

- [ ] **Step 5: Run the test**

```bash
cd C:/projects/PDF-Utility
dotnet test tests/PdfUtility.App.Tests/ --filter "MergedPages_TwoSheets_DoesNotDuplicateImages" -v
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs
git commit -m "test: assert batch subdirectory isolation and no duplicate pages in merge"
```

---

### Task 3: Fix the filename collision in the scanner backend

**Files:**
- Modify: `src/PdfUtility.Scanning/Naps2ScannerBackend.cs` (lines ~58–78)

- [ ] **Step 1: Open the file and locate the image path construction**

Read: `src/PdfUtility.Scanning/Naps2ScannerBackend.cs`

Find the block in `ScanBatchAsync` that looks like:

```csharp
Directory.CreateDirectory(sessionDirectory);
// ...
var imagePath = Path.Combine(sessionDirectory, $"page_{index:D4}.png");
```

- [ ] **Step 2: Apply the fix**

Replace the relevant lines so `imagePath` is derived from a per-batch subdirectory:

**Before:**
```csharp
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
```

**After:**
```csharp
Directory.CreateDirectory(sessionDirectory);
var batchDir = Path.Combine(sessionDirectory, $"batch{batchNumber}");
Directory.CreateDirectory(batchDir);

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
            var imagePath = Path.Combine(batchDir, $"page_{index:D4}.png");
```

- [ ] **Step 3: Build the Scanning project to confirm no compile errors**

```bash
cd C:/projects/PDF-Utility
dotnet build src/PdfUtility.Scanning/PdfUtility.Scanning.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the full test suite**

```bash
cd C:/projects/PDF-Utility
dotnet test
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PdfUtility.Scanning/Naps2ScannerBackend.cs
git commit -m "fix: isolate batch scan images into per-batch subdirectories

Batch 2 was overwriting Batch 1's page_0000.png / page_0001.png files
because both used the same session directory with index-based names.
Each batch now writes to batch1/ or batch2/ under the session dir,
eliminating the collision that caused duplicate/garbled PDFs."
```

---

### Task 4: Build and smoke-test the app

- [ ] **Step 1: Build the full solution**

```bash
cd C:/projects/PDF-Utility
dotnet build
```

Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 2: Run the app**

```bash
cd C:/projects/PDF-Utility
dotnet run --project src/PdfUtility.App/PdfUtility.App.csproj
```

- [ ] **Step 3: Smoke test with the scanner**

1. Select your scanner
2. Place 2 sheets of paper (4 page faces, numbered so you can verify order)
3. Click **Scan** — Batch 1 should capture the 2 front sides
4. Flip the output stack, feed it back in, click **Scan Other Side**
5. Click **Done**, then **Merge Document**
6. Open the resulting PDF and verify:
   - 4 pages, no duplicates
   - Correct order: front-sheet-1, back-sheet-1, front-sheet-2, back-sheet-2

- [ ] **Step 4: Verify session temp folder layout**

Open `%TEMP%\PdfUtility\scan-<guid>\` and confirm:
```
batch1/
  page_0000.png
  page_0001.png
batch2/
  page_0000.png
  page_0001.png
```
