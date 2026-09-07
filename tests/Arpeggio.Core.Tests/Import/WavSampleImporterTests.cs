using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Import;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;
using Xunit;

namespace Arpeggio.Core.Tests.Import
{
    /// <summary>取り込みのチャンネル変換・上限・失敗時の音色保持を検証する。</summary>
    public sealed class WavSampleImporterTests
    {
        /// <summary>モノラル PCM と元レートを保持し、音色の既存設定を維持する。</summary>
        [Fact]
        public void Import_MonoPreservesPcmAndInstrumentSettings()
        {
            using SampleFileFixture files = new SampleFileFixture();
            short[] input = { short.MinValue, -16384, 0, 8192, short.MaxValue };
            files.WriteWave(input);
            SnesSampleInstrument instrument = new SnesSampleInstrument { Name = "voice", Pan = -0.5, EchoSend = 0.25 };
            AdsrEnvelope envelope = instrument.Envelope;
            WavSampleImporter.Import(instrument, files.WavePath, 69, 1, 4, true);
            Assert.Equal(input, SampleDataCodec.Decode(instrument.SampleData!));
            Assert.Equal(22050, instrument.SampleRate);
            Assert.Equal(69, instrument.RootMidiNote);
            Assert.Equal(1, instrument.LoopStart);
            Assert.Equal(4, instrument.LoopEnd);
            Assert.True(instrument.Loop);
            Assert.Equal("voice", instrument.Name);
            Assert.Equal(-0.5, instrument.Pan);
            Assert.Equal(0.25, instrument.EchoSend);
            Assert.Equal(envelope, instrument.Envelope);
        }

        /// <summary>ステレオの逆相を相殺し、同相の正負飽和値を保持する。</summary>
        [Fact]
        public void Import_StereoAveragesChannels()
        {
            using SampleFileFixture files = new SampleFileFixture();
            files.WriteWave(new short[] { short.MinValue, short.MaxValue, short.MaxValue, short.MaxValue, short.MinValue, short.MinValue }, channels: 2);
            SnesSampleInstrument instrument = new SnesSampleInstrument();
            WavSampleImporter.Import(instrument, files.WavePath, 60, null, null, true);
            Assert.Equal(new short[] { 0, short.MaxValue, short.MinValue }, SampleDataCodec.Decode(instrument.SampleData!));
            Assert.Equal(0, instrument.LoopEnd);
        }

        /// <summary>ステレオのファイルサイズではなく、モノラル化後の PCM サイズで上限を判定する。</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void Import_AcceptsExactlyMaximumMonoSize(int channels)
        {
            using SampleFileFixture files = new SampleFileFixture();
            int sampleCount = SampleDataCodec.MaximumByteCount / sizeof(short);
            files.WriteWave(new short[sampleCount * channels], channels);
            SnesSampleInstrument instrument = new SnesSampleInstrument();
            WavSampleImporter.Import(instrument, files.WavePath, 60, null, null, true);
            Assert.Equal(sampleCount, instrument.SampleCount);
        }

        /// <summary>不正ループ・基準音・空・上限超過・読み込み失敗では元音色を保つ。</summary>
        [Fact]
        public void Import_FailureDoesNotMutateInstrument()
        {
            using SampleFileFixture files = new SampleFileFixture();
            files.WriteWave(new short[] { 1, 2 });
            SnesSampleInstrument instrument = new SnesSampleInstrument { SampleData = SampleDataCodec.Encode(new short[] { 99 }) };
            string before = InstrumentJson.Serialize(instrument);
            Assert.Throws<SongValidationException>(() => WavSampleImporter.Import(instrument, files.WavePath, 60, 2, 0, true));
            Assert.Throws<SongValidationException>(() => WavSampleImporter.Import(instrument, files.WavePath, 128, null, null, true));
            files.WriteWave(Array.Empty<short>());
            Assert.Throws<SongValidationException>(() => WavSampleImporter.Import(instrument, files.WavePath, 60, null, null, true));
            files.WriteWave(new short[SampleDataCodec.MaximumByteCount / sizeof(short) + 1]);
            Assert.Throws<ArgumentException>(() => WavSampleImporter.Import(instrument, files.WavePath, 60, null, null, true));
            File.Delete(files.WavePath);
            Assert.Throws<FileNotFoundException>(() => WavSampleImporter.Import(instrument, files.WavePath, 60, null, null, true));
            Assert.Equal(before, InstrumentJson.Serialize(instrument));
        }
    }
}
