using Gum.Converters;
using Gum.DataTypes;
using Gum.GueDeriving;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using RenderingLibrary.Graphics;

namespace Molib.Motion;

/// <summary>
/// Values of one element that a state transition can animate, with the units they are measured in.
/// </summary>
/// <remarks>
/// <para>
/// Values are read and written through the typed properties of the Gum runtimes, without <c>SetProperty</c> and
/// boxing. A value whose units or origin changed together with it is not animated: numbers in different units do
/// not interpolate.
/// </para>
/// <para>ADR: motion/state-transitions — How a transition runs.</para>
/// </remarks>
internal struct AnimatedValues
{
    /// <summary>Where the element keeps its color.</summary>
    public ColorSource ColorSource;

    /// <summary><c>X</c>.</summary>
    public float X;

    /// <summary><c>Y</c>.</summary>
    public float Y;

    /// <summary><c>Width</c>.</summary>
    public float Width;

    /// <summary><c>Height</c>.</summary>
    public float Height;

    /// <summary><c>Rotation</c> in degrees.</summary>
    public float Rotation;

    /// <summary><c>XUnits</c>.</summary>
    public GeneralUnitType XUnits;

    /// <summary><c>YUnits</c>.</summary>
    public GeneralUnitType YUnits;

    /// <summary><c>WidthUnits</c>.</summary>
    public DimensionUnitType WidthUnits;

    /// <summary><c>HeightUnits</c>.</summary>
    public DimensionUnitType HeightUnits;

    /// <summary><c>XOrigin</c>.</summary>
    public HorizontalAlignment XOrigin;

    /// <summary><c>YOrigin</c>.</summary>
    public VerticalAlignment YOrigin;

    /// <summary><c>Color</c>, or <c>FillColor</c> of a rectangle or a circle.</summary>
    public Color Color;

    /// <summary><c>StrokeColor</c> of a rectangle or a circle.</summary>
    public Color StrokeColor;

    /// <summary><c>Alpha</c> of an element drawn by an <c>InvisibleRenderable</c>.</summary>
    public int Alpha;

    /// <summary>
    /// Reads the animatable values of <paramref name="element"/>.
    /// </summary>
    /// <param name="element">Element to read.</param>
    /// <returns>Its current values.</returns>
    public static AnimatedValues Capture(GraphicalUiElement element)
    {
        var values = new AnimatedValues
        {
            X = element.X,
            Y = element.Y,
            Width = element.Width,
            Height = element.Height,
            Rotation = element.Rotation,
            XUnits = element.XUnits,
            YUnits = element.YUnits,
            WidthUnits = element.WidthUnits,
            HeightUnits = element.HeightUnits,
            XOrigin = element.XOrigin,
            YOrigin = element.YOrigin,
        };

        switch (element)
        {
            case SpriteRuntime sprite:
                values.ColorSource = ColorSource.Color;
                values.Color = sprite.Color;
                break;
            case NineSliceRuntime nineSlice:
                values.ColorSource = ColorSource.Color;
                values.Color = nineSlice.Color;
                break;
            case TextRuntime text:
                values.ColorSource = ColorSource.Color;
                values.Color = text.Color;
                break;
            case PolygonRuntime polygon:
                values.ColorSource = ColorSource.Color;
                values.Color = polygon.Color;
                break;
            case RectangleRuntime rectangle:
                values.ColorSource = ColorSource.FillAndStroke;
                values.Color = rectangle.FillColor;
                values.StrokeColor = rectangle.StrokeColor;
                break;
            case CircleRuntime circle:
                values.ColorSource = ColorSource.FillAndStroke;
                values.Color = circle.FillColor;
                values.StrokeColor = circle.StrokeColor;
                break;
            default:
                if (element.RenderableComponent is InvisibleRenderable invisible)
                {
                    values.ColorSource = ColorSource.Alpha;
                    values.Alpha = invisible.Alpha;
                }

                break;
        }

        return values;
    }

    /// <summary>
    /// Writes the values selected by <paramref name="changed"/> at <paramref name="progress"/> between
    /// <paramref name="from"/> and <paramref name="to"/>.
    /// </summary>
    /// <param name="element">Element the values belong to.</param>
    /// <param name="changed">Which values to write.</param>
    /// <param name="from">Values at progress 0.</param>
    /// <param name="to">Values at progress 1.</param>
    /// <param name="progress">Progress; may leave the 0–1 range when the motion overshoots.</param>
    /// <remarks>
    /// Positions, sizes and rotation follow the overshoot. Colors and transparency use the progress clamped to
    /// 0–1: integer color channels outside it overflow in Gum. A size does not cross zero when both ends have the
    /// same sign. ADR: motion/motion-core — Overshoot.
    /// </remarks>
    public static void Write(
        GraphicalUiElement element,
        AnimatedProperties changed,
        AnimatedValues from,
        AnimatedValues to,
        float progress)
    {
        if ((changed & AnimatedProperties.X) != 0)
        {
            element.X = Lerp(from.X, to.X, progress);
        }

        if ((changed & AnimatedProperties.Y) != 0)
        {
            element.Y = Lerp(from.Y, to.Y, progress);
        }

        if ((changed & AnimatedProperties.Width) != 0)
        {
            element.Width = LerpSize(from.Width, to.Width, progress);
        }

        if ((changed & AnimatedProperties.Height) != 0)
        {
            element.Height = LerpSize(from.Height, to.Height, progress);
        }

        if ((changed & AnimatedProperties.Rotation) != 0)
        {
            element.Rotation = Lerp(from.Rotation, to.Rotation, progress);
        }

        if ((changed & AnimatedProperties.Colors) != 0)
        {
            float amount = Math.Clamp(progress, 0f, 1f);
            WriteColors(
                element,
                from.ColorSource,
                changed,
                Color.Lerp(from.Color, to.Color, amount),
                Color.Lerp(from.StrokeColor, to.StrokeColor, amount),
                (int)MathF.Round(Lerp(from.Alpha, to.Alpha, amount)));
        }
    }

