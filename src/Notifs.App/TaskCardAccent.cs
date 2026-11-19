using System.Windows;
using System.Windows.Media;

namespace Notifs.App;

/// <summary>The prototype's inset 3px horizontal shadow, clipped to the rounded card interior.</summary>
public sealed class TaskCardAccent : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(TaskCardAccent),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 2 || ActualHeight <= 2 || Fill is null) return;

        // One-pixel border, radius 12: the interior has radius 11.
        // Subtract the same rounded interior shifted right, just like CSS inset 3px 0.
        var bounds = new Rect(1, 1, ActualWidth - 2, ActualHeight - 2);
        var interior = new RectangleGeometry(bounds, 11, 11);
        bounds.Offset(3, 0);
        var shifted = new RectangleGeometry(bounds, 11, 11);
        drawingContext.DrawGeometry(Fill, null, new CombinedGeometry(GeometryCombineMode.Exclude, interior, shifted));
    }
}
