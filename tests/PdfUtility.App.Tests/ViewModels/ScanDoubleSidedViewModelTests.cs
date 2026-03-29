// tests/PdfUtility.App.Tests/ViewModels/ScanDoubleSidedViewModelTests.cs
using PdfUtility.App.Tests.Fakes;
using PdfUtility.App.ViewModels;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;

namespace PdfUtility.App.Tests.ViewModels;

public class ScanDoubleSidedViewModelTests
{
    private sealed class FakePdfBuilder : IPdfBuilder
    {
        public Task BuildAsync(IEnumerable<IPageSource> pages, PdfBuildOptions options, string outputPath)
            => Task.CompletedTask;
    }

    private static ScanDoubleSidedViewModel CreateVm(FakeScannerBackend? fake = null)
    {
        fake ??= new FakeScannerBackend();
        return new ScanDoubleSidedViewModel(fake, new FakePdfBuilder(), new PdfUtility.App.Services.InMemoryUserSettings());
    }

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

    [Fact]
    public async Task StartBatch1_WithPages_TransitionsToPagedAndAddsThumbails()
    {
        var fake = new FakeScannerBackend();
        fake.BatchQueue.Enqueue(["page1.png", "page2.png"]);
        var vm = CreateVm(fake);
        vm.SelectedDevice = "Fake Scanner";

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
        vm.SelectedDevice = "Fake Scanner";
        vm.ScanMode = ScanMode.DoubleSided;
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
        vm.SelectedDevice = "Fake Scanner";

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
        vm.SelectedDevice = "Fake Scanner";
        await vm.StartBatch1Command.ExecuteAsync(null);

        await vm.DiscardSessionCommand.ExecuteAsync(null);

        Assert.Equal(ScanSessionState.Idle, vm.SessionState);
        Assert.Empty(vm.Thumbnails);
    }

    [Fact]
    public async Task ReplacePage_OnSuccess_UpdatesThumbnailImagePath()
    {
        var fake = new FakeScannerBackend();
        fake.BatchQueue.Enqueue(["original.png"]);
        fake.NextFlatbedImagePath = "replacement.png";
        var vm = CreateVm(fake);
        vm.SelectedDevice = "Fake Scanner";
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
        vm.SelectedDevice = "Fake Scanner";
        await vm.StartBatch1Command.ExecuteAsync(null);

        var thumb = vm.Thumbnails[0];
        await vm.ReplacePageCommand.ExecuteAsync(thumb);

        Assert.Equal("original.png", thumb.ImagePath); // unchanged
    }

    [Fact]
    public async Task DoneCurrentBatch_InBatch2Error_TransitionsToMergeReady()
    {
        var fake = new FakeScannerBackend();
        fake.BatchQueue.Enqueue(["f1.png"]);
        var vm = CreateVm(fake);
        vm.SelectedDevice = "Fake Scanner";
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
        vm.SelectedDevice = "Fake Scanner";
        vm.ScanMode = ScanMode.DoubleSided;
        await vm.StartBatch1Command.ExecuteAsync(null);
        Assert.Equal(ScanSessionState.Batch1Error, vm.SessionState);

        await vm.DoneCurrentBatchCommand.ExecuteAsync(null);

        Assert.Equal(ScanSessionState.Batch1Complete, vm.SessionState);
    }

    [Fact]
    public async Task MergedPages_Batch1AndBatch2_HaveDistinctSubdirectories()
    {
        // Arrange — fake scanner yields one front page then one back page
        var fake = new FakeScannerBackend();
        fake.BatchQueue.Enqueue(new List<string> { "batch1/page_0000.png" });
        fake.BatchQueue.Enqueue(new List<string> { "batch2/page_0000.png" });
        var vm = CreateVm(fake);
        vm.SelectedDevice = "Fake Scanner";  // required: CanStartBatch1 is false without this
        vm.ScanMode = ScanMode.DoubleSided;

        // Act — DoneCurrentBatchCommand dispatches correctly from both Batch1Paused and Batch2Paused
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
        vm.ScanMode = ScanMode.DoubleSided;

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
        Assert.Empty(vm.AvailableDevices);
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
        Assert.Empty(vm.AvailableDevices);
        Assert.Contains("Could not enumerate scanners", vm.StatusMessage);
    }

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
        Assert.Equal(1, vm.Thumbnails[0].PageNumber);
    }
}
