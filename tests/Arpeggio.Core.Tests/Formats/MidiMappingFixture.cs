using System.Collections.Generic;
using System.IO;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>E5 の分類から採用音色までを、Importer に依存せず接続する。</summary>
    internal static class MidiMappingFixture
    {
        internal static (IReadOnlyList<MidiVoiceTrack> Tracks, MidiInstrumentMap Instruments, ConversionReport Report)
            Map(byte[] bytes, MidiImportOptions options)
        {
            var report = new ConversionReport(ConversionFormat.Midi, options.Chip, options.Strict);
            using var stream = new MemoryStream(bytes);
            MidiFile file = MidiReader.Read(stream, report)!;
            Assert.NotNull(file);
            IReadOnlyList<MidiNote> notes = MidiNoteCollector.Collect(file, report)!;
            MidiTempoMap tempo = MidiTempoMap.Create(file, options, report)!;
            Assert.NotNull(tempo);
            MidiTickQuantizer quantizer = MidiTickQuantizer.Create(tempo, options, report)!;
            Assert.NotNull(quantizer);
            IReadOnlyList<MidiVoiceNote> mapped = MidiInstrumentMapper.Map(file, notes, tempo, quantizer, report)!;
            Assert.NotNull(mapped);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceAllocator.Allocate(mapped, options, report)!;
            Assert.NotNull(tracks);
            MidiInstrumentMap instruments = MidiInstrumentMapper.CreateInstruments(tracks, report)!;
            Assert.NotNull(instruments);
            Assert.Empty(report.Errors);
            return (tracks, instruments, report);
        }
    }
}
