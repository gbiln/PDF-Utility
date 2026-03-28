namespace PdfUtility.Core.Exceptions;

public class ScannerException : Exception
{
    public ScannerException(string message) : base(message) { }
    public ScannerException(string message, Exception inner) : base(message, inner) { }
}
