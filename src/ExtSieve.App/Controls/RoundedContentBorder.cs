using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ExtSieve.App.Controls;

/// <summary>
/// Keeps edge-to-edge content inside a uniform rounded border without clipping its frame.
/// </summary>
public sealed class RoundedContentBorder : Border
{
    private readonly RectangleGeometry _contentClip = new();

    static RoundedContentBorder()
    {
        AffectsArrange<RoundedContentBorder>(CornerRadiusProperty);
    }

    protected override Type StyleKeyOverride => typeof(Border);

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (Child is { } child)
        {
            var thickness = UseLayoutRounding
                ? LayoutHelper.RoundLayoutThickness(BorderThickness, LayoutHelper.GetLayoutScale(this))
                : BorderThickness;
            var radius = Math.Max(0, CornerRadius.TopLeft - thickness.Left / 2);
            _contentClip.Rect = new Rect(child.Bounds.Size);
            _contentClip.RadiusX = radius;
            _contentClip.RadiusY = radius;
            child.Clip = _contentClip;
        }

        return result;
    }
}
