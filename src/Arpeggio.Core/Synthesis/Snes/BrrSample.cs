using System;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>読み込み時に BRR 往復した PCM とブロック境界のループ情報。</summary>
    public sealed class BrrSample
    {
        private readonly float[] _samples;

        private BrrSample(float[] samples, int originalLength, bool loop, int loopStart, int loopEnd)
        {
            _samples = samples;
            OriginalLength = originalLength;
            Loop = loop;
            LoopEnd = AlignLoopEnd(samples.Length, originalLength, loopEnd);
            LoopStart = Math.Clamp(loopStart / BrrCodec.SamplesPerBlock * BrrCodec.SamplesPerBlock, 0, LoopEnd - 1);
        }

        /// <summary>ブロック末尾まで埋めた復号 PCM。呼び出し側から変更できない。</summary>
        public ReadOnlySpan<float> Samples => _samples;
        /// <summary>埋める前のサンプル数。</summary>
        public int OriginalLength { get; }
        /// <summary>ループの有効状態。</summary>
        public bool Loop { get; }
        /// <summary>下側の 16 サンプル境界へ丸めた開始位置。</summary>
        public int LoopStart { get; }
        /// <summary>上側の 16 サンプル境界へ丸めた終端。</summary>
        public int LoopEnd { get; }

        /// <summary>音色編集時に一度だけ BRR 往復する。空入力は拒否する。</summary>
        public static BrrSample Create(ReadOnlySpan<float> samples, bool loop = true, int loopStart = 0, int loopEnd = 0)
        {
            if (samples.IsEmpty)
            {
                throw new ArgumentException("BRR サンプルは空にできません。", nameof(samples));
            }
            return new BrrSample(BrrCodec.Decode(BrrCodec.Encode(samples, loop)), samples.Length, loop, loopStart, loopEnd);
        }

        private static int AlignLoopEnd(int paddedLength, int originalLength, int loopEnd)
        {
            int requestedEnd = loopEnd == 0 ? originalLength : loopEnd;
            long blockCount = ((long)Math.Max(1, requestedEnd) + BrrCodec.SamplesPerBlock - 1) / BrrCodec.SamplesPerBlock;
            return (int)Math.Clamp(blockCount * BrrCodec.SamplesPerBlock, 1, paddedLength);
        }

        internal BrrSample WithLoop(bool loop, int loopStart, int loopEnd)
            => new BrrSample(_samples, OriginalLength, loop, loopStart, loopEnd);
    }
}
