using System.Globalization;
using Gum;
using Gum.DataTypes;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Molib.Motion;

namespace Molib.Platform;

/// <summary>
/// Godot-style scaling: a base resolution, a zoom that keeps the whole base visible, and a canvas that expands to the
/// window along the longer side. Also tracks the orientation and applies the <c>Orientation</c> category states.
/// </summary>
/// <remarks>
/// <c>zoom = min(windowWidth / baseWidth, windowHeight / baseHeight) * UserScale</c>,
/// <c>canvas = window / zoom</c>, where the window is the back buffer size in pixels. Nothing is cropped and there
/// are no bars. Gum's own <c>EnableZoomToWindow</c> scales by one axis and crops in portrait, and
/// <c>EnableExpandToWindow</c> does not scale; do not enable either together with this module.
/// ADR: platform/scaling — Decision.
/// </remarks>
public sealed class Scaling
{
    /// <summary>
    /// Name of the Gum state category that molib applies when the orientation changes.
    /// </summary>
    public const string OrientationCategory = "Orientation";

    /// <summary>
    /// State of <see cref="OrientationCategory"/> applied when the back buffer is taller than it is wide.
    /// </summary>
    public const string PortraitState = "Portrait";

    /// <summary>
    /// State of <see cref="OrientationCategory"/> applied when the back buffer is at least as wide as it is tall.
    /// </summary>
    public const string LandscapeState = "Landscape";

    /// <summary>
    /// Smallest allowed <see cref="UserScale"/>.
    /// </summary>
    public const float MinUserScale = 0.5f;

    /// <summary>
    /// Largest allowed <see cref="UserScale"/>.
    /// </summary>
    public const float MaxUserScale = 2f;

    private readonly MolibLifetime _lifetime;
    private readonly Game _game;
    private readonly GumService _gum;
    private readonly MotionCore _motion;
    private readonly MolibDiagnostics _diagnostics;
    private Point _landscapeBase;
    private Point _portraitBase;
    private Point _backBufferSize;
    private bool _isDirty;
    private bool _isOrientationApplied;

    internal Scaling(Game game, GumService gum, MotionCore motion, MolibDiagnostics diagnostics, MolibLifetime lifetime)
    {
        _lifetime = lifetime;
        _game = game;
        _gum = gum;
        _motion = motion;
        _diagnostics = diagnostics;
        Zoom = gum.Renderer.Camera.Zoom;
        CanvasWidth = gum.CanvasWidth;
        CanvasHeight = gum.CanvasHeight;
        Point backBuffer = ReadBackBufferSize();
        IsPortrait = backBuffer.Y > backBuffer.X;
    }

