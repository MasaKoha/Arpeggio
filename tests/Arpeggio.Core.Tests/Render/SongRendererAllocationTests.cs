using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Render
{
    /// <summary>実際の発音・マクロ・ループ境界を通るコールバックの割り当てを検証する。</summary>
    public sealed class SongRendererAllocationTests
    {
        /// <summary>全チップでウォームアップ後の 1 秒間に管理ヒープを割り当てない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void Render_OneSecondWithTransitionsAllocatesZeroBytes(ChipKind chip)
        {
            const int SampleRate = 44100;
            const int StereoChannels = 2;
            const int BufferFrames = 256;
            const int SongLengthTicks = 48;
            const int LoopCount = 4;
            Song song = TestSongFactory.CreateActiveSong(chip, SongLengthTicks);
            var renderer = new SongRenderer(song, new RenderSettings(SampleRate, LoopCount, 0));
            var samples = new float[BufferFrames * StereoChannels];
            renderer.Render(samples);
            long positionBefore = renderer.PositionSamples;
            int remainingFrames = SampleRate;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            while (remainingFrames > 0)
            {
                // perf: 計測自身の割り当てを混ぜず、最後の呼び出しも含めて正確に 1 秒進める。
                int requestedFrames = Math.Min(remainingFrames, BufferFrames);
                int writtenFrames = renderer.Render(samples.AsSpan(0, requestedFrames * StereoChannels));
                remainingFrames -= writtenFrames;
                if (writtenFrames == 0)
                {
                    break;
                }
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.Equal(0L, allocatedBytes);
            Assert.Equal(0, remainingFrames);
            Assert.Equal(positionBefore + SampleRate, renderer.PositionSamples);
            Assert.False(renderer.IsFinished);
        }
    }
}
