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
