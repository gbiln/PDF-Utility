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
