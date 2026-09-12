using System;
using System.Collections.Generic;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Curves
{
    /// <summary>時間積分したスライドと連続位相のビブラートを、音程ジャンプと分離して展開する。</summary>
    internal static class SfxPitchCurveGenerator
    {
        private const int ReferenceMidiNote = 69;
        private const double ReferenceFrequencyHz = 440;
        private const int SemitonesPerOctave = 12;
        private const int CentsPerSemitone = 100;
        private const double AccelerationIntegralDivisor = 2;
        private const double FullCycleRadians = 2 * Math.PI;
        private const int MinimumRepeatFrames = 1;
        private const string ToneLayer = "tone";

        internal static SfxToneCurve Generate(SfxToneParameters parameters, SfxEnvelopeCurve envelope,
            double dutySweepPercentPerSecond, List<SfxGenerationWarning> warnings)
        {
            int repeatFrames = SfxTimeQuantizer.Quantize(parameters.RepeatPeriodSeconds,
                "tone.repeatPeriodSeconds", ToneLayer, warnings, parameters.RepeatPeriodSeconds > 0 ? MinimumRepeatFrames : 0);
            int pitchChangeFrames = SfxTimeQuantizer.Quantize(parameters.PitchChangeTimeSeconds,
                "tone.pitchChangeTimeSeconds", ToneLayer, warnings);
            double baseMidiNote = ReferenceMidiNote
                + SemitonesPerOctave * Math.Log2(parameters.BaseFrequencyHz / ReferenceFrequencyHz);
            int anchor = (int)Math.Round(baseMidiNote, MidpointRounding.AwayFromZero);
            Macro pitch = new Macro { Values = new int[envelope.BodyFrames], LoopIndex = SfxCurveGenerator.NoMacroLoop };
            Macro arpeggio = new Macro { Values = new int[envelope.BodyFrames], LoopIndex = SfxCurveGenerator.NoMacroLoop };
            for (int frame = 0; frame < envelope.BodyFrames; frame++)
            {
                int curveFrame = repeatFrames == 0 ? frame : frame % repeatFrames;
                double seconds = (double)frame / SfxParameterValidator.ControlFramesPerSecond;
                double curveSeconds = (double)curveFrame / SfxParameterValidator.ControlFramesPerSecond;
                pitch.Values[frame] = EvaluatePitch(parameters, baseMidiNote - anchor, seconds, curveSeconds);
                arpeggio.Values[frame] = curveFrame >= pitchChangeFrames ? parameters.PitchChangeSemitones : 0;
            }
            SfxToneCurve curve = new SfxToneCurve(envelope, anchor, repeatFrames, pitchChangeFrames, pitch, arpeggio);
            AddInactiveWarnings(parameters, curve, dutySweepPercentPerSecond, warnings);
            return curve;
        }

        private static int EvaluatePitch(SfxToneParameters parameters, double baseOffset, double seconds, double curveSeconds)
        {
            double slide = parameters.SlideSemitonesPerSecond * curveSeconds;
            double deltaSlide = parameters.DeltaSlideSemitonesPerSecondSquared
                * curveSeconds * curveSeconds / AccelerationIntegralDivisor;
            // ビブラートだけは曲線用時刻へ戻さず、発音全体の位相を維持する。
            double vibrato = parameters.VibratoDepthCents
                * Math.Sin(FullCycleRadians * parameters.VibratoSpeedHz * seconds);
            return (int)Math.Round(CentsPerSemitone * (baseOffset + slide + deltaSlide) + vibrato,
                MidpointRounding.AwayFromZero);
        }

        private static void AddInactiveWarnings(SfxToneParameters parameters, SfxToneCurve curve,
            double dutySweepPercentPerSecond, List<SfxGenerationWarning> warnings)
        {
            bool changeReachesEnd = curve.PitchChangeFrames >= curve.Envelope.EnvelopeFrames;
            bool repeatPreventsChange = curve.RepeatFrames > 0 && curve.PitchChangeFrames >= curve.RepeatFrames;
            if (parameters.PitchChangeSemitones != 0 && (changeReachesEnd || repeatPreventsChange))
            {
                warnings.Add(CreateInactiveWarning("InactivePitchChange", "tone.pitchChangeSemitones",
                    parameters.PitchChangeSemitones, curve.Envelope.EnvelopeFrames,
                    "音程ジャンプの待ち時間が包絡長または反復周期以上のため、発音中の変化に到達しません。"));
            }
            bool hasResetTarget = parameters.SlideSemitonesPerSecond != 0
                || parameters.DeltaSlideSemitonesPerSecondSquared != 0
                || parameters.PitchChangeSemitones != 0 || dutySweepPercentPerSecond != 0;
            if (curve.RepeatFrames > 0 && (curve.RepeatFrames >= curve.Envelope.EnvelopeFrames || !hasResetTarget))
            {
                warnings.Add(CreateInactiveWarning("InactiveRepeat", "tone.repeatPeriodSeconds",
                    parameters.RepeatPeriodSeconds, curve.Envelope.EnvelopeFrames,
                    "反復周期が包絡長以上、または先頭へ戻すスライド・ジャンプ・デューティ変化がありません。"));
            }
        }

        private static SfxGenerationWarning CreateInactiveWarning(string code, string path,
            double requested, int envelopeFrames, string message)
        {
            return new SfxGenerationWarning
            {
                Code = code,
                ParameterPath = path,
                Layer = ToneLayer,
                FromFrame = 0,
                ToFrame = envelopeFrames,
                Requested = requested,
                Message = message
            };
        }
    }
}
