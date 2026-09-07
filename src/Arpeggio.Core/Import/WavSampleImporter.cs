using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;

namespace Arpeggio.Core.Import
{
    /// <summary>WAV をモノラル PCM に変換し、検証成功時だけ SNES 音色へ適用する。</summary>
    public static class WavSampleImporter
    {
        private const int StereoChannels = 2;

        /// <summary>元レートを保持して取り込む。ループ位置はモノラルのサンプル単位、未指定終端は末尾。</summary>
        public static void Import(SnesSampleInstrument instrument, string wavPath, int rootMidiNote, int? loopStart, int? loopEnd, bool loop)
        {
            float[] stereo = ReadSource(wavPath, out int sampleRate);
            int sampleCount = stereo.Length / StereoChannels;
            if (sampleCount == 0 || sampleCount > SampleDataCodec.MaximumByteCount / SampleDataCodec.BytesPerSample)
            {
                throw new SongValidationException("WAV は空にできず、モノラル PCM で 2 MiB 以下です。");
            }
            float[] mono = new float[sampleCount];
            for (int index = 0; index < mono.Length; index++)
            {
                mono[index] = (stereo[index * StereoChannels] + stereo[index * StereoChannels + 1]) / StereoChannels;
            }
            SnesSampleInstrument candidate = new SnesSampleInstrument
            {
                SampleData = SampleDataCodec.Encode(SampleDataCodec.FromFloat(mono)),
                SampleRate = sampleRate,
                RootMidiNote = rootMidiNote,
                LoopStart = loopStart ?? 0,
                LoopEnd = loopEnd ?? 0,
                Loop = loop
            };
            InstrumentValidator.ValidateEmbeddedSample(candidate);
            instrument.SampleData = candidate.SampleData;
            instrument.SampleRate = candidate.SampleRate;
            instrument.RootMidiNote = candidate.RootMidiNote;
            instrument.LoopStart = candidate.LoopStart;
            instrument.LoopEnd = candidate.LoopEnd;
            instrument.Loop = candidate.Loop;
        }

        private static float[] ReadSource(string wavPath, out int sampleRate)
        {
            try
            {
                return WavReader.Read(wavPath, out sampleRate, SampleDataCodec.MaximumByteCount / SampleDataCodec.BytesPerSample);
            }
            catch (InvalidDataException exception)
            {
                throw new ArgumentException(exception.Message, nameof(wavPath), exception);
            }
        }
    }
}
