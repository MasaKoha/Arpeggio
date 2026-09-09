using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>時間診断・効かないjump/repeat・無音診断の安定した内容と境界。</summary>
    public sealed class SfxGenerationWarningTests
    {
        /// <summary>正規化した要求値と実効秒数を、パス・レイヤー・固定順序とともに返す。</summary>
        [Fact]
        public void Generate_ReturnsOrderedQuantizationAndInactiveDiagnostics()
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Tone = new SfxToneParameters
                {
                    Envelope = new SfxEnvelopeParameters
                    {
                        AttackSeconds = 0.0083335, SustainSeconds = 0.025, DecaySeconds = 0.075, Volume = 0
                    },
                    RepeatPeriodSeconds = 0.025,
                    PitchChangeTimeSeconds = 0.025,
                    PitchChangeSemitones = 7
                },
                Noise = new SfxNoiseParameters
                {
                    Enabled = true,
                    Envelope = new SfxEnvelopeParameters
                    {
                        AttackSeconds = 0.025, SustainSeconds = 0, DecaySeconds = 0.05, Volume = 0
                    }
                }
            };
            SfxCurveGenerationResult result = SfxCurveGenerator.Generate(parameters, ChipKind.Nes);
            Assert.Collection(result.Warnings,
                warning => AssertTimeWarning(warning, "tone.envelope.attackSeconds", "tone", 0.008334, 1.0 / 60),
                warning => AssertTimeWarning(warning, "tone.envelope.sustainSeconds", "tone", 0.025, 2.0 / 60),
                warning => AssertTimeWarning(warning, "tone.envelope.decaySeconds", "tone", 0.075, 5.0 / 60),
                warning => AssertTimeWarning(warning, "tone.repeatPeriodSeconds", "tone", 0.025, 2.0 / 60),
                warning => AssertTimeWarning(warning, "tone.pitchChangeTimeSeconds", "tone", 0.025, 2.0 / 60),
                warning => AssertInactiveWarning(warning, "InactivePitchChange", "tone.pitchChangeSemitones", 7, 8),
                warning => AssertTimeWarning(warning, "noise.envelope.attackSeconds", "noise", 0.025, 2.0 / 60),
                AssertSilentWarning);
            SfxToneCurve tone = Assert.IsType<SfxToneCurve>(result.Tone);
            Assert.Equal(8, tone.Envelope.EnvelopeFrames);
            Assert.Equal(18, result.LengthTicks);
            Assert.Equal(0.108334, tone.Envelope.RequestedEnvelopeSeconds, 12);
            Assert.Equal(0.0083335, parameters.Tone.Envelope.AttackSeconds);
            Assert.Equal(0.008334, result.Parameters.Tone.Envelope.AttackSeconds);
            Assert.Equal(0.025, result.Parameters.Tone.RepeatPeriodSeconds);
            Assert.Equal(0.025, result.Parameters.Tone.PitchChangeTimeSeconds);
        }

        /// <summary>丸め差のない時間は報告せず、ジャンプ量0でも待ち時間の丸め差は保持する。</summary>
        [Fact]
        public void Generate_ReportsOnlyChangedTimes()
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Tone = new SfxToneParameters
                {
                    Envelope = new SfxEnvelopeParameters { SustainSeconds = 0, DecaySeconds = 0.05 },
                    PitchChangeTimeSeconds = 0.008333
                }
            };
            SfxCurveGenerationResult result = SfxCurveGenerator.Generate(parameters, ChipKind.Nes);
            AssertTimeWarning(Assert.Single(result.Warnings), "tone.pitchChangeTimeSeconds", "tone", 0.008333, 0);
            Assert.Equal(0, Assert.IsType<SfxToneCurve>(result.Tone).PitchChangeFrames);
        }

        /// <summary>jumpとrepeatの両方が終端に到達する場合、診断を各一件の固定順で返す。</summary>
        [Fact]
        public void Generate_OrdersBothInactiveWarningsAtTerminalFrame()
        {
            SfxCurveGenerationResult result = GenerateTone(new SfxToneParameters
            {
                PitchChangeSemitones = 7, PitchChangeTimeSeconds = 0.05, RepeatPeriodSeconds = 0.05
            });
            Assert.Collection(result.Warnings,
                warning => AssertInactiveWarning(warning, "InactivePitchChange", "tone.pitchChangeSemitones", 7, 3),
                warning => AssertInactiveWarning(warning, "InactiveRepeat", "tone.repeatPeriodSeconds", 0.05, 3));
            Assert.Equal(new[] { 0, 0, 0, 0 }, Assert.IsType<SfxToneCurve>(result.Tone).ArpeggioMacro.Values);
        }

        /// <summary>jumpはNまたはrepeat周期以上で無効と診断し、Nのゼロ保持フレームも境界に含める。</summary>
        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(1.0 / 60, 0, false)]
        [InlineData(2.0 / 60, 0, false)]
        [InlineData(0.05, 0, true)]
        [InlineData(5, 0, true)]
        [InlineData(1.0 / 60, 2.0 / 60, false)]
        [InlineData(2.0 / 60, 2.0 / 60, true)]
        [InlineData(0.05, 2.0 / 60, true)]
        public void Generate_DiagnosesUnreachablePitchChange(double delay, double period, bool expectedInactive)
        {
            SfxCurveGenerationResult result = GenerateTone(new SfxToneParameters
            {
                PitchChangeSemitones = -7, PitchChangeTimeSeconds = delay, RepeatPeriodSeconds = period
            });
            if (expectedInactive)
            {
                SfxGenerationWarning warning = Assert.Single(result.Warnings, warning => warning.Code == "InactivePitchChange");
                AssertInactiveWarning(warning, "InactivePitchChange", "tone.pitchChangeSemitones", -7, 3);
                return;
            }
            Assert.DoesNotContain(result.Warnings, warning => warning.Code == "InactivePitchChange");
        }

        /// <summary>反復周期のN直前/N/超過と、明示された戻し対象の有無を判定する。</summary>
        [Theory]
        [InlineData(0, 0, 0, 0, false)]
        [InlineData(1.0 / 60, 0, 0, 0, true)]
        [InlineData(1.0 / 60, 12, 0, 0, false)]
        [InlineData(1.0 / 60, 0, 360, 0, false)]
        [InlineData(1.0 / 60, 0, 0, 7, false)]
        [InlineData(2.0 / 60, 12, 0, 0, false)]
        [InlineData(0.05, 12, 0, 0, true)]
        [InlineData(5, 12, 0, 0, true)]
        public void Generate_DiagnosesInactiveRepeat(double period, double slide, double delta, int jump, bool expectedInactive)
        {
            SfxCurveGenerationResult result = GenerateTone(new SfxToneParameters
            {
                RepeatPeriodSeconds = period, SlideSemitonesPerSecond = slide,
                DeltaSlideSemitonesPerSecondSquared = delta, PitchChangeSemitones = jump,
                VibratoDepthCents = 100, VibratoSpeedHz = 15
            });
            if (expectedInactive)
            {
                SfxGenerationWarning warning = Assert.Single(result.Warnings, warning => warning.Code == "InactiveRepeat");
                AssertInactiveWarning(warning, "InactiveRepeat", "tone.repeatPeriodSeconds", period, 3);
                return;
            }
            Assert.DoesNotContain(result.Warnings, warning => warning.Code == "InactiveRepeat");
        }

        /// <summary>NES/GBのdutySweepだけでもrepeatの対象になる。SNESのノイズ設定には継承しない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, false)]
        [InlineData(ChipKind.GameBoy, false)]
        [InlineData(ChipKind.Snes, true)]
        public void Generate_IncludesChipDutySweepInRepeatDiagnostic(ChipKind chip, bool expectedInactive)
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(chip) with
            {
                Tone = new SfxToneParameters { RepeatPeriodSeconds = 0.05 }
            };
            if (chip == ChipKind.Nes)
            {
                parameters = parameters with { Nes = new SfxNesParameters { DutySweepPercentPerSecond = 25 } };
            }
            if (chip == ChipKind.GameBoy)
            {
                parameters = parameters with { GameBoy = new SfxGameBoyParameters { DutySweepPercentPerSecond = 25 } };
            }
            SfxCurveGenerationResult result = SfxCurveGenerator.Generate(parameters, chip);
            if (expectedInactive)
            {
                AssertInactiveWarning(Assert.Single(result.Warnings), "InactiveRepeat", "tone.repeatPeriodSeconds", 0.05, 12);
                return;
            }
            Assert.Empty(result.Warnings);
        }

        /// <summary>無効トーンは長さ・時間診断・無効設定診断に使わず、ノイズを単独生成する。</summary>
        [Fact]
        public void Generate_SkipsDisabledToneCurvesAndDiagnostics()
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Tone = new SfxToneParameters
                {
                    Enabled = false, PitchChangeSemitones = 7, PitchChangeTimeSeconds = 0.025,
                    RepeatPeriodSeconds = 0.025, VibratoDepthCents = 100,
                    SlideSemitonesPerSecond = -360, DeltaSlideSemitonesPerSecondSquared = 1440,
                    Envelope = new SfxEnvelopeParameters { AttackSeconds = 1, SustainSeconds = 2, DecaySeconds = 2 }
                },
                Noise = new SfxNoiseParameters
                {
                    Enabled = true, Envelope = new SfxEnvelopeParameters { SustainSeconds = 0, DecaySeconds = 0.05 }
                }
            };
            SfxCurveGenerationResult result = SfxCurveGenerator.Generate(parameters, ChipKind.Nes);
            Assert.Null(result.Tone);
            Assert.Equal(new[] { 12, 8, 4, 0 }, Assert.IsType<SfxEnvelopeCurve>(result.Noise).VolumeMacro.Values);
            Assert.Equal(8, result.LengthTicks);
            Assert.Empty(result.Warnings);
            Assert.Equal(parameters, result.Parameters);
        }

        /// <summary>無音は全有効レイヤーのvolume=0だけで判断し、一件の全体診断とする。</summary>
        [Theory]
        [InlineData(false, 12, true)]
        [InlineData(true, 0, true)]
        [InlineData(true, 12, false)]
        public void Generate_DiagnosesSilentEnabledLayers(bool noiseEnabled, int noiseVolume, bool expectedSilent)
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Tone = new SfxToneParameters { Envelope = new SfxEnvelopeParameters { Volume = 0 } },
                Noise = new SfxNoiseParameters { Enabled = noiseEnabled, Envelope = new SfxEnvelopeParameters { Volume = noiseVolume } }
            };
            SfxCurveGenerationResult result = SfxCurveGenerator.Generate(parameters, ChipKind.Nes);
            if (expectedSilent)
            {
                AssertSilentWarning(Assert.Single(result.Warnings));
                return;
            }
            Assert.Empty(result.Warnings);
        }

        /// <summary>生成入口でも全OFF・punchの不成立・無効レイヤーの不正値を拒否する。</summary>
        [Fact]
        public void Generate_ValidatesAllInputBeforeGenerating()
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            Assert.Throws<SfxParameterException>(() => SfxCurveGenerator.Generate(null!, ChipKind.Nes));
            Assert.Throws<SfxParameterException>(() => SfxCurveGenerator.Generate(parameters, ChipKind.None));
            Assert.Throws<SfxParameterException>(() => SfxCurveGenerator.Generate(
                parameters with { Tone = parameters.Tone with { Enabled = false } }, ChipKind.Nes));
            SfxParameterException exception = Assert.Throws<SfxParameterException>(() => SfxCurveGenerator.Generate(
                parameters with
                {
                    Noise = new SfxNoiseParameters { Envelope = new SfxEnvelopeParameters { SustainSeconds = 0, Punch = 1 } }
                }, ChipKind.Nes));
            Assert.Equal("noise.envelope.punch", exception.ParameterPath);
        }

        private static SfxCurveGenerationResult GenerateTone(SfxToneParameters tone)
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Tone = tone with { Envelope = new SfxEnvelopeParameters { SustainSeconds = 0, DecaySeconds = 0.05 } }
            };
            return SfxCurveGenerator.Generate(parameters, ChipKind.Nes);
        }

        private static void AssertTimeWarning(SfxGenerationWarning warning, string path, string layer, double requested, double actual)
        {
            Assert.Equal("TimeQuantized", warning.Code);
            Assert.Equal(path, warning.ParameterPath);
            Assert.Equal(layer, warning.Layer);
            Assert.Equal(requested, warning.Requested);
            Assert.Equal(actual, warning.Actual);
            Assert.Null(warning.FromFrame);
            Assert.Null(warning.ToFrame);
            Assert.Equal("要求秒数を60 Hzの制御フレームへ丸めました。", warning.Message);
        }

        private static void AssertInactiveWarning(SfxGenerationWarning warning, string code, string path, double requested, int lastFrame)
        {
            Assert.Equal(code, warning.Code);
            Assert.Equal(path, warning.ParameterPath);
            Assert.Equal("tone", warning.Layer);
            Assert.Equal(0, warning.FromFrame);
            Assert.Equal(lastFrame, warning.ToFrame);
            Assert.Equal(Math.Round(requested, 6, MidpointRounding.AwayFromZero), warning.Requested);
            Assert.Null(warning.Actual);
            Assert.False(string.IsNullOrWhiteSpace(warning.Message));
        }

        private static void AssertSilentWarning(SfxGenerationWarning warning)
        {
            Assert.Equal("SilentParameters", warning.Code);
            Assert.Equal(string.Empty, warning.ParameterPath);
            Assert.Null(warning.Layer);
            Assert.Null(warning.FromFrame);
            Assert.Null(warning.ToFrame);
            Assert.Null(warning.Requested);
            Assert.Null(warning.Actual);
            Assert.Equal("全有効レイヤーのピーク音量が0です。", warning.Message);
        }
    }
}
