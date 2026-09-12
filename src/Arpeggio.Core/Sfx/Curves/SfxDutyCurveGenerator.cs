using System;
using System.Collections.Generic;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Curves
{
    /// <summary>トーンの反復時刻から四段階のデューティ列を作る。</summary>
    internal static class SfxDutyCurveGenerator
    {
        private const double MinimumPercent = 12.5;
        private const double QuarterPercent = 25;
        private const double HalfPercent = 50;
        private const double MaximumPercent = 75;
        private const double MidpointDivisor = 2;

        internal static Macro Generate(SfxToneCurve tone, double dutyPercent, double sweep,
            string chipPath, List<SfxGenerationWarning> warnings)
        {
            Macro macro = new Macro
            {
                Values = new int[tone.Envelope.BodyFrames], LoopIndex = SfxCurveGenerator.NoMacroLoop
            };
            for (int frame = 0; frame < macro.Values.Length; frame++)
            {
                int curveFrame = tone.RepeatFrames == 0 ? frame : frame % tone.RepeatFrames;
                double curveSeconds = (double)curveFrame / SfxParameterValidator.ControlFramesPerSecond;
                double requested = dutyPercent + sweep * curveSeconds;
                double clamped = Math.Clamp(requested, MinimumPercent, MaximumPercent);
                DutyCycle duty = Quantize(clamped);
                double actual = GetPercent(duty);
                macro.Values[frame] = (int)duty;
                if (requested != clamped)
                {
                    AddWarning(warnings, "DutyClamped", chipPath, frame, requested, clamped,
                        "デューティ目標を12.5〜75%へ制限しました。値は範囲先頭の代表値です。");
                }
                if (clamped != actual)
                {
                    AddWarning(warnings, "DutyQuantized", chipPath, frame, clamped, actual,
                        "デューティを最寄りの四段階へ量子化しました。同距離は小さい比率です。値は範囲先頭の代表値です。");
                }
            }
            return macro;
        }

        private static DutyCycle Quantize(double percent)
        {
            if (percent <= (MinimumPercent + QuarterPercent) / MidpointDivisor)
            {
                return DutyCycle.Percent12_5;
            }
            if (percent <= (QuarterPercent + HalfPercent) / MidpointDivisor)
            {
                return DutyCycle.Percent25;
            }
            return percent <= (HalfPercent + MaximumPercent) / MidpointDivisor
                ? DutyCycle.Percent50 : DutyCycle.Percent75;
        }

        private static double GetPercent(DutyCycle duty)
        {
            return duty switch
            {
                DutyCycle.Percent12_5 => MinimumPercent,
                DutyCycle.Percent25 => QuarterPercent,
                DutyCycle.Percent50 => HalfPercent,
                _ => MaximumPercent
            };
        }

        private static void AddWarning(List<SfxGenerationWarning> warnings, string code, string chipPath,
            int frame, double requested, double actual, string message)
        {
            SfxFrameWarningCollector.Add(warnings, new SfxGenerationWarning
            {
                Code = code, ParameterPath = chipPath + ".dutyPercent", Layer = "tone",
                FromFrame = frame, ToFrame = frame, Requested = requested, Actual = actual, Message = message
            });
        }
    }
}
