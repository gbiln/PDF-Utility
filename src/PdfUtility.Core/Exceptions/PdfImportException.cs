namespace PdfUtility.Core.Exceptions;

public class PdfImportException : Exception
{
    public PdfImportException(string message) : base(message) { }
    public PdfImportException(string message, Exception inner) : base(message, inner) { }
}
