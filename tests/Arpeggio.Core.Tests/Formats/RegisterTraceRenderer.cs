using System;
using System.Collections.Generic;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>レジスタ列を絶対サンプル境界で適用する、テスト専用の再合成スケジューラー。</summary>
    internal static class RegisterTraceRenderer
    {
        internal const int SampleRate = 44100;

        internal static (double[] Left, double[] Right) Render(IRegisterTraceChip chip,
            IReadOnlyList<(long Sample, int Address, int Value, int Order)> writes, int sampleCount)
        {
            Validate(writes, sampleCount);
            var left = new double[sampleCount];
            var right = new double[sampleCount];
            int writeIndex = 0;
            long elapsedCycles = 0;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                long targetCycles = (long)sample * chip.ClockRate / SampleRate;
                chip.AdvanceCycles(checked((int)(targetCycles - elapsedCycles)));
                elapsedCycles = targetCycles;
                while (writeIndex < writes.Count && writes[writeIndex].Sample == sample)
                {
                    var write = writes[writeIndex++];
                    chip.Apply(write.Address, write.Value);
                }
                // perf: 出力配列は走査前に確保し、サンプル境界では値型だけを受け渡す。
                (left[sample], right[sample]) = chip.Output;
            }
            return (left, right);
        }

        private static void Validate(IReadOnlyList<(long Sample, int Address, int Value, int Order)> writes, int sampleCount)
        {
            if (sampleCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleCount));
            }
            long previousSample = 0;
            for (int writeIndex = 0; writeIndex < writes.Count; writeIndex++)
            {
                var write = writes[writeIndex];
                if (write.Sample < previousSample || write.Sample >= sampleCount || write.Order != writeIndex)
                {
                    throw new ArgumentException("書き込みの時刻・順序・観測長が不正です。", nameof(writes));
                }
                previousSample = write.Sample;
            }
        }
    }
}
