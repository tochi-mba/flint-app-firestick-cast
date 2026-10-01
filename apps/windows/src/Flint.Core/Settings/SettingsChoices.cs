namespace Flint.Core.Settings;

/// <summary>What Flint does once a dropped connection to the TV comes back.</summary>
public enum ReconnectOutcome
{
    /// <summary>Reconnects and leaves the TV as it is.</summary>
    DoNothing = 0,

    /// <summary>Offers to carry on with what was showing.</summary>
    Ask = 1,

    /// <summary>Carries on with what was showing. Screen sharing still counts down visibly first.</summary>
    CarryOn = 2,
}

/// <summary>What closing the Flint window does.</summary>
public enum CloseWindowOutcome
{
    /// <summary>Asks the first time, then remembers the answer.</summary>
    Ask = 0,

    /// <summary>Hides the window; Flint keeps running in the tray.</summary>
    KeepRunning = 1,

    /// <summary>Quits Flint.</summary>
    Quit = 2,
}

/// <summary>Which display a screen share captures.</summary>
public enum ShareDisplayChoice
{
    /// <summary>The display Windows calls the main one.</summary>
    Main = 0,

    /// <summary>The display recorded in <see cref="ScreenSettings.DisplayIdentity"/>.</summary>
    Remembered = 1,
}

/// <summary>Whether pressing Share asks which display to share.</summary>
public enum ShareDisplayPrompt
{
    /// <summary>Shares the display chosen last time.</summary>
    UseLast = 0,

    /// <summary>Asks every time.</summary>
    AskEveryTime = 1,
}

/// <summary>The picture a screen share sends.</summary>
public enum PictureMode
{
    /// <summary>Everyday use.</summary>
    Balanced = 0,

    /// <summary>A cleaner picture for video.</summary>
    Movie = 1,

    /// <summary>Smoother motion.</summary>
    Game = 2,

    /// <summary>Sharp text with little motion.</summary>
    TextAndSlides = 3,

    /// <summary>For weak Wi-Fi.</summary>
    DataSaver = 4,

    /// <summary>The size, frame rate and data rate the person chose.</summary>
    Custom = 5,
}

/// <summary>The largest picture a custom screen share sends.</summary>
public enum SizeLimit
{
    /// <summary>1280 by 720.</summary>
    P720 = 0,

    /// <summary>1920 by 1080.</summary>
    P1080 = 1,

    /// <summary>The TV's own screen size.</summary>
    MatchTv = 2,

    /// <summary>The shared display's own size, never larger than the TV.</summary>
    Native = 3,
}

/// <summary>What the TV shows while a screen share is paused.</summary>
public enum PausedPicture
{
    /// <summary>The last picture sent before the pause.</summary>
    LastPicture = 0,

    /// <summary>A black screen.</summary>
    Black = 1,
}

/// <summary>Where shared sound plays.</summary>
public enum SoundDestination
{
    /// <summary>On the TV only; this PC is muted while sharing.</summary>
    TvOnly = 0,

    /// <summary>On the TV and on this PC.</summary>
    TvAndPc = 1,
}

/// <summary>Which of this PC's sound outputs is shared.</summary>
public enum SoundSource
{
    /// <summary>Whatever Windows is currently playing through.</summary>
    DefaultOutput = 0,

    /// <summary>The device recorded in <see cref="ScreenSettings.SoundDeviceIdentity"/>.</summary>
    NamedDevice = 1,
}

/// <summary>How the media queue repeats.</summary>
public enum RepeatMode
{
    /// <summary>Plays the queue once.</summary>
    Off = 0,

    /// <summary>Repeats the current item.</summary>
    One = 1,

    /// <summary>Starts the queue again after the last item.</summary>
    All = 2,
}

/// <summary>What happens when a file played before is played again.</summary>
public enum ResumeMode
{
    /// <summary>Carries on from where it stopped.</summary>
    Resume = 0,

    /// <summary>Asks whether to carry on or start over.</summary>
    Ask = 1,

    /// <summary>Starts from the beginning.</summary>
    StartOver = 2,
}

/// <summary>The order dropped files are queued in.</summary>
public enum DropOrder
{
    /// <summary>By name, with numbers in counting order.</summary>
    ByName = 0,

    /// <summary>In the order they were dropped.</summary>
    AsDropped = 1,
}

/// <summary>What the TV shows once the queue has finished.</summary>
public enum QueueEnd
{
    /// <summary>The last item stays up.</summary>
    LeaveLastItem = 0,

    /// <summary>The TV goes back to its home screen.</summary>
    TvHome = 1,
}
