using System;
using System.IO;
using System.Text;

namespace Arpeggio.Core.Tests.Import
{
    /// <summary>取り込み・CLI・MCP の検証に使う独立した PCM WAV と一時領域。</summary>
    internal sealed class SampleFileFixture : IDisposable
    {
        private const int PcmFormat = 1;
        private const int PcmBits = 16;
        private const int FormatLength = 16;
        private const int RiffOverhead = 36;
        private const int BytesPerSample = sizeof(short);

        internal SampleFileFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "arpeggio-sample-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        internal string DirectoryPath { get; }
        internal string SongPath => Path.Combine(DirectoryPath, "song.arpeggio.json");
        internal string WavePath => Path.Combine(DirectoryPath, "sample.wav");
        internal string OggPath => Path.Combine(DirectoryPath, "output.ogg");

        internal void WriteWave(short[] samples, int channels = 1, int sampleRate = 22050)
        {
            using FileStream stream = File.Create(WavePath);
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII);
            int dataLength = samples.Length * BytesPerSample;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(RiffOverhead + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(FormatLength);
            writer.Write((short)PcmFormat);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * BytesPerSample);
            writer.Write((short)(channels * BytesPerSample));
            writer.Write((short)PcmBits);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            foreach (short sample in samples)
            {
                writer.Write(sample);
            }
        }

        /// <summary>生成したファイルと履歴側車を削除する。</summary>
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
}
