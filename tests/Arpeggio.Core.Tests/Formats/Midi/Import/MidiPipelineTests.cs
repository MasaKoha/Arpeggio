using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Arpeggio.Codecs;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Arpeggio.Formats.Midi;
using Xunit;
using Arpeggio.Core.Tests.Formats.Export;
using Arpeggio.Core.Tests.Formats.Export.GameBoy;
using Arpeggio.Core.Tests.Formats.Export.Nes;
using Arpeggio.Core.Tests.Formats.Export.Nsf;
using Arpeggio.Core.Tests.Formats.Export.Vgm;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.Nsf;
using Arpeggio.Formats.Midi.Import;

namespace Arpeggio.Core.Tests.Formats.Midi.Import
{
    /// <summary>MIDI→JSON→既存音声出力／レジスタ出力を統合し、非干渉と失敗時のファイル保持を検証する。</summary>
    public sealed class MidiPipelineTests
    {
        private const int SampleRate = 44100;
        private const int StereoChannels = 2;
        private const int SongTicks = 144;
        private const int SongSamples = 66150;
        private const int ObservationStart = 33075;
        private const int ObservationSamples = 4410;
        private const double MelodyFrequency = 440;
        private const int MaximumInitCycles = 20000;
        private const int MaximumPlayCycles = 8000;

