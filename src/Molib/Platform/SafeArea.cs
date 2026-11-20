using System.Diagnostics;
using System.Globalization;
using Gum;
using Gum.Converters;
using Gum.DataTypes;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RenderingLibrary.Graphics;

namespace Molib.Platform;

/// <summary>
/// Safe area of the screen in canvas units, and containers that keep their content inside it.
/// </summary>
/// <remarks>
/// molib does not ask the operating system for cutouts and system bars: the platform head supplies them through
/// <see cref="Source"/>. molib converts the pixels to canvas units with the current Gum camera zoom and applies them
/// to the attached containers. Backgrounds stay outside those containers and keep covering the whole screen.
/// ADR: platform/safe-area — Decision.
/// </remarks>
public sealed class SafeArea
{
    private readonly Game _game;
    private readonly GumService _gum;
    private readonly MolibDiagnostics _diagnostics;
    private readonly MolibLifetime _lifetime;
    private readonly List<WeakReference<GraphicalUiElement>> _containers = [];
    private ISafeAreaSource? _source;
    private Point _backBufferSize;
    private float _zoom;
    private volatile bool _isSourceChanged;

    internal SafeArea(Game game, GumService gum, MolibDiagnostics diagnostics, MolibLifetime lifetime)
    {
        _game = game;
        _gum = gum;
        _diagnostics = diagnostics;
        _lifetime = lifetime;
    }