    /// <summary>
    /// Whether <see cref="Enable(int, int)"/> was called. Until then the module does nothing and the properties keep
    /// the values Gum had when molib was created.
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Interface size multiplier for the game settings, from <see cref="MinUserScale"/> to
    /// <see cref="MaxUserScale"/>; 1 by default. Takes effect in the next <c>MolibService.Update</c>.
    /// </summary>
    /// <remarks>
    /// A value outside the range is a data error, not a programmer error: it usually comes from a settings file.
    /// molib reports <c>scaling.badUserScale</c> and clamps it; a value that is not a number becomes 1.
    /// ADR: platform/scaling — Formula.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public float UserScale
    {
        get;
        set
        {
            _lifetime.ThrowIfDisposed();
            float scale = value;
            if (!float.IsFinite(scale) || scale < MinUserScale || scale > MaxUserScale)
            {
                scale = float.IsNaN(scale) ? 1f : Math.Clamp(scale, MinUserScale, MaxUserScale);
                _diagnostics.WarnOnce(
                    "scaling.badUserScale",
                    string.Empty,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"UserScale must be from {MinUserScale} to {MaxUserScale}; got {value}, using {scale}."));
            }

            if (scale != field)
            {
                field = scale;
                _isDirty = true;
            }
        }
    } = 1f;

    /// <summary>
    /// Back buffer pixels per canvas unit: the value of the Gum camera zoom.
    /// </summary>
    public float Zoom { get; private set; }

    /// <summary>
    /// Canvas width in canvas units: the logical width the interface is laid out in.
    /// </summary>
    public float CanvasWidth { get; private set; }

    /// <summary>
    /// Canvas height in canvas units: the logical height the interface is laid out in.
    /// </summary>
    public float CanvasHeight { get; private set; }

    /// <summary>
    /// Whether the back buffer is taller than it is wide.
    /// </summary>
    public bool IsPortrait { get; private set; }

    /// <summary>
    /// Raised after the zoom or the canvas size changed, with the new values. Orientation states are already applied
    /// when it is raised.
    /// </summary>
    public event EventHandler<ScalingChangedEventArgs>? Changed;

    /// <summary>
    /// Turns scaling on with one base resolution for both orientations, as in Godot.
    /// </summary>
    /// <param name="baseWidth">Base width in canvas units.</param>
    /// <param name="baseHeight">Base height in canvas units.</param>
    /// <exception cref="ArgumentOutOfRangeException">A side of the base is zero or negative.</exception>
    /// <exception cref="InvalidOperationException">Scaling is already enabled.</exception>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public void Enable(int baseWidth, int baseHeight) => Enable(baseWidth, baseHeight, baseWidth, baseHeight);

    /// <summary>
    /// Turns scaling on with its own base resolution for the portrait orientation.
    /// </summary>
    /// <param name="baseWidth">Base width in canvas units for the landscape orientation.</param>
    /// <param name="baseHeight">Base height in canvas units for the landscape orientation.</param>
    /// <param name="portraitBaseWidth">Base width in canvas units for the portrait orientation.</param>
    /// <param name="portraitBaseHeight">Base height in canvas units for the portrait orientation.</param>
    /// <remarks>
    /// Applies the scaling and the orientation state at once, so the first frame is already laid out.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A side of a base is zero or negative.</exception>
    /// <exception cref="InvalidOperationException">Scaling is already enabled.</exception>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public void Enable(int baseWidth, int baseHeight, int portraitBaseWidth, int portraitBaseHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(portraitBaseWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(portraitBaseHeight);
        _lifetime.ThrowIfDisposed();
        if (IsEnabled)
        {
            throw new InvalidOperationException("Scaling is already enabled; call Enable once.");
        }

        _landscapeBase = new Point(baseWidth, baseHeight);
        _portraitBase = new Point(portraitBaseWidth, portraitBaseHeight);
        IsEnabled = true;

        Point backBuffer = ReadBackBufferSize();
        if (backBuffer.X > 0 && backBuffer.Y > 0)
        {
            Apply(backBuffer);
        }
        else
        {
            _isDirty = true;
        }
    }

    /// <summary>
    /// Converts a point from back buffer pixels to canvas units through the Gum camera.
    /// </summary>
    /// <param name="screen">Point in back buffer pixels.</param>
    /// <returns>The same point in canvas units.</returns>
    public Vector2 ScreenToCanvas(Vector2 screen)
    {
        _gum.Renderer.Camera.ScreenToWorld(screen.X, screen.Y, out float x, out float y);
        return new Vector2(x, y);
    }

    /// <summary>
    /// Converts a point from canvas units to back buffer pixels through the Gum camera.
    /// </summary>
    /// <param name="canvas">Point in canvas units.</param>
    /// <returns>The same point in back buffer pixels.</returns>
    public Vector2 CanvasToScreen(Vector2 canvas)
    {
        _gum.Renderer.Camera.WorldToScreen(canvas.X, canvas.Y, out float x, out float y);
        return new Vector2(x, y);
    }

    /// <summary>
    /// Applies the state of the current orientation to <paramref name="element"/> and its descendants that have the
    /// <see cref="OrientationCategory"/> category; others are left untouched.
    /// </summary>
    /// <param name="element">An element the game added to the tree itself, after the last orientation change.</param>
    /// <remarks>
    /// molib applies the state to <c>Root</c>, <c>ModalRoot</c> and <c>PopupRoot</c> when the orientation changes;
    /// an element added later does not have it yet. The state is applied without a state transition: the element was
    /// not on screen with the old orientation. ADR: platform/scaling — Orientation.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public void ApplyOrientation(GraphicalUiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _lifetime.ThrowIfDisposed();
        _motion.BeginInstant();
        try
        {
            ApplyOrientationState(element);
        }
        finally
        {
            _motion.EndInstant();
        }
    }

    /// <summary>
    /// Recalculates the scaling when the back buffer size or <see cref="UserScale"/> changed since the last frame.
    /// </summary>
    /// <remarks>
    /// The back buffer size is compared every frame instead of listening to <c>Window.ClientSizeChanged</c> and
    /// <c>Window.OrientationChanged</c>: those come at different times on different platforms and not always.
    /// ADR: platform/scaling — When to recompute.
    /// </remarks>
    internal void Update()
    {
        if (!IsEnabled)
        {
            return;
        }

        Point backBuffer = ReadBackBufferSize();
        if ((backBuffer == _backBufferSize && !_isDirty) || backBuffer.X <= 0 || backBuffer.Y <= 0)
        {
            return;
        }

        Apply(backBuffer);
    }

    /// <summary>
    /// Sets a root to fill the canvas, the same way Gum does at the start of its own update.
    /// </summary>
    /// <remarks>
    /// molib updates after Gum, so without this the roots would keep the old canvas size until the next Gum update
    /// and one frame would be drawn with the new zoom and the old layout.
    /// </remarks>
    private static void FitToCanvas(GraphicalUiElement root, float canvasWidth, float canvasHeight)
    {
        bool suspend = !root.IsLayoutSuspended;
        if (suspend)
        {
            root.SuspendLayout();
        }

        root.X = 0;
        root.Y = 0;
        root.WidthUnits = DimensionUnitType.Absolute;
        root.HeightUnits = DimensionUnitType.Absolute;
        root.Width = canvasWidth;
        root.Height = canvasHeight;

        if (suspend)
        {
            root.ResumeLayout();
        }
    }

    private void Apply(Point backBuffer)
    {
        _backBufferSize = backBuffer;
        _isDirty = false;

        bool isPortrait = backBuffer.Y > backBuffer.X;
        Point baseSize = isPortrait ? _portraitBase : _landscapeBase;
        float zoom = MathF.Min((float)backBuffer.X / baseSize.X, (float)backBuffer.Y / baseSize.Y) * UserScale;
        float canvasWidth = backBuffer.X / zoom;
        float canvasHeight = backBuffer.Y / zoom;

        _gum.Renderer.Camera.Zoom = zoom;
        _gum.CanvasWidth = canvasWidth;
        _gum.CanvasHeight = canvasHeight;
        FitToCanvas(_gum.Root, canvasWidth, canvasHeight);
        FitToCanvas(_gum.ModalRoot, canvasWidth, canvasHeight);
        FitToCanvas(_gum.PopupRoot, canvasWidth, canvasHeight);

        bool orientationChanged = !_isOrientationApplied || isPortrait != IsPortrait;
        Zoom = zoom;
        CanvasWidth = canvasWidth;
        CanvasHeight = canvasHeight;
        IsPortrait = isPortrait;

        if (orientationChanged)
        {
            ApplyOrientationToRoots(animate: _isOrientationApplied);
            _isOrientationApplied = true;
        }

        Changed?.Invoke(this, new ScalingChangedEventArgs(zoom, canvasWidth, canvasHeight, isPortrait));
    }

    /// <remarks>
    /// The first application, from <see cref="Enable(int, int, int, int)"/>, is not animated: nothing was on screen
    /// with another orientation. Later rotations animate where a visual has transitions for the category.
    /// ADR: platform/scaling — Orientation.
    /// </remarks>
    private void ApplyOrientationToRoots(bool animate)
    {
        if (!animate)
        {
            _motion.BeginInstant();
        }

        try
        {
            ApplyOrientationState(_gum.Root);
            ApplyOrientationState(_gum.ModalRoot);
            ApplyOrientationState(_gum.PopupRoot);
        }
        finally
        {
            if (!animate)
            {
                _motion.EndInstant();
            }
        }
    }

    private void ApplyOrientationState(GraphicalUiElement element) =>
        element.ApplyStateRecursive(OrientationCategory, IsPortrait ? PortraitState : LandscapeState);

    private Point ReadBackBufferSize()
    {
        PresentationParameters parameters = _game.GraphicsDevice.PresentationParameters;
        return new Point(parameters.BackBufferWidth, parameters.BackBufferHeight);
    }
}
