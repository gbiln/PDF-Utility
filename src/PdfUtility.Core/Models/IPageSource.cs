namespace PdfUtility.Core.Models;

public interface IPageSource
{
    string ImagePath { get; }
    PageRotation Rotation { get; set; }
}
