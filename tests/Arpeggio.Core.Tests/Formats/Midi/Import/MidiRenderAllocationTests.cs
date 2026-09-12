using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Core.Tests.Analysis;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats.Midi.Import
{
    /// <summary>取り込み・保存往復・チップ変換後も、既存再生の発音交代／ループで GC を発生させない。</summary>
    [Collection(AllocationCollection.Name)]
    public sealed class MidiRenderAllocationTests
    {
        private const int SampleRate = 44100;
        private const int StereoChannels = 2;
        private const int BufferFrames = 256;
        private const int SongFrames = 66150;
        private const int LoopCount = 4;
        private const int WarmupLoops = 2;

        /// <summary>三チップの全音色・打楽器を暖機し、次の一周の管理ヒープ割り当てを計測する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void ImportedSongTransitionsAndLoopAllocateZeroBytes(ChipKind chip)
        {
            using var fixture = new MidiPipelineFixture();
            Assert.True(fixture.Import(chip).CanWrite);
            Song song = SongSerializer.Load(fixture.SongPath);
            ChipExportService.Prepare(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
            var renderer = new SongRenderer(song, new RenderSettings(SampleRate, LoopCount, 0));
            var samples = new float[BufferFrames * StereoChannels];
            Assert.Equal(0, RenderFrames(renderer, samples, SongFrames * WarmupLoops));
            long position = renderer.PositionSamples;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int remaining = RenderFrames(renderer, samples, SongFrames);
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.Equal(0L, allocatedBytes);
            Assert.Equal(0, remaining);
            Assert.Equal(position + SongFrames, renderer.PositionSamples);
            Assert.False(renderer.IsFinished);
        }

        private static int RenderFrames(SongRenderer renderer, float[] samples, int remaining)
        {
            while (remaining > 0)
            {
                // perf: 計測用の呼び出し側も割り当てず、ノート交代と周回を含む正確なフレーム数を進める。
                int requested = Math.Min(remaining, BufferFrames);
                int written = renderer.Render(samples.AsSpan(0, requested * StereoChannels));
                if (written == 0) { break; }
                remaining -= written;
            }
            return remaining;
        }
    }
}
