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