        /// <summary>三チップでテンポ・和音・打楽器を保存し、JSON 往復の PCM 全ビットと WAV／OGG を検証する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void MidiJsonAndAudioOutputsPreserveSourceAndRender(ChipKind chip)
        {
            using var fixture = new MidiPipelineFixture();
            byte[] originalMidi = File.ReadAllBytes(fixture.SourcePath);
            MidiImportResult imported = fixture.Import(chip);
            Assert.True(imported.CanWrite);
            Song song = SongSerializer.Load(fixture.SongPath);
            SongValidator.Validate(song);
            Assert.Equal(1, song.Version);
            Assert.Equal(SongTicks, song.LengthTicks);
            Assert.Equal(5, imported.Report.Statistics["acceptedMidiNotes"]);
            Assert.Single(imported.Report.Warnings, warning => warning.Code == "TempoMapFlattened");
            Assert.Equal(imported.Json, SongSerializer.Serialize(song));
            var settings = new RenderSettings(SampleRate, 1, 0);
            var originalRenderer = new SongRenderer(imported.Song!, settings);
            var loadedRenderer = new SongRenderer(song, settings);
            float[] originalSamples = originalRenderer.RenderAll();
            float[] samples = loadedRenderer.RenderAll();
            Assert.Equal(SongSamples * StereoChannels, samples.Length);
            Assert.Contains(samples, sample => Math.Abs(sample) > 0.001f);
            Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
            Assert.Equal(originalSamples.Select(BitConverter.SingleToInt32Bits), samples.Select(BitConverter.SingleToInt32Bits));
            Assert.Equal(originalRenderer.Report.Warnings, loadedRenderer.Report.Warnings);

            string wavePath = fixture.PathFor("song.wav");
            WavWriter.Write(wavePath, samples, SampleRate);
            float[] waveSamples = WavReader.Read(wavePath, out int waveRate);
            Assert.Equal(SampleRate, waveRate);
            Assert.Equal(samples.Length, waveSamples.Length);
            Assert.Contains(waveSamples, sample => Math.Abs(sample) > 0.001f);
            string oggPath = fixture.PathFor("song.ogg");
            OggWriter.Write(oggPath, samples, SampleRate);
            AssertOggEndOfStream(File.ReadAllBytes(oggPath), SongSamples);

            // 形式変換の状態が既存再生へ混入しないことを、同じ Song の再合成で検査する。
            ChipExportPlan plan = ChipExportService.Prepare(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.Equal(chip != ChipKind.Snes, plan.CanWrite);
            var after = new SongRenderer(song, settings);
            Assert.Equal(samples.Select(BitConverter.SingleToInt32Bits), after.RenderAll().Select(BitConverter.SingleToInt32Bits));
            Assert.Equal(loadedRenderer.Report.Warnings, after.Report.Warnings);
            Assert.Equal(loadedRenderer.Report.DroppedWarningCount, after.Report.DroppedWarningCount);
            Assert.Equal(originalMidi, File.ReadAllBytes(fixture.SourcePath));
            Assert.Equal(imported.Json, File.ReadAllText(fixture.SongPath));
            Assert.Equal(imported.Json, SongSerializer.Serialize(song));
        }

        /// <summary>NES／GB の保存 JSON から VGM を独立再合成し、NES は NSF CPU トレースも照合する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 1)]
        [InlineData(ChipKind.Nes, 2)]
        [InlineData(ChipKind.GameBoy, 1)]
        [InlineData(ChipKind.GameBoy, 2)]
        public void MidiJsonProducesPlayableVgmAndNsf(ChipKind chip, int loops)
        {
            using var fixture = new MidiPipelineFixture();
            MidiImportResult imported = fixture.Import(chip);
            Song song = SongSerializer.Load(fixture.SongPath);
            ChipExportPlan plan = ChipExportService.Prepare(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = loops });
            Assert.True(plan.CanWrite);
            string path = fixture.PathFor("song.vgm");
            Assert.True(ChipExportService.Write(plan, path, sourcePath: fixture.SongPath));
            Assert.Equal(plan.Report.OutputBytes, new FileInfo(path).Length);
            ParsedVgm parsed = IndependentVgmParser.Parse(File.ReadAllBytes(path));
            Assert.Equal((long)SongSamples * loops, parsed.WaitSamples);
            IRegisterTraceChip registerChip = chip == ChipKind.Nes ? new NesRegisterTraceChip() : new GameBoyRegisterTraceChip();
            var rendered = RegisterTraceRenderer.Render(registerChip, parsed.Writes, checked((int)parsed.WaitSamples + ObservationSamples));
            RegisterTraceAssertions.HasFrequency(rendered.Left.AsSpan(ObservationStart, ObservationSamples), MelodyFrequency);
            Assert.True(RegisterTraceAssertions.AlternatingRootMeanSquare(rendered.Left.AsSpan(0, SongSamples)) > 0);
            Assert.InRange(RegisterTraceAssertions.AlternatingRootMeanSquare(rendered.Left.AsSpan((int)parsed.WaitSamples)), 0, 1e-12);
            if (chip == ChipKind.Nes) { VerifyNsf(fixture, song, loops); }
            Assert.Equal(imported.Json, File.ReadAllText(fixture.SongPath));
            Assert.Equal(imported.Json, SongSerializer.Serialize(song));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>strict・不適合・上限・保存競合・キャンセル・移動失敗で既存出力と JSON を保つ。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, ConversionFormat.Nsf)]
        [InlineData(ChipKind.GameBoy, ConversionFormat.Vgm)]
        [InlineData(ChipKind.Snes, ConversionFormat.Vgm)]
        public void FailedExportsKeepMidiJsonAndDestination(ChipKind chip, ConversionFormat format)
        {
            using var fixture = new MidiPipelineFixture();
            MidiImportResult imported = fixture.Import(chip);
            Song song = SongSerializer.Load(fixture.SongPath);
            byte[] source = File.ReadAllBytes(fixture.SourcePath);
            string path = fixture.PathFor("existing.output");
            File.WriteAllText(path, "keep");
            var strict = ChipExportService.Prepare(song, new ChipExportOptions { Format = format, Strict = true });
            Assert.False(strict.CanWrite);
            Assert.False(ChipExportService.Write(strict, path, overwrite: true, sourcePath: fixture.SongPath));
            var overLimit = ChipExportService.Prepare(song, new ChipExportOptions { Format = format, Loops = 17 });
            Assert.False(ChipExportService.Write(overLimit, path, overwrite: true, sourcePath: fixture.SongPath));
            ChipExportPlan plan = ChipExportService.Prepare(song, new ChipExportOptions { Format = format });
            if (plan.CanWrite)
            {
                Assert.Throws<IOException>(() => ChipExportService.Write(plan, path, sourcePath: fixture.SongPath));
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => ChipExportService.Write(plan, path, overwrite: true,
                    cancellationToken: cancellation.Token, sourcePath: fixture.SongPath));
                string directoryDestination = fixture.PathFor("directory.output");
                Directory.CreateDirectory(directoryDestination);
                File.WriteAllText(Path.Combine(directoryDestination, "keep"), "original");
                Exception? failure = Record.Exception(() => ChipExportService.Write(plan, directoryDestination, overwrite: true, sourcePath: fixture.SongPath));
                Assert.NotNull(failure);
                Assert.True(failure is IOException or UnauthorizedAccessException);
                Assert.Equal("original", File.ReadAllText(Path.Combine(directoryDestination, "keep")));
            }
            Assert.Equal("keep", File.ReadAllText(path));
            Assert.Equal(imported.Json, File.ReadAllText(fixture.SongPath));
            Assert.Equal(imported.Json, SongSerializer.Serialize(song));
            Assert.Equal(source, File.ReadAllBytes(fixture.SourcePath));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        private static void VerifyNsf(MidiPipelineFixture fixture, Song song, int loops)
        {
            var options = new ChipExportOptions { Format = ConversionFormat.Nsf, Loops = loops };
            ChipExportPlan plan = ChipExportService.Prepare(song, options);
            Assert.True(plan.CanWrite);
            string path = fixture.PathFor("song.nsf");
            Assert.True(ChipExportService.Write(plan, path, sourcePath: fixture.SongPath));
            Assert.Equal(plan.Report.OutputBytes, new FileInfo(path).Length);
            IndependentNsfLoader loaded = IndependentNsfLoader.Load(path);
            var processor = new Limited6502(loaded.Memory);
            Assert.InRange(processor.Call(loaded.InitAddress, MaximumInitCycles), 1, plan.Report.Statistics["maximumInitCycles"]);
            ControlTimelineResult controls = ControlTimeline.Create(song, options);
            Assert.NotNull(controls.Timeline);
            NsfFrameTimeline? frames = NsfFrameCompiler.Compile(controls.Timeline, controls.Report);
            Assert.NotNull(frames);
            NsfExecutionFixture.AssertPlayback(processor, loaded.Memory, loaded.PlayAddress, frames, plan.Report.Statistics["maximumPlayCycles"]);
            Assert.InRange(plan.Report.Statistics["maximumPlayCycles"], 1, MaximumPlayCycles);
            processor.Call(loaded.InitAddress, MaximumInitCycles);
            NsfExecutionFixture.AssertPlayback(processor, loaded.Memory, loaded.PlayAddress, frames, plan.Report.Statistics["maximumPlayCycles"]);
        }

