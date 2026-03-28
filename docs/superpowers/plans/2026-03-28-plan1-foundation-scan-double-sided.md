# PDF Utility — Plan 1: Foundation + Scan Double Sided

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a working Windows desktop app that scans double-sided documents via an Epson ET-4850 ADF and saves a merged, correctly-ordered PDF.

**Architecture:** Four .NET 8 projects (Core models/interfaces, Scanning backend, PDF assembly, WPF app) with three xUnit test projects. NAPS2.Sdk drives the scanner; PDFsharp assembles the PDF. The WPF UI uses WPF-UI (Fluent) and CommunityToolkit.Mvvm. All scanner interaction flows through `IScannerBackend` so the Epson native backend can be added later without touching the UI.

**Tech Stack:** C# / .NET 8-windows, WPF, WPF-UI, CommunityToolkit.Mvvm, NAPS2.Sdk, NAPS2.Sdk.Worker.Win32, PDFsharp, Microsoft.Extensions.Hosting, xUnit, Moq

**Spec:** `docs/superpowers/specs/2026-03-28-pdf-utility-design.md`

---

## File Map

```
src/
  PdfUtility.Core/
    Exceptions/ScannerException.cs
    Interfaces/IPdfBuilder.cs
    Interfaces/IScannerBackend.cs
    Interfaces/IUserSettings.cs
    Models/IPageSource.cs
    Models/PageRotation.cs
    Models/PdfBuildOptions.cs
    Models/ScanOptions.cs
    Models/ScanSession.cs
    Models/ScanSessionState.cs
    Models/ScannedPage.cs
    Models/UserPreferences.cs
  PdfUtility.Scanning/
    Naps2ScannerBackend.cs
  PdfUtility.Pdf/
    PdfSharpPdfBuilder.cs
  PdfUtility.App/
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Services/InMemoryUserSettings.cs
    ViewModels/MainViewModel.cs
    ViewModels/PageThumbnailViewModel.cs
    ViewModels/ScanDoubleSidedViewModel.cs
    Views/Controls/PageThumbnailControl.xaml
    Views/Controls/PageThumbnailControl.xaml.cs
    Views/ScanDoubleSidedView.xaml
    Views/ScanDoubleSidedView.xaml.cs
tests/
  PdfUtility.Core.Tests/
    Models/ScannedPageTests.cs
    Models/ScanSessionInterleavingTests.cs
  PdfUtility.Pdf.Tests/
    PdfSharpPdfBuilderTests.cs
  PdfUtility.App.Tests/
    Fakes/FakeScannerBackend.cs
    ViewModels/ScanDoubleSidedViewModelTests.cs
```

---

## Task 1: Solution & Project Scaffold

**Files:**
- Create: `PdfUtility.sln`
- Create: `src/PdfUtility.Core/PdfUtility.Core.csproj`
- Create: `src/PdfUtility.Scanning/PdfUtility.Scanning.csproj`
- Create: `src/PdfUtility.Pdf/PdfUtility.Pdf.csproj`
- Create: `src/PdfUtility.App/PdfUtility.App.csproj`
- Create: `tests/PdfUtility.Core.Tests/PdfUtility.Core.Tests.csproj`
- Create: `tests/PdfUtility.Pdf.Tests/PdfUtility.Pdf.Tests.csproj`
- Create: `tests/PdfUtility.App.Tests/PdfUtility.App.Tests.csproj`
- Create: `.gitignore`

- [ ] **Step 1: Create solution and projects**

Run from `C:/Projects/PDF-Utility`:

```bash
dotnet new sln -n PdfUtility

dotnet new classlib -n PdfUtility.Core -o src/PdfUtility.Core -f net8.0-windows
dotnet new classlib -n PdfUtility.Scanning -o src/PdfUtility.Scanning -f net8.0-windows
dotnet new classlib -n PdfUtility.Pdf -o src/PdfUtility.Pdf -f net8.0-windows
dotnet new wpf -n PdfUtility.App -o src/PdfUtility.App -f net8.0-windows

dotnet new xunit -n PdfUtility.Core.Tests -o tests/PdfUtility.Core.Tests -f net8.0-windows
dotnet new xunit -n PdfUtility.Pdf.Tests -o tests/PdfUtility.Pdf.Tests -f net8.0-windows
dotnet new xunit -n PdfUtility.App.Tests -o tests/PdfUtility.App.Tests -f net8.0-windows

dotnet sln add src/PdfUtility.Core/PdfUtility.Core.csproj
dotnet sln add src/PdfUtility.Scanning/PdfUtility.Scanning.csproj
dotnet sln add src/PdfUtility.Pdf/PdfUtility.Pdf.csproj
dotnet sln add src/PdfUtility.App/PdfUtility.App.csproj
dotnet sln add tests/PdfUtility.Core.Tests/PdfUtility.Core.Tests.csproj
dotnet sln add tests/PdfUtility.Pdf.Tests/PdfUtility.Pdf.Tests.csproj
dotnet sln add tests/PdfUtility.App.Tests/PdfUtility.App.Tests.csproj
```

- [ ] **Step 2: Add project references**

```bash
dotnet add src/PdfUtility.Scanning/PdfUtility.Scanning.csproj reference src/PdfUtility.Core/PdfUtility.Core.csproj
dotnet add src/PdfUtility.Pdf/PdfUtility.Pdf.csproj reference src/PdfUtility.Core/PdfUtility.Core.csproj
dotnet add src/PdfUtility.App/PdfUtility.App.csproj reference src/PdfUtility.Core/PdfUtility.Core.csproj
dotnet add src/PdfUtility.App/PdfUtility.App.csproj reference src/PdfUtility.Scanning/PdfUtility.Scanning.csproj
dotnet add src/PdfUtility.App/PdfUtility.App.csproj reference src/PdfUtility.Pdf/PdfUtility.Pdf.csproj
dotnet add tests/PdfUtility.Core.Tests/PdfUtility.Core.Tests.csproj reference src/PdfUtility.Core/PdfUtility.Core.csproj
dotnet add tests/PdfUtility.Pdf.Tests/PdfUtility.Pdf.Tests.csproj reference src/PdfUtility.Core/PdfUtility.Core.csproj
dotnet add tests/PdfUtility.Pdf.Tests/PdfUtility.Pdf.Tests.csproj reference src/PdfUtility.Pdf/PdfUtility.Pdf.csproj
dotnet add tests/PdfUtility.App.Tests/PdfUtility.App.Tests.csproj reference src/PdfUtility.Core/PdfUtility.Core.csproj
dotnet add tests/PdfUtility.App.Tests/PdfUtility.App.Tests.csproj reference src/PdfUtility.Scanning/PdfUtility.Scanning.csproj
dotnet add tests/PdfUtility.App.Tests/PdfUtility.App.Tests.csproj reference src/PdfUtility.Pdf/PdfUtility.Pdf.csproj
dotnet add tests/PdfUtility.App.Tests/PdfUtility.App.Tests.csproj reference src/PdfUtility.App/PdfUtility.App.csproj
```

- [ ] **Step 3: Add NuGet packages**

```bash
# Scanning
dotnet add src/PdfUtility.Scanning/PdfUtility.Scanning.csproj package NAPS2.Sdk
dotnet add src/PdfUtility.Scanning/PdfUtility.Scanning.csproj package NAPS2.Sdk.Worker.Win32

# PDF
dotnet add src/PdfUtility.Pdf/PdfUtility.Pdf.csproj package PDFsharp

# App
dotnet add src/PdfUtility.App/PdfUtility.App.csproj package WPF-UI
dotnet add src/PdfUtility.App/PdfUtility.App.csproj package CommunityToolkit.Mvvm
dotnet add src/PdfUtility.App/PdfUtility.App.csproj package Microsoft.Extensions.DependencyInjection
dotnet add src/PdfUtility.App/PdfUtility.App.csproj package Microsoft.Extensions.Hosting

# Tests
dotnet add tests/PdfUtility.Core.Tests/PdfUtility.Core.Tests.csproj package Moq
dotnet add tests/PdfUtility.Pdf.Tests/PdfUtility.Pdf.Tests.csproj package Moq
dotnet add tests/PdfUtility.Pdf.Tests/PdfUtility.Pdf.Tests.csproj package System.Drawing.Common
dotnet add tests/PdfUtility.App.Tests/PdfUtility.App.Tests.csproj package Moq
```

- [ ] **Step 4: Create .gitignore**

```
bin/
obj/
.vs/
*.user
.superpowers/
```

- [ ] **Step 5: Delete auto-generated placeholder files**

```bash
rm src/PdfUtility.Core/Class1.cs
rm src/PdfUtility.Scanning/Class1.cs
rm src/PdfUtility.Pdf/Class1.cs
```

- [ ] **Step 6: Verify build**

```bash
dotnet build
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: scaffold solution with 4 projects and 3 test projects"
```

---

## Task 2: Core — Enums, Exceptions, Value Objects

**Files:**
- Create: `src/PdfUtility.Core/Models/PageRotation.cs`
- Create: `src/PdfUtility.Core/Models/ScanSessionState.cs`
- Create: `src/PdfUtility.Core/Models/IPageSource.cs`
- Create: `src/PdfUtility.Core/Models/ScanOptions.cs`
- Create: `src/PdfUtility.Core/Models/PdfBuildOptions.cs`
- Create: `src/PdfUtility.Core/Models/UserPreferences.cs`
- Create: `src/PdfUtility.Core/Exceptions/ScannerException.cs`

