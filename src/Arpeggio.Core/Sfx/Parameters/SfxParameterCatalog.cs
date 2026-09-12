using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Sfx.Parameters
{
    /// <summary>SFX パラメータ仕様と型アクセスの唯一のカタログ。生成や保存は行わない。</summary>
    public static class SfxParameterCatalog
    {
        private static readonly IReadOnlyList<SfxParameterDescription> Descriptions = Array.AsReadOnly(CreateDescriptions());

        /// <summary>三チップ共通の項目と各チップ専用項目を返す。</summary>
        public static IReadOnlyList<SfxParameterDescription> GetAll() => Descriptions;

        /// <summary>指定チップで使用できる項目を正規順序で返す。</summary>
        public static IReadOnlyList<SfxParameterDescription> GetAll(ChipKind chip)
        {
            RequireChip(chip);
            List<SfxParameterDescription> descriptions = new List<SfxParameterDescription>();
            foreach (SfxParameterDescription description in Descriptions)
            {
                if (description.IsSupported(chip))
                {
                    descriptions.Add(description);
                }
            }
            return descriptions.AsReadOnly();
        }

        /// <summary>正規パスを取得し、未知パスや不適合チップを拒否する。</summary>
        public static SfxParameterDescription Get(string path, ChipKind chip)
        {
            RequireChip(chip);
            foreach (SfxParameterDescription description in Descriptions)
            {
                if (description.Path == path && description.IsSupported(chip))
                {
                    return description;
                }
            }
            throw SfxParameterException.Unsupported(path);
        }

        /// <summary>共通初期値と指定チップ一つの初期値を作る。プリセットは適用しない。</summary>
        public static SfxParameters CreateDefaults(ChipKind chip)
        {
            return chip switch
            {
                ChipKind.Nes => new SfxParameters { Nes = new SfxNesParameters() },
                ChipKind.GameBoy => new SfxParameters { GameBoy = new SfxGameBoyParameters() },
                ChipKind.Snes => new SfxParameters { Snes = new SfxSnesParameters() },
                _ => throw SfxParameterException.Invalid("chip", "nes / gameboy / snes を指定してください。")
            };
        }

        internal static void RequireChip(ChipKind chip)
        {
            if (chip != ChipKind.Nes && chip != ChipKind.GameBoy && chip != ChipKind.Snes)
            {
                throw SfxParameterException.Invalid("chip", "nes / gameboy / snes を指定してください。");
            }
        }

        private static SfxParameterDescription[] CreateDescriptions()
        {
            // 表の範囲値・刻み・離散値は、この宣言だけを検証と各 UI の正本にする。
            return new[]
            {
                new SfxParameterDescription("tone.enabled", ChipKind.None,
                    parameters => parameters.Tone.Enabled,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { Enabled = (bool)value } })
                {
                    ValueKind = SfxParameterValueKind.Boolean, Unit = "",
                    Minimum = null, Maximum = null, Step = 1,
                    FineStep = 1, StepUnit = "",
                    Description = "トーン発音の有無。最低一つのレイヤーを有効にする。"
                },
                new SfxParameterDescription("tone.baseFrequencyHz", ChipKind.None,
                    parameters => parameters.Tone.BaseFrequencyHz,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { BaseFrequencyHz = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "Hz",
                    Minimum = 20, Maximum = 12000, Step = 1,
                    FineStep = 0.01, StepUnit = "半音",
                    IsLogarithmic = true,
                    Description = "基準周波数。入力範囲はチップの発音可能音域を保証しない。"
                },
                new SfxParameterDescription("tone.slideSemitonesPerSecond", ChipKind.None,
                    parameters => parameters.Tone.SlideSemitonesPerSecond,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { SlideSemitonesPerSecond = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "半音/秒",
                    Minimum = -360, Maximum = 360, Step = 1,
                    FineStep = (1) / 10.0, StepUnit = "半音/秒",
                    Description = "正は上昇。repeat はこの曲線時刻を戻す。"
                },
                new SfxParameterDescription("tone.deltaSlideSemitonesPerSecondSquared", ChipKind.None,
                    parameters => parameters.Tone.DeltaSlideSemitonesPerSecondSquared,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { DeltaSlideSemitonesPerSecondSquared = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "半音/秒²",
                    Minimum = -1440, Maximum = 1440, Step = 1,
                    FineStep = (1) / 10.0, StepUnit = "半音/秒²",
                    Description = "音程スライドの加速度。"
                },
                new SfxParameterDescription("tone.vibratoDepthCents", ChipKind.None,
                    parameters => parameters.Tone.VibratoDepthCents,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { VibratoDepthCents = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "cent",
                    Minimum = 0, Maximum = 200, Step = 1,
                    FineStep = (1) / 10.0, StepUnit = "cent",
                    Description = "ビブラートの片振幅。"
                },
                new SfxParameterDescription("tone.vibratoSpeedHz", ChipKind.None,
                    parameters => parameters.Tone.VibratoSpeedHz,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { VibratoSpeedHz = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "Hz",
                    Minimum = 0, Maximum = 20, Step = 0.1,
                    FineStep = (0.1) / 10.0, StepUnit = "Hz",
                    Description = "0は揺れなし。開始位相0、repeat でも位相は継続。"
                },
                new SfxParameterDescription("tone.pitchChangeSemitones", ChipKind.None,
                    parameters => parameters.Tone.PitchChangeSemitones,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { PitchChangeSemitones = (int)value } })
                {
                    ValueKind = SfxParameterValueKind.Integer, Unit = "半音",
                    Minimum = -24, Maximum = 24, Step = 1,
                    FineStep = 1, StepUnit = "半音",
                    Description = "一度だけ変える音程差。0はジャンプなし。"
                },
                new SfxParameterDescription("tone.pitchChangeTimeSeconds", ChipKind.None,
                    parameters => parameters.Tone.PitchChangeTimeSeconds,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { PitchChangeTimeSeconds = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "秒",
                    Minimum = 0, Maximum = 5, Step = SfxParameterValidator.ControlFrameSeconds,
                    FineStep = (SfxParameterValidator.ControlFrameSeconds) / 10.0, StepUnit = "秒",
                    Description = "0なら発音先頭でジャンプ。要求秒数は保持する。"
                },
                new SfxParameterDescription("tone.repeatPeriodSeconds", ChipKind.None,
                    parameters => parameters.Tone.RepeatPeriodSeconds,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { RepeatPeriodSeconds = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "秒",
                    Minimum = SfxParameterValidator.ControlFrameSeconds, Maximum = 5, Step = SfxParameterValidator.ControlFrameSeconds,
                    FineStep = (SfxParameterValidator.ControlFrameSeconds) / 10.0, StepUnit = "秒",
                    AllowsZero = true,
                    Description = "0は無効。音程・duty の曲線時刻だけを戻し、包絡・位相・ビブラートは継続。"
                },
                new SfxParameterDescription("noise.enabled", ChipKind.None,
                    parameters => parameters.Noise.Enabled,
                    (parameters, value) => parameters with { Noise = parameters.Noise with { Enabled = (bool)value } })
                {
                    ValueKind = SfxParameterValueKind.Boolean, Unit = "",
                    Minimum = null, Maximum = null, Step = 1,
                    FineStep = 1, StepUnit = "",
                    Description = "ノイズ発音の有無。トーンなしの単独発音も可能。"
                },
                new SfxParameterDescription("tone.envelope.volume", ChipKind.None,
                    parameters => parameters.Tone.Envelope.Volume,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { Envelope = parameters.Tone.Envelope with { Volume = (int)value } } })
                {
                    ValueKind = SfxParameterValueKind.Integer, Unit = "",
                    Minimum = 0, Maximum = 15, Step = 1,
                    FineStep = 1, StepUnit = "",
                    Description = "レイヤーのピーク音量。0も有効で、全有効レイヤーが0なら警告。"
                },
                new SfxParameterDescription("tone.envelope.attackSeconds", ChipKind.None,
                    parameters => parameters.Tone.Envelope.AttackSeconds,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { Envelope = parameters.Tone.Envelope with { AttackSeconds = (double)value } } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "秒",
                    Minimum = 0, Maximum = 1, Step = SfxParameterValidator.ControlFrameSeconds,
                    FineStep = (SfxParameterValidator.ControlFrameSeconds) / 10.0, StepUnit = "秒",
                    Description = "立ち上がりの要求時間。60 Hz へ量子化する。"
                },
                new SfxParameterDescription("tone.envelope.sustainSeconds", ChipKind.None,
                    parameters => parameters.Tone.Envelope.SustainSeconds,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { Envelope = parameters.Tone.Envelope with { SustainSeconds = (double)value } } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "秒",
                    Minimum = 0, Maximum = 2, Step = SfxParameterValidator.ControlFrameSeconds,
                    FineStep = (SfxParameterValidator.ControlFrameSeconds) / 10.0, StepUnit = "秒",
                    Description = "保持時間。ADSR の sustain level ではない。"
                },
                new SfxParameterDescription("tone.envelope.decaySeconds", ChipKind.None,
                    parameters => parameters.Tone.Envelope.DecaySeconds,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { Envelope = parameters.Tone.Envelope with { DecaySeconds = (double)value } } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "秒",
                    Minimum = SfxParameterValidator.ControlFrameSeconds, Maximum = 2, Step = SfxParameterValidator.ControlFrameSeconds,
                    FineStep = (SfxParameterValidator.ControlFrameSeconds) / 10.0, StepUnit = "秒",
                    Description = "保持後にゼロまで下がる時間。最低一制御フレーム。"
                },
                new SfxParameterDescription("tone.envelope.punch", ChipKind.None,
                    parameters => parameters.Tone.Envelope.Punch,
                    (parameters, value) => parameters with { Tone = parameters.Tone with { Envelope = parameters.Tone.Envelope with { Punch = (double)value } } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "",
                    Minimum = 0, Maximum = 1, Step = 0.01,
                    FineStep = (0.01) / 10.0, StepUnit = "",
                    Description = "保持冒頭の相対的な強調。正値には量子化後1フレーム以上の sustain が必要。ピークは増やさない。"
                },
                new SfxParameterDescription("noise.envelope.volume", ChipKind.None,
                    parameters => parameters.Noise.Envelope.Volume,
                    (parameters, value) => parameters with { Noise = parameters.Noise with { Envelope = parameters.Noise.Envelope with { Volume = (int)value } } })
                {
                    ValueKind = SfxParameterValueKind.Integer, Unit = "",
                    Minimum = 0, Maximum = 15, Step = 1,
                    FineStep = 1, StepUnit = "",
                    Description = "レイヤーのピーク音量。0も有効で、全有効レイヤーが0なら警告。"
                },
                new SfxParameterDescription("noise.envelope.attackSeconds", ChipKind.None,
                    parameters => parameters.Noise.Envelope.AttackSeconds,
                    (parameters, value) => parameters with { Noise = parameters.Noise with { Envelope = parameters.Noise.Envelope with { AttackSeconds = (double)value } } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "秒",
                    Minimum = 0, Maximum = 1, Step = SfxParameterValidator.ControlFrameSeconds,
                    FineStep = (SfxParameterValidator.ControlFrameSeconds) / 10.0, StepUnit = "秒",
                    Description = "立ち上がりの要求時間。60 Hz へ量子化する。"
                },
                new SfxParameterDescription("noise.envelope.sustainSeconds", ChipKind.None,
                    parameters => parameters.Noise.Envelope.SustainSeconds,
                    (parameters, value) => parameters with { Noise = parameters.Noise with { Envelope = parameters.Noise.Envelope with { SustainSeconds = (double)value } } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "秒",
                    Minimum = 0, Maximum = 2, Step = SfxParameterValidator.ControlFrameSeconds,
                    FineStep = (SfxParameterValidator.ControlFrameSeconds) / 10.0, StepUnit = "秒",
                    Description = "保持時間。ADSR の sustain level ではない。"
                },
                new SfxParameterDescription("noise.envelope.decaySeconds", ChipKind.None,
                    parameters => parameters.Noise.Envelope.DecaySeconds,
                    (parameters, value) => parameters with { Noise = parameters.Noise with { Envelope = parameters.Noise.Envelope with { DecaySeconds = (double)value } } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "秒",
                    Minimum = SfxParameterValidator.ControlFrameSeconds, Maximum = 2, Step = SfxParameterValidator.ControlFrameSeconds,
                    FineStep = (SfxParameterValidator.ControlFrameSeconds) / 10.0, StepUnit = "秒",
                    Description = "保持後にゼロまで下がる時間。最低一制御フレーム。"
                },
                new SfxParameterDescription("noise.envelope.punch", ChipKind.None,
                    parameters => parameters.Noise.Envelope.Punch,
                    (parameters, value) => parameters with { Noise = parameters.Noise with { Envelope = parameters.Noise.Envelope with { Punch = (double)value } } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "",
                    Minimum = 0, Maximum = 1, Step = 0.01,
                    FineStep = (0.01) / 10.0, StepUnit = "",
                    Description = "保持冒頭の相対的な強調。正値には量子化後1フレーム以上の sustain が必要。ピークは増やさない。"
                },
                new SfxParameterDescription("nes.dutyPercent", ChipKind.Nes,
                    parameters => parameters.Nes!.DutyPercent,
                    (parameters, value) => parameters with { Nes = parameters.Nes! with { DutyPercent = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "%",
                    Minimum = 12.5, Maximum = 75, Step = 1,
                    FineStep = 1, StepUnit = "%",
                    Choices = Array.AsReadOnly(new object[] { 12.5, 25.0, 50.0, 75.0 }),
                    Description = "選択可能な4段階のパルス比率。"
                },
                new SfxParameterDescription("nes.dutySweepPercentPerSecond", ChipKind.Nes,
                    parameters => parameters.Nes!.DutySweepPercentPerSecond,
                    (parameters, value) => parameters with { Nes = parameters.Nes! with { DutySweepPercentPerSecond = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "%ポイント/秒",
                    Minimum = -100, Maximum = 100, Step = 1,
                    FineStep = (1) / 10.0, StepUnit = "%ポイント/秒",
                    Description = "連続目標を4段階へ量子化。同距離は小さい比率。"
                },
                new SfxParameterDescription("gameBoy.dutyPercent", ChipKind.GameBoy,
                    parameters => parameters.GameBoy!.DutyPercent,
                    (parameters, value) => parameters with { GameBoy = parameters.GameBoy! with { DutyPercent = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "%",
                    Minimum = 12.5, Maximum = 75, Step = 1,
                    FineStep = 1, StepUnit = "%",
                    Choices = Array.AsReadOnly(new object[] { 12.5, 25.0, 50.0, 75.0 }),
                    Description = "選択可能な4段階のパルス比率。"
                },
                new SfxParameterDescription("gameBoy.dutySweepPercentPerSecond", ChipKind.GameBoy,
                    parameters => parameters.GameBoy!.DutySweepPercentPerSecond,
                    (parameters, value) => parameters with { GameBoy = parameters.GameBoy! with { DutySweepPercentPerSecond = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "%ポイント/秒",
                    Minimum = -100, Maximum = 100, Step = 1,
                    FineStep = (1) / 10.0, StepUnit = "%ポイント/秒",
                    Description = "連続目標を4段階へ量子化。同距離は小さい比率。"
                },
                new SfxParameterDescription("nes.noiseMode", ChipKind.Nes,
                    parameters => parameters.Nes!.NoiseMode.ToString().ToLowerInvariant(),
                    (parameters, value) => parameters with { Nes = parameters.Nes! with { NoiseMode = Enum.Parse<NoiseMode>((string)value, true) } })
                {
                    ValueKind = SfxParameterValueKind.Choice, Unit = "",
                    Minimum = null, Maximum = null, Step = 1,
                    FineStep = 1, StepUnit = "",
                    Choices = Array.AsReadOnly(new object[] { "long", "short" }),
                    Description = "発音中は固定のノイズモード。"
                },
                new SfxParameterDescription("nes.noisePeriodIndex", ChipKind.Nes,
                    parameters => parameters.Nes!.NoisePeriodIndex,
                    (parameters, value) => parameters with { Nes = parameters.Nes! with { NoisePeriodIndex = (int)value } })
                {
                    ValueKind = SfxParameterValueKind.Integer, Unit = "添字",
                    Minimum = 0, Maximum = 15, Step = 1,
                    FineStep = 1, StepUnit = "添字",
                    Description = "16周期表の添字。大きいほど遅いクロック。"
                },
                new SfxParameterDescription("nes.noiseSlideIndicesPerSecond", ChipKind.Nes,
                    parameters => parameters.Nes!.NoiseSlideIndicesPerSecond,
                    (parameters, value) => parameters with { Nes = parameters.Nes! with { NoiseSlideIndicesPerSecond = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "添字/秒",
                    Minimum = -60, Maximum = 60, Step = 1,
                    FineStep = (1) / 10.0, StepUnit = "添字/秒",
                    Description = "0〜15へ飽和し折り返さない。トーンの曲線は継承しない。"
                },
                new SfxParameterDescription("gameBoy.noiseWidth", ChipKind.GameBoy,
                    parameters => parameters.GameBoy!.NoiseWidth,
                    (parameters, value) => parameters with { GameBoy = parameters.GameBoy! with { NoiseWidth = (int)value } })
                {
                    ValueKind = SfxParameterValueKind.Integer, Unit = "bit",
                    Minimum = 7, Maximum = 15, Step = 1,
                    FineStep = 1, StepUnit = "bit",
                    Choices = Array.AsReadOnly(new object[] { 7, 15 }),
                    Description = "固定の LFSR 幅。"
                },
                new SfxParameterDescription("gameBoy.noiseSelection", ChipKind.GameBoy,
                    parameters => parameters.GameBoy!.NoiseSelection,
                    (parameters, value) => parameters with { GameBoy = parameters.GameBoy! with { NoiseSelection = (int)value } })
                {
                    ValueKind = SfxParameterValueKind.Integer, Unit = "選択値",
                    Minimum = 0, Maximum = 127, Step = 1,
                    FineStep = 1, StepUnit = "選択値",
                    Description = "既存合成器の選択値。NR43 の raw byte ではなく、明るさの単調性も保証しない。"
                },
                new SfxParameterDescription("gameBoy.noiseSlideSelectionsPerSecond", ChipKind.GameBoy,
                    parameters => parameters.GameBoy!.NoiseSlideSelectionsPerSecond,
                    (parameters, value) => parameters with { GameBoy = parameters.GameBoy! with { NoiseSlideSelectionsPerSecond = (double)value } })
                {
                    ValueKind = SfxParameterValueKind.Number, Unit = "選択値/秒",
                    Minimum = -240, Maximum = 240, Step = 1,
                    FineStep = (1) / 10.0, StepUnit = "選択値/秒",
                    Description = "0〜127へ飽和し折り返さない。トーンの曲線は継承しない。"
                },
                new SfxParameterDescription("snes.waveform", ChipKind.Snes,
                    parameters => parameters.Snes!.Waveform.ToString().ToLowerInvariant(),
                    (parameters, value) => parameters with { Snes = parameters.Snes! with { Waveform = Enum.Parse<SnesWaveformKind>((string)value, true) } })
                {
                    ValueKind = SfxParameterValueKind.Choice, Unit = "",
                    Minimum = null, Maximum = null, Step = 1,
                    FineStep = 1, StepUnit = "",
                    Choices = Array.AsReadOnly(new object[] { "sine", "square", "saw", "triangle", "pulse" }),
                    Description = "内蔵周期波形。pulse=25%、square=50%。常にループ。"
                },
                new SfxParameterDescription("snes.noiseRate", ChipKind.Snes,
                    parameters => parameters.Snes!.NoiseRate,
                    (parameters, value) => parameters with { Snes = parameters.Snes! with { NoiseRate = (int)value } })
                {
                    ValueKind = SfxParameterValueKind.Integer, Unit = "rate",
                    Minimum = 1, Maximum = 31, Step = 1,
                    FineStep = 1, StepUnit = "rate",
                    Description = "固定 DSP ノイズ速度。停止は noise.enabled=false で指定する。"
                },
            };
        }
    }
}
