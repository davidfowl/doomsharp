namespace DoomSharp;

/// <summary>
/// General-MIDI-program-aware voicing and pitch-bend helpers for <see cref="Music"/>'s software
/// synthesizer. Categorizes the 128 GM program numbers into broad timbre families (percussive,
/// plucked, sustained, bright/reed, synth) and gives each family distinct harmonic content and
/// amplitude-envelope shaping, plus shared MUS/MIDI pitch-bend-to-semitone conversion. This is a
/// clean-room approximation of General MIDI behavior, not a soundfont or OPL-accurate renderer.
/// </summary>
static class Instruments
{
    /// <summary>Broad timbre family driving envelope shape, independent of exact harmonic content.</summary>
    internal enum Family { Percussive, Plucked, Sustained, Bright, Synth }

    /// <summary>
    /// Resolved synthesis parameters for one GM program: additive-harmonic weights (fundamental
    /// first) and an amplitude envelope described as attack-to-peak, an exponential decay down to
    /// a sustain fraction of peak over <see cref="DecaySeconds"/>, and a release time. A nonzero
    /// <see cref="PluckOvershoot"/> adds a brief above-peak transient at the start of the note,
    /// approximating a plucked/struck attack.
    /// </summary>
    internal readonly record struct Voice(double[] Harmonics, double SustainLevel, double DecaySeconds, double ReleaseSeconds, double PluckOvershoot);

    /// <summary>Groups a 0-127 General MIDI program number into a broad timbre family.</summary>
    internal static Family Classify(int program) => Math.Clamp(program, 0, 127) switch
    {
        >= 0 and <= 15 => Family.Percussive,  // piano, chromatic percussion
        >= 16 and <= 23 => Family.Sustained,  // organ
        >= 24 and <= 39 => Family.Plucked,    // guitar, bass
        >= 40 and <= 55 => Family.Sustained,  // strings, ensemble
        >= 56 and <= 63 => Family.Bright,     // brass
        >= 64 and <= 79 => Family.Bright,     // reed, pipe
        >= 88 and <= 95 => Family.Sustained,  // synth pad
        _ => Family.Synth,                    // synth lead/effects and anything unmapped
    };

    static readonly double[] PercussiveHarmonics = [1, .5, .25, .12];
    static readonly double[] PluckedHarmonics = [1, .35, .15];
    static readonly double[] SustainedHarmonics = [1, .8, .6, .45, .3];
    static readonly double[] BrightHarmonics = [1, .7, .5, .35, .25];
    static readonly double[] SynthHarmonics = [1, .5, .25];

    /// <summary>Resolves the full synthesis <see cref="Voice"/> for a GM program number (0-127).</summary>
    internal static Voice ForProgram(int program) => Classify(program) switch
    {
        // Fast fundamental-heavy decay down to a quiet sustain: piano/mallet-like.
        Family.Percussive => new(PercussiveHarmonics, SustainLevel: .35, DecaySeconds: .25, ReleaseSeconds: .03, PluckOvershoot: 0),
        // Sharp attack transient followed by a moderate decay: guitar/bass pluck.
        Family.Plucked => new(PluckedHarmonics, SustainLevel: .55, DecaySeconds: .18, ReleaseSeconds: .05, PluckOvershoot: .3),
        // Near-constant level while held, long release: organ/strings/pad.
        Family.Sustained => new(SustainedHarmonics, SustainLevel: 1, DecaySeconds: double.PositiveInfinity, ReleaseSeconds: .12, PluckOvershoot: 0),
        // Bright, slight decay, medium release: brass/reed.
        Family.Bright => new(BrightHarmonics, SustainLevel: .8, DecaySeconds: .6, ReleaseSeconds: .07, PluckOvershoot: 0),
        _ => new(SynthHarmonics, SustainLevel: .9, DecaySeconds: .4, ReleaseSeconds: .03, PluckOvershoot: 0),
    };

    /// <summary>
    /// Converts a 7-bit MUS pitch-wheel controller value (0-127, 64 = centered/no bend) to
    /// semitones, assuming the default +/-2 semitone bend range id Software's DMX driver used.
    /// </summary>
    internal static double MusPitchBendSemitones(int value) => (Math.Clamp(value, 0, 127) - 64) / 64.0 * 2.0;

    /// <summary>
    /// Converts a 14-bit Standard MIDI File pitch-bend value (combined LSB/MSB data bytes, 0-16383,
    /// 8192 = centered/no bend) to semitones, assuming the General MIDI default +/-2 semitone range.
    /// </summary>
    internal static double MidiPitchBendSemitones(int lsb, int msb)
    {
        int value = ((msb & 0x7F) << 7) | (lsb & 0x7F);
        return (value - 8192) / 8192.0 * 2.0;
    }
}
