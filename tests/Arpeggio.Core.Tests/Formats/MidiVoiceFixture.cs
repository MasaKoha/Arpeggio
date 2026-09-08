using System;
using System.Collections.Generic;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>音色生成や PCM に依存せず、収集済み On と確定 gate・音域を割り当てテストへ渡す。</summary>
    internal static class MidiVoiceFixture
    {
        internal static MidiVoiceNote Note(int pitch = 60, int velocity = 127, int channel = 1,
            int startTick = 0, int endTick = 100, MidiPitchRange? sampleRange = null,
            MidiDrumPriority drumPriority = MidiDrumPriority.None, int? outputPitch = null)
        {
            using var track = new MemoryStream();
            WriteDelta(track, startTick);
            track.Write(new[] { (byte)(0x90 + channel - 1), (byte)pitch, (byte)velocity });
            WriteDelta(track, endTick - startTick);
            track.Write(new[] { (byte)(0x80 + channel - 1), (byte)pitch, (byte)0 });
            track.Write(MidiFileFixture.Bytes("00 FF 2F 00"));
            MidiNote source = Assert.Single(MidiNoteCollectorTests.Collect(MidiFileFixture.Report(), track.ToArray()));
            return new MidiVoiceNote(new MidiQuantizedNote(source, startTick, endTick), outputPitch ?? pitch, sampleRange, drumPriority);
        }

        internal static IReadOnlyList<MidiVoiceTrack> Allocate(ChipKind chip, IReadOnlyList<MidiVoiceNote> notes,
            ConversionReport report, MidiPolyphonyMode polyphony = MidiPolyphonyMode.StealOldest,
            IReadOnlyDictionary<int, IReadOnlyList<int>>? channelMap = null)
        {
            IReadOnlyList<MidiVoiceTrack>? tracks = MidiVoiceAllocator.Allocate(notes,
                new MidiImportOptions { Chip = chip, Polyphony = polyphony, ChannelMap = channelMap }, report);
            Assert.NotNull(tracks);
            Assert.Empty(report.Errors);
            return tracks;
        }

        internal static IReadOnlyDictionary<int, IReadOnlyList<int>> Map(int channel, params int[] candidates)
            => new Dictionary<int, IReadOnlyList<int>> { [channel] = candidates };

        private static void WriteDelta(Stream stream, int value)
        {
            const int DataBits = 7;
            const int DataMask = 127;
            const int Continuation = 128;
            Span<byte> bytes = stackalloc byte[4];
            int position = bytes.Length - 1;
            bytes[position] = (byte)(value & DataMask);
            while ((value >>= DataBits) != 0)
            {
                bytes[--position] = (byte)((value & DataMask) | Continuation);
            }
            stream.Write(bytes.Slice(position));
        }
    }
}
