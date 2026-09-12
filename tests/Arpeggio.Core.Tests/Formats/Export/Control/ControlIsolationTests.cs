using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Tests.Analysis;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Control;

namespace Arpeggio.Core.Tests.Formats.Export.Control
{
    /// <summary>制御列の不変性、入力 JSON / PCM の非干渉と依存方向を検証する。</summary>
    public sealed class ControlIsolationTests
    {
        /// <summary>作成後の元ノート・効果・音色・波形・トラック変更から制御列を隔離する。</summary>
        [Fact]
        public void TimelineDoesNotExposeMutableDocumentObjects()
        {
            Song song = SongFactory.Create(ChipKind.GameBoy, lengthTicks: 8);
            var instrument = new GbWaveInstrument { Id = 2, ArpeggioMacro = new Macro { Values = new[] { 0, 7 } } };
            song.Instruments.Add(instrument);
            var note = new Note { DurationTicks = 8, InstrumentId = 2, Effects = new[] { new NoteEffect(NoteEffectKind.Vibrato, 100) } };
            song.Tracks[2].Notes.Add(note);
            ControlTimelineResult result = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.NotNull(result.Timeline);
            ControlTimeline timeline = result.Timeline;
            ControlEvent[] expected = timeline.Events.ToArray();
            ControlEvent onset = Assert.Single(timeline.Events, control => control.Kind == ControlEventKind.NoteOn);
            Assert.NotNull(onset.Note);
            song.Title = "変更後";
            song.Tracks[2].Pan = 1;
            song.Tracks[2].Muted = true;
            note.MidiNote = 72;
            note.Effects[0] = new NoteEffect(NoteEffectKind.Vibrato, 0);
            instrument.Waveform[0] = 15;
            instrument.ArpeggioMacro.Values[0] = 12;
            song.Instruments.Clear();
            song.Tracks.Clear();
            Assert.Equal(expected, timeline.Events);
            Assert.NotEqual(song.Title, timeline.Title);
            Assert.Equal(0, timeline.Tracks[2].Pan);
            Assert.False(timeline.Tracks[2].Muted);
            Assert.Equal(60, onset.Note.MidiNote);
            Assert.Equal(100, onset.Note.Effects[0].Value);
            Assert.Equal(0, timeline.Instruments[2].Waveform[0]);
            Assert.Throws<NotSupportedException>(() => ((IList<ControlEvent>)timeline.Events).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<NoteEffect>)onset.Note.Effects).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<int>)timeline.Instruments[2].Waveform)[0] = 15);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<int, ControlInstrument>)timeline.Instruments).Clear());
        }

        /// <summary>制御列作成または SNES の拒否によって既存 PCM の全ビット・JSON の全バイト・警告が変わらない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void ConversionPreservesExistingPcmJsonAndRenderReport(ChipKind chip)
        {
            Song song = TestSongFactory.CreateActiveSong(chip, lengthTicks: 48);
            byte[] expectedJson = Encoding.UTF8.GetBytes(SongSerializer.Serialize(song));
            var settings = new RenderSettings(SampleRate: 44100, LoopCount: 2, TailSeconds: 0);
            var before = new SongRenderer(song, settings);
            int[] expectedPcmBits = before.RenderAll().Select(BitConverter.SingleToInt32Bits).ToArray();
            RenderWarning[] expectedWarnings = before.Report.Warnings.ToArray();
            ControlTimelineResult result = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = 2 });
            Assert.Equal(chip != ChipKind.Snes, result.Report.CanWrite);
            var after = new SongRenderer(song, settings);
            Assert.Equal(expectedPcmBits, after.RenderAll().Select(BitConverter.SingleToInt32Bits));
            Assert.Equal(expectedJson, Encoding.UTF8.GetBytes(SongSerializer.Serialize(song)));
            Assert.Equal(expectedWarnings, after.Report.Warnings);
            Assert.Equal(before.Report.DroppedWarningCount, after.Report.DroppedWarningCount);
        }

        /// <summary>Formats は Core と BCL だけを参照し、Core からの逆依存を追加しない。</summary>
        [Fact]
        public void AssemblyReferencesPreserveCoreBoundary()
        {
            AssemblyName[] references = typeof(ControlTimeline).Assembly.GetReferencedAssemblies();
            Assert.Contains(references, reference => reference.Name == "Arpeggio.Core");
            Assert.All(references, reference => Assert.True(reference.Name == "Arpeggio.Core" ||
                (reference.Name != null && reference.Name.StartsWith("System", StringComparison.Ordinal))));
            Assert.DoesNotContain(typeof(Song).Assembly.GetReferencedAssemblies(), reference => reference.Name == "Arpeggio.Formats");
        }
    }
}
