using System;
using System.Collections.Generic;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Curves
{
    /// <summary>ノイズ選択を固有範囲へ飽和し、基準選択からのセント差へ写す。</summary>
    internal static class SfxNoiseCurveGenerator
    {
        private const int CentsPerSelection = 100;
        internal const int MaximumNesSelection = 15;
        internal const int MaximumGameBoySelection = 127;

        internal static Macro Generate(SfxEnvelopeCurve envelope, int baseSelection, double slide,
            int maximumSelection, string parameterPath, List<SfxGenerationWarning> warnings)
        {
            Macro macro = new Macro
            {
                Values = new int[envelope.BodyFrames], LoopIndex = SfxCurveGenerator.NoMacroLoop
            };
            for (int frame = 0; frame < macro.Values.Length; frame++)
            {
                double seconds = (double)frame / SfxParameterValidator.ControlFramesPerSecond;
                double target = baseSelection + slide * seconds;
                int requested = (int)Math.Round(target, MidpointRounding.AwayFromZero);
                int actual = Math.Clamp(requested, 0, maximumSelection);
                // 合成器側の剰余へ範囲外の添字を渡すと周期が折り返すため、生成時に飽和する。
                macro.Values[frame] = CentsPerSelection * (actual - baseSelection);
                if (requested != actual)
                {
                    SfxFrameWarningCollector.Add(warnings, new SfxGenerationWarning
                    {
                        Code = "NoiseSelectionClamped", ParameterPath = parameterPath, Layer = "noise",
                        FromFrame = frame, ToFrame = frame, Requested = requested, Actual = actual,
                        Message = "ノイズ選択をチップの範囲へ飽和しました。値は範囲先頭の代表値です。"
                    });
                }
            }
            return macro;
        }
    }
}
