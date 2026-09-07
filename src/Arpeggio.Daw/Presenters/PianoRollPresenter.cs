using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>ノートのヒット判定・グリッド・ドラッグ履歴を管理する。</summary>
    public sealed class PianoRollPresenter
    {
        /// <summary>16 分音符の tick 数。</summary>
        public const int GridTicks = 12;
        /// <summary>MIDI 音高の上限。</summary>
        public const int MaximumMidiNote = 127;
        private const int InitialDuration = 24;
        private const int MaximumVolume = 15;
        private readonly DawDocument document;
        private readonly Action changed;
        private readonly Func<int> instrument;
        private int lastDuration = InitialDuration;
        private double pointerStartTick;
        private int pointerStartPitch;
        private Note? originalNote;
        private Song? gestureSnapshot;
        private Song[] undoSnapshots = Array.Empty<Song>();
        private Song[] redoSnapshots = Array.Empty<Song>();

        /// <summary>編集セッションと表示更新を結び付ける。</summary>
        public PianoRollPresenter(DawDocument document, Action changed, Func<int> instrument)
        {
            this.document = document;
            this.changed = changed;
            this.instrument = instrument;
        }
        /// <summary>編集中のソング。View が初期表示位置の計算に使う。</summary>
        public Song Song => document.Song;
        /// <summary>選択トラック。</summary>
        public int SelectedTrack { get; private set; }
        /// <summary>選択ノートの開始 tick。</summary>
        public int? SelectedTick { get; private set; }
        /// <summary>現在のドラッグ状態。</summary>
        public PianoRollDragMode DragMode { get; private set; }
        /// <summary>選択中のノート。</summary>
        public Note? SelectedNote => document.Song.Tracks[SelectedTrack].Notes.Find(note => note.Tick == SelectedTick);

        /// <summary>トラック切替時に前の操作を確定する。</summary>
        public void SelectTrack(int trackIndex)
        {
            EndDrag();
            SelectedTrack = Math.Clamp(trackIndex, 0, document.Song.Tracks.Count - 1);
            SelectedTick = null;
            changed();
        }
        /// <summary>同一音高で時間範囲が交わるノートだけを拾う。</summary>
        public Note? HitTest(double tick, int midiNote)
        {
            foreach (Note note in document.Song.Tracks[SelectedTrack].Notes)
            {
                if (note.MidiNote == midiNote && tick >= note.Tick && tick < note.Tick + note.DurationTicks)
                {
                    return note;
                }
            }
            return null;
        }
        /// <summary>空白なら追加し、既存音なら移動または右端変更を開始する。</summary>
        public void Press(double tick, int midiNote, double resizeToleranceTicks, bool bypassSnap)
        {
            EndDrag();
            Note? hit = HitTest(tick, midiNote);
            if (hit == null)
            {
                Add(tick, midiNote, bypassSnap);
                return;
            }
            SelectedTick = hit.Tick;
            originalNote = hit;
            pointerStartTick = tick;
            pointerStartPitch = midiNote;
            gestureSnapshot = SongSerializer.Deserialize(SongSerializer.Serialize(document.Song));
            undoSnapshots = document.Session.History.GetUndoSnapshots();
            redoSnapshots = document.Session.History.GetRedoSnapshots();
            DragMode = hit.Tick + hit.DurationTicks - tick <= resizeToleranceTicks
                ? PianoRollDragMode.Resize : PianoRollDragMode.Move;
            changed();
        }
        /// <summary>最後に置いた長さを使い、曲末では長さを切り詰める。</summary>
        public void Add(double tick, int midiNote, bool bypassSnap = false)
        {
            EndDrag();
            int startTick = Math.Clamp(Snap(tick, bypassSnap), 0, document.Song.LengthTicks - 1);
            Note note = new Note
            {
                Tick = startTick, MidiNote = Math.Clamp(midiNote, 0, MaximumMidiNote),
                DurationTicks = Math.Min(lastDuration, document.Song.LengthTicks - startTick),
                InstrumentId = instrument(), Volume = MaximumVolume
            };
            document.Session.Notes.Add(SelectedTrack, note);
            SelectedTick = startTick;
            lastDuration = note.DurationTicks;
            changed();
        }
        /// <summary>有効な位置だけを次のオーディオバッファへ公開する。</summary>
        public void Drag(double tick, int midiNote, bool bypassSnap)
        {
            if (originalNote == null || SelectedTick == null) { return; }
            Note current = SelectedNote!;
            int newTick = originalNote.Tick;
            int duration = originalNote.DurationTicks;
            int pitch = originalNote.MidiNote;
            if (DragMode == PianoRollDragMode.Move)
            {
                newTick = Math.Clamp(Snap(originalNote.Tick + tick - pointerStartTick, bypassSnap), 0,
                    document.Song.LengthTicks - duration);
                pitch = Math.Clamp(originalNote.MidiNote + midiNote - pointerStartPitch, 0, MaximumMidiNote);
            }
            else
            {
                int maximumDuration = document.Song.LengthTicks - originalNote.Tick;
                int minimumDuration = Math.Min(bypassSnap ? 1 : GridTicks, maximumDuration);
                duration = Math.Clamp(Snap(originalNote.Tick + originalNote.DurationTicks + tick - pointerStartTick, bypassSnap)
                    - originalNote.Tick, minimumDuration, maximumDuration);
            }
            if (current.Tick == newTick && current.MidiNote == pitch && current.DurationTicks == duration) { return; }
            Note replacement = new Note { Tick = newTick, MidiNote = pitch, DurationTicks = duration,
                Volume = current.Volume, InstrumentId = current.InstrumentId, Effects = current.Effects };
            document.Session.Notes.Update(SelectedTrack, current.Tick, replacement);
            SelectedTick = newTick;
            lastDuration = duration;
            changed();
        }
        /// <summary>ドラッグ途中の履歴を一操作へまとめる。</summary>
        public void EndDrag()
        {
            if (gestureSnapshot != null)
            {
                bool hasChanged = SongSerializer.Serialize(gestureSnapshot) != SongSerializer.Serialize(document.Song);
                document.Session.History.Restore(undoSnapshots, redoSnapshots);
                if (hasChanged) { document.Session.History.Record(gestureSnapshot); }
            }
            gestureSnapshot = null;
            originalNote = null;
            DragMode = PianoRollDragMode.None;
        }
        /// <summary>選択を解除する。</summary>
        public void ClearSelection() { EndDrag(); SelectedTick = null; }
        /// <summary>選択ノートを削除する。</summary>
        public void Delete()
        {
            EndDrag();
            if (SelectedTick == null) { return; }
            document.Session.Notes.Remove(SelectedTrack, SelectedTick.Value);
            SelectedTick = null;
            changed();
        }
        /// <summary>右クリック位置のノートを削除する。</summary>
        public void DeleteAt(double tick, int midiNote)
        {
            Note? hit = HitTest(tick, midiNote);
            if (hit == null) { return; }
            SelectedTick = hit.Tick;
            Delete();
        }
        /// <summary>選択音の音量を 0〜15 に制限して変更する。</summary>
        public void ChangeVolume(int direction)
        {
            EndDrag();
            Note? note = SelectedNote;
            if (note == null) { return; }
            int volume = Math.Clamp(note.Volume + direction, 0, MaximumVolume);
            if (volume == note.Volume) { return; }
            document.Session.Notes.SetVolume(SelectedTrack, note.Tick, volume);
            changed();
        }
        /// <summary>起動時に最初に見せる最高音。全トラックの最高ノートに余白を足して返し、ノートが無ければ C6 を返す。</summary>
        public static int GetInitialTopPitch(Song song)
        {
            const int DefaultTopPitch = 84;
            const int MarginRows = 3;
            int highest = -1;
            foreach (Track track in song.Tracks)
            {
                foreach (Note note in track.Notes)
                {
                    highest = Math.Max(highest, note.MidiNote);
                }
            }
            if (highest < 0)
            {
                return DefaultTopPitch;
            }
            return Math.Min(MaximumMidiNote, highest + MarginRows);
        }
        /// <summary>Alt 時だけ整数 tick 単位にする。</summary>
        public static int Snap(double tick, bool bypassSnap)
        {
            int resolution = bypassSnap ? 1 : GridTicks;
            return checked((int)Math.Round(tick / resolution, MidpointRounding.AwayFromZero) * resolution);
        }
    }
}
