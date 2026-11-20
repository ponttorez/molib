namespace Molib.Platform;

/// <summary>
/// Distances from the four edges of a rectangle, such as safe area insets.
/// </summary>
/// <param name="Left">Distance from the left edge.</param>
/// <param name="Top">Distance from the top edge.</param>
/// <param name="Right">Distance from the right edge.</param>
/// <param name="Bottom">Distance from the bottom edge.</param>
public readonly record struct Thickness(float Left, float Top, float Right, float Bottom);
