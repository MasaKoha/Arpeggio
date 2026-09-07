using System;
using System.Numerics;

namespace Arpeggio.Core.Analysis
{
    /// <summary>作業領域内で計算する基数 2 の離散フーリエ変換。</summary>
    public static class FastFourierTransform
    {
        /// <summary>2 の累乗長の複素数列を、正規化しない順変換で上書きする。</summary>
        public static void Transform(Span<Complex> spectrum)
        {
            int length = spectrum.Length;
            if (length < 2 || (length & (length - 1)) != 0)
            {
                throw new ArgumentException("変換点数は 2 以上の 2 の累乗です。", nameof(spectrum));
            }
            ReverseBits(spectrum);
            for (int blockLength = 2; blockLength <= length; blockLength *= 2)
            {
                TransformStage(spectrum, blockLength);
                if (blockLength == length)
                {
                    break;
                }
            }
        }

        private static void ReverseBits(Span<Complex> spectrum)
        {
            int reversed = 0;
            for (int index = 1; index < spectrum.Length; index++)
            {
                int bit = spectrum.Length / 2;
                while ((reversed & bit) != 0)
                {
                    reversed ^= bit;
                    bit >>= 1;
                }
                reversed ^= bit;
                if (index < reversed)
                {
                    Complex previous = spectrum[index];
                    spectrum[index] = spectrum[reversed];
                    spectrum[reversed] = previous;
                }
            }
        }

        private static void TransformStage(Span<Complex> spectrum, int blockLength)
        {
            int halfLength = blockLength / 2;
            Complex rotation = Complex.FromPolarCoordinates(1, -2 * Math.PI / blockLength);
            for (int start = 0; start < spectrum.Length; start += blockLength)
            {
                Complex factor = Complex.One;
                for (int offset = 0; offset < halfLength; offset++)
                {
                    Complex even = spectrum[start + offset];
                    Complex odd = factor * spectrum[start + offset + halfLength];
                    spectrum[start + offset] = even + odd;
                    spectrum[start + offset + halfLength] = even - odd;
                    factor *= rotation;
                }
            }
        }
    }
}
