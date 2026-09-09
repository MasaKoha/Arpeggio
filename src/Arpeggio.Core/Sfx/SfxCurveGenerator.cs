using System.Collections.Generic;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx
{
    /// <summary>パラメータを検証し、Song に依存しない60 Hzの包絡・ピッチ曲線を純粋生成する。</summary>
    public static class SfxCurveGenerator
    {
        /// <summary>保存する生成規則の版。</summary>
        public const int GeneratorVersion = 1;

        /// <summary>制御フレームと2 tickを対応させるテンポ。</summary>
        public const int TempoBpm = 150;

        /// <summary>一制御フレームの tick 数。48 ticks/beat、テンポ150が前提。</summary>
        public const int TicksPerControlFrame = 2;

        /// <summary>発音停止前にゼロ音量を保持する制御フレーム数。</summary>
        public const int TerminalHoldFrames = 1;

        /// <summary>有限長マクロの末尾を保持するループ指定。</summary>
        public const int NoMacroLoop = -1;

        /// <summary>全入力を検証・正規化し、有効レイヤーだけの新しいマクロと診断を返す。入力は変更しない。</summary>
        public static SfxCurveGenerationResult Generate(SfxParameters parameters, ChipKind chip)
        {
            // 全レイヤーの検証を先に終え、範囲外の秒数による配列確保を防ぐ。
            SfxParameters normalized = SfxParameterValidator.Normalize(parameters, chip);
            List<SfxGenerationWarning> warnings = new List<SfxGenerationWarning>();
            SfxToneCurve? tone = null;
            if (normalized.Tone.Enabled)
            {
                SfxEnvelopeCurve envelope = SfxEnvelopeGenerator.Generate(normalized.Tone.Envelope, "tone", warnings);
                tone = SfxPitchCurveGenerator.Generate(normalized.Tone, envelope, GetDutySweep(normalized, chip), warnings);
            }
            SfxEnvelopeCurve? noise = normalized.Noise.Enabled
                ? SfxEnvelopeGenerator.Generate(normalized.Noise.Envelope, "noise", warnings)
                : null;
            foreach (SfxParameterWarning warning in SfxParameterValidator.Validate(normalized, chip))
            {
                warnings.Add(new SfxGenerationWarning
                {
                    Code = warning.Code,
                    ParameterPath = warning.ParameterPath,
                    Message = warning.Message
                });
            }
            return new SfxCurveGenerationResult(normalized, tone, noise, warnings.AsReadOnly());
        }

        private static double GetDutySweep(SfxParameters parameters, ChipKind chip)
        {
            // 現在チップの設定は Normalize で必須性を検証済み。
            return chip switch
            {
                ChipKind.Nes => parameters.Nes!.DutySweepPercentPerSecond,
                ChipKind.GameBoy => parameters.GameBoy!.DutySweepPercentPerSecond,
                _ => 0
            };
        }
    }
}
