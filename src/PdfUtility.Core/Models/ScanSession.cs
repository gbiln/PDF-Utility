// src/PdfUtility.Core/Models/ScanSession.cs
namespace PdfUtility.Core.Models;

public class ScanSession
{
    public List<ScannedPage> Batch1 { get; } = new();
    public List<ScannedPage> Batch2 { get; } = new();
    public ScanSessionState State { get; set; } = ScanSessionState.Idle;

    /// <summary>
    /// Interleaves Batch1 (front sides) with Batch2 (back sides) to produce a
    /// correctly ordered double-sided page sequence.
    ///
    /// ADF correction: when the user flips the front stack and re-feeds it through
    /// the ADF, the scanner delivers the backs in reverse order.  We correct for
    /// this by reversing the first <c>min(Batch1.Count + 1, Batch2.Count)</c>
    /// pages of Batch2 (the "+1" handles the case where Batch2 is longer than
    /// Batch1 — one extra "overflow" back ends up at the seam of the reversal
    /// window, which places it correctly as a trailing extra rather than pairing
    /// it with a non-existent front page).
    ///
    /// Any pages that remain after the reversal window, and any Batch1 pages
    /// without a matching back, are appended at the end.
    /// </summary>
    public List<ScannedPage> BuildMergedPages()
    {
        // Reverse only the ADF-affected window of Batch2; anything beyond is
        // appended as-is (it was not subject to the ADF stack-flip reversal).
        int pairingCount = Math.Min(Batch1.Count + 1, Batch2.Count);
        var batch2Ordered = Batch2.Take(pairingCount).Reverse()
            .Concat(Batch2.Skip(pairingCount))
            .ToList();

        var merged = new List<ScannedPage>();
        int count = Math.Max(Batch1.Count, batch2Ordered.Count);
        for (int i = 0; i < count; i++)
        {
            if (i < Batch1.Count) merged.Add(Batch1[i]);
            if (i < batch2Ordered.Count) merged.Add(batch2Ordered[i]);
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
