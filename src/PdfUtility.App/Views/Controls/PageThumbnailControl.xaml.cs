// src/PdfUtility.App/Views/Controls/PageThumbnailControl.xaml.cs
using System.Windows;
using System.Windows.Controls;

namespace PdfUtility.App.Views.Controls;

public partial class PageThumbnailControl : UserControl
{
    public static readonly DependencyProperty ShowReplaceLinkProperty =
        DependencyProperty.Register(
            nameof(ShowReplaceLink),
            typeof(bool),
            typeof(PageThumbnailControl),
            new PropertyMetadata(true));

    public bool ShowReplaceLink
    {
        get => (bool)GetValue(ShowReplaceLinkProperty);
        set => SetValue(ShowReplaceLinkProperty, value);
    }

    public PageThumbnailControl()
    {
        InitializeComponent();
    }
}
