using System;
using System.IO;
using System.Text;

namespace Arpeggio.Core.Render
{
    /// <summary>RIFF の PCM 16 bit モノラル／ステレオを読み込む。</summary>
    public static class WavReader
    {
        private const int PcmFormat = 1;
        private const int PcmBits = 16;
        private const int BytesPerSample = PcmBits / 8;
        private const int StereoChannels = 2;
        private const int MinimumFormatLength = 16;
        private const int ChunkHeaderLength = 8;
        private const int RiffHeaderLength = 12;
        private const float NegativePcmScale = 32768;
        private const float PositivePcmScale = 32767;

        /// <summary>16 bit PCM を読み、ステレオ float（-1〜1）で返す。モノラルは左右へ複製する。</summary>
        public static float[] Read(string path, out int sampleRate)
        {
            using FileStream stream = File.OpenRead(path);
            return Read(stream, out sampleRate);
        }

        /// <summary>シーク可能なストリームの 16 bit PCM をステレオへ変換する。ストリームは閉じない。</summary>
        public static float[] Read(Stream stream, out int sampleRate)
        {
            if (!stream.CanRead || !stream.CanSeek)
            {
                throw new ArgumentException("読み取り・シーク可能なストリームを指定してください。", nameof(stream));
            }
            long start = stream.Position;
            if (stream.Length - start < RiffHeaderLength)
            {
                throw new InvalidDataException("WAV ヘッダーが途中で終了しています。");
            }
            using BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            RequireTag(reader, "RIFF");
            long end = start + ChunkHeaderLength + reader.ReadUInt32();
            RequireTag(reader, "WAVE");
            if (end != stream.Length || end < stream.Position)
            {
                throw new InvalidDataException("RIFF の宣言長と実データ長が一致しません。");
            }
            return ReadChunks(reader, end, out sampleRate);
        }

        private static float[] ReadChunks(BinaryReader reader, long end, out int sampleRate)
        {
            Stream stream = reader.BaseStream;
            int channels = 0;
            sampleRate = 0;
            long dataPosition = -1;
            uint dataLength = 0;
            while (stream.Position < end)
            {
                if (end - stream.Position < ChunkHeaderLength)
                {
                    throw new InvalidDataException("WAV チャンクヘッダーが途中で終了しています。");
                }
                string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint length = reader.ReadUInt32();
                long next = stream.Position + length + (length & 1);
                if (next > end)
                {
                    throw new InvalidDataException("WAV チャンクが RIFF の宣言長を超えています。");
                }
                if (tag == "fmt ")
                {
                    if (channels != 0)
                    {
                        throw new InvalidDataException("fmt チャンクが重複しています。");
                    }
                    channels = ReadFormat(reader, length, out sampleRate);
                }
                else if (tag == "data")
                {
                    if (dataPosition >= 0)
                    {
                        throw new InvalidDataException("data チャンクが重複しています。");
                    }
                    dataPosition = stream.Position;
                    dataLength = length;
                }
                stream.Position = next;
            }
            if (channels == 0 || dataPosition < 0)
            {
                throw new InvalidDataException("fmt または data チャンクがありません。");
            }
            stream.Position = dataPosition;
            float[] samples = ReadSamples(reader, dataLength, channels);
            stream.Position = end;
            return samples;
        }

        private static int ReadFormat(BinaryReader reader, uint length, out int sampleRate)
        {
            if (length < MinimumFormatLength)
            {
                throw new InvalidDataException("fmt チャンクが短すぎます。");
            }
            int format = reader.ReadUInt16();
            int channels = reader.ReadUInt16();
            sampleRate = reader.ReadInt32();
            uint byteRate = reader.ReadUInt32();
            int blockAlign = reader.ReadUInt16();
            int bits = reader.ReadUInt16();
            if (format != PcmFormat || (channels != 1 && channels != StereoChannels) || bits != PcmBits)
            {
                throw new InvalidDataException("対応形式は PCM 16 bit のモノラル／ステレオです。");
            }
            if (sampleRate <= 0 || blockAlign != channels * BytesPerSample || byteRate != (long)sampleRate * blockAlign)
            {
                throw new InvalidDataException("WAV のサンプルレート・バイトレート・フレーム幅が不正です。");
            }
            return channels;
        }

        private static float[] ReadSamples(BinaryReader reader, uint length, int channels)
        {
            int frameBytes = channels * BytesPerSample;
            if (length % frameBytes != 0)
            {
                throw new InvalidDataException("PCM がフレームの途中で終了しています。");
            }
            long sampleCount = (long)length / frameBytes * StereoChannels;
            if (sampleCount > Array.MaxLength)
            {
                throw new InvalidDataException("WAV が一括読み込みの上限を超えています。");
            }
            float[] samples = new float[(int)sampleCount];
            for (int index = 0; index < samples.Length; index += StereoChannels)
            {
                float left = NormalizeSample(reader.ReadInt16());
                samples[index] = left;
                samples[index + 1] = channels == 1 ? left : NormalizeSample(reader.ReadInt16());
            }
            return samples;
        }

        private static float NormalizeSample(short sample)
        {
            // 正負の飽和端点を両方 ±1 に対応させ、書き出し後もクリップを検出できるようにする。
            return sample < 0 ? sample / NegativePcmScale : sample / PositivePcmScale;
        }

        private static void RequireTag(BinaryReader reader, string expected)
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != expected)
            {
                throw new InvalidDataException("RIFF / WAVE 識別子が不正です。");
            }
        }
    }
}
