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
/// <para>
/// Threads: molib and Gum are used from the game thread, the one that runs <c>Game.Update</c>. Every method,
/// property and event of molib belongs to that thread: events are raised there, inside <see cref="Update"/> or
/// inside a Gum event, and tasks complete there, so code after <c>await</c> may change the interface. Two things may
/// come from any thread: cancelling the token of <c>ShowAsync</c> or <c>HideAsync</c>, and the <c>Changed</c> event
/// of a safe area source. molib only marks them and acts in the next <see cref="Update"/>.
/// ADR: foundation/architecture — Threads.
/// </para>
/// </remarks>
public sealed class MolibService : IDisposable
{
    private const float MaxDeltaSeconds = 0.1f;

    private readonly Game _game;
    private bool _isBackBufferChecked;
    private bool _isDisposing;

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
        Lifetime = new MolibLifetime();
        Diagnostics = new MolibDiagnostics();
        Motion = new MotionCore();
        Sound = new Sound(this);
        Scaling = new Scaling(game, gum, Motion, Diagnostics, Lifetime);
        SafeArea = new SafeArea(game, gum, Diagnostics, Lifetime);
        Transitions = new Transitions(gum, Motion, Diagnostics, Lifetime);
        ShowHideAnimator = new ShowHideAnimator(Motion, Diagnostics);
        Input = new Input(game, gum, Lifetime);
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
    /// Whether the service is disposed, shared with the modules.
    /// </summary>
    internal MolibLifetime Lifetime { get; }

    /// <summary>
    /// Runs the long presses of elements.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    internal Gestures Gestures
    {
        get
        {
            ObjectDisposedException.ThrowIf(Lifetime.IsDisposed, this);
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
            ObjectDisposedException.ThrowIf(Lifetime.IsDisposed, this);
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
            ObjectDisposedException.ThrowIf(Lifetime.IsDisposed, this);
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
    /// after a pause or a stall. An event handler may dispose the service in the middle of the frame; the update then
    /// stops after that module. ADR: foundation/architecture — Update order, Disposal.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public void Update(GameTime gameTime)
    {
        ArgumentNullException.ThrowIfNull(gameTime);
        ObjectDisposedException.ThrowIf(Lifetime.IsDisposed, this);

        if (!_isBackBufferChecked)
        {
            _isBackBufferChecked = true;
            CheckBackBufferPreserved();
        }

        float deltaSeconds = Math.Clamp((float)gameTime.ElapsedGameTime.TotalSeconds, 0f, MaxDeltaSeconds);
        Scaling.Update();
        if (IsEnding())
        {
            return;
        }

        SafeArea.Update();
        Input.Update();
        if (IsEnding())
        {
            return;
        }

        Layers.Update();
        if (IsEnding())
        {
            return;
        }

        Sound.Update(deltaSeconds);
        Gestures.Update(deltaSeconds);
        if (IsEnding())
        {
            return;
        }

        DragDrop.Update(deltaSeconds);
        if (IsEnding())
        {
            return;
        }

        Scrolling.Update(deltaSeconds);
        if (IsEnding())
        {
            return;
        }

        Motion.Update(deltaSeconds);
        if (IsEnding())
        {
            return;
        }

        Toasts.Update(deltaSeconds);
        Input.EndFrame();
    }

    /// <summary>
    /// Ends molib: finishes what is running, gives Gum back what molib changed, takes molib's handlers off Gum
    /// elements and removes the service from <see cref="Game.Services"/>. A new service may be created afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In order: sound events stop; every running animation jumps to its end and completes (a hide hides or removes
    /// its element, a <c>ShowAsync</c> or <c>HideAsync</c> task completes with <see langword="true"/>); a drag ends
    /// without events, its source gets its own look back and the copy leaves the tree; every layer closes at once
    /// and its <c>Closed</c> completes with <see cref="LayerCloseReason.Code"/>; every toast, shown or waiting, goes
    /// away; long presses, scroll inertia and sound subscriptions leave their elements; Gum's direction keys and
    /// menu gamepads come back as Gum had them; the safe area forgets its source and containers. Scaling stays as
    /// it is, so the screen does not jump.
    /// </para>
    /// <para>
    /// Dispose raises no molib events. After it, methods that add or start something throw
    /// <see cref="ObjectDisposedException"/>, while methods that remove, stop or undo something do nothing.
    /// ADR: foundation/architecture — Disposal.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (Lifetime.IsDisposed || _isDisposing)
        {
            return;
        }

        _isDisposing = true;
        Sound.Shutdown();
        Motion.Shutdown();
        DragDrop.Shutdown();
        Layers.Shutdown();
        Toasts.Shutdown();
        Gestures.Shutdown();
        Scrolling.Shutdown();
        Input.Shutdown();
        SafeArea.Shutdown();
        Lifetime.End();
        _game.Services.RemoveService(typeof(MolibService));
    }

    private bool IsEnding() => Lifetime.IsDisposed || _isDisposing;

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
