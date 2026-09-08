using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>書き出し前の固定資源上限と設定の境界を検証する。</summary>
    public sealed class ConversionLimitsTests
    {
        /// <summary>共通オプションの既定値は有限一周と最小 MIDI グリッドを維持する。</summary>
        [Fact]
        public void OptionsHaveSpecifiedDefaults()
        {
            var export = new ChipExportOptions();
            Assert.Equal(ConversionFormat.None, export.Format);
            Assert.Equal(1, export.Loops);
            Assert.Equal(string.Empty, export.Author);
            Assert.Equal(string.Empty, export.Copyright);
            Assert.False(export.Strict);
            var import = new MidiImportOptions();
            Assert.Equal(ChipKind.None, import.Chip);
            Assert.Null(import.Tempo);
            Assert.Equal(1, import.QuantizeTicks);
            Assert.Equal(MidiPolyphonyMode.StealOldest, import.Polyphony);
            Assert.Null(import.ChannelMap);
            Assert.Null(import.Title);
            Assert.Equal("midi", import.SourceName);
            Assert.False(import.Strict);
        }

        /// <summary>有限回数の両端だけを許可する。</summary>
        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(16, true)]
        [InlineData(17, false)]
        public void LoopLimitsAreInclusive(int loops, bool accepted)
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 48);
            ConversionReport report = Validate(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = loops });
            Assert.Equal(accepted, report.CanWrite);
        }

        /// <summary>展開後 1800 秒を許可し、丸め前に僅かでも超過すれば拒否する。</summary>
        [Theory]
        [InlineData(86400, 1, true)]
        [InlineData(86401, 1, false)]
        [InlineData(43200, 2, true)]
        [InlineData(43201, 2, false)]
        public void DurationLimitUsesExpandedDuration(int lengthTicks, int loops, bool accepted)
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 60, lengthTicks: lengthTicks);
            ConversionReport report = Validate(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = loops });
            Assert.Equal(accepted, report.CanWrite);
            if (accepted)
            {
                Assert.Equal(1800.0, report.DurationSeconds);
            }
        }

        /// <summary>ミュートも含めて展開前の元ノート上限を検査する。</summary>
        [Fact]
        public void SourceNoteLimitIncludesMutedNotes()
        {
            const int MaximumNotes = 250000;
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 1000, lengthTicks: MaximumNotes + 1);
            song.Tracks[0].Muted = true;
            for (int tick = 0; tick < MaximumNotes; tick++)
            {
                song.Tracks[0].Notes.Add(new Note { Tick = tick, DurationTicks = 1 });
            }
            var options = new ChipExportOptions { Format = ConversionFormat.Vgm };
            Assert.True(Validate(song, options).CanWrite);
            song.Tracks[0].Notes.Add(new Note { Tick = MaximumNotes, DurationTicks = 1 });
            ConversionReport report = Validate(song, options);
            Assert.False(report.CanWrite);
            Assert.Contains(report.Errors, diagnostic => diagnostic.Code == "SourceNoteLimitExceeded");
        }

        /// <summary>VGM のレジスタ数とファイルサイズの上限を独立に検査する。</summary>
        [Theory]
        [InlineData(4000000, 67108864, true)]
        [InlineData(4000001, 67108864, false)]
        [InlineData(4000000, 67108865, false)]
        public void VgmSizeIsRejectedBeforeAnyWriter(long registerWrites, long outputBytes, bool accepted)
        {
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes);
            ConversionLimits.ValidateExportSize(report, registerWrites, outputBytes);
            Assert.Equal(accepted, report.CanWrite);
            Assert.Equal(outputBytes, report.OutputBytes);
        }

        /// <summary>NSF はヘッダーと ROM、およびプレイヤーを除いた曲データを別に検査する。</summary>
        [Theory]
        [InlineData(1048704, 1044480, true)]
        [InlineData(1048705, 1044480, false)]
        [InlineData(1048704, 1044481, false)]
        public void NsfSizeIncludesSeparateHeaderAndPlayer(long outputBytes, long dataBytes, bool accepted)
        {
            var report = new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes);
            ConversionLimits.ValidateExportSize(report, 0, outputBytes, dataBytes);
            Assert.Equal(accepted, report.CanWrite);
        }

        /// <summary>MIDI の全入力資源は上限ちょうどを許可し、超過をエラーにする。</summary>
        [Fact]
        public void MidiResourceLimitsAreInclusive()
        {
            var report = new ConversionReport(ConversionFormat.Midi, ChipKind.Snes);
            ConversionLimits.ValidateMidiSize(report, 33554432, 1000000, 250000, 1800);
            Assert.True(report.CanWrite);
            ConversionLimits.ValidateMidiSize(report, 33554433, 1000001, 250001, 1800.001);
            Assert.Equal(4, report.ErrorCount);
            Assert.False(report.CanWrite);
        }

        /// <summary>メタデータは UTF-16 単位で制限し、NUL を拒否する。</summary>
        [Fact]
        public void MetadataUsesUtf16LengthAndRejectsNul()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Title = new string('曲', 1024);
            var options = new ChipExportOptions { Format = ConversionFormat.Nsf, Author = new string('著', 1024), Copyright = "2026" };
            Assert.True(Validate(song, options).CanWrite);
            song.Title += "曲";
            Assert.False(Validate(song, options).CanWrite);
            song.Title = "曲\0名";
            Assert.False(Validate(song, options).CanWrite);
            song.Title = "曲";
            Assert.False(Validate(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Copyright = "2026" }).CanWrite);
            Assert.False(Validate(song, new ChipExportOptions { Format = ConversionFormat.Nsf, Author = "著\0者" }).CanWrite);
        }

        /// <summary>形式とチップの対応を先に検証する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, ConversionFormat.Nsf, true)]
        [InlineData(ChipKind.Nes, ConversionFormat.Vgm, true)]
        [InlineData(ChipKind.GameBoy, ConversionFormat.Vgm, true)]
        [InlineData(ChipKind.GameBoy, ConversionFormat.Nsf, false)]
        [InlineData(ChipKind.Snes, ConversionFormat.Vgm, false)]
        [InlineData(ChipKind.Nes, ConversionFormat.None, false)]
        public void FormatAndChipMustMatch(ChipKind chip, ConversionFormat format, bool accepted)
        {
            Assert.Equal(accepted, Validate(SongFactory.Create(chip), new ChipExportOptions { Format = format }).CanWrite);
        }

        private static ConversionReport Validate(Song song, ChipExportOptions options)
        {
            SongValidator.Validate(song);
            var report = new ConversionReport(options.Format, song.Chip, options.Strict);
            ConversionLimits.ValidateExportInput(song, options, report);
            return report;
        }
    }
}