    /// <summary>
    /// Writes the values selected by <paramref name="changed"/> exactly as they are.
    /// </summary>
    /// <param name="element">Element the values belong to.</param>
    /// <param name="changed">Which values to write.</param>
    public readonly void WriteTo(GraphicalUiElement element, AnimatedProperties changed)
    {
        if ((changed & AnimatedProperties.X) != 0)
        {
            element.X = X;
        }

        if ((changed & AnimatedProperties.Y) != 0)
        {
            element.Y = Y;
        }

        if ((changed & AnimatedProperties.Width) != 0)
        {
            element.Width = Width;
        }

        if ((changed & AnimatedProperties.Height) != 0)
        {
            element.Height = Height;
        }

        if ((changed & AnimatedProperties.Rotation) != 0)
        {
            element.Rotation = Rotation;
        }

        if ((changed & AnimatedProperties.Colors) != 0)
        {
            WriteColors(element, ColorSource, changed, Color, StrokeColor, Alpha);
        }
    }

    /// <summary>
    /// Compares these values, taken before a state was applied, with the values after it.
    /// </summary>
    /// <param name="after">Values of the same element after the state.</param>
    /// <returns>The values to animate.</returns>
    public readonly AnimatedProperties ChangesTo(AnimatedValues after)
    {
        AnimatedProperties changed = AnimatedProperties.None;
        if (XUnits == after.XUnits && XOrigin == after.XOrigin && X != after.X)
        {
            changed |= AnimatedProperties.X;
        }

        if (YUnits == after.YUnits && YOrigin == after.YOrigin && Y != after.Y)
        {
            changed |= AnimatedProperties.Y;
        }

        if (WidthUnits == after.WidthUnits && Width != after.Width)
        {
            changed |= AnimatedProperties.Width;
        }

        if (HeightUnits == after.HeightUnits && Height != after.Height)
        {
            changed |= AnimatedProperties.Height;
        }

        if (Rotation != after.Rotation)
        {
            changed |= AnimatedProperties.Rotation;
        }

        if (Color != after.Color)
        {
            changed |= AnimatedProperties.Color;
        }

        if (StrokeColor != after.StrokeColor)
        {
            changed |= AnimatedProperties.StrokeColor;
        }

        if (Alpha != after.Alpha)
        {
            changed |= AnimatedProperties.Alpha;
        }

        return changed;
    }

    private static float Lerp(float from, float to, float progress) => from + ((to - from) * progress);

    private static float LerpSize(float from, float to, float progress)
    {
        float value = Lerp(from, to, progress);
        if (from >= 0 && to >= 0)
        {
            return MathF.Max(value, 0);
        }

        return from <= 0 && to <= 0 ? MathF.Min(value, 0) : value;
    }

    private static void WriteColors(
        GraphicalUiElement element,
        ColorSource source,
        AnimatedProperties changed,
        Color color,
        Color strokeColor,
        int alpha)
    {
        switch (source)
        {
            case ColorSource.Color when (changed & AnimatedProperties.Color) != 0:
                WriteColor(element, color);
                break;
            case ColorSource.FillAndStroke:
                WriteFillAndStroke(element, changed, color, strokeColor);
                break;
            case ColorSource.Alpha when (changed & AnimatedProperties.Alpha) != 0:
                if (element is ContainerRuntime container)
                {
                    container.Alpha = alpha;
                }
                else if (element.RenderableComponent is InvisibleRenderable invisible)
                {
                    invisible.Alpha = alpha;
                }

                break;
        }
    }

    private static void WriteColor(GraphicalUiElement element, Color color)
    {
        switch (element)
        {
            case SpriteRuntime sprite:
                sprite.Color = color;
                break;
            case NineSliceRuntime nineSlice:
                nineSlice.Color = color;
                break;
            case TextRuntime text:
                text.Color = color;
                break;
            case PolygonRuntime polygon:
                polygon.Color = color;
                break;
        }
    }

    private static void WriteFillAndStroke(
        GraphicalUiElement element,
        AnimatedProperties changed,
        Color fill,
        Color stroke)
    {
        bool writesFill = (changed & AnimatedProperties.Color) != 0;
        bool writesStroke = (changed & AnimatedProperties.StrokeColor) != 0;
        if (element is RectangleRuntime rectangle)
        {
            if (writesFill)
            {
                rectangle.FillColor = fill;
            }

            if (writesStroke)
            {
                rectangle.StrokeColor = stroke;
            }
        }
        else if (element is CircleRuntime circle)
        {
            if (writesFill)
            {
                circle.FillColor = fill;
            }

            if (writesStroke)
            {
                circle.StrokeColor = stroke;
            }
        }
    }
}