- [ ] **Step 1: Write `PageRotation.cs`**

```csharp
// src/PdfUtility.Core/Models/PageRotation.cs
namespace PdfUtility.Core.Models;

public enum PageRotation
{
    None = 0,
    CW90 = 90,
    CW180 = 180,
    CW270 = 270
}
```

- [ ] **Step 2: Write `ScanSessionState.cs`**

```csharp
// src/PdfUtility.Core/Models/ScanSessionState.cs
namespace PdfUtility.Core.Models;

public enum ScanSessionState
{
    Idle,
    Batch1Scanning,
    Batch1Paused,    // ADF emptied naturally; user can continue or declare done
    Batch1Error,     // feeder/jam error during Batch 1
    Batch1Complete,
    Batch2Scanning,
    Batch2Paused,    // ADF emptied naturally; user can continue or declare done
    Batch2Error,     // feeder/jam error during Batch 2
    Batch2Complete,
    MergeReady,
    Saved
}
```

- [ ] **Step 3: Write `IPageSource.cs`**

```csharp
// src/PdfUtility.Core/Models/IPageSource.cs
namespace PdfUtility.Core.Models;

public interface IPageSource
{
    string ImagePath { get; }
    PageRotation Rotation { get; set; }
}
```

- [ ] **Step 4: Write `ScanOptions.cs`**

```csharp
// src/PdfUtility.Core/Models/ScanOptions.cs
namespace PdfUtility.Core.Models;

public enum ColorMode { Color, Grayscale, BlackAndWhite }
public enum PaperSize { Letter, Legal, AutoDetect }

public class ScanOptions
{
    public int Dpi { get; init; } = 300;
    public ColorMode ColorMode { get; init; } = ColorMode.Color;
    public PaperSize PaperSize { get; init; } = PaperSize.Letter;
}
```

- [ ] **Step 5: Write `PdfBuildOptions.cs`**

```csharp
// src/PdfUtility.Core/Models/PdfBuildOptions.cs
namespace PdfUtility.Core.Models;

public enum PdfFormat { Standard, PdfA }

public class PdfBuildOptions
{
    public PdfFormat Format { get; init; } = PdfFormat.Standard;
    public int JpegQuality { get; init; } = 85;
    public PaperSize PaperSize { get; init; } = PaperSize.Letter;
}
```

- [ ] **Step 6: Write `UserPreferences.cs`**

```csharp
// src/PdfUtility.Core/Models/UserPreferences.cs
namespace PdfUtility.Core.Models;

public class UserPreferences
{
    public int ScanDpi { get; set; } = 300;
    public ColorMode ColorMode { get; set; } = ColorMode.Color;
    public PdfFormat PdfFormat { get; set; } = PdfFormat.Standard;
    public PaperSize PaperSize { get; set; } = PaperSize.Letter;
    public int JpegQuality { get; set; } = 85;
    public string DefaultSaveFolder { get; set; } = string.Empty;
    public string ScannerBackend { get; set; } = "Naps2";
}
```

- [ ] **Step 7: Write `ScannerException.cs`**

```csharp
// src/PdfUtility.Core/Exceptions/ScannerException.cs
namespace PdfUtility.Core.Exceptions;

public class ScannerException : Exception
{
    public ScannerException(string message) : base(message) { }
    public ScannerException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 8: Build and verify**

```bash
dotnet build src/PdfUtility.Core/PdfUtility.Core.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 9: Commit**

```bash
git add src/PdfUtility.Core/
git commit -m "feat(core): add enums, exceptions, and value objects"
```

---

## Task 3: Core — ScannedPage + ScanSession + Interleaving

**Files:**
- Create: `src/PdfUtility.Core/Models/ScannedPage.cs`
- Create: `src/PdfUtility.Core/Models/ScanSession.cs`
- Create: `tests/PdfUtility.Core.Tests/Models/ScannedPageTests.cs`
- Create: `tests/PdfUtility.Core.Tests/Models/ScanSessionInterleavingTests.cs`

- [ ] **Step 1: Write failing tests for `ScannedPage`**

```csharp
// tests/PdfUtility.Core.Tests/Models/ScannedPageTests.cs
using PdfUtility.Core.Models;

namespace PdfUtility.Core.Tests.Models;

public class ScannedPageTests
{
    [Fact]
    public void Constructor_SetsImagePathAndBatch()
    {
        var page = new ScannedPage("path/to/page.png", sourceBatch: 1);
        Assert.Equal("path/to/page.png", page.ImagePath);
        Assert.Equal(1, page.SourceBatch);
        Assert.Equal(PageRotation.None, page.Rotation);
        Assert.False(page.HasWarning);
    }

    [Fact]
    public void ReplaceImage_UpdatesPathAndClearsWarning()
    {
        // Create a real temp file so File.Delete doesn't throw
        var oldPath = Path.GetTempFileName();
        var newPath = Path.GetTempFileName();
        try
        {
            var page = new ScannedPage(oldPath, sourceBatch: 1) { HasWarning = true };
            page.ReplaceImage(newPath);
            Assert.Equal(newPath, page.ImagePath);
            Assert.False(page.HasWarning);
            Assert.False(File.Exists(oldPath)); // old file was deleted
        }
        finally
        {
            if (File.Exists(newPath)) File.Delete(newPath);
        }
    }
}
```

- [ ] **Step 2: Write failing tests for interleaving logic**

```csharp
// tests/PdfUtility.Core.Tests/Models/ScanSessionInterleavingTests.cs
using PdfUtility.Core.Models;

namespace PdfUtility.Core.Tests.Models;

public class ScanSessionInterleavingTests
{
    private static ScannedPage Page(string name, int batch) =>
        new ScannedPage(name, batch);

    [Fact]
    public void Interleave_EvenBatches_ProducesCorrectOrder()
    {
        // F1 F2 F3 F4 scanned, then B4 B3 B2 B1 (ADF reverses back sides)
        // After reversing batch2: B1 B2 B3 B4
        // Expected merge: F1 B1 F2 B2 F3 B3 F4 B4
        var session = new ScanSession();
        session.Batch1.Add(Page("F1", 1));
        session.Batch1.Add(Page("F2", 1));
        session.Batch1.Add(Page("F3", 1));
        session.Batch1.Add(Page("F4", 1));
        // User feeds stack flipped — ADF delivers backs in reverse order
        session.Batch2.Add(Page("B4", 2));
        session.Batch2.Add(Page("B3", 2));
        session.Batch2.Add(Page("B2", 2));
        session.Batch2.Add(Page("B1", 2));

        var merged = session.BuildMergedPages();

        Assert.Equal(8, merged.Count);
        Assert.Equal("F1", merged[0].ImagePath);
        Assert.Equal("B1", merged[1].ImagePath);
        Assert.Equal("F2", merged[2].ImagePath);
        Assert.Equal("B2", merged[3].ImagePath);
        Assert.Equal("F3", merged[4].ImagePath);
        Assert.Equal("B3", merged[5].ImagePath);
        Assert.Equal("F4", merged[6].ImagePath);
        Assert.Equal("B4", merged[7].ImagePath);
    }

    [Fact]
    public void Interleave_Batch1Longer_AppendsExtrasAtEnd()
    {
        var session = new ScanSession();
        session.Batch1.Add(Page("F1", 1));
        session.Batch1.Add(Page("F2", 1));
        session.Batch1.Add(Page("F3", 1)); // extra
        session.Batch2.Add(Page("B2", 2));
        session.Batch2.Add(Page("B1", 2));

        var merged = session.BuildMergedPages();

        Assert.Equal(5, merged.Count);
        Assert.Equal("F1", merged[0].ImagePath);
        Assert.Equal("B1", merged[1].ImagePath);
        Assert.Equal("F2", merged[2].ImagePath);
        Assert.Equal("B2", merged[3].ImagePath);
        Assert.Equal("F3", merged[4].ImagePath); // extra appended
    }

    [Fact]
    public void Interleave_Batch2Longer_AppendsExtrasAtEnd()
    {
        var session = new ScanSession();
        session.Batch1.Add(Page("F1", 1));
        session.Batch2.Add(Page("B2", 2));
        session.Batch2.Add(Page("B1", 2));
        session.Batch2.Add(Page("B_extra", 2));

        var merged = session.BuildMergedPages();

        Assert.Equal(4, merged.Count);
        Assert.Equal("F1", merged[0].ImagePath);
        Assert.Equal("B1", merged[1].ImagePath);
        Assert.Equal("B2", merged[2].ImagePath);
        Assert.Equal("B_extra", merged[3].ImagePath);
    }

    [Fact]
    public void Interleave_SinglePage_ReturnsSinglePage()
    {
        var session = new ScanSession();
        session.Batch1.Add(Page("F1", 1));

        var merged = session.BuildMergedPages();

        Assert.Single(merged);
        Assert.Equal("F1", merged[0].ImagePath);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/PdfUtility.Core.Tests/ -v n
```

