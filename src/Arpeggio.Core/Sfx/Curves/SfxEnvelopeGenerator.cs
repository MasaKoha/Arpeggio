using System;
using System.Collections.Generic;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Curves
{
    /// <summary>ASDecay と punch をピーク正規化した有限長の音量列へ展開する。</summary>
    internal static class SfxEnvelopeGenerator
    {
        private const int MaximumVolume = 15;
        private const int MinimumDecayFrames = 1;
        private const double PunchGain = 2;

        internal static SfxEnvelopeCurve Generate(SfxEnvelopeParameters parameters, string layer,
            List<SfxGenerationWarning> warnings)
        {
            string path = layer + ".envelope.";
            int attackFrames = SfxTimeQuantizer.Quantize(parameters.AttackSeconds, path + "attackSeconds", layer, warnings);
            int sustainFrames = SfxTimeQuantizer.Quantize(parameters.SustainSeconds, path + "sustainSeconds", layer, warnings);
            int decayFrames = SfxTimeQuantizer.Quantize(parameters.DecaySeconds, path + "decaySeconds", layer, warnings, MinimumDecayFrames);
            int envelopeFrames = attackFrames + sustainFrames + decayFrames;
            Macro volumeMacro = new Macro
            {
                Values = new int[envelopeFrames + SfxCurveGenerator.TerminalHoldFrames],
                LoopIndex = SfxCurveGenerator.NoMacroLoop
            };
            SfxEnvelopeCurve curve = new SfxEnvelopeCurve(
                parameters.AttackSeconds + parameters.SustainSeconds + parameters.DecaySeconds,
                attackFrames, sustainFrames, decayFrames, volumeMacro);
            double peak = 1 + PunchGain * parameters.Punch;
            for (int frame = 0; frame < envelopeFrames; frame++)
            {
                double envelope = Evaluate(frame, curve, parameters.Punch);
                volumeMacro.Values[frame] = Math.Clamp(
                    (int)Math.Round(parameters.Volume * envelope / peak, MidpointRounding.AwayFromZero), 0, MaximumVolume);
            }
            return curve;
        }

        private static double Evaluate(int frame, SfxEnvelopeCurve curve, double punch)
        {
            // 区間の上端を先に判定するため、長さ0の区間を除算へ渡さない。
            if (frame < curve.AttackFrames)
            {
                return (double)frame / curve.AttackFrames;
            }
            int sustainEnd = curve.AttackFrames + curve.SustainFrames;
            if (frame < sustainEnd)
            {
                return 1 + PunchGain * punch * (1 - (double)(frame - curve.AttackFrames) / curve.SustainFrames);
            }
            return 1 - (double)(frame - sustainEnd) / curve.DecayFrames;
        }
    }
}
