using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis.Nes;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Nes
{
    /// <summary>M1 の DPCM プレースホルダーが波形へ干渉しないことを検証する。</summary>
    public sealed class NesDpcmSynthesizerTests
    {
        /// <summary>ノートとフレーム進行を受けても既存のサンプル値を保持する。</summary>
        [Fact]
        public void Render_LeavesExistingBufferUntouched()
        {
            const int SampleCount = 1024;
            const float ExistingSignal = 0.125f;
            var synthesizer = new NesDpcmSynthesizer();
            var samples = new float[SampleCount];
            Array.Fill(samples, ExistingSignal);
            synthesizer.NoteOn(60, 15, new NesDpcmInstrument(), ReadOnlySpan<NoteEffect>.Empty);
            synthesizer.AdvanceFrame();
            synthesizer.Render(samples);
            synthesizer.NoteOff();
            synthesizer.Render(samples);

            Assert.All(samples, sample => Assert.Equal(ExistingSignal, sample));
        }
    }
}
