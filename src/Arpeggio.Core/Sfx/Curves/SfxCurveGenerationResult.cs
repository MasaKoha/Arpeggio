using System;
using System.Collections.Generic;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Curves
{
    /// <summary>Song を組み立てずに返す、正規化パラメータ・共通曲線・時間診断。</summary>
    public sealed class SfxCurveGenerationResult
    {
        internal SfxCurveGenerationResult(SfxParameters parameters, SfxToneCurve? tone,
            SfxEnvelopeCurve? noise, IReadOnlyList<SfxGenerationWarning> warnings)
        {
            Parameters = parameters;
            Tone = tone;
            Noise = noise;
            Warnings = warnings;
            BodyFrames = Math.Max(tone?.Envelope.BodyFrames ?? 0, noise?.BodyFrames ?? 0);
        }

        /// <summary>曲線の展開規則の版。</summary>
        public int GeneratorVersion => SfxCurveGenerator.GeneratorVersion;

        /// <summary>検証済み・小数6桁のパラメータ。要求時間をフレーム秒数で置き換えない。</summary>
        public SfxParameters Parameters { get; }

        /// <summary>トーン曲線。無効レイヤーの場合は null。</summary>
        public SfxToneCurve? Tone { get; }

        /// <summary>ノイズ専用の包絡。無効レイヤーの場合は null。</summary>
        public SfxEnvelopeCurve? Noise { get; }

        /// <summary>有効レイヤーの最大長。終端ゼロの1フレームを含む。</summary>
        public int BodyFrames { get; }

        /// <summary>テンポ150に対応するソング本体の tick 数。</summary>
        public int LengthTicks => BodyFrames * SfxCurveGenerator.TicksPerControlFrame;

        /// <summary>終端余白を含むソング本体の秒数。export の tail は含まない。</summary>
        public double BodyDurationSeconds => (double)BodyFrames / SfxParameterValidator.ControlFramesPerSecond;

        /// <summary>安定した順序の生成診断。チップ音域・duty/noise 曲線の診断は後段で行う。</summary>
        public IReadOnlyList<SfxGenerationWarning> Warnings { get; }
    }
}
