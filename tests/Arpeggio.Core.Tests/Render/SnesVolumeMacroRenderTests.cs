using System;
using System.Runtime.InteropServices;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Xunit;

namespace Arpeggio.Core.Tests.Render
{
    /// <summary>60 Hz 包絡の最終ゼロと DSP レート変換・曲末尾の接続を検証する。</summary>
    public sealed class SnesVolumeMacroRenderTests
    {
        private const int ControlFramesPerSecond = 60;
        private const int StereoChannels = 2;
        private const int EnvelopeFrames = 3;
        private const int SongTicks = 8;
        private const int DspSampleRate = 32000;
        private const int ResamplingHistorySamples = 2;

        /// <summary>トーンと DSP ノイズは tail の有無にかかわらず最終ゼロを保持し、分割再生でも全バイト一致する。</summary>
        [Theory]
        [InlineData(44100, false, 0)]
        [InlineData(44100, false, 0.05)]
        [InlineData(48000, false, 0)]
        [InlineData(44101, false, 0.05)]
        [InlineData(44100, true, 0)]
        [InlineData(44100, true, 0.05)]
        [InlineData(48000, true, 0.05)]
        [InlineData(44101, true, 0)]
        public void FinalZeroSurvivesResamplingNoteOffAndBufferSplits(int sampleRate, bool noise, double tailSeconds)
        {
            Song song = SongFactory.Create(ChipKind.Snes, tempoBpm: 150, lengthTicks: SongTicks);
            var instrument = Assert.IsType<SnesSampleInstrument>(song.Instruments[0]);
            instrument.Waveform = SnesWaveformKind.Pulse;
            instrument.NoiseEnabled = noise;
            instrument.AdsrRegisters = new SnesAdsrRegisters(15, 0, 7, 0);
            instrument.Envelope = new AdsrEnvelope(0, 0, 1, 0);
            instrument.VolumeMacro = new Macro { Values = new[] { 12, 8, 4, 0 } };
            instrument.EchoSend = 0;
            song.SnesEcho.DelayMilliseconds = 0;
            song.Tracks[0].Notes.Add(new Note { DurationTicks = SongTicks, MidiNote = 60, Volume = 15 });
            var settings = new RenderSettings(sampleRate, 1, tailSeconds);
            float[] expected = new SongRenderer(song, settings).RenderAll();
            int bodyFrames = (int)Math.Round((EnvelopeFrames + 1) * sampleRate / (double)ControlFramesPerSecond, MidpointRounding.AwayFromZero);
            int tailFrames = (int)Math.Round(tailSeconds * sampleRate, MidpointRounding.AwayFromZero);
            Assert.Equal((bodyFrames + tailFrames) * StereoChannels, expected.Length);
            int zeroBoundary = (int)Math.Ceiling(EnvelopeFrames * sampleRate / (double)ControlFramesPerSecond);
            int resamplingHistory = (int)Math.Ceiling(ResamplingHistorySamples * sampleRate / (double)DspSampleRate);
            Assert.Contains(expected.AsSpan(0, zeroBoundary * StereoChannels).ToArray(), sample => sample != 0);
            Assert.All(expected.AsSpan((zeroBoundary + resamplingHistory) * StereoChannels).ToArray(), sample => Assert.Equal(0f, sample));
            Assert.Equal(0f, expected[^1]);
            Assert.Equal(0f, expected[bodyFrames * StereoChannels - 1]);

            foreach (int bufferFrames in new[] { 1, 733, 1024 })
            {
                var renderer = new SongRenderer(song, settings);
                var actual = new float[expected.Length];
                int offset = 0;
                while (offset < actual.Length)
                {
                    int count = Math.Min(bufferFrames * StereoChannels, actual.Length - offset);
                    int rendered = renderer.Render(actual.AsSpan(offset, count));
                    Assert.True(rendered > 0);
                    offset += rendered * StereoChannels;
                }
                Assert.True(renderer.IsFinished);
                Assert.Equal(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(), MemoryMarshal.AsBytes(actual.AsSpan()).ToArray());
                renderer.Reset();
                Assert.Equal(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(), MemoryMarshal.AsBytes(renderer.RenderAll().AsSpan()).ToArray());
            }
        }
    }
}
