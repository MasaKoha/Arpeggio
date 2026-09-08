using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>SMF から version 1 の有効なドキュメントへの全段接続を検証する。</summary>
    public sealed class MidiImporterTests
    {
        /// <summary>三チップ・両 SMF 形式で全固定トラックと正規 JSON の往復を保持する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 0, 5)]
        [InlineData(ChipKind.Nes, 1, 5)]
        [InlineData(ChipKind.GameBoy, 0, 4)]
        [InlineData(ChipKind.GameBoy, 1, 4)]
        [InlineData(ChipKind.Snes, 0, 8)]
        [InlineData(ChipKind.Snes, 1, 8)]
        public void ImportCreatesValidCanonicalSong(ChipKind chip, int format, int trackCount)
        {
            byte[] bytes = MidiFileFixture.Create(format, 480, MidiFileFixture.Bytes(
                "00 C0 28 00 90 3C 7F 00 90 40 7F 00 90 43 7F 00 99 24 7F 01 89 24 00 83 5F B0 78 00 00 FF 2F 00"));
            byte[] original = bytes.ToArray();
            using var stream = new MemoryStream(bytes);
            MidiImportResult result = MidiImporter.Import(stream, new MidiImportOptions { Chip = chip, Title = "取り込み 🎵" });
            Assert.True(result.CanWrite);
            Assert.NotNull(result.Song);
            Assert.NotNull(result.Json);
            Song song = result.Song;
            SongValidator.Validate(song);
            Assert.Equal(result.Json, SongSerializer.Serialize(SongSerializer.Deserialize(result.Json)));
            Assert.Equal(result.Json, SongSerializer.Serialize(song));
            Assert.Equal(Encoding.UTF8.GetByteCount(result.Json), result.Report.OutputBytes);
            Assert.Equal(1, song.Version);
            Assert.Equal(48, song.TicksPerBeat);
            Assert.Equal(120, song.TempoBpm);
            Assert.Equal(48, song.LengthTicks);
            Assert.Equal("取り込み 🎵", song.Title);
            Assert.Equal(0, song.LoopStartTick);
            Assert.Equal(0, song.SnesEcho.DelayMilliseconds);
            Assert.Equal(trackCount, song.Tracks.Count);
            Assert.Equal(4, result.Report.Statistics["acceptedMidiNotes"]);
            Assert.Equal(0.5, result.Report.DurationSeconds);
            Assert.NotEmpty(result.Report.Limitations);
            Assert.Empty(result.Report.Errors);
            Assert.Equal(original, bytes);
            Assert.True(stream.CanRead);
            Assert.All(song.Tracks, track => VerifyTrack(track, song));
            if (chip == ChipKind.Nes)
            {
                Assert.Empty(song.Tracks[4].Notes);
                Assert.DoesNotContain(song.Instruments, instrument => instrument is NesDpcmInstrument);
                Assert.Equal(15, Assert.Single(song.Tracks[2].Notes).Volume);
            }
        }

        /// <summary>先頭無音と tempo 変更を跨ぐ gate を 0/48/144 の絶対時間で配置する。</summary>
        [Fact]
        public void ImportBakesTempoMapAndPreservesLeadingSilence()
        {
            byte[] bytes = MidiFileFixture.Create(
                MidiFileFixture.Bytes("00 FF 51 03 07 A1 20 83 60 FF 51 03 0F 42 40 83 60 FF 2F 00"),
                MidiFileFixture.Bytes("81 70 90 3C 7F 85 50 80 3C 00 00 FF 2F 00"));
            MidiImportResult result = Import(bytes);
            Assert.True(result.CanWrite);
            Note note = Assert.Single(result.Song!.Tracks[0].Notes);
            Assert.Equal(24, note.Tick);
            Assert.Equal(120, note.DurationTicks);
            Assert.Equal(144, result.Song.LengthTicks);
            Assert.Equal(1.5, result.Report.DurationSeconds);
            Assert.Single(result.Report.Warnings, warning => warning.Code == "TempoMapFlattened");
        }

        /// <summary>明示曲名、track 0 名、入力ファイル名の順でタイトルを選ぶ。</summary>
        [Theory]
        [InlineData("明示", "00 FF 03 04 54 65 73 74", "folder/source.mid", "明示")]
        [InlineData(null, "00 FF 03 04 54 65 73 74", "folder/source.mid", "Test")]
        [InlineData(null, "00 FF 03 00", "folder/source.mid", "source")]
        [InlineData(null, "00 FF 03 01 E9", "source.mid", "é")]
        [InlineData("", "00 FF 03 04 54 65 73 74", "source.mid", "")]
        public void TitleUsesSpecifiedPrecedence(string? title, string metadata, string sourceName, string expected)
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes(metadata + " 00 90 3C 7F 83 60 80 3C 00 00 FF 2F 00"));
            MidiImportResult result = Import(bytes, new MidiImportOptions { Chip = ChipKind.Nes, Title = title, SourceName = sourceName });
            Assert.True(result.CanWrite);
            Assert.Equal(expected, result.Song!.Title);
        }

        /// <summary>strict でも全段を実行し、警告・候補・予定サイズを返して保存を拒否する。</summary>
        [Fact]
        public void StrictCompletesDiagnosticsAndCandidateBeforeRejectingSave()
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes(
                "00 E0 00 50 00 C0 08 00 90 00 7F 00 99 00 7F 01 89 00 00 01 80 00 00 00 FF 2F 00"));
            MidiImportResult result = Import(bytes, new MidiImportOptions { Chip = ChipKind.Nes, Strict = true });
            Assert.False(result.CanWrite);
            Assert.NotNull(result.Song);
            Assert.NotNull(result.Json);
            Assert.True(result.Report.OutputBytes > 0);
            Assert.Empty(result.Report.Errors);
            foreach (string code in new[] { "MidiExpressionIgnored", "ProgramApproximated", "MidiPitchClamped", "UnknownDrumMapped", "DrumGateReplaced", "DrumApproximated" })
            {
                Assert.Contains(result.Report.Warnings, warning => warning.Code == code);
            }
            SongValidator.Validate(result.Song);
        }

        /// <summary>strict は保持上限後の警告を含む全数で判定し、最後の音色生成まで行う。</summary>
        [Fact]
        public void FullReportCountsWarningsBeyondDetailLimit()
        {
            var events = new List<byte>();
            for (int eventIndex = 0; eventIndex < 4100; eventIndex++)
            {
                events.AddRange(new byte[] { 0, 0xE0, 0, 0x50 });
            }
            events.AddRange(MidiFileFixture.Bytes("00 90 3C 7F 83 60 80 3C 00 00 FF 2F 00"));
            MidiImportResult result = Import(MidiFileFixture.Create(events.ToArray()), new MidiImportOptions { Chip = ChipKind.Nes, Strict = true });
            Assert.False(result.CanWrite);
            Assert.NotNull(result.Json);
            Assert.Equal(4096, result.Report.Warnings.Count);
            Assert.Equal(4101, result.Report.WarningCount);
            Assert.Equal(5, result.Report.DroppedWarningCount);
            Assert.Equal(1, result.Report.WarningCountsByCode["ProgramApproximated"]);
            Assert.Equal(1, result.Report.Statistics["generatedInstruments"]);
        }

        /// <summary>方式制限だけの SNES 打楽器は strict で保存可能。</summary>
        [Fact]
        public void StrictAcceptsLimitationsWithoutConversionWarnings()
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes("00 99 24 7F 82 20 89 24 00 00 FF 2F 00"));
            MidiImportResult result = Import(bytes, new MidiImportOptions { Chip = ChipKind.Snes, Tempo = 100, Strict = true });
            Assert.True(result.CanWrite);
            Assert.Empty(result.Report.Warnings);
            Assert.NotEmpty(result.Report.Limitations);
            Assert.Equal(24, Assert.Single(result.Song!.Tracks[6].Notes).DurationTicks);
        }

        /// <summary>破損・全音消失・設定不正時に部分 Song を返さない。</summary>
        [Theory]
        [InlineData("00 FF 2F 00", "NoPlayableNotes")]
        [InlineData("00 B0 07 00 00 90 3C 7F 83 60 80 3C 00 00 FF 2F 00", "NoPlayableNotes")]
        [InlineData("00 90 3C 7F", "InvalidMidi")]
        public void InvalidOrSilentInputHasNoCandidate(string track, string expectedCode)
        {
            MidiImportResult result = Import(MidiFileFixture.Create(MidiFileFixture.Bytes(track)));
            Assert.False(result.CanWrite);
            Assert.Null(result.Song);
            Assert.Null(result.Json);
            Assert.Contains(result.Report.Errors, error => error.Code == expectedCode);
        }

        /// <summary>全チャンネル除外を含む不正設定・map を保存前に拒否する。</summary>
        [Fact]
        public void InvalidOptionsAndExclusionAreReported()
        {
            byte[] bytes = SimpleMidi();
            MidiImportOptions[] options =
            {
                new MidiImportOptions(),
                new MidiImportOptions { Chip = ChipKind.Nes, Tempo = 0 },
                new MidiImportOptions { Chip = ChipKind.Nes, QuantizeTicks = 5 },
                new MidiImportOptions { Chip = ChipKind.Nes, Polyphony = MidiPolyphonyMode.None },
                new MidiImportOptions { Chip = ChipKind.Nes, ChannelMap = MidiVoiceFixture.Map(1, 4) },
                new MidiImportOptions { Chip = ChipKind.Nes, ChannelMap = MidiVoiceFixture.Map(1) },
                new MidiImportOptions { Chip = ChipKind.Nes, Title = "bad\0title" },
                new MidiImportOptions { Chip = ChipKind.Nes, Title = new string('a', 1025) },
                new MidiImportOptions { Chip = ChipKind.Nes, SourceName = null! }
            };
            foreach (MidiImportOptions option in options)
            {
                MidiImportResult result = Import(bytes, option);
                Assert.False(result.CanWrite);
                Assert.Null(result.Song);
                Assert.Null(result.Json);
                Assert.NotEmpty(result.Report.Errors);
            }
        }

        /// <summary>入力終端直前の固定打撃が 1800 秒を越える場合は切り詰めず拒否する。</summary>
        [Fact]
        public void DrumExtensionBeyondDurationLimitIsRejected()
        {
            byte[] bytes = MidiFileFixture.Create(0, 1,
                MidiFileFixture.Bytes("9C 0F 99 31 7F 01 FF 2F 00"));
            MidiImportResult result = Import(bytes);
            Assert.False(result.CanWrite);
            Assert.Null(result.Song);
            Assert.Contains(result.Report.Errors, error => error.Code == "DurationLimitExceeded");
        }

        /// <summary>入力は 1800 秒以内でも短音の 1 グリッド延長が上限を越えれば拒否する。</summary>
        [Fact]
        public void ShortNoteExtensionBeyondFinalSongLimitIsRejected()
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes("E9 BB 7F 90 3C 7F 01 80 3C 00 00 FF 2F 00"));
            MidiImportResult result = Import(bytes);
            Assert.False(result.CanWrite);
            Assert.Null(result.Song);
            Assert.Null(result.Json);
            Assert.Single(result.Report.Warnings, warning => warning.Code == "ShortNoteExtended");
            Assert.Contains(result.Report.Errors, error => error.Code == "DurationLimitExceeded");
        }

        /// <summary>入力 byte・map の配列・前の結果を変更せず、繰り返し変換が決定的である。</summary>
        [Fact]
        public void RepeatedImportsDoNotMutateInputsOrShareCandidates()
        {
            byte[] bytes = SimpleMidi();
            byte[] original = bytes.ToArray();
            int[] candidates = { 1, 0 };
            var options = new MidiImportOptions { Chip = ChipKind.GameBoy, ChannelMap = new Dictionary<int, IReadOnlyList<int>> { [1] = candidates } };
            MidiImportResult first = Import(bytes, options);
            MidiImportResult second = Import(bytes, options);
            Assert.Equal(first.Json, second.Json);
            Assert.Equal(new[] { 1, 0 }, candidates);
            Assert.Equal(original, bytes);
            first.Song!.Tracks[0].Notes.Clear();
            first.Song.Instruments[0].Name = "編集";
            Assert.Single(second.Song!.Tracks[0].Notes);
            Assert.NotEqual("編集", second.Song.Instruments[0].Name);
            Assert.Equal(second.Json, first.Json);
        }

        /// <summary>シーク不能入力を読め、成功・I/O 失敗のいずれでも所有元の Stream を閉じない。</summary>
        [Fact]
        public void ImportLeavesNonSeekableInputOpenAndPropagatesIoFailure()
        {
            using var stream = new MidiFragmentedStream(SimpleMidi());
            Assert.True(MidiImporter.Import(stream, new MidiImportOptions { Chip = ChipKind.Nes }).CanWrite);
            Assert.True(stream.CanRead);
            using var failing = new MidiFragmentedStream(SimpleMidi(), 20);
            Assert.Throws<IOException>(() => MidiImporter.Import(failing, new MidiImportOptions { Chip = ChipKind.Nes }));
            Assert.True(failing.CanRead);
        }

        internal static byte[] SimpleMidi() => MidiFileFixture.Create(MidiFileFixture.Bytes("00 90 3C 7F 83 60 80 3C 00 00 FF 2F 00"));

        internal static MidiImportResult Import(byte[] bytes, MidiImportOptions? options = null)
        {
            using var stream = new MemoryStream(bytes);
            return MidiImporter.Import(stream, options ?? new MidiImportOptions { Chip = ChipKind.Nes });
        }

        private static void VerifyTrack(Track track, Song song)
        {
            Assert.False(track.Muted);
            Assert.Equal(0, track.Pan);
            Assert.Null(track.DefaultInstrumentId);
            int previousEnd = 0;
            foreach (Note note in track.Notes)
            {
                Assert.True(note.Tick >= previousEnd);
                Assert.True(note.DurationTicks > 0);
                previousEnd = note.Tick + note.DurationTicks;
                Assert.InRange(previousEnd, 1, song.LengthTicks);
                Instrument instrument = Assert.Single(song.Instruments, candidate => candidate.Id == note.InstrumentId);
                ChannelKind channel = instrument switch
                {
                    NesPulseInstrument or GbPulseInstrument => ChannelKind.Pulse,
                    NesTriangleInstrument => ChannelKind.Triangle,
                    GbWaveInstrument => ChannelKind.Wave,
                    NesNoiseInstrument or GbNoiseInstrument => ChannelKind.Noise,
                    SnesSampleInstrument => ChannelKind.Sample,
                    _ => throw new InvalidOperationException("取り込み対象外の音色です。")
                };
                Assert.Equal(track.Channel, channel);
            }
        }
    }
}
