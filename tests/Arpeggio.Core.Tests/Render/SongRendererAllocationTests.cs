using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Render
{
    /// <summary>実際の発音・マクロ・ループ境界を通るコールバックの割り当てを検証する。</summary>
    [Collection(AllocationCollection.Name)]
    public sealed class SongRendererAllocationTests
    {
        /// <summary>全チップでウォームアップ後の 1 秒間に管理ヒープを割り当てない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, false)]
        [InlineData(ChipKind.GameBoy, false)]
        [InlineData(ChipKind.Snes, false)]
        [InlineData(ChipKind.GameBoy, true)]
        [InlineData(ChipKind.Snes, true)]
        public void Render_OneSecondWithTransitionsAllocatesZeroBytes(ChipKind chip, bool useOptionalMacro)
        {
            const int SampleRate = 44100;
            const int StereoChannels = 2;
            const int BufferFrames = 256;
            const int SongLengthTicks = 48;
            const int LoopCount = 16;
            Song song = TestSongFactory.CreateActiveSong(chip, SongLengthTicks);
            if (useOptionalMacro)
            {
                foreach (Instrument instrument in song.Instruments)
                {
                    if (instrument is GbPulseInstrument pulse)
                    {
                        pulse.DutyMacro = new Macro { Values = new[] { 1, 2, 3, 4 }, LoopIndex = 0 };
                    }
                    if (instrument is SnesSampleInstrument sample)
                    {
                        sample.VolumeMacro = new Macro { Values = new[] { 12, 8, 4, 0 }, LoopIndex = 0 };
                    }
                }
            }
            var renderer = new SongRenderer(song, new RenderSettings(SampleRate, LoopCount, 0));
            var samples = new float[BufferFrames * StereoChannels];
            // 静的テーブルの初回初期化や最初のノート遷移・マクロ進行を計測区間に入れないため、1 秒ぶん先に鳴らす。
            int warmupFrames = SampleRate;
            while (warmupFrames > 0)
            {
                warmupFrames -= renderer.Render(samples.AsSpan(0, Math.Min(warmupFrames, BufferFrames) * StereoChannels));
            }
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
