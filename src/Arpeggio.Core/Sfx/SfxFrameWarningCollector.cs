using System.Collections.Generic;

namespace Arpeggio.Core.Sfx
{
    /// <summary>同じ種類の連続フレームをまとめ、先頭の要求値・実効値を代表として残す。</summary>
    internal static class SfxFrameWarningCollector
    {
        internal static void Add(List<SfxGenerationWarning> warnings, SfxGenerationWarning warning)
        {
            for (int index = warnings.Count - 1; index >= 0; index--)
            {
                SfxGenerationWarning previous = warnings[index];
                if (previous.Code != warning.Code || previous.ParameterPath != warning.ParameterPath
                    || previous.Layer != warning.Layer)
                {
                    continue;
                }
                if (previous.ToFrame.HasValue && previous.ToFrame.Value + 1 == warning.FromFrame)
                {
                    warnings[index] = previous with { ToFrame = warning.ToFrame };
                    return;
                }
                break;
            }
            warnings.Add(warning);
        }
    }
}
