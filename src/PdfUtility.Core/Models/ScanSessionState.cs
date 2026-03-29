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
