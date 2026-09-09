using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>設計表の全初期値と、新旧プリセットの独立した生成契約を固定する。</summary>
    public sealed class SfxParameterPresetCatalogTests
    {
        /// <summary>全八用途と三チップの組み合わせ。</summary>
        public static IEnumerable<object[]> Cases()
        {
            foreach (ChipKind chip in new[] { ChipKind.Nes, ChipKind.GameBoy, ChipKind.Snes })
            {
                foreach (SfxPresetKind kind in Enum.GetValues<SfxPresetKind>())
                {
                    if (kind != SfxPresetKind.None)
                    {
                        yield return new object[] { chip, kind };
                    }
                }
            }
        }

        /// <summary>差分指定だけでなく、無効レイヤーを含む全値を独立した期待値と比較する。</summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void Get_MatchesCompleteDesignTable(ChipKind chip, SfxPresetKind kind)
        {
            SfxParameterPresetDescription preset = SfxParameterPresetCatalog.Get(kind, chip);
            SfxParameters expected = ExpectedParameters(chip, kind);
            Assert.Equal(expected, preset.Parameters);
            Assert.Equal(chip, preset.Chip);
            Assert.Equal(kind, SfxParameterPresetCatalog.Parse(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Description));
            Assert.Equal(expected, SfxParameterValidator.Normalize(preset.Parameters, chip));
        }

        /// <summary>全24種が有効な通常Songとなり、保存往復後も有限・非無音・非クリップで鳴る。</summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void Compile_AllPresetsProduceValidSongsAndAudio(ChipKind chip, SfxPresetKind kind)
        {
            SfxParameterPresetDescription preset = SfxParameterPresetCatalog.Get(kind, chip);
            Song song = SfxSongCompiler.Compile(preset.Parameters, chip, preset.Name).Song;
            SongValidator.Validate(song);
            Song restored = SongSerializer.Deserialize(SongSerializer.Serialize(song));
            Assert.Equal(SongSerializer.Serialize(song), SongSerializer.Serialize(restored));
            Assert.Equal(1, song.Version);
            Assert.Null(song.Sfx);
            int[] expectedLengths = { 20, 20, 20, 50, 50, 20, 6, 14 };
            Assert.Equal(expectedLengths[(int)kind - 1], song.LengthTicks);
            Assert.All(song.Tracks.SelectMany(track => track.Notes), note => Assert.Empty(note.Effects));
            foreach (int sampleRate in new[] { 44100, 48000 })
            {
                float[] samples = new SongRenderer(restored, new RenderSettings(sampleRate, 1, 0)).RenderAll();
                Assert.NotEmpty(samples);
                Assert.All(samples, sample => Assert.True(float.IsFinite(sample) && Math.Abs(sample) < 1));
                Assert.Contains(samples, sample => sample != 0);
            }
        }

        /// <summary>新カタログを使っても旧八種と音色十六種の名前・既定入口を変更しない。</summary>
        [Fact]
        public void Catalog_KeepsLegacyNamesAndAcceptsOnlyNewAliases()
        {
            string[] names = { "jump", "coin", "hit", "explosion", "powerup", "laser", "blip", "select" };
            Assert.Equal(names, SfxParameterPresetCatalog.GetAll(ChipKind.Nes).Select(preset => preset.Name));
            Assert.Equal(names, SfxPresetCatalog.GetAll().Select(preset => preset.Name));
            Assert.Equal(new[] { "strings", "brass", "organ", "choir", "flute", "lead", "bass", "piano",
                "pluck", "bell", "kick", "snare", "hat", "openhat", "tom", "crash" },
                SnesInstrumentCatalog.All.Select(preset => preset.Name));
            Assert.Equal(SfxPresetKind.Coin, SfxParameterPresetCatalog.Parse(" PICKUP "));
            Assert.Equal(SfxPresetKind.PowerUp, SfxParameterPresetCatalog.Parse("power-up"));
            Assert.Throws<ArgumentException>(() => SfxPresetCatalog.Parse("pickup"));
            Assert.Throws<SfxParameterException>(() => SfxParameterPresetCatalog.Parse("any"));
            Assert.Throws<SfxParameterException>(() => SfxParameterPresetCatalog.Parse("1"));
            Assert.Throws<ArgumentOutOfRangeException>(() => SfxParameterPresetCatalog.Get(SfxPresetKind.None, ChipKind.Nes));
            Assert.Throws<SfxParameterException>(() => SfxParameterPresetCatalog.GetAll(ChipKind.None));
            foreach (object[] example in Cases())
            {
                ChipKind chip = (ChipKind)example[0];
                SfxPresetKind kind = (SfxPresetKind)example[1];
                string before = SongSerializer.Serialize(SfxPresetFactory.Create(chip, kind));
                SfxSongCompiler.Compile(SfxParameterPresetCatalog.Get(kind, chip).Parameters, chip);
                Assert.Equal(before, SongSerializer.Serialize(SfxPresetFactory.Create(chip, kind)));
            }
        }

        private static SfxParameters ExpectedParameters(ChipKind chip, SfxPresetKind kind)
        {
            (bool enabled, double frequency, double sustain, double decay, double slide, double acceleration,
                int pitchChange, double changeTime, double punch) = kind switch
            {
                SfxPresetKind.Jump => (true, 196, 0.016667, 0.133333, 128, 0, 0, 0.05, 0),
                SfxPresetKind.Coin => (true, 523.251131, 0.05, 0.1, 0, 0, 7, 0.05, 0.25),
                SfxPresetKind.Hit => (true, 196, 0, 0.15, -80, 0, 0, 0.05, 0),
                SfxPresetKind.Explosion => (false, 440, 0.05, 0.15, 0, 0, 0, 0.05, 0),
                SfxPresetKind.PowerUp => (true, 196, 0.1, 0.3, 24, 48, 12, 0.2, 0),
                SfxPresetKind.Laser => (true, 880, 0, 0.15, -240, 0, 0, 0.05, 0),
                SfxPresetKind.Blip => (true, 523.251131, 0.016667, 0.016667, 0, 0, 0, 0.05, 0),
                SfxPresetKind.Select => (true, 523.251131, 0.05, 0.05, 0, 0, 5, 0.05, 0),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            bool isHit = kind == SfxPresetKind.Hit;
            bool isExplosion = kind == SfxPresetKind.Explosion;
            SfxEnvelopeParameters noiseEnvelope = new SfxEnvelopeParameters
            {
                Volume = 12, AttackSeconds = 0, SustainSeconds = 0.05, DecaySeconds = 0.15, Punch = 0
            };
            if (isHit)
            {
                noiseEnvelope = noiseEnvelope with { SustainSeconds = 0, DecaySeconds = 0.066667 };
            }
            if (isExplosion)
            {
                noiseEnvelope = noiseEnvelope with { SustainSeconds = 0.016667, DecaySeconds = 0.383333 };
            }
            int snesNoiseRate = isExplosion ? 18 : 24;
            return new SfxParameters
            {
                Tone = new SfxToneParameters
                {
                    Enabled = enabled, BaseFrequencyHz = frequency, SlideSemitonesPerSecond = slide,
                    DeltaSlideSemitonesPerSecondSquared = acceleration, VibratoDepthCents = 0, VibratoSpeedHz = 6,
                    PitchChangeSemitones = pitchChange, PitchChangeTimeSeconds = changeTime, RepeatPeriodSeconds = 0,
                    Envelope = new SfxEnvelopeParameters
                    {
                        Volume = 12, AttackSeconds = 0, SustainSeconds = sustain, DecaySeconds = decay, Punch = punch
                    }
                },
                Noise = new SfxNoiseParameters { Enabled = isHit || isExplosion, Envelope = noiseEnvelope },
                Nes = chip == ChipKind.Nes ? new SfxNesParameters
                {
                    DutyPercent = 25, DutySweepPercentPerSecond = 0, NoiseMode = isHit ? NoiseMode.Short : NoiseMode.Long,
                    NoisePeriodIndex = isHit ? 3 : 12, NoiseSlideIndicesPerSecond = 0
                } : null,
                GameBoy = chip == ChipKind.GameBoy ? new SfxGameBoyParameters
                {
                    DutyPercent = 25, DutySweepPercentPerSecond = 0, NoiseWidth = isHit ? 7 : 15,
                    NoiseSelection = isHit ? 120 : 96, NoiseSlideSelectionsPerSecond = 0
                } : null,
                Snes = chip == ChipKind.Snes ? new SfxSnesParameters
                {
                    Waveform = SnesWaveformKind.Pulse, NoiseRate = isHit ? 31 : snesNoiseRate
                } : null
            };
        }
    }
}
