using System;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>BRR ブロックの予測量子化と復号。呼び出しごとに独立した履歴を持つ。</summary>
    public static class BrrCodec
    {
        /// <summary>一ブロックの符号化バイト数。</summary>
        public const int BytesPerBlock = 9;
        /// <summary>一ブロックの復号サンプル数。</summary>
        public const int SamplesPerBlock = 16;
        private const int MaximumShift = 12;
        private const int FilterCount = 4;
        private const int NibbleBits = 4;
        private const int NibbleMask = 15;
        private const int MinimumNibble = -8;
        private const int MaximumNibble = 7;
        private const int LoopFlag = 2;
        private const int EndFlag = 1;
        private const int FilterShift = 2;
        private const double PcmScale = 32768.0;
        private const int InvalidShiftResidual = -2048;
        private const int SixteenthShift = 4;
        private const int ThirtySecondShift = 5;
        private const int SixtyFourthShift = 6;
        private const int FilterTwoCorrection = 3;
        private const int FilterThreeCorrection = 13;

        /// <summary>全ブロックの shift / filter を探索する。不足分は最終値で埋め、最終ヘッダへ end / loop を付ける。</summary>
        public static byte[] Encode(ReadOnlySpan<float> samples, bool loop = false)
        {
            if (samples.IsEmpty)
            {
                return Array.Empty<byte>();
            }
            int blockCount = (samples.Length - 1) / SamplesPerBlock + 1;
            var encoded = new byte[checked(blockCount * BytesPerBlock)];
            Span<short> source = stackalloc short[SamplesPerBlock];
            int previous = 0;
            int older = 0;
            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                ReadSourceBlock(samples, blockIndex, source);
                Span<byte> block = encoded.AsSpan(blockIndex * BytesPerBlock, BytesPerBlock);
                EncodeBlock(source, block, ref previous, ref older);
            }
            encoded[(blockCount - 1) * BytesPerBlock] |= (byte)(EndFlag | (loop ? LoopFlag : 0));
            return encoded;
        }

        /// <summary>ヘッダのフラグを返し、全ブロックを signed 16 bit 相当の PCM へ復号する。</summary>
        public static float[] Decode(ReadOnlySpan<byte> encoded, out bool loop, out bool end)
        {
            if (encoded.Length % BytesPerBlock != 0)
            {
                throw new ArgumentException("BRR は 9 バイト単位です。", nameof(encoded));
            }
            loop = false;
            end = false;
            var decoded = new float[checked(encoded.Length / BytesPerBlock * SamplesPerBlock)];
            int previous = 0;
            int older = 0;
            for (int blockIndex = 0; blockIndex < encoded.Length / BytesPerBlock; blockIndex++)
            {
                ReadOnlySpan<byte> block = encoded.Slice(blockIndex * BytesPerBlock, BytesPerBlock);
                int blockHeader = block[0];
                loop = (blockHeader & LoopFlag) != 0;
                end = (blockHeader & EndFlag) != 0;
                for (int sampleIndex = 0; sampleIndex < SamplesPerBlock; sampleIndex++)
                {
                    int packed = block[1 + sampleIndex / 2];
                    int nibble = (sampleIndex & 1) == 0 ? packed >> NibbleBits : packed & NibbleMask;
                    nibble = nibble > MaximumNibble ? nibble - SamplesPerBlock : nibble;
                    int value = DecodeNibble(nibble, blockHeader >> NibbleBits, (blockHeader >> FilterShift) & (FilterCount - 1), previous, older);
                    older = previous;
                    previous = value;
                    decoded[blockIndex * SamplesPerBlock + sampleIndex] = (float)(value / PcmScale);
                }
            }
            return decoded;
        }

        /// <summary>フラグを必要としない PCM キャッシュ用の復号。</summary>
        public static float[] Decode(ReadOnlySpan<byte> encoded) => Decode(encoded, out _, out _);

        private static void ReadSourceBlock(ReadOnlySpan<float> samples, int blockIndex, Span<short> destination)
        {
            for (int sampleIndex = 0; sampleIndex < SamplesPerBlock; sampleIndex++)
            {
                float value = samples[Math.Min(blockIndex * SamplesPerBlock + sampleIndex, samples.Length - 1)];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new ArgumentException("BRR 入力は有限の PCM です。", nameof(samples));
                }
                destination[sampleIndex] = (short)Math.Clamp(Math.Round(value * PcmScale), short.MinValue, short.MaxValue);
            }
        }

        private static void EncodeBlock(ReadOnlySpan<short> source, Span<byte> destination, ref int previous, ref int older)
        {
            Span<byte> candidate = stackalloc byte[BytesPerBlock];
            double bestError = double.PositiveInfinity;
            int bestPrevious = previous;
            int bestOlder = older;
            for (int shiftAmount = 0; shiftAmount <= MaximumShift; shiftAmount++)
            {
                for (int filterIndex = 0; filterIndex < FilterCount; filterIndex++)
                {
                    double error = EvaluateBlock(source, candidate, shiftAmount, filterIndex, previous, older, out int last, out int penultimate);
                    if (error >= bestError)
                    {
                        continue;
                    }
                    bestError = error;
                    candidate.CopyTo(destination);
                    bestPrevious = last;
                    bestOlder = penultimate;
                }
                if (bestError == 0)
                {
                    break;
                }
            }
            previous = bestPrevious;
            older = bestOlder;
        }

        private static double EvaluateBlock(ReadOnlySpan<short> source, Span<byte> destination, int shiftAmount,
            int filterIndex, int previous, int older, out int last, out int penultimate)
        {
            destination.Clear();
            destination[0] = (byte)((shiftAmount << NibbleBits) | (filterIndex << FilterShift));
            double error = 0;
            for (int sampleIndex = 0; sampleIndex < SamplesPerBlock; sampleIndex++)
            {
                int prediction = Predict(filterIndex, previous, older);
                int nibble = (int)Math.Clamp(Math.Round((source[sampleIndex] / 2.0 - prediction) * 2 / (1 << shiftAmount)), MinimumNibble, MaximumNibble);
                nibble = SelectNibble(source[sampleIndex], nibble, shiftAmount, filterIndex, previous, older, out int decoded);
                double difference = source[sampleIndex] - decoded;
                error += difference * difference;
                older = previous;
                previous = decoded;
                int packed = (nibble & NibbleMask) << ((sampleIndex & 1) == 0 ? NibbleBits : 0);
                destination[1 + sampleIndex / 2] |= (byte)packed;
            }
            last = previous;
            penultimate = older;
            return error;
        }

        private static int SelectNibble(int target, int estimate, int shiftAmount, int filterIndex, int previous, int older, out int decoded)
        {
            int bestNibble = estimate;
            decoded = DecodeNibble(estimate, shiftAmount, filterIndex, previous, older);
            int bestError = Math.Abs(target - decoded);
            // 15 bit の正端を越える候補は負端へ折り返すため、隣接候補も復号後の誤差で選ぶ。
            for (int candidate = Math.Max(MinimumNibble, estimate - 1); candidate <= Math.Min(MaximumNibble, estimate + 1); candidate++)
            {
                int candidateValue = DecodeNibble(candidate, shiftAmount, filterIndex, previous, older);
                int error = Math.Abs(target - candidateValue);
                if (error < bestError)
                {
                    bestError = error;
                    bestNibble = candidate;
                    decoded = candidateValue;
                }
            }
            return bestNibble;
        }

        private static int DecodeNibble(int nibble, int shiftAmount, int filterIndex, int previous, int older)
        {
            int residual = shiftAmount <= MaximumShift ? (nibble << shiftAmount) >> 1 : (nibble < 0 ? InvalidShiftResidual : 0);
            int decoded = Math.Clamp(residual + Predict(filterIndex, previous, older), short.MinValue, short.MaxValue);
            // BRR は 15 bit の予測履歴を使い、倍化時に signed 16 bit へ折り返す。
            return unchecked((short)(decoded * 2));
        }

        private static int Predict(int filterIndex, int previous, int older)
        {
            int first = previous >> 1;
            int second = older >> 1;
            return filterIndex switch
            {
                1 => first + ((-first) >> SixteenthShift),
                2 => first * 2 + ((-first * FilterTwoCorrection) >> ThirtySecondShift) - second + (second >> SixteenthShift),
                3 => first * 2 + ((-first * FilterThreeCorrection) >> SixtyFourthShift) - second + ((second * FilterTwoCorrection) >> SixteenthShift),
                _ => 0
            };
        }
    }
}
