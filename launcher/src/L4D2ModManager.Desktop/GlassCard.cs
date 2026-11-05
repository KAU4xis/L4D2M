using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace L4D2ModManager.Desktop;

/// <summary>
/// Samples the exact part of the window background behind this control. The
/// template blurs only that sampled layer, keeping text and controls sharp.
/// </summary>
public sealed class GlassCard : ContentControl
{
    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(
        nameof(Backdrop), typeof(Visual), typeof(GlassCard),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnBackdropChanged));

    private Rectangle? backdropLayer;
    private Border? clipHost;
    private VisualBrush? backdropBrush;

    static GlassCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GlassCard), new FrameworkPropertyMetadata(typeof(GlassCard)));
    }

    public Visual? Backdrop
    {
        get => (Visual?)GetValue(BackdropProperty);
        set => SetValue(BackdropProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        backdropLayer = GetTemplateChild("PART_Backdrop") as Rectangle;
        clipHost = GetTemplateChild("PART_ClipHost") as Border;
        UpdateRoundedClip();
        RefreshBackdrop();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateRoundedClip();
        RefreshBackdrop();
    }

    private static void OnBackdropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((GlassCard)d).RefreshBackdrop();

    private void UpdateRoundedClip()
    {
        if (clipHost is null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        // Border.ClipToBounds is rectangular in WPF. An explicit rounded
        // geometry prevents the blurred backdrop and hover layers from
        // leaking into the four corners.
        clipHost.Clip = new RectangleGeometry(
            new Rect(0, 0, ActualWidth, ActualHeight), 20, 20);
    }

    private void RefreshBackdrop()
    {
        if (backdropLayer is null || Backdrop is not FrameworkElement source ||
            ActualWidth <= 0 || ActualHeight <= 0 || source.ActualWidth <= 0 || source.ActualHeight <= 0)
            return;

        try
        {
            var origin = TransformToVisual(source).Transform(new Point(0, 0));
            backdropBrush ??= new VisualBrush
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewport = new Rect(0, 0, 1, 1),
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                TileMode = TileMode.None,
                AutoLayoutContent = false
            };

            backdropBrush.Visual = source;
            backdropBrush.Viewbox = new Rect(origin.X, origin.Y, ActualWidth, ActualHeight);
            backdropLayer.Fill = backdropBrush;
        }
        catch (InvalidOperationException)
        {
            // The visual can briefly be disconnected while the window is resizing.
        }
    }
}
