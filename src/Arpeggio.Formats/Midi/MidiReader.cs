using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Arpeggio.Formats.Midi
{
    /// <summary>SMF format 0 / 1 を上限付きで解析し、元位置を保つイベント列を作る。</summary>
    public sealed class MidiReader
    {
        private const uint HeaderIdentifier = 0x4D546864;
        private const uint TrackIdentifier = 0x4D54726B;
        private const int HeaderLength = 6;
        private const int HighestHeaderByteShift = 24;
        private const int MiddleHeaderByteShift = 16;
        private const int ByteBits = 8;
        private const int DivisionTimeCodeMask = 0x8000;
        private const int StatusMask = 0x80;
        private const int MessageMask = 0xF0;
        private const int ChannelMask = 0x0F;
        private const int NoteOffStatus = 0x80;
        private const int NoteOnStatus = 0x90;
        private const int PolyphonicPressureStatus = 0xA0;
        private const int ControlChangeStatus = 0xB0;
        private const int PitchBendStatus = 0xE0;
        private const int ProgramStatus = 0xC0;
        private const int ChannelPressureStatus = 0xD0;
        private const int FirstChannelStatus = 0x80;
        private const int LastChannelStatus = 0xEF;
        private const int SystemExclusiveStatus = 0xF0;
        private const int SystemEscapeStatus = 0xF7;
        private const int MetaStatus = 0xFF;
        private const int SequenceNumberMeta = 0x00;
        private const int TrackNameMeta = 0x03;
        private const int ChannelPrefixMeta = 0x20;
        private const int PortMeta = 0x21;
        private const int EndOfTrackMeta = 0x2F;
        private const int TempoMeta = 0x51;
        private const int TimeCodeMeta = 0x54;
        private const int TimeSignatureMeta = 0x58;
        private const int KeySignatureMeta = 0x59;
        private const int TempoLength = 3;
        private const int SequenceNumberLength = 2;
        private const int SingleByteMetaLength = 1;
        private const int TimeCodeLength = 5;
        private const int TimeSignatureLength = 4;
        private const int KeySignatureLength = 2;
        private readonly MidiBinaryInput _input;
        private readonly ConversionReport _report;
        private readonly List<MidiEvent> _events = new List<MidiEvent>();
        private int _sourceTrack;
        private long _sourceEvent;
        private long _tick;
        private long _eventCount;
        private long _noteOnCount;
        private long _endTick;
        private string? _trackName;
        private bool _isReadingTrack;

        private MidiReader(Stream stream, ConversionReport report)
        {
            _input = new MidiBinaryInput(stream);
            _report = report;
        }

        /// <summary>現在位置から EOF まで読む。Stream は閉じず、I/O 例外は伝播する。入力エラー時は null、strict 警告時も後続診断用の中間列は返す。</summary>
        public static MidiFile? Read(Stream stream, ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(report);
            if (!stream.CanRead)
            {
                throw new ArgumentException("読み取り可能な Stream が必要です。", nameof(stream));
            }
            if (report.Format != ConversionFormat.Midi)
            {
                throw new ArgumentException("MIDI のレポートが必要です。", nameof(report));
            }
            if (report.ErrorCount != 0)
            {
                return null;
            }
            var reader = new MidiReader(stream, report);
            try
            {
                return reader.ReadFile();
            }
            catch (MidiReadException exception)
            {
                report.AddError(reader.CreateError(exception.Code, exception.Message, exception.EventSource));
                return null;
            }
            catch (OverflowException)
            {
                report.AddError(reader.CreateError("MidiTimeOverflow", "MIDI の tick または実時間積算が整数範囲を超えています。"));
                return null;
            }
        }

        private MidiFile ReadFile()
        {
            if (_input.ReadUInt32() != HeaderIdentifier)
            {
                throw new MidiReadException("UnsupportedMidiFormat", "先頭は SMF の MThd でなければなりません。");
            }
            uint headerLength = _input.ReadUInt32();
            if (headerLength < HeaderLength)
            {
                throw new MidiReadException("InvalidMidi", "MThd の長さは 6 byte 以上が必要です。");
            }
            _input.BeginChunk(headerLength);
            int format = _input.ReadUInt16();
            int trackCount = _input.ReadUInt16();
            int division = _input.ReadUInt16();
            ValidateHeader(format, trackCount, division);
            _input.Skip(_input.Remaining);
            _input.EndChunk();
            ReadChunks(trackCount);
            _events.Sort(CompareEvents);
            MidiDurationValidator.Validate(_events, division, _report);
            _report.SetStatistic("midiInputBytes", _input.BytesRead);
            _report.SetStatistic("midiEvents", _eventCount);
            _report.SetStatistic("midiNoteOns", _noteOnCount);
            return new MidiFile(format, trackCount, division, _endTick, _trackName, _events);
        }

        private static void ValidateHeader(int format, int trackCount, int division)
        {
            if (format != 0 && format != 1)
            {
                throw new MidiReadException("UnsupportedMidiFormat", "SMF format 0 / 1 だけに対応しています。");
            }
            if (trackCount < 1 || trackCount > ConversionLimits.MaximumMidiTracks || (format == 0 && trackCount != 1))
            {
                throw new MidiReadException("InvalidMidi", "SMF format と MTrk 数が不正です。");
            }
            if (division == 0 || (division & DivisionTimeCodeMask) != 0)
            {
                throw new MidiReadException("UnsupportedMidiDivision", "division は PPQN 1〜32767 が必要です。");
            }
        }

        private void ReadChunks(int trackCount)
        {
            int tracksRead = 0;
            int firstByte;
            while ((firstByte = _input.ReadOptionalByte()) >= 0)
            {
                uint identifier = ((uint)firstByte << HighestHeaderByteShift) |
                    ((uint)_input.ReadByte() << MiddleHeaderByteShift) | (uint)_input.ReadUInt16();
                uint length = _input.ReadUInt32();
                _input.BeginChunk(length);
                if (identifier == HeaderIdentifier)
                {
                    throw new MidiReadException("InvalidMidi", "重複 MThd は許可されません。");
                }
                if (identifier == TrackIdentifier)
                {
                    if (tracksRead == trackCount)
                    {
                        throw new MidiReadException("InvalidMidi", "MTrk 数がヘッダーの宣言を超えています。");
                    }
                    ReadTrack(tracksRead++);
                    _isReadingTrack = false;
                }
                else
                {
                    _input.Skip(_input.Remaining);
                    _report.AddWarning(new ConversionDiagnostic("UnknownMidiChunkIgnored", "未知の SMF チャンクを読み飛ばしました。"));
                }
                _input.EndChunk();
            }
            if (tracksRead != trackCount)
            {
                throw new MidiReadException("InvalidMidi", "宣言された MTrk が不足しています。");
            }
        }

        private void ReadTrack(int trackIndex)
        {
            _isReadingTrack = true;
            _sourceTrack = trackIndex;
            _sourceEvent = 0;
            _tick = 0;
            int runningStatus = 0;
            while (_input.Remaining > 0)
            {
                if (_eventCount == ConversionLimits.MaximumMidiEvents)
                {
                    throw new MidiReadException("MidiEventLimitExceeded", "MIDI イベント数の上限を超えています。");
                }
                _tick = checked(_tick + _input.ReadVariableLength());
                int firstByte = _input.ReadByte();
                bool ended = ReadMessage(firstByte, ref runningStatus);
                _eventCount++;
                _sourceEvent++;
                if (ended)
                {
                    _endTick = Math.Max(_endTick, _tick);
                    return;
                }
            }
            throw new MidiReadException("InvalidMidi", "各 MTrk には End Of Track が必要です。");
        }

        private bool ReadMessage(int firstByte, ref int runningStatus)
        {
            int status = firstByte;
            int? firstData = null;
            if (firstByte < StatusMask)
            {
                if (runningStatus == 0)
                {
                    throw new MidiReadException("InvalidMidi", "有効な running status がありません。");
                }
                status = runningStatus;
                firstData = firstByte;
            }
            if (status >= FirstChannelStatus && status <= LastChannelStatus)
            {
                runningStatus = status;
                ReadChannelMessage(status, firstData);
                return false;
            }
            runningStatus = 0;
            if (status == MetaStatus)
            {
                return ReadMeta();
            }
            if (status == SystemExclusiveStatus || status == SystemEscapeStatus)
            {
                _input.Skip(_input.ReadVariableLength());
                Warn("SysExIgnored", "SysEx を読み飛ばしました。機器への送信や reset の解釈は行いません。");
                return false;
            }
            throw new MidiReadException("UnsupportedMidiStatus", "SMF で対象外の system status です。");
        }

        private void ReadChannelMessage(int status, int? firstData)
        {
            int command = status & MessageMask;
            int dataOne = firstData ?? _input.ReadDataByte();
            int dataTwo = command == ProgramStatus || command == ChannelPressureStatus ? 0 : _input.ReadDataByte();
            if (command == NoteOnStatus && dataTwo > 0)
            {
                if (_noteOnCount == ConversionLimits.MaximumMidiNoteOns)
                {
                    throw new MidiReadException("SourceNoteLimitExceeded", "MIDI NoteOn 数の上限を超えています。");
                }
                _noteOnCount++;
            }
            MidiMessageKind kind = command switch
            {
                NoteOffStatus => MidiMessageKind.NoteOff,
                NoteOnStatus => MidiMessageKind.NoteOn,
                PolyphonicPressureStatus => MidiMessageKind.PolyphonicPressure,
                ControlChangeStatus => MidiMessageKind.ControlChange,
                ProgramStatus => MidiMessageKind.ProgramChange,
                ChannelPressureStatus => MidiMessageKind.ChannelPressure,
                PitchBendStatus => MidiMessageKind.PitchBend,
                _ => throw new MidiReadException("InvalidMidi", "channel status が不正です。")
            };
            _events.Add(new MidiEvent(_tick, _sourceTrack, _sourceEvent, kind, (status & ChannelMask) + 1, dataOne, dataTwo));
        }

        private bool ReadMeta()
        {
            int kind = _input.ReadDataByte();
            int length = _input.ReadVariableLength();
            _input.Require(length);
            ValidateMetaLength(kind, length);
            if (kind == EndOfTrackMeta)
            {
                if (_input.Remaining != 0)
                {
                    throw new MidiReadException("InvalidMidi", "EOT 後にチャンク内の byte が残っています。");
                }
                _events.Add(CurrentEvent(MidiMessageKind.EndOfTrack));
                return true;
            }
            if (kind == TempoMeta)
            {
                int tempo = (_input.ReadUInt16() << ByteBits) | _input.ReadByte();
                if (tempo == 0)
                {
                    throw new MidiReadException("InvalidMidi", "Tempo は正の値が必要です。");
                }
                _events.Add(CurrentEvent(MidiMessageKind.Tempo, tempo));
                return false;
            }
            ReadOtherMeta(kind, length);
            return false;
        }

        private static void ValidateMetaLength(int kind, int length)
        {
            int fixedLength = kind switch
            {
                SequenceNumberMeta => SequenceNumberLength,
                ChannelPrefixMeta or PortMeta => SingleByteMetaLength,
                EndOfTrackMeta => 0,
                TempoMeta => TempoLength,
                TimeCodeMeta => TimeCodeLength,
                TimeSignatureMeta => TimeSignatureLength,
                KeySignatureMeta => KeySignatureLength,
                _ => -1
            };
            if (fixedLength >= 0 && length != fixedLength)
            {
                throw new MidiReadException("InvalidMidi", "固定長 meta の payload 長が不正です。");
            }
        }

        private void ReadOtherMeta(int kind, int length)
        {
            if (kind == PortMeta)
            {
                if (_input.ReadByte() != 0)
                {
                    throw new MidiReadException("UnsupportedMidiPort", "MIDI port 0 以外には対応していません。");
                }
                return;
            }
            if (kind == TrackNameMeta && _sourceTrack == 0 && _trackName is null && length > 0)
            {
                byte[] bytes = _input.ReadBytes(length);
                try
                {
                    _trackName = new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    _trackName = Encoding.Latin1.GetString(bytes);
                    Warn("MidiTextDecoded", "Track Name を UTF-8 で復号できないため Latin-1 を使用しました。");
                }
                return;
            }
            _input.Skip(length);
            Warn("MidiMetadataIgnored", "取り込み先に保存しない meta を読み飛ばしました。");
        }

        private ConversionDiagnostic CreateError(string code, string message, MidiEvent? source = null)
        {
            if (source is MidiEvent current)
            {
                return current.Diagnose(code, message);
            }
            return _isReadingTrack ? CurrentEvent(MidiMessageKind.None).Diagnose(code, message) : new ConversionDiagnostic(code, message);
        }

        private MidiEvent CurrentEvent(MidiMessageKind kind, int dataOne = 0) =>
            new MidiEvent(_tick, _sourceTrack, _sourceEvent, kind, dataOne: dataOne);

        private void Warn(string code, string message) => _report.AddWarning(CurrentEvent(MidiMessageKind.None).Diagnose(code, message));

        private static int CompareEvents(MidiEvent left, MidiEvent right)
        {
            int comparison = left.Tick.CompareTo(right.Tick);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.SourceTrack.CompareTo(right.SourceTrack);
            return comparison != 0 ? comparison : left.SourceEvent.CompareTo(right.SourceEvent);
        }
    }
}
