namespace DoomSharp;

/// <summary>
/// Decodes classic "D_*" music lumps (either id Software's MUS format or a Standard MIDI File, as
/// bundled by some IWADs such as Freedoom) into looping PCM, using a small built-in additive/noise
/// synthesizer. There is no General MIDI soundfont or DMX OPL emulation; timbres are approximated
/// by General MIDI program group (see <see cref="Instruments"/>), and pitch-bend controller events
/// are translated to a frequency offset, but this is still not byte-for-byte original playback.
/// </summary>
public static class Music
{
    const double TicksPerSecondMus = 140;
    const double MaxDurationSeconds = 240;

    /// <summary>
    /// A resolved note: an absolute time span, pitch (including any applied pitch-bend) and loudness
    /// independent of the source format. <see cref="Bend"/> is the pitch-bend offset in semitones that
    /// was folded into <see cref="Frequency"/>, retained separately for diagnostics/testing.
    /// </summary>
    internal readonly record struct NoteSpan(double Start, double End, double Frequency, double Amplitude, int Instrument, bool Percussion, double Bend);

    public static short[]? Render(byte[]? lump, int sampleRate)
    {
        if (lump == null) return null;
        List<NoteSpan> notes; double duration;
        if (IsMus(lump)) (notes, duration) = ParseMus(lump);
        else if (IsMidi(lump)) (notes, duration) = ParseMidi(lump);
        else return null;
        return Synthesize(notes, duration, sampleRate);
    }

    /// <summary>Test-only entry point exposing the resolved MUS note sequence (including pitch-bend).</summary>
    internal static (List<NoteSpan> Notes, double Duration) ParseMusForTest(byte[] lump) => ParseMus(lump);

    static bool IsMus(byte[] b) => b.Length >= 4 && b[0] == 'M' && b[1] == 'U' && b[2] == 'S' && b[3] == 0x1A;
    static bool IsMidi(byte[] b) => b.Length >= 4 && b[0] == 'M' && b[1] == 'T' && b[2] == 'h' && b[3] == 'd';

    static NoteSpan MakeNote(long startTick, long endTick, double ticksPerSecond, int note, int velocity, int volume, int patch, bool percussion, double bendSemitones)
    {
        double start = startTick / ticksPerSecond, end = Math.Max(start + .01, endTick / ticksPerSecond);
        double amplitude = Math.Clamp(velocity / 127.0, 0, 1) * Math.Clamp(volume / 127.0, 0, 1);
        double frequency = 440.0 * Math.Pow(2, (Math.Clamp(note, 0, 127) - 69) / 12.0) * Math.Pow(2, bendSemitones / 12.0);
        return new NoteSpan(start, end, frequency, amplitude, patch, percussion, bendSemitones);
    }

    // --- MUS (id Software's native sequencer format; see e.g. https://moddingwiki.shikadi.net/wiki/MUS_Format) ---

