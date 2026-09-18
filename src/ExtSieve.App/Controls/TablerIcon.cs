using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace ExtSieve.App.Controls;

public sealed class TablerIcon : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<TablerIcon, Geometry?>(nameof(Data));

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}
