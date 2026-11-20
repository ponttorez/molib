using Gum;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Molib.Integration;
using Molib.Interaction;
using Molib.Motion;
using Molib.Platform;
using Molib.Widgets;

namespace Molib;

/// <summary>
/// Entry point of molib: holds every module and updates them once per frame after Gum.
/// </summary>
/// <remarks>
/// <para>
/// Create it after <c>GumService.Initialize</c>, call <see cref="Update"/> right after <c>GumService.Update</c>, and
/// dispose it together with the game. molib draws nothing itself and owns no elements: everything on screen is Gum.
/// </para>
/// <para>
/// Gum keeps its state in statics, so two sets of molib modules over one Gum would fight over the same roots. The
/// service registers itself in <see cref="Game.Services"/> and refuses to be created twice; the registration also
/// lets any code that has the game find the service. ADR: foundation/architecture — Entry point.
/// </para>
/// </remarks>
public sealed class MolibService : IDisposable
{
    private const float MaxDeltaSeconds = 0.1f;

    private readonly Game _game;
    private bool _isBackBufferChecked;
    private bool _isDisposed;

    /// <summary>
    /// Creates the service and registers it in <see cref="Game.Services"/>.
    /// </summary>
    /// <param name="game">The running game; its graphics device must already exist.</param>
    /// <param name="gum">The initialized Gum service, usually <c>GumService.Default</c>.</param>
    /// <exception cref="InvalidOperationException">
    /// Gum is not initialized, or the game already has a <see cref="MolibService"/> that is not disposed.
    /// </exception>
    public MolibService(Game game, GumService gum)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(gum);
        if (!gum.IsInitialized)
        {
            throw new InvalidOperationException(
                "Initialize Gum with GumService.Initialize before creating MolibService.");
        }

        if (game.Services.GetService<MolibService>() is not null)
        {
            throw new InvalidOperationException(
                "This game already has a MolibService. Dispose it before creating another one.");
        }

        _game = game;
        Diagnostics = new MolibDiagnostics();
        Motion = new MotionCore();
        Sound = new Sound(this);
        Scaling = new Scaling(game, gum, Motion, Diagnostics);
        SafeArea = new SafeArea(game, gum, Diagnostics);
        Transitions = new Transitions(gum, Motion, Diagnostics);
        ShowHideAnimator = new ShowHideAnimator(Motion, Diagnostics);
        Input = new Input(game, gum);
        Layers = new Layers(this, game, gum);
        Gestures = new Gestures();
        DragDrop = new DragDrop(this, game, gum);
        Scrolling = new Scrolling(this);
        Toasts = new Toasts(this, gum);
        game.Services.AddService(this);
    }

    /// <summary>
    /// Warnings about data errors. Subscribe before the first <see cref="Update"/> to receive the start-up checks.
    /// </summary>
    public MolibDiagnostics Diagnostics { get; }

    /// <summary>
    /// Godot-style scaling and orientation.
    /// </summary>
    public Scaling Scaling { get; }

    /// <summary>
    /// Safe area insets and containers that keep their content inside them.
    /// </summary>
    public SafeArea SafeArea { get; }

    /// <summary>
    /// The motion core: time scale and the number of running animations.
    /// </summary>
    public MotionCore Motion { get; }

    /// <summary>
    /// Smooth transitions between Gum states.
    /// </summary>
    public Transitions Transitions { get; }

    /// <summary>
    /// Gamepads, the last device, hiding and restoring the focus, the initial focus of a screen, arrow keys and
    /// paging.
    /// </summary>
    public Input Input { get; }

    /// <summary>
    /// Modal and popup layers and the cancel stack.
    /// </summary>
    public Layers Layers { get; }

    /// <summary>
    /// Drag and drop between containers, with the mouse, touch, a gamepad and the keyboard.
    /// </summary>
    public DragDrop DragDrop { get; }

    /// <summary>
    /// Toasts: short messages over the interface that disappear by themselves.
    /// </summary>
    public Toasts Toasts { get; }

    /// <summary>
    /// Sound events of the interface, handed to the game.
    /// </summary>
    public Sound Sound { get; }

    /// <summary>
    /// Runs the long presses of elements.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    internal Gestures Gestures
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return field;
        }
    }

    /// <summary>
    /// Runs the inertia and snapping of scroll viewers.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    internal Scrolling Scrolling
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return field;
        }
    }

    /// <summary>
    /// Runs <c>ShowAsync</c> and <c>HideAsync</c>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    internal ShowHideAnimator ShowHideAnimator
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return field;
        }
    }

    /// <summary>
    /// Updates every module. Call once per frame right after <c>GumService.Update</c>.
    /// </summary>
    /// <param name="gameTime">Time of the current frame.</param>
    /// <remarks>
    /// Gum has already handled input and applied the Forms states of this frame, so molib sees the result. Modules
    /// run in a fixed order: scaling first, because the safe area depends on the zoom; input before the layers,
    /// because a press that brought the focus back does not cancel; long press, drag and drop and scrolling after
    /// the layers, because a cancel press ends a drag first; motion after the modules that start animations; the
    /// timers of toasts after motion, which completes their animations; last, the focus is remembered for the next
    /// frame, after anything that could still move it. The frame time is limited to 0.1 s, so animations do not jump
    /// after a pause or a stall.
    /// ADR: foundation/architecture — Update order.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public void Update(GameTime gameTime)
    {
        ArgumentNullException.ThrowIfNull(gameTime);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!_isBackBufferChecked)
        {
            _isBackBufferChecked = true;
            CheckBackBufferPreserved();
        }

        float deltaSeconds = Math.Clamp((float)gameTime.ElapsedGameTime.TotalSeconds, 0f, MaxDeltaSeconds);
        Scaling.Update();
        SafeArea.Update();
        Input.Update();
        Layers.Update();
        Sound.Update(deltaSeconds);
        Gestures.Update(deltaSeconds);
        DragDrop.Update(deltaSeconds);
        Scrolling.Update(deltaSeconds);
        Motion.Update(deltaSeconds);
        Toasts.Update(deltaSeconds);
        Input.EndFrame();
    }

    /// <summary>
    /// Unsubscribes molib from the game and Gum and removes the service from <see cref="Game.Services"/>.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        SafeArea.Dispose();
        DragDrop.Shutdown();
        Input.Shutdown();
        Motion.Shutdown();
        _game.Services.RemoveService(typeof(MolibService));
    }

    /// <remarks>
    /// Runs in the first <see cref="Update"/> rather than in the constructor: the game cannot subscribe to
    /// <see cref="Diagnostics"/> before the service exists. Without <see cref="RenderTargetUsage.PreserveContents"/>
    /// a container drawn through a render target erases what the game drew before Gum in that frame.
    /// ADR: motion/show-hide — Transparency.
    /// </remarks>
    private void CheckBackBufferPreserved()
    {
        if (_game.GraphicsDevice.PresentationParameters.RenderTargetUsage != RenderTargetUsage.PreserveContents)
        {
            Diagnostics.WarnOnce(
                "motion.backBufferNotPreserved",
                string.Empty,
                "The back buffer does not use RenderTargetUsage.PreserveContents: a fading layer will erase what the "
                    + "game drew before Gum. Set it in GraphicsDeviceManager.PreparingDeviceSettings.");
        }
    }
}