    static (List<NoteSpan>, double) ParseMus(byte[] b)
    {
        if (b.Length < 16) throw new InvalidDataException("Truncated MUS header.");
        int scoreLength = Wad.U16(b, 4), scoreStart = Wad.U16(b, 6);
        if (scoreStart < 0 || scoreLength < 0 || scoreStart > b.Length || scoreLength > b.Length - scoreStart)
            throw new InvalidDataException("Invalid MUS score offsets.");
        int[] patch = new int[16];
        int[] volume = new int[16]; Array.Fill(volume, 127);
        int[] lastVelocity = new int[16]; Array.Fill(lastVelocity, 64);
        double[] bend = new double[16];
        Dictionary<(int Channel, int Note), (long Tick, int Velocity, int Volume, int Patch)> active = [];
        List<NoteSpan> notes = [];
        int pos = scoreStart, end = scoreStart + scoreLength;
        long tick = 0;
        bool finished = false;
        while (pos < end && !finished)
        {
            byte eventByte = b[pos++];
            bool last = (eventByte & 0x80) != 0;
            int type = (eventByte >> 4) & 7, channel = eventByte & 0xF;
            switch (type)
            {
                case 0: // release note
                    if (pos >= end) throw new InvalidDataException("Truncated MUS release-note event.");
                    int releaseNote = b[pos++] & 0x7F;
                    if (active.Remove((channel, releaseNote), out var released))
                        notes.Add(MakeNote(released.Tick, tick, TicksPerSecondMus, releaseNote, released.Velocity, released.Volume, released.Patch, channel == 15, bend[channel]));
                    break;
                case 1: // play note
                    if (pos >= end) throw new InvalidDataException("Truncated MUS play-note event.");
                    byte noteByte = b[pos++];
                    int note = noteByte & 0x7F;
                    if ((noteByte & 0x80) != 0)
                    {
                        if (pos >= end) throw new InvalidDataException("Truncated MUS note volume.");
                        lastVelocity[channel] = b[pos++] & 0x7F;
                    }
                    if (active.Remove((channel, note), out var retriggered))
                        notes.Add(MakeNote(retriggered.Tick, tick, TicksPerSecondMus, note, retriggered.Velocity, retriggered.Volume, retriggered.Patch, channel == 15, bend[channel]));
                    active[(channel, note)] = (tick, lastVelocity[channel], volume[channel], patch[channel]);
                    break;
                case 2: // pitch bend; the 7-bit wheel value (64 = centered) is translated to semitones
                    if (pos >= end) throw new InvalidDataException("Truncated MUS pitch-bend event.");
                    bend[channel] = Instruments.MusPitchBendSemitones(b[pos++] & 0x7F);
                    break;
                case 3: // system event (all sounds/notes off, mono, poly, reset controllers)
                    if (pos >= end) throw new InvalidDataException("Truncated MUS system event.");
                    int system = b[pos++] & 0x7F;
                    if (system is 10 or 11)
                        foreach ((int Channel, int Note) key in active.Keys.Where(k => k.Channel == channel).ToArray())
                        {
                            var info = active[key]; active.Remove(key);
                            notes.Add(MakeNote(info.Tick, tick, TicksPerSecondMus, key.Note, info.Velocity, info.Volume, info.Patch, channel == 15, bend[channel]));
                        }
                    break;
                case 4: // controller change; only patch (0) and volume (3) affect playback here
                    if (pos + 1 >= end) throw new InvalidDataException("Truncated MUS controller event.");
                    int number = b[pos++] & 0x7F, value = b[pos++] & 0x7F;
                    if (number == 0) patch[channel] = value;
                    else if (number == 3) volume[channel] = value;
                    break;
                case 6: // score end
                    finished = true;
                    break;
                default: break; // 5 end-of-measure and 7 unused carry no data
            }
            if (!finished && last)
            {
                long delta = 0; byte more;
                do
                {
                    if (pos >= end) throw new InvalidDataException("Truncated MUS delta time.");
                    more = b[pos++];
                    delta = (delta << 7) | (uint)(more & 0x7F);
                } while ((more & 0x80) != 0);
                tick += delta;
            }
        }
        foreach (((int Channel, int Note) key, var info) in active)
            notes.Add(MakeNote(info.Tick, tick, TicksPerSecondMus, key.Note, info.Velocity, info.Volume, info.Patch, key.Channel == 15, bend[key.Channel]));
        return (notes, tick / TicksPerSecondMus);
    }

    // --- Standard MIDI File (format 0/1; used directly by some IWADs, e.g. Freedoom's D_* lumps) ---

    static uint ReadU32(byte[] b, int offset) => (uint)((b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3]);
    static int ReadU16(byte[] b, int offset) => (b[offset] << 8) | b[offset + 1];
    static string ReadId(byte[] b, int offset) => System.Text.Encoding.ASCII.GetString(b, offset, 4);
    static long ReadVarLen(byte[] b, ref int pos, int end)
    {
        long value = 0; byte more;
        do
        {
            if (pos >= end) throw new InvalidDataException("Truncated MIDI variable-length value.");
            more = b[pos++];
            value = (value << 7) | (uint)(more & 0x7F);
        } while ((more & 0x80) != 0);
        return value;
    }

