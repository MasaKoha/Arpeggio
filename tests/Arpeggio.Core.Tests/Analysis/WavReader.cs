using System;
using System.IO;
using System.Text;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>書き出し実装に依存せず RIFF チャンクと PCM データを読み戻す。</summary>
    internal sealed class WavReader
    {
        private const int PcmFormat = 1;
        private const int PcmBits = 16;
        private const int StereoChannels = 2;
        private const int BytesPerSample = 2;

        internal int SampleRate { get; private set; }
        internal int Channels { get; private set; }
        internal int BitsPerSample { get; private set; }
        internal short[] Samples { get; private set; } = Array.Empty<short>();

        internal WavReader(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            RequireTag(reader, "RIFF");
            uint riffLength = reader.ReadUInt32();
            if (riffLength + 8L != stream.Length)
            {
                throw new InvalidDataException("RIFF の宣言長と実データ長が一致しません。");
            }

            RequireTag(reader, "WAVE");
            bool hasFormat = false;
            bool hasData = false;
            while (stream.Position < stream.Length)
            {
                string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint length = reader.ReadUInt32();
                long nextPosition = stream.Position + length + (length & 1);
                if (tag == "fmt ")
                {
                    ReadFormat(reader);
                    hasFormat = true;
                }
                else if (tag == "data")
                {
                    ReadSamples(reader, length);
                    hasData = true;
                }

                stream.Position = nextPosition;
            }

            if (!hasFormat || !hasData)
            {
                throw new InvalidDataException("PCM の書式または波形チャンクがありません。");
            }
        }

        private void ReadFormat(BinaryReader reader)
        {
            ushort format = reader.ReadUInt16();
            Channels = reader.ReadUInt16();
            SampleRate = reader.ReadInt32();
            int byteRate = reader.ReadInt32();
            ushort blockAlign = reader.ReadUInt16();
            BitsPerSample = reader.ReadUInt16();
            bool validFormat = format == PcmFormat && Channels == StereoChannels && BitsPerSample == PcmBits;
            bool validRates = byteRate == SampleRate * Channels * BytesPerSample && blockAlign == Channels * BytesPerSample;
            if (!validFormat || !validRates)
            {
                throw new InvalidDataException("16 bit ステレオ PCM の形式に一致しません。");
            }
        }

        private void ReadSamples(BinaryReader reader, uint length)
        {
            if (length % (StereoChannels * BytesPerSample) != 0)
            {
                throw new InvalidDataException("ステレオフレームの途中でデータが終了しています。");
            }

            Samples = new short[checked((int)(length / BytesPerSample))];
            for (int index = 0; index < Samples.Length; index++)
            {
                Samples[index] = reader.ReadInt16();
            }
        }

        private static void RequireTag(BinaryReader reader, string expected)
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != expected)
            {
                throw new InvalidDataException("WAV 識別子が不正です。");
            }
        }
    }
}
