// src/PdfUtility.App/ViewModels/MainViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using PdfUtility.Core.Models;

namespace PdfUtility.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _selectedTabIndex = 0;
    [ObservableProperty] private int _scanDpi = 300;
    [ObservableProperty] private ColorMode _colorMode = ColorMode.Color;
    [ObservableProperty] private PdfFormat _pdfFormat = PdfFormat.Standard;
    [ObservableProperty] private PaperSize _paperSize = PaperSize.Letter;

    public int[] DpiOptions { get; } = [150, 300, 600];
    public ColorMode[] ColorModeOptions { get; } = Enum.GetValues<ColorMode>();
    public PdfFormat[] PdfFormatOptions { get; } = Enum.GetValues<PdfFormat>();
    public PaperSize[] PaperSizeOptions { get; } = Enum.GetValues<PaperSize>();
}
