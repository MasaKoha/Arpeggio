using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>独立した合成器を 60 Hz のフレーム進行とともに観測する。</summary>
    internal static class SynthSamples
    {
        internal const int SampleRate = 44100;
        internal const int ReferenceNote = 69;
        internal const int MaximumVolume = 15;
        internal const int QuietVolume = 7;
        private const int FrameRate = 60;

        internal static float[] Render(IChannelSynthesizer synthesizer, Instrument instrument,
            int volume = MaximumVolume, int midiNote = ReferenceNote)
        {
            synthesizer.NoteOn(midiNote, volume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            var samples = new float[SampleRate];
            int samplesPerFrame = SampleRate / FrameRate;
            for (int offset = 0; offset < samples.Length; offset += samplesPerFrame)
            {
                synthesizer.Render(samples.AsSpan(offset, samplesPerFrame));
                synthesizer.AdvanceFrame();
            }

            return samples;
        }
    }
}
