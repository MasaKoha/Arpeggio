using System;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>再合成時刻の半開区間・同時書き込み順とテスト用 VGM の量子化を検証する。</summary>
    public sealed class RegisterTraceRendererTests
    {
        /// <summary>同時刻の DAC off／on／trigger は列順に処理し、指定サンプルそのものから出力へ反映する。</summary>
        [Fact]
        public void WritesApplyAtExactSampleInOriginalOrder()
        {
            const int sampleCount = 6;
            var writes = new (long Sample, int Address, int Value, int Order)[]
            {
                (0, 0xFF26, 0x80, 0), (0, 0xFF24, 0x77, 1), (0, 0xFF25, 0x44, 2),
                (0, 0xFF30, 0xFF, 3), (0, 0xFF1C, 0x20, 4),
                (1, 0xFF1A, 0x80, 5), (1, 0xFF1E, 0x80, 6),
                (2, 0xFF1A, 0, 7), (3, 0xFF1A, 0x80, 8),
                (4, 0xFF1E, 0x80, 9), (4, 0xFF1A, 0, 10),
                (5, 0xFF1A, 0x80, 11), (5, 0xFF1E, 0x80, 12)
            };
            var audio = RegisterTraceRenderer.Render(new GameBoyRegisterTraceChip(), writes, sampleCount);
            Assert.Equal(new double[] { 0, 15, 0, 0, 0, 15 }, audio.Left);
            Assert.Equal(audio.Left, audio.Right);
        }

        /// <summary>負時刻・順序番号の破損・観測区間外を黙って読み飛ばさない。</summary>
        [Theory]
        [InlineData(-1, 0)] [InlineData(1, 1)] [InlineData(3, 0)]
        public void InvalidTimelineFailsExplicitly(long sample, int order)
        {
            var writes = new[] { (sample, 0x4015, 0, order) };
            Assert.Throws<ArgumentException>(() => RegisterTraceRenderer.Render(new NesRegisterTraceChip(), writes, 3));
        }

        /// <summary>独立パースで PLAY の固定丸め値・長待機分割・同値書き込みの順序を照合する。</summary>
        [Fact]
        public void QuantizedVgmUsesAbsoluteTimesAndPreservesDuplicateWrites()
        {
            var timeline = new NsfFrameTimeline(100, new[]
            {
                new NsfRegisterWrite(0, 0x4015, 0),
                new NsfRegisterWrite(1, 0x4003, 0),
                new NsfRegisterWrite(1, 0x4003, 0),
                new NsfRegisterWrite(100, 0x4015, 0)
            });
            ParsedVgm parsed = IndependentVgmParser.Parse(QuantizedNesVgmFixture.Write(timeline));
            Assert.Equal(new[] { 734, 65535, 7109 }, parsed.Waits);
            Assert.Equal(new (long Sample, int Address, int Value, int Order)[]
            {
                (0, 0x4015, 0, 0), (734, 0x4003, 0, 1), (734, 0x4003, 0, 2), (73378, 0x4015, 0, 3)
            }, parsed.Writes);
            Assert.Equal(73378, parsed.WaitSamples);
        }
    }
}
