using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Flint.App.Controls;

/// <summary>
/// A seek bar: a slider that says when it is being dragged and sends one seek when it is let go.
/// </summary>
/// <remarks>
/// <para>
/// A plain slider changes its value continuously while dragged, and a player told about every
/// change would seek dozens of times on the way to where the person meant. This one reports the
/// drag starting, with <see cref="ScrubStarted"/>, and the place it ended, with
/// <see cref="SeekCommitted"/>, and nothing in between.
/// </para>
/// <para>
/// The arrow keys step by the skip distances and Home and End go to the start and to ten seconds
/// before the end, each committing at once. A screen reader hears the position in words, from
/// <see cref="SpokenValue"/>, rather than as a bare number of seconds.
/// </para>
/// </remarks>
public sealed class SeekBar : Slider
{
    /// <summary>How far before the end End goes, in seconds.</summary>
    public const double EndMargin = 10;

    /// <summary>Whether the bar is being dragged.</summary>
    public static readonly DirectProperty<SeekBar, bool> IsScrubbingProperty =
        AvaloniaProperty.RegisterDirect<SeekBar, bool>(nameof(IsScrubbing), bar => bar.IsScrubbing);

    /// <summary>What the bubble above the dragged thumb says.</summary>
    public static readonly StyledProperty<string> BubbleTextProperty =
        AvaloniaProperty.Register<SeekBar, string>(nameof(BubbleText), string.Empty);

    /// <summary>The value as a screen reader says it.</summary>
    public static readonly StyledProperty<string> SpokenValueProperty =
        AvaloniaProperty.Register<SeekBar, string>(nameof(SpokenValue), string.Empty);

    /// <summary>How far Left goes back, in seconds.</summary>
    public static readonly StyledProperty<double> StepBackProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(StepBack), 10);

    /// <summary>How far Right goes ahead, in seconds.</summary>
    public static readonly StyledProperty<double> StepForwardProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(StepForward), 30);

    private bool isScrubbing;

    /// <summary>Creates a seek bar.</summary>
    public SeekBar()
    {
        // Handled-too, and on the way back up: the slider has moved its value to the pointer by
        // the time these run, so the drag starts and ends where the person actually pressed.
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => Finish(), RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Raised when the bar is taken hold of, with where it is.</summary>
    public event EventHandler<double>? ScrubStarted;

    /// <summary>Raised once when the bar is let go, or a key moves it, with where it should go.</summary>
    public event EventHandler<double>? SeekCommitted;

    /// <inheritdoc cref="IsScrubbingProperty"/>
    public bool IsScrubbing
    {
        get => isScrubbing;
        private set => SetAndRaise(IsScrubbingProperty, ref isScrubbing, value);
    }

    /// <inheritdoc cref="BubbleTextProperty"/>
    public string BubbleText
    {
        get => GetValue(BubbleTextProperty);
        set => SetValue(BubbleTextProperty, value);
    }

    /// <inheritdoc cref="SpokenValueProperty"/>
    public string SpokenValue
    {
        get => GetValue(SpokenValueProperty);
        set => SetValue(SpokenValueProperty, value);
    }

    /// <inheritdoc cref="StepBackProperty"/>
    public double StepBack
    {
        get => GetValue(StepBackProperty);
        set => SetValue(StepBackProperty, value);
    }

    /// <inheritdoc cref="StepForwardProperty"/>
    public double StepForward
    {
        get => GetValue(StepForwardProperty);
        set => SetValue(StepForwardProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(SeekBar);

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        double? target = e.Key switch
        {
            Key.Left or Key.Down => Value - StepBack,
            Key.Right or Key.Up => Value + StepForward,
            Key.Home => Minimum,
            Key.End => Maximum - EndMargin,
            _ => null,
        };
        if (target is not { } to || !IsEnabled)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;
        SeekCommitted?.Invoke(this, Math.Clamp(to, Minimum, Maximum));
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new SeekBarAutomationPeer(this);

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Route is RoutingStrategies.Tunnel || IsScrubbing || !IsEnabled
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        IsScrubbing = true;
        ScrubStarted?.Invoke(this, Value);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Route is RoutingStrategies.Bubble)
        {
            Finish();
        }
    }

    private void Finish()
    {
        if (!IsScrubbing)
        {
            return;
        }

        IsScrubbing = false;
        SeekCommitted?.Invoke(this, Value);
    }

    /// <summary>Says the position in words, as well as the slider's numbers.</summary>
    private sealed class SeekBarAutomationPeer(SeekBar owner) : SliderAutomationPeer(owner), IValueProvider
    {
        bool IValueProvider.IsReadOnly => true;

        string? IValueProvider.Value => owner.SpokenValue;

        void IValueProvider.SetValue(string? value) =>
            throw new InvalidOperationException("The position is set by seeking, not by text.");
    }
}