        internal static void AssertOggEndOfStream(byte[] bytes, long frames)
        {
            const int HeaderBytes = 27;
            const int SegmentCountOffset = 26;
            const int FlagsOffset = 5;
            const int GranuleOffset = 6;
            const int EndOfStreamFlag = 4;
            const int SignatureBytes = 4;
            int offset = 0;
            bool ended = false;
            long finalGranule = -1;
            while (offset < bytes.Length)
            {
                Assert.False(ended);
                Assert.True(bytes.Length - offset >= HeaderBytes);
                Assert.Equal("OggS", Encoding.ASCII.GetString(bytes, offset, SignatureBytes));
                int segments = bytes[offset + SegmentCountOffset];
                Assert.True(bytes.Length - offset >= HeaderBytes + segments);
                int bodyBytes = 0;
                for (int segment = 0; segment < segments; segment++) { bodyBytes += bytes[offset + HeaderBytes + segment]; }
                ended = (bytes[offset + FlagsOffset] & EndOfStreamFlag) != 0;
                finalGranule = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset + GranuleOffset));
                offset += HeaderBytes + segments + bodyBytes;
            }
            Assert.Equal(bytes.Length, offset);
            Assert.True(ended);
            // OggVorbisEncoder 1.2.2 は最終 granule を入力フレーム数より常に 1 ブロック（1024 サンプル）小さく書く。
            // 曲の長さ・排出方法に依らず一定であることを実測で確認しており、こちらの書き出し漏れではない。
            // 詳細は docs/implementation.md の「既知の制限」を参照する。
            const long VorbisGranuleDeficit = 1024;
            Assert.Equal(frames - VorbisGranuleDeficit, finalGranule);
        }
    }
}
