using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Import;

namespace Arpeggio.Core.Session
{
    /// <summary>音色参照の整合性を保つセッション編集操作。</summary>
    public sealed class InstrumentEditor
    {
        private readonly EditSession _session;

        /// <summary>編集対象のセッションを指定する。</summary>
        public InstrumentEditor(EditSession session)
        {
            _session = session;
        }

        /// <summary>指定 ID の音色を追加する。重複 ID は拒否する。</summary>
        public void Add(Instrument instrument)
        {
            _session.Change(song => song.Instruments.Add(instrument));
        }

        /// <summary>同じ ID の音色を置き換え、既存ノートとの適合性を検証する。</summary>
        public void Update(Instrument instrument)
        {
            if (instrument is null)
            {
                throw new SongValidationException("instrument は null にできません。");
            }
            _session.Change(song =>
            {
                int index = GetInstrumentIndex(song, instrument.Id);
                song.Instruments[index] = instrument;
            });
        }

        /// <summary>既存 SNES 音色の複製へ WAV を取り込み、一回の履歴として保存する。</summary>
        public void ImportWavSample(int instrumentId, string wavPath, int rootMidiNote = 60,
            int? loopStart = null, int? loopEnd = null, bool loop = true)
        {
            Song song = _session.GetSong();
            Instrument existing = song.Instruments[GetInstrumentIndex(song, instrumentId)];
            if (!(existing is SnesSampleInstrument))
            {
                throw new ArgumentException("WAV を取り込めるのは SNES 音色だけです。", nameof(instrumentId));
            }
            SnesSampleInstrument replacement = (SnesSampleInstrument)InstrumentJson.Deserialize(InstrumentJson.Serialize(existing));
            WavSampleImporter.Import(replacement, wavPath, rootMidiNote, loopStart, loopEnd, loop);
            Update(replacement);
        }

        /// <summary>参照されていない音色を削除する。</summary>
        public void Remove(int instrumentId)
        {
            _session.Change(song =>
            {
                int index = GetInstrumentIndex(song, instrumentId);
                EnsureUnreferenced(song, instrumentId);
                song.Instruments.RemoveAt(index);
            });
        }

        private static int GetInstrumentIndex(Song song, int instrumentId)
        {
            int index = song.Instruments.FindIndex(instrument => instrument.Id == instrumentId);
            if (index < 0)
            {
                throw new ArgumentException($"音色 ID {instrumentId} がありません。", nameof(instrumentId));
            }
            return index;
        }

        private static void EnsureUnreferenced(Song song, int instrumentId)
        {
            foreach (Track track in song.Tracks)
            {
                if (track.Notes.Exists(note => note.InstrumentId == instrumentId))
                {
                    throw new InvalidOperationException($"音色 ID {instrumentId} はノートから参照されています。");
                }
            }
        }
    }
}
