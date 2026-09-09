using System;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>先頭音では検出できない全軌跡の制限と、通常の周期量子化を検証する。</summary>
    public sealed class SfxPitchDiagnosticsTests
    {
        /// <summary>通常音域でも各フレームのレジスタ量子化値を返し、クランプ警告と区別する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 1789773.0 / (16 * 254))]
        [InlineData(ChipKind.GameBoy, 131072.0 / 298)]
        public void Compile_ReportsQuantizationWithoutClampWarning(ChipKind chip, double actualFrequency)
        {
            SfxSongCompilationResult result = SfxSongCompiler.Compile(SfxCompilationTestData.ShortParameters(chip), chip);
            Assert.Equal(4, result.TonePitchFrames.Count);
            Assert.All(result.TonePitchFrames, frame =>
            {
                Assert.Equal(69, frame.RequestedMidiNote);
                Assert.Equal(440.0, frame.RequestedFrequencyHz);
                Assert.Equal(actualFrequency, frame.ActualFrequencyHz, 10);
                Assert.False(frame.IsClamped);
            });
            Assert.DoesNotContain(result.Warnings, warning => warning.Code == "PitchClamped");
        }

        /// <summary>発音途中から上限を越える軌跡は、終端のゼロ音量フレームまで一範囲で診断する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 10, 1789773.0 / (16 * 9))]
        [InlineData(ChipKind.GameBoy, 17, 131072)]
        public void Compile_DiagnosesUpperClampBeyondTheBaseNote(ChipKind chip, int firstClamped, double maximum)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(chip);
            parameters = parameters with
            {
                Tone = parameters.Tone with { SlideSemitonesPerSecond = 360, Envelope = parameters.Tone.Envelope with { DecaySeconds = 0.4 } }
            };
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, chip);
            Assert.False(result.TonePitchFrames[0].IsClamped);
            Assert.False(result.TonePitchFrames[firstClamped - 1].IsClamped);
            Assert.True(result.TonePitchFrames[firstClamped].IsClamped);
            Assert.Equal(maximum, result.TonePitchFrames[firstClamped].ActualFrequencyHz, 8);
            SfxGenerationWarning warning = Assert.Single(result.Warnings, candidate => candidate.Code == "PitchClamped");
            Assert.Equal(firstClamped, warning.FromFrame);
            Assert.Equal(24, warning.ToFrame);
            Assert.Equal(result.TonePitchFrames[firstClamped].RequestedFrequencyHz, warning.Requested);
            Assert.Equal(25, result.TonePitchFrames.Count);
        }

        /// <summary>低音入力は入力を書き換えず、全軌跡を各チップの下限へ制限する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 1789773.0 / (16 * 2048))]
        [InlineData(ChipKind.GameBoy, 64)]
        public void Compile_ReportsLowerLimitWithoutChangingMacros(ChipKind chip, double minimum)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(chip);
            parameters = parameters with { Tone = parameters.Tone with { BaseFrequencyHz = 20, SlideSemitonesPerSecond = -360 } };
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, chip);
            Assert.Equal(20, result.Curves.Parameters.Tone.BaseFrequencyHz);
            Assert.All(result.TonePitchFrames, frame => Assert.Equal(minimum, frame.ActualFrequencyHz, 8));
            Assert.All(result.TonePitchFrames, frame => Assert.True(frame.IsClamped));
            SfxGenerationWarning warning = Assert.Single(result.Warnings, candidate => candidate.Code == "PitchClamped");
            Assert.Equal(0, warning.FromFrame);
            Assert.Equal(3, warning.ToFrame);
            Assert.True(result.Curves.Tone!.PitchMacro.Values[3] < result.Curves.Tone.PitchMacro.Values[0]);
        }

        /// <summary>repeat で一時的に音域へ戻る箇所では警告を分け、jump と vibrato の加算結果を診断する。</summary>
        [Fact]
        public void Compile_SeparatesRangesAtRepeatBoundary()
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Nes);
            parameters = parameters with
            {
                Tone = parameters.Tone with
                {
                    BaseFrequencyHz = 880, PitchChangeSemitones = 24, PitchChangeTimeSeconds = 1.0 / 60,
                    SlideSemitonesPerSecond = 360, VibratoDepthCents = 200, VibratoSpeedHz = 15,
                    RepeatPeriodSeconds = 0.1, Envelope = parameters.Tone.Envelope with { DecaySeconds = 0.2 }
                }
            };
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, ChipKind.Nes);
            SfxGenerationWarning[] warnings = result.Warnings.Where(warning => warning.Code == "PitchClamped").ToArray();
            Assert.Equal(new int?[] { 4, 10 }, warnings.Select(warning => warning.FromFrame));
            Assert.Equal(new int?[] { 5, 11 }, warnings.Select(warning => warning.ToFrame));
            Assert.Equal(113, result.TonePitchFrames[1].RequestedMidiNote);
            Assert.False(result.TonePitchFrames[6].IsClamped);
        }

        /// <summary>許容最大曲線でも非有限 JSON を出さず、有限 MIDI と最大長602 tickを保持する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 1)]
        [InlineData(ChipKind.Nes, -1)]
        [InlineData(ChipKind.GameBoy, 1)]
        [InlineData(ChipKind.GameBoy, -1)]
        [InlineData(ChipKind.Snes, 1)]
        [InlineData(ChipKind.Snes, -1)]
        public void Compile_MaximumCurveKeepsSerializableDiagnostics(ChipKind chip, int direction)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(chip);
            parameters = parameters with
            {
                Tone = parameters.Tone with
                {
                    SlideSemitonesPerSecond = direction * 360, DeltaSlideSemitonesPerSecondSquared = direction * 1440,
                    Envelope = new SfxEnvelopeParameters { AttackSeconds = 1, SustainSeconds = 2, DecaySeconds = 2 }
                }
            };
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, chip);
            Assert.Equal(602, result.Song.LengthTicks);
            Assert.Equal(301, result.TonePitchFrames.Count);
            Assert.Null(result.TonePitchFrames[300].RequestedFrequencyHz);
            Assert.All(result.TonePitchFrames, frame =>
            {
                Assert.True(double.IsFinite(frame.RequestedMidiNote));
                Assert.True(double.IsFinite(frame.ActualFrequencyHz));
            });
            Assert.NotEmpty(JsonSerializer.Serialize(result.Warnings));
            Assert.NotEmpty(JsonSerializer.Serialize(result.TonePitchFrames));
        }

        /// <summary>トーン無効時は保存した音程から音域診断を生成しない。</summary>
        [Fact]
        public void Compile_DisabledToneProducesNoPitchDiagnostics()
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Nes);
            parameters = parameters with
            {
                Tone = parameters.Tone with { Enabled = false, BaseFrequencyHz = 20 },
                Noise = parameters.Noise with { Enabled = true }
            };
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, ChipKind.Nes);
            Assert.Empty(result.TonePitchFrames);
            Assert.DoesNotContain(result.Warnings, warning => warning.Layer == "tone");
        }
    }
}
