using System;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Tests.Analysis;
using Arpeggio.Daw.Audio;
using Arpeggio.Daw.Audio.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Audio.Sfx
{
    /// <summary>試聴コールバックの発音・マクロ進行・ゲイン処理で確保がないことを検証する。</summary>
    [Collection(AllocationCollection.Name)]
    public sealed class SfxPreviewAllocationTests
    {
        private const int BufferFrames = 64;
        private const int StereoChannels = 2;
        private const int BufferCount = 256;

        /// <summary>三チップともレンダラー構築と初回JITを計測から除き、再発音以降のGC0を確認する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public async Task CallbackDoesNotAllocate(ChipKind chip)
        {
            var output = new PreviewAudioOutput();
            using var playback = new PlaybackEngine(output);
            using var player = new SfxPreviewPlayer(playback);
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(chip);
            parameters = parameters with
            {
                Tone = parameters.Tone with { Envelope = parameters.Tone.Envelope with { SustainSeconds = 1, DecaySeconds = 1 } }
            };
            Song song = SfxEditor.CreateCandidate(parameters, chip).Song;
            var buffer = new float[BufferFrames * StereoChannels];
            player.Play(song);
            await SfxPreviewPlayerTests.WaitFor(player, SfxPreviewState.Playing);
            for (int index = 0; index < BufferCount; index++) { output.Request(buffer); }
            player.Play(song);
            await SfxPreviewPlayerTests.WaitFor(player, SfxPreviewState.Playing);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < BufferCount; index++) { output.Request(buffer); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(SfxPreviewState.Playing, player.State);
        }
    }
}