Expected: compilation errors (types don't exist yet)

- [ ] **Step 4: Implement `ScannedPage`**

```csharp
// src/PdfUtility.Core/Models/ScannedPage.cs
namespace PdfUtility.Core.Models;

public class ScannedPage : IPageSource
{
    public string ImagePath { get; private set; }
    public PageRotation Rotation { get; set; } = PageRotation.None;
    public int SourceBatch { get; }
    public bool HasWarning { get; set; }

    public ScannedPage(string imagePath, int sourceBatch)
    {
        ImagePath = imagePath;
        SourceBatch = sourceBatch;
    }

    public void ReplaceImage(string newPath)
    {
        File.Delete(ImagePath);   // delete previous temp PNG immediately
        ImagePath = newPath;
        HasWarning = false;
    }
}
```

- [ ] **Step 5: Implement `ScanSession`**

```csharp
// src/PdfUtility.Core/Models/ScanSession.cs
namespace PdfUtility.Core.Models;

public class ScanSession
{
    public List<ScannedPage> Batch1 { get; } = new();
    public List<ScannedPage> Batch2 { get; } = new();
    public ScanSessionState State { get; set; } = ScanSessionState.Idle;

    /// <summary>
    /// Reverses Batch2 (corrects for ADF stack flip) then interleaves with Batch1.
    /// Extra pages from the longer batch are appended at the end.
    /// </summary>
    public List<ScannedPage> BuildMergedPages()
    {
        var batch2Reversed = Enumerable.Reverse(Batch2).ToList();
        var merged = new List<ScannedPage>();
        int count = Math.Max(Batch1.Count, batch2Reversed.Count);
        for (int i = 0; i < count; i++)
        {
            if (i < Batch1.Count) merged.Add(Batch1[i]);
            if (i < batch2Reversed.Count) merged.Add(batch2Reversed[i]);
        }
        return merged;
    }

    public bool HasPageCountMismatch =>
        Batch1.Count > 0 && Batch2.Count > 0 && Batch1.Count != Batch2.Count;

    public void DiscardTempFiles()
    {
        foreach (var page in Batch1.Concat(Batch2))
        {
            if (File.Exists(page.ImagePath))
                File.Delete(page.ImagePath);
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test tests/PdfUtility.Core.Tests/ -v n
```

Expected: All tests PASS

- [ ] **Step 7: Commit**

```bash
git add src/PdfUtility.Core/ tests/PdfUtility.Core.Tests/
git commit -m "feat(core): add ScannedPage, ScanSession, and interleaving logic with tests"
```

---

## Task 4: Core — Interfaces

**Files:**
- Create: `src/PdfUtility.Core/Interfaces/IScannerBackend.cs`
- Create: `src/PdfUtility.Core/Interfaces/IPdfBuilder.cs`
- Create: `src/PdfUtility.Core/Interfaces/IUserSettings.cs`

These are contracts only — no tests needed for interfaces themselves.

- [ ] **Step 1: Write `IScannerBackend.cs`**

```csharp
// src/PdfUtility.Core/Interfaces/IScannerBackend.cs
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
        string sessionDirectory,
        int startingPageIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a single page from the flatbed glass.
    /// Throws ScannerException on failure.
    /// </summary>
    Task<ScannedPage> ScanSingleFlatbedAsync(
        ScanOptions options,
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all available scanner device names.</summary>
    Task<IReadOnlyList<string>> GetAvailableDevicesAsync();

    /// <summary>Pre-warms the scanner context (call at app startup).</summary>
    Task InitialiseAsync();
}
```

- [ ] **Step 2: Write `IPdfBuilder.cs`**

```csharp
// src/PdfUtility.Core/Interfaces/IPdfBuilder.cs
using PdfUtility.Core.Models;

namespace PdfUtility.Core.Interfaces;

public interface IPdfBuilder
{
    /// <summary>
    /// Assembles IPageSource images into a PDF at outputPath.
    /// Each image is JPEG-compressed at options.JpegQuality.
    /// </summary>
    Task BuildAsync(
        IEnumerable<IPageSource> pages,
        PdfBuildOptions options,
        string outputPath);
}
```

- [ ] **Step 3: Write `IUserSettings.cs`**

```csharp
// src/PdfUtility.Core/Interfaces/IUserSettings.cs
using PdfUtility.Core.Models;

namespace PdfUtility.Core.Interfaces;

public interface IUserSettings
{
    UserPreferences Load();
    void Save(UserPreferences prefs);
}
```

- [ ] **Step 4: Build Core**

```bash
dotnet build src/PdfUtility.Core/PdfUtility.Core.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/PdfUtility.Core/
git commit -m "feat(core): add IScannerBackend, IPdfBuilder, IUserSettings interfaces"
```

---

## Task 5: FakeScannerBackend (for ViewModel tests)

**Files:**
- Create: `tests/PdfUtility.App.Tests/Fakes/FakeScannerBackend.cs`

The real NAPS2 backend requires hardware. This fake lets ViewModel tests run anywhere.

- [ ] **Step 1: Write `FakeScannerBackend`**

```csharp
// tests/PdfUtility.App.Tests/Fakes/FakeScannerBackend.cs
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

    public async IAsyncEnumerable<ScannedPage> ScanBatchAsync(
        ScanOptions options,
        string sessionDirectory,
        int startingPageIndex,
        CancellationToken cancellationToken = default)
    {
        var paths = BatchQueue.Count > 0 ? BatchQueue.Dequeue() : new List<string>();
        int index = startingPageIndex;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ScannedPage(path, sourceBatch: 0);
            index++;
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
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        if (FlatbedShouldFail)
            throw new ScannerException("Fake flatbed failure");
        var path = NextFlatbedImagePath ?? "fake_flatbed.png";
        return Task.FromResult(new ScannedPage(path, sourceBatch: 0));
    }

    public Task<IReadOnlyList<string>> GetAvailableDevicesAsync() =>
        Task.FromResult<IReadOnlyList<string>>(["Fake Scanner"]);

    public Task InitialiseAsync()
    {
        InitialiseCalled = true;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Build test project**

```bash
dotnet build tests/PdfUtility.App.Tests/PdfUtility.App.Tests.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add tests/PdfUtility.App.Tests/
git commit -m "test: add FakeScannerBackend for ViewModel unit tests"
```

---

## Task 6: PdfUtility.Scanning — Naps2ScannerBackend

**Files:**
- Create: `src/PdfUtility.Scanning/Naps2ScannerBackend.cs`

- [ ] **Step 1: Write `Naps2ScannerBackend`**

```csharp
// src/PdfUtility.Scanning/Naps2ScannerBackend.cs
using NAPS2.Scan;
using NAPS2.Images.Gdi;
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

    public async Task InitialiseAsync()
    {
        _context = new ScanningContext(new GdiImageContext());
        _context.WorkerFactory = new LocalWorkerFactory();
        _controller = new ScanController(_context);

        // Enumerate devices on background thread to pre-warm WIA
        var devices = await _controller.GetDeviceList(
            new NAPS2.Scan.ScanOptions { Driver = Driver.Wia });
        _selectedDevice = devices.FirstOrDefault();
    }

    public async Task<IReadOnlyList<string>> GetAvailableDevicesAsync()
    {
        EnsureInitialised();
        var devices = await _controller!.GetDeviceList(
            new NAPS2.Scan.ScanOptions { Driver = Driver.Wia });
        return devices.Select(d => d.Name).ToList();
    }

    public async IAsyncEnumerable<ScannedPage> ScanBatchAsync(
        ScanOptions options,
        string sessionDirectory,
        int startingPageIndex,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        Directory.CreateDirectory(sessionDirectory);

        var naps2Options = BuildNaps2Options(options, PaperSource.Feeder);
        int index = startingPageIndex;

        await foreach (var image in _controller!.Scan(naps2Options).WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imagePath = Path.Combine(sessionDirectory, $"page_{index:D4}.png");
            try
            {
                image.Save(imagePath);
            }
            finally
            {
                image.Dispose();
            }
            yield return new ScannedPage(imagePath, sourceBatch: 0);
            index++;
        }
    }

    public async Task<ScannedPage> ScanSingleFlatbedAsync(
        ScanOptions options,
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        Directory.CreateDirectory(sessionDirectory);

        var naps2Options = BuildNaps2Options(options, PaperSource.Flatbed);
        var imagePath = Path.Combine(sessionDirectory, $"replace_{pageIndex:D4}_{Guid.NewGuid():N}.png");

        await foreach (var image in _controller!.Scan(naps2Options).WithCancellation(cancellationToken))
        {
            try
            {
                image.Save(imagePath);
                return new ScannedPage(imagePath, sourceBatch: 0);
            }
            finally
            {
                image.Dispose();
            }
        }

        throw new ScannerException("Flatbed scan produced no image. Ensure a document is on the glass.");
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
                PaperSize.AutoDetect => null,
                _ => PageSize.Letter
            }
        };
    }

    private void EnsureInitialised()
    {
        if (_controller is null)
            throw new InvalidOperationException("Call InitialiseAsync before scanning.");
    }

    public void Dispose() => _context?.Dispose();
}
```

> **Note:** NAPS2.Sdk API surface may differ slightly by version. Check `NAPS2.Sdk` README on NuGet/GitHub and adjust method names (`ScanController.Scan`, `LocalWorkerFactory` constructor, `PageSize` enum) to match the installed version. The pattern above (async enumerable, one ProcessedImage per page, Save to path) is stable across versions.

- [ ] **Step 2: Build**

```bash
dotnet build src/PdfUtility.Scanning/PdfUtility.Scanning.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/PdfUtility.Scanning/
git commit -m "feat(scanning): add Naps2ScannerBackend with WIA ADF and flatbed support"
```

---

## Task 7: PdfUtility.Pdf — PdfSharpPdfBuilder

**Files:**
- Create: `src/PdfUtility.Pdf/PdfSharpPdfBuilder.cs`
- Create: `tests/PdfUtility.Pdf.Tests/PdfSharpPdfBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/PdfUtility.Pdf.Tests/PdfSharpPdfBuilderTests.cs
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;
using PdfUtility.Pdf;

