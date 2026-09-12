using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Presets
{
    /// <summary>旧8プリセットの optional マクロ未指定・空列・保存往復を全 PCM バイトで比較する。</summary>
    public sealed class LegacySfxMacroCompatibilityTests
    {
        /// <summary>既存プリセットの JSON と音声は未指定の往復と空マクロで変化しない。</summary>
        [Theory]
        [InlineData(ChipKind.GameBoy, 44100)]
        [InlineData(ChipKind.GameBoy, 48000)]
        [InlineData(ChipKind.Snes, 44100)]
        [InlineData(ChipKind.Snes, 48000)]
        public void LegacyPresetsPreservePcmAndWavBytes(ChipKind chip, int sampleRate)
        {
            foreach (SfxPresetDescription preset in SfxPresetCatalog.GetAll())
            {
                Song original = SfxPresetFactory.Create(chip, preset.Kind);
                string originalJson = SongSerializer.Serialize(original);
                Song restored = SongSerializer.Deserialize(originalJson);
                Assert.Equal(Encoding.UTF8.GetBytes(originalJson), Encoding.UTF8.GetBytes(SongSerializer.Serialize(restored)));
                foreach (double tailSeconds in new[] { 0.0, 0.05 })
                {
                    var settings = new RenderSettings(sampleRate, 1, tailSeconds);
                    float[] expected = new SongRenderer(original, settings).RenderAll();
                    float[] actual = new SongRenderer(restored, settings).RenderAll();
                    Assert.Equal(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(), MemoryMarshal.AsBytes(actual.AsSpan()).ToArray());
                    Song emptyMacros = SongSerializer.Deserialize(originalJson);
                    SetEmptyOptionalMacros(emptyMacros);
                    float[] empty = new SongRenderer(emptyMacros, settings).RenderAll();
                    Assert.Equal(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(), MemoryMarshal.AsBytes(empty.AsSpan()).ToArray());
                    using var expectedWave = new MemoryStream();
                    using var actualWave = new MemoryStream();
                    WavWriter.Write(expectedWave, expected, sampleRate);
                    WavWriter.Write(actualWave, empty, sampleRate);
                    Assert.Equal(expectedWave.ToArray(), actualWave.ToArray());
                }
            }
        }
        private static void SetEmptyOptionalMacros(Song song)
        {
            foreach (Instrument instrument in song.Instruments)
            {
                if (instrument is GbPulseInstrument pulse)
                {
                    Assert.Null(pulse.DutyMacro);
                    pulse.DutyMacro = new Macro();
                }
                if (instrument is SnesSampleInstrument sample)
                {
                    Assert.Null(sample.VolumeMacro);
                    sample.VolumeMacro = new Macro();
                }
            }
        }
    }
}
