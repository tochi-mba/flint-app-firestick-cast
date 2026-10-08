using Flint.Core.Settings;

namespace Flint.App.ViewModels.Settings;

/// <summary>Sound with screen sharing: where it comes from, its quality and its delay.</summary>
public sealed partial class ScreenSettingsViewModel
{
    private IReadOnlyList<ScreenOption<string?>>? sources;

    /// <summary>Which output is shared.</summary>
    public SettingText SoundSourceText { get; } = new(
        "Sound comes from",
        "The output Windows plays through, or one you choose. Flint shares what that output plays.");

    /// <summary>The sound quality.</summary>
    public SettingText SoundQualityText { get; } = new(
        "Sound quality",
        "Higher sounds better and uses more data.");

    /// <summary>The sound delay trim.</summary>
    public SettingText SoundDelayText { get; } = new(
        "Sound delay",
        "If lips and voices do not line up on the TV, add delay here.");

    /// <summary>The longest delay the slider offers, in milliseconds.</summary>
    public static double MaximumSoundDelay => ScreenSettings.MaximumSoundDelayMilliseconds;

    /// <summary>The outputs sound can come from: the default, then each one Windows lists.</summary>
    /// <remarks>A chosen output that is not connected stays in the list, so the choice still shows.</remarks>
    public IReadOnlyList<ScreenOption<string?>> SoundSourceChoices => sources ??= ListSources();

    /// <summary>The output in use.</summary>
    public ScreenOption<string?>? SoundSource
    {
        get => SoundSourceChoices.FirstOrDefault(choice => choice.Value == ChosenOutput);
        set => Choose(value, (screen, id) => id is null
            ? screen with { SoundSource = Core.Settings.SoundSource.DefaultOutput }
            : screen with { SoundSource = Core.Settings.SoundSource.NamedDevice, SoundDeviceIdentity = id });
    }

    /// <summary>The sound qualities.</summary>
    public IReadOnlyList<ScreenOption<int>> SoundQualityChoices { get; } =
        [.. ScreenSettings.SoundBitrates.Select(kbps => new ScreenOption<int>(kbps, kbps switch
        {
            96 => "96 kbps, the least data",
            192 => "192 kbps, the best",
            _ => $"{kbps} kbps",
        }))];

    /// <summary>The sound quality in use.</summary>
    public ScreenOption<int>? SoundQuality
    {
        get => SoundQualityChoices.FirstOrDefault(choice => choice.Value == Screen.SoundKbps);
        set => Choose(value, (screen, kbps) => screen with { SoundKbps = kbps });
    }

    /// <summary>The sound delay, in milliseconds.</summary>
    public double SoundDelay
    {
        get => Screen.SoundDelayMilliseconds;
        set => Update(screen => screen with { SoundDelayMilliseconds = (int)Math.Round(value) });
    }

    /// <summary>The output sound comes from, or null for the default.</summary>
    private string? ChosenOutput =>
        Screen.SoundSource is Core.Settings.SoundSource.NamedDevice ? Screen.SoundDeviceIdentity : null;

    /// <summary>Reads the outputs again, for the list, the next time it is shown.</summary>
    private void ForgetSources() => sources = null;

    private List<ScreenOption<string?>> ListSources()
    {
        var choices = new List<ScreenOption<string?>> { new(null, "The output Windows plays through") };
        choices.AddRange(outputs().Select(output => new ScreenOption<string?>(output.Id, output.Name)));
        if (ChosenOutput is { } chosen && !choices.Exists(choice => choice.Value == chosen))
        {
            choices.Add(new(chosen, "An output that is not connected"));
        }

        return choices;
    }
}
