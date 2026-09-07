using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Synthesis.Snes;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Snes
{
    /// <summary>サンプル再生の補間・音程・ループ・リアルタイムキャッシュを検証する。</summary>
    public sealed class SnesEmbeddedSampleTests
    {
        private const int MaximumVolume = 15;
        private const int RootNote = 60;

        /// <summary>初回だけ前奏区間を通り、終端から開始への補間を行う。</summary>
        [Fact]
        public void Render_InterpolatesAcrossLoopBoundary()
        {
            SnesSampleInstrument instrument = CreateInstrument(new short[] { 0, short.MinValue, short.MaxValue, 0 }, 4);
            instrument.LoopStart = 1;
            instrument.LoopEnd = 3;
            float[] samples = Render(instrument, 8, RootNote, 10);
            Assert.Equal(new float[] { 0, -0.5f, -1, 0, 1, 0, -1, 0, 1, 0 }, samples);
        }

        /// <summary>非ループでは末尾を補間用に保持してから停止し、先頭へ補間しない。</summary>
        [Fact]
        public void Render_OneShotStopsAtEndWithoutWrapping()
        {
            SnesSampleInstrument instrument = CreateInstrument(new short[] { 0, short.MinValue, short.MaxValue }, 4);
            instrument.Loop = false;
            instrument.LoopStart = -1;
            instrument.LoopEnd = int.MaxValue;
            Assert.Equal(new float[] { 0, -0.5f, -1, 0, 1, 1, 0, 0 }, Render(instrument, 8, RootNote, 8));
        }

        /// <summary>一サンプルループと複数周を飛び越す再生増分を扱う。</summary>
        [Fact]
        public void Render_HandlesSingleSampleLoopAndLargeSteps()
        {
            SnesSampleInstrument single = CreateInstrument(new short[] { short.MaxValue }, 32000);
            Assert.Equal(new float[] { 1, 1, 1, 1 }, Render(single, 8000, RootNote + 12, 4));
            SnesSampleInstrument instrument = CreateInstrument(new short[] { 0, short.MinValue, short.MaxValue, 0 }, 8);
            instrument.LoopStart = 1;
            instrument.LoopEnd = 3;
            Assert.Equal(new float[] { 0, 1, 1, 1 }, Render(instrument, 2, RootNote, 4));
        }

        /// <summary>異なる基準音・元レートでも同じサンプルのオクターブ変換を保つ。</summary>
        [Theory]
        [InlineData(22050, 44100, 69, 81, 880)]
        [InlineData(44100, 22050, 81, 69, 220)]
        [InlineData(22050, 48000, 96, 96, 440)]
        public void Render_PitchUsesRootNoteAndSourceRate(int sourceRate, int outputRate, int rootNote, int note, double expectedFrequency)
        {
            const double SourceFrequency = 440;
            short[] source = new short[sourceRate];
            for (int index = 0; index < source.Length; index++)
            {
                source[index] = (short)(short.MaxValue * Math.Sin(2 * Math.PI * SourceFrequency * index / sourceRate));
            }
            SnesSampleInstrument instrument = CreateInstrument(source, sourceRate);
            instrument.RootMidiNote = rootNote;
            float[] samples = Render(instrument, outputRate, note, outputRate);
            double actual = SignalAnalysis.EstimateFrequency(samples, outputRate);
            Assert.InRange(Math.Abs(actual / expectedFrequency - 1), 0, SignalAnalysis.FrequencyRelativeTolerance);
        }

        /// <summary>アルペジオの更新も root 基準の位置増分へ反映する。</summary>
        [Fact]
        public void AdvanceFrame_UpdatesEmbeddedPitch()
        {
            SnesSampleInstrument instrument = CreateInstrument(new short[] { 0, short.MaxValue, 0, short.MinValue }, 4);
            instrument.RootMidiNote = 96;
            instrument.ArpeggioMacro = new Macro { Values = new[] { 0, 12 } };
            SnesVoiceSynthesizer synthesizer = new SnesVoiceSynthesizer(4);
            synthesizer.NoteOn(96, MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            float[] first = new float[1];
            synthesizer.Render(first);
            synthesizer.AdvanceFrame();
            float[] remainder = new float[4];
            synthesizer.Render(remainder);
            Assert.Equal(new float[] { 1, -1, 1, -1 }, remainder);
        }

        /// <summary>埋め込みサンプルでも既存 ADSR のリリース時間を守る。</summary>
        [Fact]
        public void NoteOff_ReleasesEmbeddedSample()
        {
            const int SampleRate = 1000;
            SnesSampleInstrument instrument = CreateInstrument(new short[] { short.MaxValue }, SampleRate);
            instrument.Envelope = new AdsrEnvelope(0, 0, 1, 0.004);
            SnesVoiceSynthesizer synthesizer = new SnesVoiceSynthesizer(SampleRate);
            synthesizer.NoteOn(RootNote, MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            synthesizer.Render(new float[1]);
            synthesizer.NoteOff();
            float[] samples = new float[6];
            synthesizer.Render(samples);
            Assert.Equal(new float[] { 1, 0.75f, 0.5f, 0.25f, 0, 0 }, samples);
        }

        /// <summary>サンプルを解除すると従来の合成波形へ戻る。</summary>
        [Fact]
        public void Render_ClearedSampleUsesSyntheticWaveform()
        {
            SnesSampleInstrument instrument = CreateInstrument(new short[] { short.MaxValue }, 22050);
            instrument.SampleData = null;
            SnesSampleInstrument synthesized = new SnesSampleInstrument { Envelope = instrument.Envelope };
            Assert.Equal(Render(synthesized, 44100, RootNote, 256), Render(instrument, 44100, RootNote, 256));
        }

        /// <summary>サンプル代入後の初回 NoteOn とサンプル差し替えを含めても確保しない。</summary>
        [Fact]
        public void NoteOn_UsesPreparedCacheWithoutAllocating()
        {
            SnesVoiceSynthesizer synthesizer = new SnesVoiceSynthesizer();
            SnesSampleInstrument warmup = CreateInstrument(new short[] { 0, short.MaxValue, short.MinValue }, 22050);
            float[] buffer = new float[256];
            synthesizer.NoteOn(RootNote, MaximumVolume, warmup, ReadOnlySpan<NoteEffect>.Empty);
            synthesizer.Render(buffer);
            SnesSampleInstrument replacement = CreateInstrument(new short[] { short.MaxValue, short.MinValue }, 32000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            synthesizer.NoteOn(RootNote + 12, MaximumVolume, replacement, ReadOnlySpan<NoteEffect>.Empty);
            synthesizer.Render(buffer);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>8 ボイスの発音・マクロ・曲ループ・未使用キャッシュへの差し替えも確保しない。</summary>
        [Fact]
        public void SongRenderer_TransitionsAndReplacementAllocateZeroBytes()
        {
            const int SampleRate = 44100;
            const int BufferFrames = 256;
            const int StereoChannels = 2;
            const int LoopCount = 16;
            Song song = TestSongFactory.CreateActiveSong(ChipKind.Snes, 48);
            foreach (Instrument instrument in song.Instruments)
            {
                SnesSampleInstrument sample = (SnesSampleInstrument)instrument;
                sample.SampleData = SampleDataCodec.Encode(new short[] { 0, short.MaxValue, 0, short.MinValue });
                sample.SampleRate = 22050;
            }
            Song restored = SongSerializer.Deserialize(SongSerializer.Serialize(song));
            SongRenderer renderer = new SongRenderer(restored, new RenderSettings(SampleRate, LoopCount, 0));
            float[] buffer = new float[BufferFrames * StereoChannels];
            RenderFrames(renderer, buffer, SampleRate);
            List<Instrument> replacements = new List<Instrument>(restored.Instruments);
            SnesSampleInstrument replacement = CreateInstrument(new short[] { short.MinValue, short.MaxValue }, 32000);
            replacement.Id = replacements[0].Id;
            replacements[0] = replacement;
            long before = GC.GetAllocatedBytesForCurrentThread();
            restored.Instruments = replacements;
            RenderFrames(renderer, buffer, SampleRate);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, allocated);
            Assert.Equal(SampleRate * 2L, renderer.PositionSamples);
        }

        /// <summary>埋め込みの高い基準音を従来の C4 上限で誤警告しない。</summary>
        [Fact]
        public void SongRenderer_WarningsUseEmbeddedRootNote()
        {
            Song song = SongFactory.Create(ChipKind.Snes, lengthTicks: 48);
            SnesSampleInstrument instrument = CreateInstrument(new short[] { short.MinValue, short.MaxValue }, 22050);
            instrument.Id = 1;
            instrument.RootMidiNote = 96;
            song.Instruments[0] = instrument;
            song.Tracks[0].Notes.Add(new Note { MidiNote = 96, DurationTicks = 48 });
            SongRenderer renderer = new SongRenderer(song, new RenderSettings());
            renderer.RenderAll();
            Assert.Empty(renderer.Report.Warnings);
            instrument.RootMidiNote = RootNote;
            SongRenderer clamped = new SongRenderer(song, new RenderSettings());
            clamped.RenderAll();
            Assert.Contains(clamped.Report.Warnings, warning => warning.Kind == RenderWarningKind.PitchClamped);
        }

        private static void RenderFrames(SongRenderer renderer, float[] buffer, int remaining)
        {
            const int StereoChannels = 2;
            while (remaining > 0)
            {
                int count = renderer.Render(buffer.AsSpan(0, Math.Min(remaining, buffer.Length / StereoChannels) * StereoChannels));
                if (count == 0)
                {
                    break;
                }
                remaining -= count;
            }
        }

        private static SnesSampleInstrument CreateInstrument(short[] samples, int sourceRate)
        {
            return new SnesSampleInstrument
            {
                SampleData = SampleDataCodec.Encode(samples), SampleRate = sourceRate,
                Envelope = new AdsrEnvelope(0, 0, 1, 0)
            };
        }

        private static float[] Render(SnesSampleInstrument instrument, int outputRate, int note, int count)
        {
            SnesVoiceSynthesizer synthesizer = new SnesVoiceSynthesizer(outputRate);
            synthesizer.NoteOn(note, MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            float[] samples = new float[count];
            synthesizer.Render(samples);
            return samples;
        }
    }
}
