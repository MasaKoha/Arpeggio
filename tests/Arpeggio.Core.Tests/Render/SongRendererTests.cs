using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Render
{
    /// <summary>ストリーミングと一括合成の時間軸、警告、編集反映を検証する。</summary>
    public sealed class SongRendererTests
    {
        private const int StereoChannels = 2;
        private const int SampleRate = 44100;

        /// <summary>150 BPM の 4 小節と末尾余白が指定どおりの長さになる。</summary>
        [Theory]
        [InlineData(44100)]
        [InlineData(48000)]
        public void RenderAll_FourBarsHaveExactLengthAndAudibleOutput(int sampleRate)
        {
            const int FourBarTicks = 768;
            const double BodySeconds = 6.4;
            const double TailSeconds = 0.5;
            Song song = TestSongFactory.CreateActiveSong(ChipKind.Nes, FourBarTicks);
            var renderer = new SongRenderer(song, new RenderSettings(sampleRate, 1, TailSeconds));

            float[] samples = renderer.RenderAll();
            long expectedFrames = (long)Math.Round(BodySeconds * sampleRate, MidpointRounding.AwayFromZero)
                + (long)Math.Round(TailSeconds * sampleRate, MidpointRounding.AwayFromZero);

            Assert.Equal(expectedFrames * StereoChannels, samples.LongLength);
            Assert.Equal(expectedFrames, renderer.PositionSamples);
            Assert.True(renderer.IsFinished);
            Assert.True(SignalAnalysis.RootMeanSquare(samples) > SignalAnalysis.SilenceTolerance);
            Assert.All(samples, sample => Assert.InRange(sample, -1f, 1f));
        }

        /// <summary>非ゼロのループ開始 tick 以後だけを追加周回する。</summary>
        [Fact]
        public void RenderAll_LoopsOnlyTheRequestedRegion()
        {
            const int LengthTicks = 192;
            const int LoopStartTick = 48;
            const int LoopCount = 3;
            const double TailSeconds = 0.125;
            Song song = TestSongFactory.CreateActiveSong(ChipKind.Nes, LengthTicks);
            song.LoopStartTick = LoopStartTick;
            var renderer = new SongRenderer(song, new RenderSettings(SampleRate, LoopCount, TailSeconds));
            double totalTicks = LengthTicks + (LoopCount - 1) * (LengthTicks - LoopStartTick);
            long bodyFrames = (long)Math.Round(totalTicks * 60 * SampleRate / (song.TempoBpm * 48), MidpointRounding.AwayFromZero);
            long tailFrames = (long)Math.Round(TailSeconds * SampleRate, MidpointRounding.AwayFromZero);

            Assert.Equal((bodyFrames + tailFrames) * StereoChannels, renderer.RenderAll().LongLength);
        }

        /// <summary>バッファの切り方を変えても全チップの波形が一括出力と一致する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void Render_ArbitraryBufferBoundariesMatchRenderAll(ChipKind chip)
        {
            const int LengthTicks = 96;
            const int BufferFrames = 317;
            Song song = TestSongFactory.CreateActiveSong(chip, LengthTicks);
            var settings = new RenderSettings(SampleRate, 2, 0.1);
            float[] expected = new SongRenderer(song, settings).RenderAll();
            var renderer = new SongRenderer(song, settings);
            var actual = new float[expected.Length];
            int offset = 0;
            while (!renderer.IsFinished)
            {
                int requestedSamples = Math.Min(BufferFrames * StereoChannels, actual.Length - offset);
                int writtenFrames = renderer.Render(actual.AsSpan(offset, requestedSamples));
                Assert.True(writtenFrames > 0);
                offset += writtenFrames * StereoChannels;
            }

            Assert.Equal(expected.Length, offset);
            Assert.Equal(expected, actual);
        }

        /// <summary>シークは位相とマクロを再現し、リセットは先頭の出力を再現する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void SeekAndReset_ReproduceOriginalSamples(ChipKind chip)
        {
            const int SeekFrame = 12345;
            const int ObservationSamples = 1024;
            Song song = TestSongFactory.CreateActiveSong(chip, 192);
            var settings = new RenderSettings(SampleRate, 2, 0.1);
            float[] expected = new SongRenderer(song, settings).RenderAll();
            var renderer = new SongRenderer(song, settings);
            renderer.Seek(SeekFrame);
            Assert.Equal((long)SeekFrame, renderer.PositionSamples);
            var actual = new float[ObservationSamples];
            Assert.Equal(ObservationSamples / StereoChannels, renderer.Render(actual));
            Assert.Equal(expected.AsSpan(SeekFrame * StereoChannels, ObservationSamples).ToArray(), actual);

            renderer.Reset();
            Assert.Equal(0L, renderer.PositionSamples);
            Assert.False(renderer.IsFinished);
            renderer.Render(actual);
            Assert.Equal(expected.AsSpan(0, ObservationSamples).ToArray(), actual);
        }

        /// <summary>音域外ノートと三角波音量は例外ではなく警告を一件ずつ残す。</summary>
        [Fact]
        public void RenderAll_ReportsPitchClampAndIgnoredTriangleVolume()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Instruments.Add(new NesTriangleInstrument { Id = 2 });
            song.Tracks[0].Notes.Add(new Note { MidiNote = 0, InstrumentId = 1 });
            song.Tracks[2].Notes.Add(new Note { MidiNote = 69, Volume = 7, InstrumentId = 2 });
            var renderer = new SongRenderer(song, new RenderSettings(SampleRate, 2, 0));

            float[] samples = renderer.RenderAll();

            Assert.Collection(renderer.Report.Warnings,
                warning => Assert.Equal(RenderWarningKind.PitchClamped, warning.Kind),
                warning => Assert.Equal(RenderWarningKind.TriangleVolumeIgnored, warning.Kind));
            Assert.True(SignalAnalysis.RootMeanSquare(samples) > SignalAnalysis.SilenceTolerance);
        }

        /// <summary>次のコールバックから差し替えたノート列とミュートを反映する。</summary>
        [Fact]
        public void Render_ObservesReplacedNotesAndMuteOnNextCall()
        {
            const int BufferSamples = 512;
            Song song = SongFactory.Create(ChipKind.Nes);
            var renderer = new SongRenderer(song, new RenderSettings(SampleRate, 1, 0));
            var samples = new float[BufferSamples];
            renderer.Render(samples);
            Assert.InRange(SignalAnalysis.RootMeanSquare(samples), 0, SignalAnalysis.SilenceTolerance);

            song.Tracks[0].Notes = new List<Note> { new Note { Tick = 0, DurationTicks = song.LengthTicks, MidiNote = 69 } };
            renderer.Render(samples);
            Assert.True(SignalAnalysis.RootMeanSquare(samples) > SignalAnalysis.SilenceTolerance);
            song.Tracks[0].Muted = true;
            renderer.Render(samples);
            Assert.InRange(SignalAnalysis.RootMeanSquare(samples), 0, SignalAnalysis.SilenceTolerance);
        }

        /// <summary>フレーム境界で音色を更新しても、再発音の先頭マクロを飛ばさない。</summary>
        [Fact]
        public void Render_InstrumentReplacementStartsAtFirstMacroValue()
        {
            const int RenderSampleRate = 48000;
            const int FramesPerSecond = 60;
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Tracks[0].Notes.Add(new Note { DurationTicks = song.LengthTicks, MidiNote = 69 });
            var renderer = new SongRenderer(song, new RenderSettings(RenderSampleRate, 1, 0));
            renderer.Render(new float[RenderSampleRate / FramesPerSecond * StereoChannels]);
            song.Instruments = new List<Instrument>
            {
                new NesPulseInstrument
                {
                    Id = 1,
                    VolumeMacro = new Macro { Values = new int[] { 15, 0 }, LoopIndex = -1 }
                }
            };

            var firstFrame = new float[StereoChannels];
            Assert.Equal(1, renderer.Render(firstFrame));
            Assert.True(SignalAnalysis.RootMeanSquare(firstFrame) > SignalAnalysis.SilenceTolerance);
        }

        /// <summary>終端を越えるバッファの余りと終了後のバッファをゼロで埋める。</summary>
        [Fact]
        public void Render_ZeroFillsAfterEndAndReturnsFrameCount()
        {
            const int BufferFrames = 1000;
            const float ExistingSignal = 0.75f;
            Song song = SongFactory.Create(ChipKind.Nes, 150, 1);
            var renderer = new SongRenderer(song, new RenderSettings(SampleRate, 1, 0));
            var samples = new float[BufferFrames * StereoChannels];
            Array.Fill(samples, ExistingSignal);
            int frames = renderer.Render(samples);

            Assert.InRange(frames, 1, BufferFrames - 1);
            Assert.True(renderer.IsFinished);
            Assert.All(samples, sample => Assert.Equal(0f, sample));
            Array.Fill(samples, ExistingSignal);
            Assert.Equal(0, renderer.Render(samples));
            Assert.All(samples, sample => Assert.Equal(0f, sample));
        }
    }
}
