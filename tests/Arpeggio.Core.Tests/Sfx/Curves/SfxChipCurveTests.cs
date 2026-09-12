using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Compile;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Synthesis;
using Arpeggio.Core.Tests.Sfx.Compile;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Curves
{
    /// <summary>デューティの中間値・反復とノイズの飽和・独立時刻を検証する。</summary>
    public sealed class SfxChipCurveTests
    {
        /// <summary>四つの固定デューティと二チップの組合せ。</summary>
        public static IEnumerable<object[]> DutyCases()
        {
            foreach (ChipKind chip in new[] { ChipKind.Nes, ChipKind.GameBoy })
            {
                yield return new object[] { chip, 12.5, 1 };
                yield return new object[] { chip, 25.0, 2 };
                yield return new object[] { chip, 50.0, 3 };
                yield return new object[] { chip, 75.0, 4 };
            }
        }

        /// <summary>一定値も全フレームを保存し、通常の四段階には余分な量子化警告を出さない。</summary>
        [Theory]
        [MemberData(nameof(DutyCases))]
        public void Compile_ExpandsEveryConstantDuty(ChipKind chip, double percent, int expected)
        {
            SfxParameters parameters = SfxCompilationTestData.WithDuty(SfxCompilationTestData.ShortParameters(chip), chip, percent, 0);
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, chip);
            Macro duty = SfxCompilationTestData.Duty(Assert.Single(result.Song.Instruments));
            Assert.Equal(new[] { expected, expected, expected, expected }, duty.Values);
            Assert.Equal(-1, duty.LoopIndex);
            Assert.DoesNotContain(result.Warnings, warning => warning.Code.StartsWith("Duty", StringComparison.Ordinal));
        }

        /// <summary>下限を割るデューティは12.5%へ飽和し、上限側へ折り返さない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        public void Compile_DutySaturatesAtLowerLimit(ChipKind chip)
        {
            SfxParameters parameters = SfxCompilationTestData.WithDuty(SfxCompilationTestData.ShortParameters(chip), chip, 12.5, -100);
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, chip);
            Assert.Equal(new[] { 1, 1, 1, 1 }, SfxCompilationTestData.Duty(result.Song.Instruments[0]).Values);
            SfxGenerationWarning warning = Assert.Single(result.Warnings, candidate => candidate.Code == "DutyClamped");
            Assert.Equal(1, warning.FromFrame);
            Assert.Equal(3, warning.ToFrame);
            Assert.Equal(12.5, warning.Actual);
        }

        /// <summary>NES の長短モードと GB のビット幅をノイズ音色へ固定する。</summary>
        [Theory]
        [InlineData(NoiseMode.Long, 15)]
        [InlineData(NoiseMode.Short, 7)]
        public void Compile_PreservesNoiseModeAndWidth(NoiseMode mode, int width)
        {
            SfxParameters nes = SfxCompilationTestData.ShortParameters(ChipKind.Nes);
            nes = nes with { Tone = nes.Tone with { Enabled = false }, Noise = nes.Noise with { Enabled = true }, Nes = nes.Nes! with { NoiseMode = mode } };
            var nesNoise = Assert.IsType<NesNoiseInstrument>(Assert.Single(SfxSongCompiler.Compile(nes, ChipKind.Nes).Song.Instruments));
            Assert.Equal(mode, nesNoise.NoiseMode);
            SfxParameters gameBoy = SfxCompilationTestData.ShortParameters(ChipKind.GameBoy);
            gameBoy = gameBoy with { Tone = gameBoy.Tone with { Enabled = false }, Noise = gameBoy.Noise with { Enabled = true }, GameBoy = gameBoy.GameBoy! with { NoiseWidth = width } };
            var gameBoyNoise = Assert.IsType<GbNoiseInstrument>(Assert.Single(SfxSongCompiler.Compile(gameBoy, ChipKind.GameBoy).Song.Instruments));
            Assert.Equal(width, gameBoyNoise.LfsrWidth);
        }

        /// <summary>三つの中間点は小さい比率に寄せ、直後のフレームでは隣の比率へ進む。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 12.5, 75, 5, 1, 2)]
        [InlineData(ChipKind.GameBoy, 12.5, 75, 5, 1, 2)]
        [InlineData(ChipKind.Nes, 25, 75, 10, 2, 3)]
        [InlineData(ChipKind.GameBoy, 25, 75, 10, 2, 3)]
        [InlineData(ChipKind.Nes, 50, 75, 10, 3, 4)]
        [InlineData(ChipKind.GameBoy, 50, 75, 10, 3, 4)]
        public void Compile_DutyTiesChooseLowerPercent(ChipKind chip, double percent, double sweep, int frame, int lower, int upper)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(chip);
            parameters = parameters with { Tone = parameters.Tone with { Envelope = parameters.Tone.Envelope with { DecaySeconds = 0.3 } } };
            parameters = SfxCompilationTestData.WithDuty(parameters, chip, percent, sweep);
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, chip);
            Macro duty = SfxCompilationTestData.Duty(result.Song.Instruments[0]);
            Assert.Equal(lower, duty.Values[frame]);
            Assert.Equal(upper, duty.Values[frame + 1]);
            Assert.Contains(result.Warnings, warning => warning.Code == "DutyQuantized"
                && warning.FromFrame <= frame && warning.ToFrame >= frame);
        }

        /// <summary>デューティは repeat で戻り、包絡は継続する。飽和警告は周期の切れ目で別範囲になる。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        public void Compile_DutyRepeatsAndGroupsClampRanges(ChipKind chip)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(chip);
            parameters = parameters with
            {
                Tone = parameters.Tone with { RepeatPeriodSeconds = 0.05, Envelope = parameters.Tone.Envelope with { DecaySeconds = 0.1 } }
            };
            parameters = SfxCompilationTestData.WithDuty(parameters, chip, 75, 100);
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, chip);
            Assert.Equal(new[] { 4, 4, 4, 4, 4, 4, 4 }, SfxCompilationTestData.Duty(result.Song.Instruments[0]).Values);
            Assert.Equal(new[] { 12, 10, 8, 6, 4, 2, 0 }, SfxCompilationTestData.Volume(result.Song.Instruments[0]).Values);
            SfxGenerationWarning[] clamped = result.Warnings.Where(warning => warning.Code == "DutyClamped").ToArray();
            Assert.Equal(new int?[] { 1, 4 }, clamped.Select(warning => warning.FromFrame));
            Assert.Equal(new int?[] { 2, 5 }, clamped.Select(warning => warning.ToFrame));
            Assert.All(clamped, warning => Assert.Equal(75.0, warning.Actual));

            parameters = SfxCompilationTestData.WithDuty(parameters, chip, 25, 100);
            parameters = parameters with { Tone = parameters.Tone with { RepeatPeriodSeconds = 0.2, Envelope = parameters.Tone.Envelope with { DecaySeconds = 0.4 } } };
            int[] values = SfxCompilationTestData.Duty(SfxSongCompiler.Compile(parameters, chip).Song.Instruments[0]).Values;
            Assert.Equal(2, values[0]);
            Assert.Equal(3, values[11]);
            Assert.Equal(values.Take(12), values.Skip(12).Take(12));
            Assert.Equal(2, values[24]);
        }

        /// <summary>選択値を上下限で保持し、トーンの repeat・jump・slide をノイズへ伝播させない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 1, -60, new[] { 0, -100, -100, -100 }, 2, 3)]
        [InlineData(ChipKind.Nes, 14, 60, new[] { 0, 100, 100, 100 }, 2, 3)]
        [InlineData(ChipKind.GameBoy, 2, -240, new[] { 0, -200, -200, -200 }, 1, 3)]
        [InlineData(ChipKind.GameBoy, 125, 240, new[] { 0, 200, 200, 200 }, 1, 3)]
        [InlineData(ChipKind.GameBoy, 6, 60, new[] { 0, 100, 200, 300 }, -1, -1)]
        [InlineData(ChipKind.Nes, 0, 30, new[] { 0, 100, 100, 200 }, -1, -1)]
        public void Compile_NoiseSaturatesWithoutFoldingOrToneModulation(ChipKind chip, int selection, double slide,
            int[] expected, int warningFrom, int warningTo)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(chip);
            parameters = parameters with
            {
                Tone = parameters.Tone with
                {
                    SlideSemitonesPerSecond = 360, RepeatPeriodSeconds = 1.0 / 60,
                    PitchChangeSemitones = 24, PitchChangeTimeSeconds = 0, VibratoDepthCents = 200
                },
                Noise = parameters.Noise with { Enabled = true }
            };
            parameters = SfxCompilationTestData.WithNoise(parameters, chip, selection, slide);
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, chip);
            Instrument noise = Assert.Single(result.Song.Instruments, instrument => instrument.Id == 2);
            Macro pitch = SfxCompilationTestData.Pitch(noise);
            Assert.Equal(expected, pitch.Values);
            Assert.Equal(selection, Assert.Single(result.Song.Tracks[3].Notes).MidiNote);
            var modulation = new VoiceModulation();
            modulation.Start(selection, 15, Array.Empty<NoteEffect>());
            modulation.Configure(SfxCompilationTestData.Volume(noise), null, pitch);
            for (int frame = 0; frame < expected.Length; frame++)
            {
                Assert.Equal(selection + expected[frame] / 100.0, modulation.MidiNote);
                Assert.InRange(modulation.MidiNote, 0, chip == ChipKind.Nes ? 15 : 127);
                modulation.AdvanceFrame();
            }
            if (warningFrom < 0)
            {
                Assert.DoesNotContain(result.Warnings, warning => warning.Code == "NoiseSelectionClamped");
                return;
            }
            SfxGenerationWarning warning = Assert.Single(result.Warnings, candidate => candidate.Code == "NoiseSelectionClamped");
            Assert.Equal(warningFrom, warning.FromFrame);
            Assert.Equal(warningTo, warning.ToFrame);
            Assert.Equal("noise", warning.Layer);
            Assert.Equal(chip == ChipKind.Nes ? "nes.noisePeriodIndex" : "gameBoy.noiseSelection", warning.ParameterPath);
        }
    }
}