    static (List<NoteSpan>, double) ParseMidi(byte[] b)
    {
        if (b.Length < 14 || ReadId(b, 0) != "MThd") throw new InvalidDataException("Invalid MIDI header.");
        uint headerLength = ReadU32(b, 4);
        if (headerLength < 6 || 8 + headerLength > b.Length) throw new InvalidDataException("Invalid MIDI header length.");
        int trackCount = ReadU16(b, 10), division = ReadU16(b, 12);
        if (trackCount <= 0) throw new InvalidDataException("MIDI file has no tracks.");
        if ((division & 0x8000) != 0) division = 96; // SMPTE time code is not supported; fall back to a plausible resolution.
        if (division == 0) throw new InvalidDataException("Invalid MIDI ticks-per-quarter-note.");

        List<(long Tick, int Status, int Data1, int Data2)> events = [];
        List<(long Tick, int Microseconds)> tempos = [];
        int pos = 8 + (int)headerLength;
        long lastTick = 0;
        for (int t = 0; t < trackCount; t++)
        {
            if (pos + 8 > b.Length || ReadId(b, pos) != "MTrk") throw new InvalidDataException("Missing MIDI MTrk chunk.");
            uint length = ReadU32(b, pos + 4);
            int trackEnd = pos + 8 + (int)length;
            if (trackEnd < pos + 8 || trackEnd > b.Length) throw new InvalidDataException("Invalid MIDI track length.");
            int p = pos + 8;
            long tick = 0; int runningStatus = 0;
            while (p < trackEnd)
            {
                tick += ReadVarLen(b, ref p, trackEnd);
                if (p >= trackEnd) throw new InvalidDataException("Truncated MIDI event.");
                int status = b[p];
                if (status < 0x80) status = runningStatus == 0 ? throw new InvalidDataException("Invalid MIDI running status.") : runningStatus;
                else p++;
                if (status == 0xFF)
                {
                    if (p >= trackEnd) throw new InvalidDataException("Truncated MIDI meta event.");
                    int metaType = b[p++];
                    long len = ReadVarLen(b, ref p, trackEnd);
                    if (len < 0 || p + len > trackEnd) throw new InvalidDataException("Invalid MIDI meta-event length.");
                    if (metaType == 0x51 && len == 3) tempos.Add((tick, (b[p] << 16) | (b[p + 1] << 8) | b[p + 2]));
                    p += (int)len;
                    runningStatus = 0;
                    continue;
                }
                if (status is 0xF0 or 0xF7)
                {
                    long len = ReadVarLen(b, ref p, trackEnd);
                    if (len < 0 || p + len > trackEnd) throw new InvalidDataException("Invalid MIDI sysex length.");
                    p += (int)len;
                    runningStatus = 0;
                    continue;
                }
                runningStatus = status;
                int kind = status & 0xF0;
                if (p >= trackEnd) throw new InvalidDataException("Truncated MIDI channel event.");
                int data1 = b[p++], data2 = 0;
                if (kind != 0xC0 && kind != 0xD0)
                {
                    if (p >= trackEnd) throw new InvalidDataException("Truncated MIDI channel event.");
                    data2 = b[p++];
                }
                events.Add((tick, status, data1, data2));
            }
            lastTick = Math.Max(lastTick, tick);
            pos = trackEnd;
        }
        events.Sort((x, y) => x.Tick.CompareTo(y.Tick));
        tempos.Sort((x, y) => x.Tick.CompareTo(y.Tick));
        if (tempos.Count == 0 || tempos[0].Tick != 0) tempos.Insert(0, (0, 500000)); // default 120 BPM
        double[] boundarySeconds = new double[tempos.Count];
        for (int i = 1; i < tempos.Count; i++)
            boundarySeconds[i] = boundarySeconds[i - 1] + (tempos[i].Tick - tempos[i - 1].Tick) * (tempos[i - 1].Microseconds / 1_000_000.0) / division;
        double TickToSeconds(long tick)
        {
            int i = 0;
            while (i + 1 < tempos.Count && tempos[i + 1].Tick <= tick) i++;
            return boundarySeconds[i] + (tick - tempos[i].Tick) * (tempos[i].Microseconds / 1_000_000.0) / division;
        }

        int[] patch = new int[16];
        int[] volume = new int[16]; Array.Fill(volume, 100);
        double[] bend = new double[16];
        Dictionary<(int Channel, int Note), (long Tick, int Velocity, int Volume, int Patch)> active = [];
        List<NoteSpan> notes = [];
        foreach ((long tick, int status, int data1, int data2) in events)
        {
            int kind = status & 0xF0, channel = status & 0xF;
            bool percussion = channel == 9;
            switch (kind)
            {
                case 0x90 when data2 > 0: // note on
                    if (active.Remove((channel, data1), out var retriggered))
                        notes.Add(MakeNote(retriggered.Tick, tick, 1, data1, retriggered.Velocity, retriggered.Volume, retriggered.Patch, percussion, bend[channel]));
                    active[(channel, data1)] = (tick, data2, volume[channel], patch[channel]);
                    break;
                case 0x90: // note-on with velocity 0 behaves as note off
                case 0x80:
                    if (active.Remove((channel, data1), out var released))
                        notes.Add(MakeNote(released.Tick, tick, 1, data1, released.Velocity, released.Volume, released.Patch, percussion, bend[channel]));
                    break;
                case 0xB0:
                    if (data1 == 7) volume[channel] = data2; // channel volume
                    else if (data1 is 120 or 123) // all sounds/notes off
                        foreach ((int Channel, int Note) key in active.Keys.Where(k => k.Channel == channel).ToArray())
                        {
                            var info = active[key]; active.Remove(key);
                            notes.Add(MakeNote(info.Tick, tick, 1, key.Note, info.Velocity, info.Volume, info.Patch, percussion, bend[channel]));
                        }
                    break;
                case 0xC0: patch[channel] = data1; break; // program change
                case 0xE0: bend[channel] = Instruments.MidiPitchBendSemitones(data1, data2); break; // pitch bend
                default: break; // aftertouch is not modeled
            }
        }
        foreach (((int Channel, int Note) key, var info) in active)
            notes.Add(MakeNote(info.Tick, lastTick, 1, key.Note, info.Velocity, info.Volume, info.Patch, key.Channel == 9, bend[key.Channel]));
        // Event ticks above were left in raw MIDI ticks (seconds-per-tick=1); convert through the tempo map now.
        List<NoteSpan> converted = notes.ConvertAll(n => n with
        {
            Start = TickToSeconds((long)n.Start), End = Math.Max(TickToSeconds((long)n.Start) + .01, TickToSeconds((long)n.End))
        });
        return (converted, TickToSeconds(lastTick));
    }