namespace PdfUtility.Pdf.Tests;

public class PdfSharpPdfBuilderTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), $"PdfBuilderTests_{Guid.NewGuid():N}");

    public PdfSharpPdfBuilderTests() => Directory.CreateDirectory(_testDir);

    public void Dispose() => Directory.Delete(_testDir, recursive: true);

    private string CreateTestPng(string name, int widthPx = 100, int heightPx = 130)
    {
        // Create a minimal valid PNG using System.Drawing
        var path = Path.Combine(_testDir, name);
        using var bmp = new System.Drawing.Bitmap(widthPx, heightPx);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.White);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    [Fact]
    public async Task BuildAsync_SinglePage_CreatesValidPdf()
    {
        var builder = new PdfSharpPdfBuilder();
        var pngPath = CreateTestPng("page1.png");
        var outputPath = Path.Combine(_testDir, "output.pdf");
        var pages = new[] { new TestPageSource(pngPath) };
        var opts = new PdfBuildOptions();

        await builder.BuildAsync(pages, opts, outputPath);

        Assert.True(File.Exists(outputPath));
        Assert.True(new FileInfo(outputPath).Length > 0);
    }

    [Fact]
    public async Task BuildAsync_MultiplePages_CreatesFileWithCorrectSize()
    {
        var builder = new PdfSharpPdfBuilder();
        var pages = Enumerable.Range(1, 3)
            .Select(i => new TestPageSource(CreateTestPng($"page{i}.png")))
            .ToArray();
        var outputPath = Path.Combine(_testDir, "multi.pdf");

        await builder.BuildAsync(pages, new PdfBuildOptions(), outputPath);

        Assert.True(File.Exists(outputPath));
        // Multi-page PDF should be larger than single-page
        var singleOutputPath = Path.Combine(_testDir, "single.pdf");
        await builder.BuildAsync(new[] { pages[0] }, new PdfBuildOptions(), singleOutputPath);
        Assert.True(new FileInfo(outputPath).Length > new FileInfo(singleOutputPath).Length);
    }

    private record TestPageSource(string ImagePath) : IPageSource
    {
        public PageRotation Rotation { get; set; } = PageRotation.None;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/PdfUtility.Pdf.Tests/ -v n
```

Expected: compilation error (PdfSharpPdfBuilder not yet created)

- [ ] **Step 3: Implement `PdfSharpPdfBuilder`**

```csharp
// src/PdfUtility.Pdf/PdfSharpPdfBuilder.cs
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;

namespace PdfUtility.Pdf;

public class PdfSharpPdfBuilder : IPdfBuilder
{
    // A4/Letter dimensions at 300 DPI — we size pages to match the images.
    public Task BuildAsync(
        IEnumerable<IPageSource> pages,
        PdfBuildOptions options,
        string outputPath)
    {
        using var document = new PdfDocument();
        document.Info.Title = "PDF Utility Document";

        foreach (var source in pages)
        {
            using var xImage = XImage.FromFile(source.ImagePath);

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(xImage.PointWidth);
            page.Height = XUnit.FromPoint(xImage.PointHeight);

            // Apply rotation if needed
            using var gfx = XGraphics.FromPdfPage(page);
            if (source.Rotation == PageRotation.None)
            {
                gfx.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
            }
            else
            {
                var cx = page.Width.Point / 2;
                var cy = page.Height.Point / 2;
                gfx.RotateAtTransform((double)source.Rotation, new XPoint(cx, cy));
                gfx.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        document.Save(outputPath);
        return Task.CompletedTask;
    }
}
```

> **Note:** PDFsharp uses `XImage.FromFile` which keeps the source PNG as a reference when building the document. JPEG quality is applied at the PDFsharp level via image compression settings — check the PDFsharp docs for `XImage` JPEG quality options if you need to tune compression. The images are embedded directly into the PDF.

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/PdfUtility.Pdf.Tests/ -v n
```

Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/PdfUtility.Pdf/ tests/PdfUtility.Pdf.Tests/
git commit -m "feat(pdf): add PdfSharpPdfBuilder with tests"
```

---

## Task 8: App — Startup, DI, MainWindow Shell

**Files:**
- Modify: `src/PdfUtility.App/App.xaml`
- Modify: `src/PdfUtility.App/App.xaml.cs`
- Modify: `src/PdfUtility.App/MainWindow.xaml`
- Modify: `src/PdfUtility.App/MainWindow.xaml.cs`
- Create: `src/PdfUtility.App/Services/InMemoryUserSettings.cs`
- Create: `src/PdfUtility.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Write `InMemoryUserSettings`**

```csharp
// src/PdfUtility.App/Services/InMemoryUserSettings.cs
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;

namespace PdfUtility.App.Services;

public class InMemoryUserSettings : IUserSettings
{
    private UserPreferences _prefs = new();
    public UserPreferences Load() => _prefs;
    public void Save(UserPreferences prefs) => _prefs = prefs;
}
```

- [ ] **Step 2: Write `MainViewModel`**

```csharp
// src/PdfUtility.App/ViewModels/MainViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using PdfUtility.Core.Models;

namespace PdfUtility.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _selectedTabIndex = 0;
    [ObservableProperty] private int _scanDpi = 300;
    [ObservableProperty] private ColorMode _colorMode = ColorMode.Color;
    [ObservableProperty] private PdfFormat _pdfFormat = PdfFormat.Standard;
    [ObservableProperty] private PaperSize _paperSize = PaperSize.Letter;

    public int[] DpiOptions { get; } = [150, 300, 600];
    public ColorMode[] ColorModeOptions { get; } = Enum.GetValues<ColorMode>();
    public PdfFormat[] PdfFormatOptions { get; } = Enum.GetValues<PdfFormat>();
    public PaperSize[] PaperSizeOptions { get; } = Enum.GetValues<PaperSize>();
}
```

- [ ] **Step 3: Write DI setup in `App.xaml.cs`**

```csharp
// src/PdfUtility.App/App.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PdfUtility.App.Services;
using PdfUtility.App.ViewModels;
using PdfUtility.Core.Interfaces;
using PdfUtility.Pdf;
using PdfUtility.Scanning;
using System.Windows;

namespace PdfUtility.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IUserSettings, InMemoryUserSettings>();
                services.AddSingleton<IScannerBackend, Naps2ScannerBackend>();
                services.AddSingleton<IPdfBuilder, PdfSharpPdfBuilder>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<ScanDoubleSidedViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        // Pre-warm scanner on background thread
        var scanner = _host.Services.GetRequiredService<IScannerBackend>();
        _ = Task.Run(async () =>
        {
            try { await scanner.InitialiseAsync(); }
            catch { /* scanner not connected at startup — that's fine */ }
        });

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: Update `App.xaml` — remove StartupUri and declare global converters**

```xml
<!-- src/PdfUtility.App/App.xaml -->
<Application x:Class="PdfUtility.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             xmlns:converters="clr-namespace:PdfUtility.App.Converters">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Light" />
                <ui:ControlsDictionary />
            </ResourceDictionary.MergedDictionaries>
            <!-- Global converters — accessible from all views and UserControls -->
            <BooleanToVisibilityConverter x:Key="BoolToVisibility"/>
            <converters:PathToBitmapConverter x:Key="PathToBitmap"/>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

> **Note:** `PathToBitmapConverter` is created in Task 13. Create a placeholder `Converters/PathToBitmapConverter.cs` now with an empty class so this XAML compiles, then fill in the implementation in Task 13.

- [ ] **Step 5: Write `MainWindow.xaml` shell with two tabs**

```xml
<!-- src/PdfUtility.App/MainWindow.xaml -->
<ui:FluentWindow x:Class="PdfUtility.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        xmlns:vm="clr-namespace:PdfUtility.App.ViewModels"
        xmlns:views="clr-namespace:PdfUtility.App.Views"
        Title="PDF Utility" Width="1100" Height="720"
        MinWidth="900" MinHeight="600">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- toolbar -->
            <RowDefinition Height="*"/>     <!-- content tabs -->
        </Grid.RowDefinitions>

        <!-- Toolbar -->
        <ui:TitleBar Grid.Row="0" Title="PDF Utility">
            <ui:TitleBar.TrailingContent>
                <StackPanel Orientation="Horizontal" Margin="0,0,8,0" VerticalAlignment="Center">
                    <TextBlock Text="DPI:" VerticalAlignment="Center" Margin="0,0,4,0"/>
                    <ComboBox ItemsSource="{Binding DpiOptions}"
                              SelectedItem="{Binding ScanDpi}"
                              Width="70" Margin="0,0,12,0"/>
                    <TextBlock Text="Color:" VerticalAlignment="Center" Margin="0,0,4,0"/>
                    <ComboBox ItemsSource="{Binding ColorModeOptions}"
                              SelectedItem="{Binding ColorMode}"
                              Width="100" Margin="0,0,12,0"/>
                    <TextBlock Text="PDF:" VerticalAlignment="Center" Margin="0,0,4,0"/>
                    <ComboBox ItemsSource="{Binding PdfFormatOptions}"
                              SelectedItem="{Binding PdfFormat}"
                              Width="90" Margin="0,0,12,0"/>
                    <TextBlock Text="Size:" VerticalAlignment="Center" Margin="0,0,4,0"/>
                    <ComboBox ItemsSource="{Binding PaperSizeOptions}"
                              SelectedItem="{Binding PaperSize}"
                              Width="90" Margin="0,0,12,0"/>
                    <ui:Button Content="⚙ Settings" Margin="0,0,4,0"/>
                </StackPanel>
            </ui:TitleBar.TrailingContent>
        </ui:TitleBar>

        <!-- Tab content -->
        <TabControl Grid.Row="1" SelectedIndex="{Binding SelectedTabIndex}">
            <TabItem Header="Scan Double Sided">
                <views:ScanDoubleSidedView/>
            </TabItem>
            <TabItem Header="Merge Documents">
                <TextBlock Text="Merge Documents — coming in Plan 2"
                           HorizontalAlignment="Center" VerticalAlignment="Center"
                           Foreground="Gray"/>
            </TabItem>
        </TabControl>
    </Grid>
</ui:FluentWindow>
```

- [ ] **Step 6: Write `MainWindow.xaml.cs`**

```csharp
// src/PdfUtility.App/MainWindow.xaml.cs
using PdfUtility.App.ViewModels;
using Wpf.Ui.Controls;

namespace PdfUtility.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel vm, ScanDoubleSidedViewModel scanVm)
    {
        InitializeComponent();
        DataContext = vm;
        // ScanDoubleSidedView gets its VM via DI — see ScanDoubleSidedView.xaml.cs
    }
}
```

- [ ] **Step 7: Build**

```bash
dotnet build src/PdfUtility.App/PdfUtility.App.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add src/PdfUtility.App/
git commit -m "feat(app): add DI host, MainWindow shell, toolbar dropdowns, InMemoryUserSettings"
```

---

## Task 9: ScanDoubleSidedViewModel — State Machine & Commands

**Files:**
- Create: `src/PdfUtility.App/ViewModels/PageThumbnailViewModel.cs`
- Create: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`
- Create: `tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs`

- [ ] **Step 1: Write `PageThumbnailViewModel`**

```csharp
// src/PdfUtility.App/ViewModels/PageThumbnailViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using PdfUtility.Core.Models;

namespace PdfUtility.App.ViewModels;

public partial class PageThumbnailViewModel : ObservableObject
{
    [ObservableProperty] private string _imagePath = string.Empty;
    [ObservableProperty] private int _pageNumber;
    [ObservableProperty] private string _sourceLabel = string.Empty; // "Front" or "Back"
    [ObservableProperty] private bool _hasWarning;

    public ScannedPage? ScannedPage { get; set; }
}
```

- [ ] **Step 2: Write failing ViewModel tests**

```csharp
// tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs
using PdfUtility.App.Tests.Fakes;
using PdfUtility.App.ViewModels;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Models;

namespace PdfUtility.App.Tests.ViewModels;

public class ScanDoubleSidedViewModelTests
{
    private ScanDoubleSidedViewModel CreateVm(FakeScannerBackend? fake = null)
    {
        fake ??= new FakeScannerBackend();
        return new ScanDoubleSidedViewModel(fake);
    }

    [Fact]
    public void InitialState_IsIdle()
    {
        var vm = CreateVm();
        Assert.Equal(ScanSessionState.Idle, vm.SessionState);
        Assert.True(vm.StartBatch1Command.CanExecute(null));
        Assert.False(vm.ContinueScanningCommand.CanExecute(null));
        Assert.False(vm.DoneBatch1Command.CanExecute(null));
        Assert.False(vm.ScanOtherSideCommand.CanExecute(null));
    }

    [Fact]
    public async Task StartBatch1_WithPages_TransitionsToPagedAndAddsThumbails()
    {
        var fake = new FakeScannerBackend();
        fake.BatchQueue.Enqueue(["page1.png", "page2.png"]);
        var vm = CreateVm(fake);

        await vm.StartBatch1Command.ExecuteAsync(null);

        Assert.Equal(ScanSessionState.Batch1Paused, vm.SessionState);
        Assert.Equal(2, vm.Thumbnails.Count);
        Assert.Equal("Front", vm.Thumbnails[0].SourceLabel);
    }

    [Fact]
    public async Task DoneBatch1_TransitionsToBatch1Complete()
    {
        var fake = new FakeScannerBackend();
        fake.BatchQueue.Enqueue(["p1.png"]);
        var vm = CreateVm(fake);
        await vm.StartBatch1Command.ExecuteAsync(null);

        await vm.DoneBatch1Command.ExecuteAsync(null);

        Assert.Equal(ScanSessionState.Batch1Complete, vm.SessionState);
        Assert.True(vm.ScanOtherSideCommand.CanExecute(null));
        Assert.False(vm.DoneBatch1Command.CanExecute(null));
    }

    [Fact]
    public async Task FeederError_TransitionsToBatch1Error_KeepsPagesScanned()
    {
        var fake = new FakeScannerBackend();
        fake.BatchQueue.Enqueue(["p1.png", "p2.png"]);
        fake.NextScanError = new ScannerException("Paper jam");
        var vm = CreateVm(fake);

        await vm.StartBatch1Command.ExecuteAsync(null);

        Assert.Equal(ScanSessionState.Batch1Error, vm.SessionState);
        Assert.Equal(2, vm.Thumbnails.Count);
        Assert.True(vm.Thumbnails.Last().HasWarning); // last page flagged
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task DiscardSession_ResetsToIdle()
    {
        var fake = new FakeScannerBackend();
        fake.BatchQueue.Enqueue(["p1.png"]);
        var vm = CreateVm(fake);
        await vm.StartBatch1Command.ExecuteAsync(null);

        await vm.DiscardSessionCommand.ExecuteAsync(null);

        Assert.Equal(ScanSessionState.Idle, vm.SessionState);
        Assert.Empty(vm.Thumbnails);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/PdfUtility.App.Tests/ --filter "ScanDoubleSidedViewModelTests" -v n
```

Expected: compilation error (ScanDoubleSidedViewModel not yet created)

- [ ] **Step 4: Write `ScanDoubleSidedViewModel`**

```csharp
// src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;
using System.Collections.ObjectModel;

namespace PdfUtility.App.ViewModels;

public partial class ScanDoubleSidedViewModel : ObservableObject
{
    private readonly IScannerBackend _scanner;
    private ScanSession _session = new();
    private string _sessionDirectory = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBatch1Command))]
    [NotifyCanExecuteChangedFor(nameof(ContinueScanningCommand))]
    [NotifyCanExecuteChangedFor(nameof(DoneBatch1Command))]
    [NotifyCanExecuteChangedFor(nameof(ScanOtherSideCommand))]
    [NotifyCanExecuteChangedFor(nameof(DoneBatch2Command))]
    [NotifyCanExecuteChangedFor(nameof(MergeDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardSessionCommand))]
    private ScanSessionState _sessionState = ScanSessionState.Idle;

    [ObservableProperty] private string _statusMessage = "Ready to scan.";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _showErrorBanner;
    [ObservableProperty] private bool _showMismatchWarning;
    [ObservableProperty] private string _mismatchWarningText = string.Empty;

    public ObservableCollection<PageThumbnailViewModel> Thumbnails { get; } = new();

    // Settings (set by MainViewModel via binding or DI)
    public ScanOptions CurrentScanOptions { get; set; } = new();

    public ScanDoubleSidedViewModel(IScannerBackend scanner)
    {
        _scanner = scanner;
    }

    // ── Commands ──────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartBatch1))]
    private async Task StartBatch1()
    {
        _sessionDirectory = Path.Combine(
            Path.GetTempPath(), "PdfUtility", $"scan-{Guid.NewGuid():N}");
        _session = new ScanSession();
        Thumbnails.Clear();
        ErrorMessage = string.Empty;
        ShowErrorBanner = false;

        SessionState = ScanSessionState.Batch1Scanning;
        StatusMessage = "Scanning Batch 1 (front sides)…";
        await RunScanBatchAsync(targetBatch: 1, ScanSessionState.Batch1Paused, ScanSessionState.Batch1Error);
    }
    private bool CanStartBatch1() => SessionState == ScanSessionState.Idle;

    [RelayCommand(CanExecute = nameof(CanContinueScanning))]
    private async Task ContinueScanning()
    {
        ShowErrorBanner = false;
        ErrorMessage = string.Empty;

        bool isBatch1 = SessionState is ScanSessionState.Batch1Paused or ScanSessionState.Batch1Error;
        int targetBatch = isBatch1 ? 1 : 2;
        var scanningState = isBatch1 ? ScanSessionState.Batch1Scanning : ScanSessionState.Batch2Scanning;
        var pausedState = isBatch1 ? ScanSessionState.Batch1Paused : ScanSessionState.Batch2Paused;
        var errorState = isBatch1 ? ScanSessionState.Batch1Error : ScanSessionState.Batch2Error;

        SessionState = scanningState;
        StatusMessage = $"Scanning Batch {targetBatch} (continuing)…";
        await RunScanBatchAsync(targetBatch, pausedState, errorState);
    }
    private bool CanContinueScanning() =>
        SessionState is ScanSessionState.Batch1Paused or ScanSessionState.Batch1Error
                     or ScanSessionState.Batch2Paused or ScanSessionState.Batch2Error;

    [RelayCommand(CanExecute = nameof(CanDoneBatch1))]
    private Task DoneBatch1()
    {
        SessionState = ScanSessionState.Batch1Complete;
        StatusMessage = $"Batch 1 complete ({_session.Batch1.Count} pages). Now scan the other side.";
        return Task.CompletedTask;
    }
    // Enabled from both Batch1Paused (normal) and Batch1Error (error recovery)
    private bool CanDoneBatch1() =>
        SessionState is ScanSessionState.Batch1Paused or ScanSessionState.Batch1Error;

    [RelayCommand(CanExecute = nameof(CanScanOtherSide))]
    private async Task ScanOtherSide()
    {
        SessionState = ScanSessionState.Batch2Scanning;
        StatusMessage = "Scanning Batch 2 (back sides)…";
        await RunScanBatchAsync(targetBatch: 2, ScanSessionState.Batch2Paused, ScanSessionState.Batch2Error);
    }
    private bool CanScanOtherSide() => SessionState == ScanSessionState.Batch1Complete;

    [RelayCommand(CanExecute = nameof(CanDoneBatch2))]
    private Task DoneBatch2()
    {
        // Go directly to MergeReady — Batch2Complete is an internal detail, no need for two assignments
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
    // Enabled from both Batch2Paused (normal) and Batch2Error (error recovery)
    private bool CanDoneBatch2() =>
        SessionState is ScanSessionState.Batch2Paused or ScanSessionState.Batch2Error;

    [RelayCommand(CanExecute = nameof(CanMergeDocument))]
    private Task MergeDocument()
    {
        // Invoked from UI — actual save handled via SaveMergedPdfAsync called from View
        SessionState = ScanSessionState.Saved;
        return Task.CompletedTask;
    }
    private bool CanMergeDocument() => SessionState == ScanSessionState.MergeReady;

    [RelayCommand(CanExecute = nameof(CanDiscardSession))]
    private Task DiscardSession()
    {
        _session.DiscardTempFiles();
        _session = new ScanSession();
        Thumbnails.Clear();
        ErrorMessage = string.Empty;
        ShowErrorBanner = false;
        ShowMismatchWarning = false;
        SessionState = ScanSessionState.Idle;
        StatusMessage = "Ready to scan.";
        return Task.CompletedTask;
    }
    private bool CanDiscardSession() =>
        SessionState != ScanSessionState.Idle && SessionState != ScanSessionState.Saved;

    [RelayCommand(CanExecute = nameof(CanRescanLastPage))]
    private async Task RescanLastPage()
    {
        bool isBatch1 = SessionState == ScanSessionState.Batch1Error;
        var batch = isBatch1 ? _session.Batch1 : _session.Batch2;
        int targetBatch = isBatch1 ? 1 : 2;

        // Remove last (potentially damaged) page
        if (batch.Count > 0)
        {
            var last = batch[^1];
            if (File.Exists(last.ImagePath)) File.Delete(last.ImagePath);
            batch.RemoveAt(batch.Count - 1);
            if (Thumbnails.Count > 0) Thumbnails.RemoveAt(Thumbnails.Count - 1);
        }

        ShowErrorBanner = false;
        ErrorMessage = string.Empty;
        var scanningState = isBatch1 ? ScanSessionState.Batch1Scanning : ScanSessionState.Batch2Scanning;
        var pausedState = isBatch1 ? ScanSessionState.Batch1Paused : ScanSessionState.Batch2Paused;
        var errorState = isBatch1 ? ScanSessionState.Batch1Error : ScanSessionState.Batch2Error;

        SessionState = scanningState;
        await RunScanBatchAsync(targetBatch, pausedState, errorState);
    }
    private bool CanRescanLastPage()
    {
        if (SessionState == ScanSessionState.Batch1Error)
            return _session.Batch1.Count > 0;
        if (SessionState == ScanSessionState.Batch2Error)
            return _session.Batch2.Count > 0;
        return false;
    }

    // ── Core scan loop ────────────────────────────────────────────────

    private async Task RunScanBatchAsync(
        int targetBatch,
        ScanSessionState pausedState,
        ScanSessionState errorState)
    {
        var batch = targetBatch == 1 ? _session.Batch1 : _session.Batch2;
        int startIndex = batch.Count; // preserve count across Continue calls

        try
        {
            await foreach (var page in _scanner.ScanBatchAsync(
                CurrentScanOptions, _sessionDirectory, startIndex))
            {
                // Assign source batch number for display
                var sourcedPage = new ScannedPage(page.ImagePath, targetBatch);
                batch.Add(sourcedPage);

                var thumb = new PageThumbnailViewModel
                {
                    ImagePath = page.ImagePath,
                    PageNumber = batch.Count,
                    SourceLabel = targetBatch == 1 ? "Front" : "Back",
                    ScannedPage = sourcedPage
                };
                // Dispatch to UI thread
                System.Windows.Application.Current?.Dispatcher.Invoke(
                    () => Thumbnails.Add(thumb));

                StatusMessage = $"Scanning page {batch.Count}… (ADF feeding)";
            }

            // ADF tray empty — natural stop
            SessionState = pausedState;
            StatusMessage = $"Batch {targetBatch}: {batch.Count} page(s) scanned. Continue or declare done.";
        }
        catch (ScannerException ex)
        {
            SessionState = errorState;
            ErrorMessage = $"Paper jam or feeder error — fix the jam, then choose how to continue.\n({ex.Message})";
            ShowErrorBanner = true;
            StatusMessage = "Scanner error.";

            // Flag the last page as potentially partial
            if (batch.Count > 0)
                batch[^1].HasWarning = true;
            if (Thumbnails.Count > 0)
                Thumbnails[^1].HasWarning = true;
        }
    }

    // Called by the View after MergeDocumentCommand to actually save the PDF
    public List<ScannedPage> GetMergedPages() => _session.BuildMergedPages();
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/PdfUtility.App.Tests/ --filter "ScanDoubleSidedViewModelTests" -v n
```

Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/PdfUtility.App/ViewModels/ tests/PdfUtility.App.Tests/
git commit -m "feat(app): add ScanDoubleSidedViewModel with state machine and tests"
```

---

## Task 10: ScanDoubleSidedView — XAML

**Files:**
- Create: `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml`
- Create: `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml.cs`
- Create: `src/PdfUtility.App/Views/Controls/PageThumbnailControl.xaml`
- Create: `src/PdfUtility.App/Views/Controls/PageThumbnailControl.xaml.cs`

- [ ] **Step 1: Write `PageThumbnailControl.xaml`**

```xml
<!-- src/PdfUtility.App/Views/Controls/PageThumbnailControl.xaml -->
<UserControl x:Class="PdfUtility.App.Views.Controls.PageThumbnailControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Width="90" Margin="6">
    <StackPanel>
        <Grid>
            <!-- Thumbnail image -->
            <Border BorderBrush="#DDD" BorderThickness="1" CornerRadius="4"
                    Background="#F0F0F0" Width="90" Height="116">
                <Image Source="{Binding ImagePath}" Stretch="Uniform"
                       RenderOptions.BitmapScalingMode="HighQuality">
                    <Image.Style>
                        <Style TargetType="Image">
                            <!-- BitmapImage loaded with OnLoad so file handle released -->
                        </Style>
                    </Image.Style>
                </Image>
            </Border>
            <!-- Page number badge -->
            <Border Background="#0078D4" CornerRadius="3" Padding="3,1"
                    HorizontalAlignment="Right" VerticalAlignment="Top" Margin="3">
                <TextBlock Text="{Binding PageNumber}" Foreground="White" FontSize="9" FontWeight="Bold"/>
            </Border>
            <!-- Warning badge -->
            <Border Background="#FF8C00" CornerRadius="3" Padding="3,1"
                    HorizontalAlignment="Left" VerticalAlignment="Top" Margin="3"
                    Visibility="{Binding HasWarning, Converter={StaticResource BoolToVisibility}}">
                <TextBlock Text="⚠" Foreground="White" FontSize="9"/>
            </Border>
        </Grid>
        <!-- Source label -->
        <TextBlock Text="{Binding SourceLabel}" FontSize="10" Foreground="#555"
                   HorizontalAlignment="Center" Margin="0,2,0,0"/>
        <!-- Replace link -->
        <TextBlock FontSize="9" HorizontalAlignment="Center" Margin="0,2,0,0">
            <Hyperlink Command="{Binding DataContext.ReplacePageCommand,
                RelativeSource={RelativeSource AncestorType=UserControl}}"
                       CommandParameter="{Binding}">Replace</Hyperlink>
        </TextBlock>
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Write `PageThumbnailControl.xaml.cs`**

```csharp
// src/PdfUtility.App/Views/Controls/PageThumbnailControl.xaml.cs
namespace PdfUtility.App.Views.Controls;
public partial class PageThumbnailControl : System.Windows.Controls.UserControl
{
    public PageThumbnailControl() => InitializeComponent();
}
```

- [ ] **Step 3: Write `ScanDoubleSidedView.xaml`**

```xml
<!-- src/PdfUtility.App/Views/ScanDoubleSidedView.xaml -->
<UserControl x:Class="PdfUtility.App.Views.ScanDoubleSidedView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             xmlns:controls="clr-namespace:PdfUtility.App.Views.Controls">

    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVisibility"/>
    </UserControl.Resources>

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="230"/>   <!-- controls panel -->
            <ColumnDefinition Width="*"/>     <!-- preview panel -->
        </Grid.ColumnDefinitions>

        <!-- ── Left: Scan Controls ───────────────────────────────── -->
        <Border Grid.Column="0" BorderBrush="#E0E0E0" BorderThickness="0,0,1,0" Padding="12">
            <StackPanel>

                <!-- Batch 1 -->
                <TextBlock Text="BATCH 1 — Front Sides" FontSize="11" FontWeight="SemiBold"
                           Foreground="#0078D4" Margin="0,0,0,8"/>
                <ui:Button Content="▶ Start Scanning Batch 1" HorizontalAlignment="Stretch"
                           Command="{Binding StartBatch1Command}" Margin="0,0,0,6"
                           Appearance="Primary"/>
                <ui:Button Content="⊕ Continue Scanning" HorizontalAlignment="Stretch"
                           Command="{Binding ContinueScanningCommand}" Margin="0,0,0,6"/>
                <ui:Button Content="✓ Done with Batch 1" HorizontalAlignment="Stretch"
                           Command="{Binding DoneBatch1Command}" Margin="0,0,0,16"/>

                <!-- Batch 2 -->
                <TextBlock Text="BATCH 2 — Back Sides" FontSize="11" FontWeight="SemiBold"
                           Foreground="#0078D4" Margin="0,0,0,8"/>
                <ui:Button Content="▶ Scan Other Side" HorizontalAlignment="Stretch"
                           Command="{Binding ScanOtherSideCommand}" Margin="0,0,0,6"
                           Appearance="Primary"/>
                <ui:Button Content="⊕ Continue Scanning (B2)" HorizontalAlignment="Stretch"
                           Command="{Binding ContinueScanningCommand}" Margin="0,0,0,6"/>
                <ui:Button Content="✓ Done with Batch 2" HorizontalAlignment="Stretch"
                           Command="{Binding DoneBatch2Command}" Margin="0,0,0,16"/>

                <!-- Merge -->
                <ui:Button Content="⇄ Merge Document" HorizontalAlignment="Stretch"
                           Command="{Binding MergeDocumentCommand}"
                           Appearance="Success" Margin="0,0,0,8"/>

                <!-- Discard -->
                <ui:Button Content="🗑 Discard Session" HorizontalAlignment="Stretch"
                           Command="{Binding DiscardSessionCommand}"
                           Appearance="Danger"/>

                <!-- Error recovery buttons -->
                <Border Background="#FFF3E0" CornerRadius="6" Padding="10" Margin="0,16,0,0"
                        Visibility="{Binding ShowErrorBanner, Converter={StaticResource BoolToVisibility}}">
                    <StackPanel>
                        <TextBlock Text="{Binding ErrorMessage}" TextWrapping="Wrap"
                                   Foreground="#E65100" FontSize="11" Margin="0,0,0,8"/>
                        <ui:Button Content="▶ Continue Scanning" HorizontalAlignment="Stretch"
                                   Command="{Binding ContinueScanningCommand}" Margin="0,0,0,4"/>
                        <ui:Button Content="↩ Rescan Last Page" HorizontalAlignment="Stretch"
                                   Command="{Binding RescanLastPageCommand}" Margin="0,0,0,4"/>
                        <ui:Button Content="✓ Done with Batch" HorizontalAlignment="Stretch"
                                   Command="{Binding DoneBatch1Command}"/>
                    </StackPanel>
                </Border>

                <!-- Mismatch warning -->
                <Border Background="#FFF8E1" CornerRadius="6" Padding="10" Margin="0,8,0,0"
                        Visibility="{Binding ShowMismatchWarning, Converter={StaticResource BoolToVisibility}}">
                    <TextBlock Text="{Binding MismatchWarningText}" TextWrapping="Wrap"
                               Foreground="#F57F17" FontSize="11"/>
                </Border>
            </StackPanel>
        </Border>

        <!-- ── Right: Preview ────────────────────────────────────── -->
        <Grid Grid.Column="1">
            <Grid.RowDefinitions>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <ScrollViewer Grid.Row="0" VerticalScrollBarVisibility="Auto" Padding="12">
                <ItemsControl ItemsSource="{Binding Thumbnails}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel/>
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <controls:PageThumbnailControl DataContext="{Binding}"/>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>

            <!-- Status bar -->
            <Border Grid.Row="1" Background="#E3F2FD" Padding="12,6">
                <TextBlock Text="{Binding StatusMessage}" Foreground="#1565C0" FontSize="12"/>
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 4: Write `ScanDoubleSidedView.xaml.cs`**

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
        // Resolve ViewModel from DI when running in the real app
        if (System.Windows.Application.Current is App app)
            DataContext = app.Services.GetRequiredService<ScanDoubleSidedViewModel>();
    }
}
```

> **Note:** Expose `App.Services` as a public property: add `public IServiceProvider Services => _host!.Services;` to `App.xaml.cs`.

- [ ] **Step 5: Add `ReplacePageCommand` stub to `ScanDoubleSidedViewModel`**

Add to `ScanDoubleSidedViewModel.cs`:

```csharp
[RelayCommand]
private async Task ReplacePage(PageThumbnailViewModel thumb)
{
    // Flatbed replacement — implemented in Task 12
    await Task.CompletedTask;
}
```

- [ ] **Step 6: Build app**

```bash
dotnet build src/PdfUtility.App/PdfUtility.App.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 7: Smoke test — launch the app and verify it starts**

```bash
dotnet run --project src/PdfUtility.App/PdfUtility.App.csproj
```

Expected: Window opens with two tabs and toolbar dropdowns visible.

- [ ] **Step 8: Commit**

```bash
git add src/PdfUtility.App/Views/
git commit -m "feat(app): add ScanDoubleSidedView XAML and PageThumbnailControl"
```

---

## Task 11: Merge Document Action (Interleave + Save PDF)

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`
- Modify: `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml.cs`

Wire up the actual PDF save when the user clicks "Merge Document".

- [ ] **Step 1: Update `MergeDocument` command to trigger save dialog**

Replace the `MergeDocument` command body in `ScanDoubleSidedViewModel.cs`:

```csharp
private readonly IPdfBuilder _pdfBuilder;
private readonly IUserSettings _userSettings;

// Add to constructor:
public ScanDoubleSidedViewModel(
    IScannerBackend scanner,
    IPdfBuilder pdfBuilder,
    IUserSettings userSettings)
{
    _scanner = scanner;
    _pdfBuilder = pdfBuilder;
    _userSettings = userSettings;
}

[RelayCommand(CanExecute = nameof(CanMergeDocument))]
private async Task MergeDocument()
{
    // Show SaveFileDialog
    var dialog = new Microsoft.Win32.SaveFileDialog
    {
        Title = "Save Merged PDF",
        Filter = "PDF Files (*.pdf)|*.pdf",
        DefaultExt = ".pdf",
        FileName = $"scan_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
    };

    var prefs = _userSettings.Load();
    if (!string.IsNullOrEmpty(prefs.DefaultSaveFolder))
        dialog.InitialDirectory = prefs.DefaultSaveFolder;

    if (dialog.ShowDialog() != true) return;

    StatusMessage = "Building PDF…";

    var mergedPages = GetMergedPages();
    var options = new PdfBuildOptions
    {
        Format = prefs.PdfFormat,
        JpegQuality = prefs.JpegQuality,
        PaperSize = prefs.PaperSize
    };

    try
    {
        await _pdfBuilder.BuildAsync(mergedPages, options, dialog.FileName);
        SessionState = ScanSessionState.Saved;
        StatusMessage = $"Saved: {dialog.FileName}";
    }
    catch (Exception ex)
    {
        StatusMessage = $"Save failed: {ex.Message}";
    }
}
```

- [ ] **Step 2: Update DI registration in `App.xaml.cs`**

```csharp
services.AddSingleton<ScanDoubleSidedViewModel>(sp =>
    new ScanDoubleSidedViewModel(
        sp.GetRequiredService<IScannerBackend>(),
        sp.GetRequiredService<IPdfBuilder>(),
        sp.GetRequiredService<IUserSettings>()));
```

- [ ] **Step 3: Build and smoke test**

```bash
dotnet build && dotnet run --project src/PdfUtility.App/PdfUtility.App.csproj
```

Expected: App launches. Clicking "Merge Document" (once enabled) opens a Save dialog.

- [ ] **Step 4: Commit**

```bash
git add src/PdfUtility.App/
git commit -m "feat(app): wire Merge Document command to PDF save dialog"
```

---

## Task 12: Flatbed Page Replacement

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`

- [ ] **Step 1: Write failing test for ReplacePage**

Add to `ScanDoubleSidedViewModelTests.cs`:

```csharp
[Fact]
public async Task ReplacePage_OnSuccess_UpdatesThumbnailImagePath()
{
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(["original.png"]);
    fake.NextFlatbedImagePath = "replacement.png";
    var vm = CreateVm(fake);
    await vm.StartBatch1Command.ExecuteAsync(null);

    var thumb = vm.Thumbnails[0];
    await vm.ReplacePageCommand.ExecuteAsync(thumb);

    Assert.Equal("replacement.png", thumb.ImagePath);
    Assert.False(thumb.HasWarning);
}

[Fact]
public async Task ReplacePage_OnFailure_LeavesOriginalUnchanged()
{
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(["original.png"]);
    fake.FlatbedShouldFail = true;
    var vm = CreateVm(fake);
    await vm.StartBatch1Command.ExecuteAsync(null);

    var thumb = vm.Thumbnails[0];
    await vm.ReplacePageCommand.ExecuteAsync(thumb);

    Assert.Equal("original.png", thumb.ImagePath); // unchanged
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/PdfUtility.App.Tests/ --filter "ReplacePage" -v n
```

Expected: FAIL (current stub does nothing)

- [ ] **Step 3: Implement `ReplacePage` in ViewModel**

Replace the stub `ReplacePage` method:

```csharp
[RelayCommand]
private async Task ReplacePage(PageThumbnailViewModel thumb)
{
    if (thumb.ScannedPage is null) return;

    try
    {
        StatusMessage = "Scanning replacement page from flatbed…";
        var replacement = await _scanner.ScanSingleFlatbedAsync(
            CurrentScanOptions,
            _sessionDirectory,
            thumb.PageNumber - 1);

        thumb.ScannedPage.ReplaceImage(replacement.ImagePath);
        thumb.ImagePath = replacement.ImagePath;
        thumb.HasWarning = false;
        StatusMessage = $"Page {thumb.PageNumber} replaced.";
    }
    catch (ScannerException ex)
    {
        StatusMessage = $"Could not scan replacement page: {ex.Message}";
        // original page unchanged — no state mutation happened
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/PdfUtility.App.Tests/ --filter "ReplacePage" -v n
```

Expected: Both tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/PdfUtility.App/ tests/PdfUtility.App.Tests/
git commit -m "feat(app): implement flatbed page replacement with error handling"
```

---

## Task 13: Thumbnail Image Loading Fix (WPF file-handle release)

**Files:**
- Modify: `src/PdfUtility.App/Views/Controls/PageThumbnailControl.xaml`
- Create: `src/PdfUtility.App/Converters/PathToBitmapConverter.cs`

WPF's default `Image Source` binding holds a file lock. We need to load with `BitmapCacheOption.OnLoad` so the PNG can be overwritten by `ReplaceImage`.

- [ ] **Step 1: Write `PathToBitmapConverter`**

```csharp
// src/PdfUtility.App/Converters/PathToBitmapConverter.cs
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace PdfUtility.App.Converters;

public class PathToBitmapConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad; // releases file handle immediately
        bmp.DecodePixelWidth = 180;                 // limit memory: thumbnail size only
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

- [ ] **Step 2: Fill in the `PathToBitmapConverter` implementation (placeholder was created in Task 8)**

The converter key `PathToBitmap` is already registered globally in `App.xaml` (Task 8 Step 4). Just ensure `PageThumbnailControl.xaml` uses it on the Image:

```xml
<Image Source="{Binding ImagePath, Converter={StaticResource PathToBitmap}}" Stretch="Uniform"/>
```

No `xmlns:converters` or resource re-registration needed in `PageThumbnailControl.xaml` — it inherits from `App.xaml`.

- [ ] **Step 3: Build**

```bash
dotnet build src/PdfUtility.App/PdfUtility.App.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/PdfUtility.App/
git commit -m "fix(app): release PNG file handles using BitmapCacheOption.OnLoad in thumbnail converter"
```

---

## Task 14: Paper Jam Recovery — DoneCurrentBatch for Error Banner

The `RescanLastPage` command is already implemented in Task 9. This task adds `DoneCurrentBatchCommand` so the "Done with Batch" button in the error banner works correctly from both Batch 1 and Batch 2 error states.

**Files:**
- Modify: `src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs`
- Modify: `src/PdfUtility.App/Views/ScanDoubleSidedView.xaml`
- Modify: `tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs`

- [ ] **Step 1: Write failing test for DoneCurrentBatch in Batch2Error state**

Add to `ScanDoubleSidedViewModelTests.cs`:

```csharp
[Fact]
public async Task DoneCurrentBatch_InBatch2Error_TransitionsToMergeReady()
{
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(["f1.png"]);
    var vm = CreateVm(fake);
    await vm.StartBatch1Command.ExecuteAsync(null);
    await vm.DoneBatch1Command.ExecuteAsync(null);

    fake.BatchQueue.Enqueue(["b1.png"]);
    fake.NextScanError = new ScannerException("Jam");
    await vm.ScanOtherSideCommand.ExecuteAsync(null);
    Assert.Equal(ScanSessionState.Batch2Error, vm.SessionState);

    await vm.DoneCurrentBatchCommand.ExecuteAsync(null);

    Assert.Equal(ScanSessionState.MergeReady, vm.SessionState);
}

[Fact]
public async Task DoneCurrentBatch_InBatch1Error_TransitionsToBatch1Complete()
{
    var fake = new FakeScannerBackend();
    fake.BatchQueue.Enqueue(["f1.png"]);
    fake.NextScanError = new ScannerException("Jam");
    var vm = CreateVm(fake);
    await vm.StartBatch1Command.ExecuteAsync(null);
    Assert.Equal(ScanSessionState.Batch1Error, vm.SessionState);

    await vm.DoneCurrentBatchCommand.ExecuteAsync(null);

    Assert.Equal(ScanSessionState.Batch1Complete, vm.SessionState);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/PdfUtility.App.Tests/ --filter "DoneCurrentBatch" -v n
```

Expected: compilation error (`DoneCurrentBatchCommand` not yet defined)

- [ ] **Step 3: Implement `DoneCurrentBatchCommand` and update error banner XAML**

Add to `ScanDoubleSidedViewModel.cs`:

```csharp
[RelayCommand(CanExecute = nameof(CanDoneCurrentBatch))]
private Task DoneCurrentBatch()
{
    if (SessionState is ScanSessionState.Batch1Paused or ScanSessionState.Batch1Error)
        return DoneBatch1();
    return DoneBatch2();
}
private bool CanDoneCurrentBatch() =>
    SessionState is ScanSessionState.Batch1Paused or ScanSessionState.Batch1Error
                 or ScanSessionState.Batch2Paused or ScanSessionState.Batch2Error;
```

Update the error banner "Done with Batch" button in `ScanDoubleSidedView.xaml`:
```xml
<ui:Button Content="✓ Done with Batch" HorizontalAlignment="Stretch"
           Command="{Binding DoneCurrentBatchCommand}"/>
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/PdfUtility.App.Tests/ -v n
```

Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/PdfUtility.App/ tests/PdfUtility.App.Tests/
git commit -m "feat(app): add DoneCurrentBatch command for error recovery in both batches"
```

---

## Task 15: Run All Tests + Final Smoke Test

- [ ] **Step 1: Run full test suite**

```bash
dotnet test -v n
```

Expected: All tests PASS, 0 failures.

- [ ] **Step 2: Build release**

```bash
dotnet publish src/PdfUtility.App/PdfUtility.App.csproj -c Release -r win-x64 --self-contained
```

Expected: `Publish succeeded.`

- [ ] **Step 3: Launch the published build and verify end-to-end**

Manual test checklist:
- [ ] App launches without errors
- [ ] Scanner pre-warms in background (check no startup crash if scanner not connected)
- [ ] Toolbar dropdowns show 150/300/600 DPI, Color/Grayscale/B&W, Standard/PdfA, Letter/Legal/Auto
- [ ] "Start Scanning Batch 1" is enabled, all other scan buttons disabled at start
- [ ] After Batch 1 completes naturally: "Continue Scanning" and "Done with Batch 1" appear enabled
- [ ] After "Done with Batch 1": "Scan Other Side" becomes enabled
- [ ] After both batches done: "Merge Document" becomes enabled
- [ ] "Merge Document" opens Save dialog, saves PDF, status bar shows saved path
- [ ] Paper jam (unplug scanner mid-scan) shows error banner with three recovery buttons
- [ ] "Replace" on a thumbnail triggers flatbed scan prompt

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "feat: Plan 1 complete — working double-sided scanner with PDF output"
```

---

## What's Next

- **Plan 2:** Merge Documents tab + Settings persistence (settings.json, modal dialog) + iText7 PDF/A support + Windows.Data.Pdf PDF import rasterisation
- **Plan 3:** Inno Setup installer + startup orphan temp-file cleanup