    /// <summary>
    /// Where the insets come from; <see langword="null"/> means the whole screen is safe, as on desktop.
    /// </summary>
    /// <remarks>Setting a source reads its insets at once and applies them to the attached containers.</remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed and the value is not
    /// <see langword="null"/>.</exception>
    public ISafeAreaSource? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value))
            {
                return;
            }

            if (value is not null)
            {
                _lifetime.ThrowIfDisposed();
            }

            _source?.Changed -= OnSourceChanged;
            _source = value;
            _source?.Changed += OnSourceChanged;
            if (!_lifetime.IsDisposed)
            {
                Refresh();
            }
        }
    }

    /// <summary>
    /// Current insets in canvas units.
    /// </summary>
    public Thickness Insets { get; private set; }

    /// <summary>
    /// Current insets in back buffer pixels, as the source reported them.
    /// </summary>
    public Thickness InsetsInPixels { get; private set; }

    /// <summary>
    /// Raised after <see cref="Insets"/> changed and the attached containers were updated.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Makes <paramref name="container"/> fill its parent minus the insets and keeps it so when the insets change.
    /// </summary>
    /// <param name="container">Container that holds the content; usually the root container of a screen.</param>
    /// <remarks>
    /// molib takes over the position and size of the container: <c>X</c> and <c>Y</c> become the left and top
    /// insets, <c>Width</c> and <c>Height</c> become <c>RelativeToParent</c> minus both insets of the axis. Attaching
    /// the same container again does nothing. The container is held by a weak reference, so a screen the game throws
    /// away without <see cref="Detach"/> is not kept alive. ADR: platform/safe-area — Applying.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public void Attach(GraphicalUiElement container)
    {
        ArgumentNullException.ThrowIfNull(container);
        _lifetime.ThrowIfDisposed();
        if (IndexOf(container) < 0)
        {
            _containers.Add(new WeakReference<GraphicalUiElement>(container));
            Fit(container, Insets);
        }
    }

    /// <summary>
    /// Stops updating <paramref name="container"/>; its position and size stay as they are.
    /// </summary>
    /// <param name="container">A container passed to <see cref="Attach"/>.</param>
    /// <returns><see langword="true"/> if the container was attached.</returns>
    public bool Detach(GraphicalUiElement container)
    {
        ArgumentNullException.ThrowIfNull(container);
        int index = IndexOf(container);
        if (index < 0)
        {
            return false;
        }

        _containers.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Reads the source again when it reported a change or when the back buffer size or the camera zoom changed.
    /// </summary>
    /// <remarks>
    /// The zoom is read from the Gum camera rather than from <see cref="Scaling"/>, so the module works with any
    /// scaling the game uses. A quiet frame costs one flag and three comparisons.
    /// </remarks>
    internal void Update()
    {
        PresentationParameters parameters = _game.GraphicsDevice.PresentationParameters;
        if (!_isSourceChanged
            && parameters.BackBufferWidth == _backBufferSize.X
            && parameters.BackBufferHeight == _backBufferSize.Y
            && _gum.Renderer.Camera.Zoom == _zoom)
        {
            return;
        }

        Refresh();
    }

    /// <summary>
    /// Unsubscribes from the source.
    /// </summary>
    /// <summary>
    /// Leaves the source's event and forgets the attached containers; the service is being disposed.
    /// </summary>
    /// <remarks>
    /// The containers keep the insets they have: the screen does not jump under the cutout while the game tears
    /// molib down or creates a new service. ADR: platform/safe-area — Applying; foundation/architecture — Disposal.
    /// </remarks>
    internal void Shutdown()
    {
        _source?.Changed -= OnSourceChanged;
        _source = null;
        _containers.Clear();
    }

    private static void Fit(GraphicalUiElement container, Thickness insets)
    {
        bool suspend = !container.IsLayoutSuspended;
        if (suspend)
        {
            container.SuspendLayout();
        }

        container.XUnits = GeneralUnitType.PixelsFromSmall;
        container.YUnits = GeneralUnitType.PixelsFromSmall;
        container.XOrigin = HorizontalAlignment.Left;
        container.YOrigin = VerticalAlignment.Top;
        container.X = insets.Left;
        container.Y = insets.Top;
        container.WidthUnits = DimensionUnitType.RelativeToParent;
        container.HeightUnits = DimensionUnitType.RelativeToParent;
        container.Width = -(insets.Left + insets.Right);
        container.Height = -(insets.Top + insets.Bottom);

        if (suspend)
        {
            container.ResumeLayout();
        }
    }

    private static float ValidInset(float inset) => float.IsFinite(inset) && inset > 0 ? inset : 0;

    private int IndexOf(GraphicalUiElement container)
    {
        for (int i = 0; i < _containers.Count; i++)
        {
            if (_containers[i].TryGetTarget(out GraphicalUiElement? attached) && ReferenceEquals(attached, container))
            {
                return i;
            }
        }

        return -1;
    }

    private void OnSourceChanged(object? sender, EventArgs e) => _isSourceChanged = true;

    /// <remarks>
    /// The flag is cleared before the source is read: a change reported while reading sets it again and is picked up
    /// next frame, and one reported just before reading is already in the values read.
    /// </remarks>
    private void Refresh()
    {
        _isSourceChanged = false;
        PresentationParameters parameters = _game.GraphicsDevice.PresentationParameters;
        _backBufferSize = new Point(parameters.BackBufferWidth, parameters.BackBufferHeight);
        _zoom = _gum.Renderer.Camera.Zoom;
        Debug.Assert(_zoom > 0, "The Gum camera zoom must be positive.");

        Thickness pixels = Source?.GetInsetsInPixels() ?? default;
        var valid = new Thickness(
            ValidInset(pixels.Left),
            ValidInset(pixels.Top),
            ValidInset(pixels.Right),
            ValidInset(pixels.Bottom));
        if (valid != pixels)
        {
            string returned = string.Create(
                CultureInfo.InvariantCulture,
                $"{pixels.Left}, {pixels.Top}, {pixels.Right}, {pixels.Bottom}");
            _diagnostics.WarnOnce(
                "safeArea.badInsets",
                Source?.GetType().FullName ?? string.Empty,
                $"The safe area source returned {returned}; negative and non-finite insets were replaced with 0.");
        }

        InsetsInPixels = valid;
        var insets = new Thickness(valid.Left / _zoom, valid.Top / _zoom, valid.Right / _zoom, valid.Bottom / _zoom);
        if (insets == Insets)
        {
            return;
        }

        Insets = insets;
        for (int i = _containers.Count - 1; i >= 0; i--)
        {
            if (_containers[i].TryGetTarget(out GraphicalUiElement? container))
            {
                Fit(container, insets);
            }
            else
            {
                _containers.RemoveAt(i);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
