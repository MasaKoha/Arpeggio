using System;
using System.Collections.Generic;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Curves
{
    /// <summary>検証・正規化済みの秒数を60 Hzへ量子化し、秒数の差を保持する。</summary>
    internal static class SfxTimeQuantizer
    {
        internal static int Quantize(double seconds, string parameterPath, string layer,
            List<SfxGenerationWarning> warnings, int minimumFrames = 0)
        {
            int frames = Math.Max(minimumFrames,
                (int)Math.Round(seconds * SfxParameterValidator.ControlFramesPerSecond, MidpointRounding.AwayFromZero));
            double actualSeconds = (double)frames / SfxParameterValidator.ControlFramesPerSecond;
            if (seconds != actualSeconds)
            {
                warnings.Add(new SfxGenerationWarning
                {
                    Code = "TimeQuantized",
                    ParameterPath = parameterPath,
                    Layer = layer,
                    Requested = seconds,
                    Actual = actualSeconds,
                    Message = "要求秒数を60 Hzの制御フレームへ丸めました。"
                });
            }
            return frames;
        }
    }
}
