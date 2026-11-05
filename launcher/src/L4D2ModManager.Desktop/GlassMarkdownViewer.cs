using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WpfAnimatedGif;
using WpfMarkdown.Controls;

namespace L4D2ModManager.Desktop;

/// <summary>Keeps WpfMarkdown native rendering while removing its opaque GitHub surface.</summary>
public sealed class GlassMarkdownViewer : MarkdownViewer
{
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Resources["MarkdownViewer.SurfaceBrush"] = Brushes.Transparent;
        Resources["MarkdownViewer.DocumentBrush"] = Brushes.Transparent;
        Resources["MarkdownViewer.BorderBrush"] = new SolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
        Resources["MarkdownViewer.SubtleBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        ApplyGlassDocument();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == MarkdownProperty)
            Dispatcher.BeginInvoke(ApplyGlassDocument);
    }

    private void ApplyGlassDocument()
    {
        if (Document is null) return;
        Document.Background = Brushes.Transparent;
        Document.PagePadding = new Thickness(8, 6, 10, 8);
        Document.FontSize = 13;
        Document.LineHeight = 20;
        EnableGifAnimations(Document.Blocks);
    }

    private static void EnableGifAnimations(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    EnableGifAnimations(paragraph.Inlines);
                    break;
                case Section section:
                    EnableGifAnimations(section.Blocks);
                    break;
                case List list:
                    foreach (var item in list.ListItems)
                        EnableGifAnimations(item.Blocks);
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells)
                                EnableGifAnimations(cell.Blocks);
                    break;
            }
        }
    }

    private static void EnableGifAnimations(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case InlineUIContainer { Child: Image image }:
                    AnimateGif(image);
                    break;
                case Span span:
                    EnableGifAnimations(span.Inlines);
                    break;
            }
        }
    }

    private static void AnimateGif(Image image)
    {
        if (image.Source is not BitmapImage { UriSource: { } uri } ||
            !uri.AbsolutePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            return;

        var animatedSource = new BitmapImage();
        animatedSource.BeginInit();
        animatedSource.CacheOption = BitmapCacheOption.OnLoad;
        animatedSource.UriSource = uri;
        animatedSource.EndInit();
        animatedSource.Freeze();

        ImageBehavior.SetRepeatBehavior(image, RepeatBehavior.Forever);
        ImageBehavior.SetAnimatedSource(image, animatedSource);
    }
}