    // --- Synthesis: a small additive-harmonic/noise model, not a General MIDI soundfont. ---

    static short[] Synthesize(List<NoteSpan> notes, double duration, int sampleRate)
    {
        duration = Math.Clamp(duration, .05, MaxDurationSeconds);
        int totalSamples = (int)Math.Ceiling(duration * sampleRate);
        if (totalSamples <= 0 || notes.Count == 0) return new short[Math.Max(1, totalSamples)];
        double[] mix = new double[totalSamples];
        const double attack = .006, scale = .2;
        foreach (NoteSpan note in notes)
        {
            if (note.Amplitude <= 0) continue;
            int startSample = Math.Max(0, (int)(note.Start * sampleRate));
            int sustainSamples = Math.Max(1, (int)((note.End - note.Start) * sampleRate));
            if (note.Percussion)
            {
                int releaseSamples = (int)(.03 * sampleRate);
                int length = sustainSamples + releaseSamples;
                int burst = Math.Min(length, (int)(.12 * sampleRate) + releaseSamples);
                uint rng = (uint)(note.Frequency * 977) ^ 0x9E3779B9u;
                double cutoff = Math.Clamp(note.Frequency / (sampleRate / 2.0), .02, .6);
                double filtered = 0;
                for (int i = 0; i < burst; i++)
                {
                    int sampleIndex = startSample + i;
                    if (sampleIndex >= totalSamples) break;
                    rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                    double white = (rng / (double)uint.MaxValue) * 2 - 1;
                    filtered += (white - filtered) * cutoff;
                    double env = i < burst - releaseSamples ? 1 : Math.Max(0, (burst - i) / (double)Math.Max(1, releaseSamples));
                    mix[sampleIndex] += filtered * env * note.Amplitude * scale * 1.5;
                }
                continue;
            }
            // GM-program-aware voicing: harmonic content plus an attack/decay-to-sustain/release
            // envelope shaped by the instrument's timbre family (see Instruments.ForProgram).
            Instruments.Voice voice = Instruments.ForProgram(note.Instrument);
            double[] harmonics = voice.Harmonics;
            double weightSum = harmonics.Sum();
            int releaseSamples2 = Math.Max(1, (int)(voice.ReleaseSeconds * sampleRate));
            int attackSamples = Math.Min(sustainSamples, (int)(attack * sampleRate));
            int length2 = sustainSamples + releaseSamples2;
            double peakLevel = 1 + voice.PluckOvershoot;
            double SustainEnvelope(int i)
            {
                if (attackSamples > 0 && i < attackSamples) return peakLevel * (i / (double)attackSamples);
                if (!double.IsPositiveInfinity(voice.DecaySeconds))
                {
                    double t = (i - attackSamples) / (double)sampleRate;
                    double frac = Math.Clamp(t / voice.DecaySeconds, 0, 1);
                    return peakLevel + (voice.SustainLevel - peakLevel) * frac;
                }
                return peakLevel;
            }
            double heldLevel = SustainEnvelope(Math.Max(0, sustainSamples - 1));
            for (int i = 0; i < length2; i++)
            {
                int sampleIndex = startSample + i;
                if (sampleIndex >= totalSamples) break;
                double t = i / (double)sampleRate;
                double env = i < sustainSamples ? SustainEnvelope(i) : heldLevel * Math.Max(0, (length2 - i) / (double)releaseSamples2);
                double value = 0;
                for (int h = 0; h < harmonics.Length; h++)
                    value += Math.Sin(2 * Math.PI * note.Frequency * (h + 1) * t) * harmonics[h];
                mix[sampleIndex] += value / weightSum * env * note.Amplitude * scale;
            }
        }
        double peak = 0;
        foreach (double v in mix) peak = Math.Max(peak, Math.Abs(v));
        double limiter = peak > .95 ? .95 / peak : 1;
        short[] pcm = new short[totalSamples];
        for (int i = 0; i < totalSamples; i++) pcm[i] = (short)Math.Clamp(mix[i] * limiter * short.MaxValue, short.MinValue, short.MaxValue);
        return pcm;
    }
}
