using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Sfx
{
    /// <summary>独立した一声の量子化時間と、終端ゼロを含む音量マクロ。</summary>
    public sealed class SfxEnvelopeCurve
    {
        internal SfxEnvelopeCurve(double requestedEnvelopeSeconds, int attackFrames, int sustainFrames,
            int decayFrames, Macro volumeMacro)
        {
            RequestedEnvelopeSeconds = requestedEnvelopeSeconds;
            AttackFrames = attackFrames;
            SustainFrames = sustainFrames;
            DecayFrames = decayFrames;
            VolumeMacro = volumeMacro;
        }

        /// <summary>正規化後の要求 A/S/D 秒数の合計。終端余白は含まない。</summary>
        public double RequestedEnvelopeSeconds { get; }

        /// <summary>立ち上がりの制御フレーム数。</summary>
        public int AttackFrames { get; }

        /// <summary>保持の制御フレーム数。</summary>
        public int SustainFrames { get; }

        /// <summary>減衰の制御フレーム数。最低1。</summary>
        public int DecayFrames { get; }

        /// <summary>終端余白を除く制御フレーム数 N。</summary>
        public int EnvelopeFrames => AttackFrames + SustainFrames + DecayFrames;

        /// <summary>量子化後の包絡秒数。終端余白は含まない。</summary>
        public double EnvelopeDurationSeconds => (double)EnvelopeFrames / SfxParameterValidator.ControlFramesPerSecond;

        /// <summary>終端のゼロ音量保持を含む制御フレーム数。</summary>
        public int BodyFrames => EnvelopeFrames + SfxCurveGenerator.TerminalHoldFrames;

        /// <summary>終端のゼロ音量保持を含むノート長。テンポ150での tick 数。</summary>
        public int DurationTicks => BodyFrames * SfxCurveGenerator.TicksPerControlFrame;

        /// <summary>終端のゼロ音量保持を含む一声の本体秒数。</summary>
        public double BodyDurationSeconds => (double)BodyFrames / SfxParameterValidator.ControlFramesPerSecond;

        /// <summary>N+1 要素、最終値0、ループなしの音量マクロ。結果が所有する。</summary>
        public Macro VolumeMacro { get; }
    }
}
