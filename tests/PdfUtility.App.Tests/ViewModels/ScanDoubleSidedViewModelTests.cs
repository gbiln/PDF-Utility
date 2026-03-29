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
        await vm.StartBatch1Command.ExecuteAsync(null);
        Assert.Equal(ScanSessionState.Batch1Error, vm.SessionState);

        await vm.DoneCurrentBatchCommand.ExecuteAsync(null);

        Assert.Equal(ScanSessionState.Batch1Complete, vm.SessionState);
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
}
