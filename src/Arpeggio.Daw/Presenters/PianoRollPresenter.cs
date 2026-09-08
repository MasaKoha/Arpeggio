using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>選択トラックのノート編集と入力の判断を統括する。</summary>
    public sealed class PianoRollPresenter
    {
        /// <summary>既定の十六分音符の tick 数。</summary>
        public const int GridTicks = 12;
        /// <summary>MIDI 音高の上限。</summary>
        public const int MaximumMidiNote = 127;
        private const int InitialDuration = 24;
        private const int MaximumVolume = 15;
        private readonly DawDocument document;
        private readonly Action changed;
        private readonly Func<int> instrument;
        private readonly PianoRollGesture gesture;
        private readonly NoteClipboard clipboard = new NoteClipboard();
        private int lastDuration = InitialDuration;

        /// <summary>編集セッションと表示更新を結び付ける。</summary>
        public PianoRollPresenter(DawDocument document, Action changed, Func<int> instrument)
        {
            this.document = document;
            this.changed = changed;
            this.instrument = instrument;
            gesture = new PianoRollGesture(document, Selection);
            Velocity = new VelocityLanePresenter(document, Selection, changed);
        }
        /// <summary>編集中のソング。</summary>
        public Song Song => document.Song;
        /// <summary>選択トラック。</summary>
        public int SelectedTrack { get; private set; }
        /// <summary>選択ノート集合。</summary>
        public NoteSelection Selection { get; } = new NoteSelection();
        /// <summary>単音編集パネルへ渡す代表ノートの tick。</summary>
        public int? SelectedTick => Selection.FirstTick;
        /// <summary>代表ノート。</summary>
        public Note? SelectedNote => Song.Tracks[SelectedTrack].Notes.Find(note => note.Tick == SelectedTick);
        /// <summary>現在のドラッグ状態。</summary>
        public PianoRollDragMode DragMode => gesture.Mode;
        /// <summary>ドラッグ中の選択矩形。</summary>
        public NoteSelectionRectangle? SelectionRectangle => gesture.Rectangle;
        /// <summary>現在のスナップ単位。</summary>
        public SnapResolution Resolution { get; private set; } = SnapResolution.Sixteenth;
        /// <summary>現在のグリッド一つ分。</summary>
        public int SnapTicks => SnapGrid.ToTicks(Resolution);
        /// <summary>上書き件数などの操作結果。</summary>
        public string StatusText { get; private set; } = string.Empty;
        /// <summary>音量レーンの入力判断。</summary>
        public VelocityLanePresenter Velocity { get; }
        /// <summary>トラック切替時に前の操作を確定する。</summary>
        public void SelectTrack(int trackIndex)
        {
            EndDrag();
            SelectedTrack = Math.Clamp(trackIndex, 0, Song.Tracks.Count - 1);
            Selection.Clear();
            StatusText = string.Empty;
            changed();
        }
        /// <summary>グリッドを選択する。</summary>
        public void SetSnapResolution(SnapResolution resolution)
        {
            _ = SnapGrid.ToTicks(resolution);
            EndDrag();
            Resolution = resolution;
            changed();
        }
        /// <summary>同一音高で時間範囲が交わるノートだけを拾う。</summary>
        public Note? HitTest(double tick, int midiNote) => Song.Tracks[SelectedTrack].Notes.Find(note =>
            note.MidiNote == midiNote && tick >= note.Tick && tick < (long)note.Tick + note.DurationTicks);
        /// <summary>修飾入力と選択件数に応じて追加・選択・ドラッグを開始する。</summary>
        public void Press(double tick, int midiNote, double resizeToleranceTicks, bool bypassSnap,
            NotePointerModifiers modifiers = NotePointerModifiers.None)
        {
            EndDrag();
            StatusText = string.Empty;
            Note? hit = HitTest(tick, midiNote);
            if (hit == null)
            {
                PressBlank(tick, midiNote, bypassSnap, modifiers);
                return;
            }
            lastDuration = hit.DurationTicks;
            if (modifiers.HasFlag(NotePointerModifiers.Shift))
            {
                Selection.Toggle(hit.Tick);
                changed();
                return;
            }
            if (!Selection.Contains(hit.Tick)) { Selection.Select(hit.Tick); }
            gesture.BeginHistory();
            PianoRollDragMode mode = hit.Tick + hit.DurationTicks - tick <= resizeToleranceTicks
                ? PianoRollDragMode.Resize : PianoRollDragMode.Move;
            gesture.Begin(hit, Selection.Resolve(Song.Tracks[SelectedTrack].Notes), tick, midiNote, mode);
            changed();
        }
        private void PressBlank(double tick, int midiNote, bool bypassSnap, NotePointerModifiers modifiers)
        {
            if (modifiers.HasFlag(NotePointerModifiers.Control))
            {
                gesture.BeginSelection(tick, midiNote);
            }
            else if (Selection.Count >= 2) { Selection.Clear(); }
            else
            {
                gesture.BeginHistory();
                try
                {
                    Note note = AddNote(tick, midiNote, bypassSnap);
                    gesture.Begin(note, new[] { note }, tick, midiNote, PianoRollDragMode.Create);
                }
                catch { gesture.End(); throw; }
            }
            changed();
        }
        /// <summary>最後に置いた、または掴んだ長さで追加する。</summary>
        public void Add(double tick, int midiNote, bool bypassSnap = false)
        {
            EndDrag();
            AddNote(tick, midiNote, bypassSnap);
            changed();
        }
        private Note AddNote(double tick, int midiNote, bool bypassSnap)
        {
            int startTick = Math.Clamp(SnapGrid.Snap(tick, Resolution, bypassSnap), 0, Song.LengthTicks - 1);
            Note note = new Note
            {
                Tick = startTick, MidiNote = Math.Clamp(midiNote, 0, MaximumMidiNote),
                DurationTicks = Math.Min(lastDuration, Song.LengthTicks - startTick),
                InstrumentId = instrument(), Volume = MaximumVolume
            };
            document.Session.Notes.Add(SelectedTrack, note);
            Selection.Select(startTick);
            lastDuration = note.DurationTicks;
            return note;
        }
        /// <summary>全件が有効な変更だけ公開し、拒否時は最後の有効状態を保つ。</summary>
        public void Drag(double tick, int midiNote, bool bypassSnap)
        {
            int? duration = gesture.Drag(tick, midiNote, SelectedTrack, Resolution, bypassSnap);
            if (duration.HasValue) { lastDuration = duration.Value; }
            changed();
        }
        /// <summary>他のドラッグを確定し、選択トラックの音量編集を開始する。</summary>
        public void PressVelocity(double tick, double volume, double toleranceTicks)
        {
            EndDrag();
            Velocity.Press(SelectedTrack, tick, volume, toleranceTicks);
        }
        /// <summary>ドラッグ途中の履歴を一操作へまとめる。</summary>
        public void EndDrag()
        {
            gesture.End();
            Velocity.EndDrag();
        }
        /// <summary>選択を解除する。</summary>
        public void ClearSelection() { EndDrag(); Selection.Clear(); changed(); }
        /// <summary>選択トラックの全ノートを選択する。</summary>
        public void SelectAll() { EndDrag(); Selection.Replace(Song.Tracks[SelectedTrack].Notes); changed(); }
        /// <summary>選択ノートを全部削除する。</summary>
        public void Delete()
        {
            EndDrag();
            NoteBatchEditor.Replace(document.Session, SelectedTrack, Selection.Resolve(Song.Tracks[SelectedTrack].Notes), Array.Empty<Note>());
            Selection.Clear();
            changed();
        }
        /// <summary>右クリック一回でノートを削除する。</summary>
        public void DeleteAt(double tick, int midiNote) { BeginErase(tick, midiNote); EndDrag(); }
        /// <summary>右ドラッグの削除を開始する。</summary>
        public void BeginErase(double tick, int midiNote)
        {
            EndDrag();
            gesture.BeginErase(tick, midiNote, SelectedTrack);
            changed();
        }
        /// <summary>選択全体を同じ tick・半音差で移動する。</summary>
        public void MoveSelection(int tickDelta, int pitchDelta) => TransformSelection(note =>
        {
            note.Tick = checked(note.Tick + tickDelta);
            note.MidiNote = checked(note.MidiNote + pitchDelta);
        });
        /// <summary>選択全体の音量を 0〜15 に制限して変更する。</summary>
        public void ChangeVolume(int direction) => TransformSelection(note => note.Volume = (int)Math.Clamp((long)note.Volume + direction, 0, MaximumVolume));
        private void TransformSelection(Action<Note> transform)
        {
            EndDrag();
            Note[] current = Selection.Resolve(Song.Tracks[SelectedTrack].Notes);
            Note[] replacements = current.Select(NoteBatchEditor.Copy).ToArray();
            foreach (Note note in replacements) { transform(note); }
            if (current.Zip(replacements).All(pair => pair.First.Tick == pair.Second.Tick &&
                pair.First.MidiNote == pair.Second.MidiNote && pair.First.Volume == pair.Second.Volume)) { return; }
            NoteBatchEditor.Replace(document.Session, SelectedTrack, current, replacements);
            Selection.Replace(replacements);
            changed();
        }
        /// <summary>選択ノートを内部クリップボードへコピーする。</summary>
        public void Copy() { EndDrag(); clipboard.Copy(Song, SelectedTrack, Selection.Resolve(Song.Tracks[SelectedTrack].Notes)); }
        /// <summary>選択ノートをコピーして一履歴で削除する。</summary>
        public void Cut() { Copy(); Delete(); }
        /// <summary>再生カーソルの整数 tick に貼り付け、時間が重なる既存音を置換する。</summary>
        public void Paste(double positionTick = 0)
        {
            EndDrag();
            PasteNotes(clipboard.CreatePaste(Song, SelectedTrack, checked((int)Math.Floor(positionTick))));
        }
        /// <summary>選択範囲の長さだけ右へ複製する。</summary>
        public void Duplicate()
        {
            EndDrag();
            Note[] selected = Selection.Resolve(Song.Tracks[SelectedTrack].Notes);
            if (selected.Length == 0) { return; }
            NoteClipboard duplicate = new NoteClipboard();
            duplicate.Copy(Song, SelectedTrack, selected);
            PasteNotes(duplicate.CreatePaste(Song, SelectedTrack, checked(selected.Min(note => note.Tick) + duplicate.LengthTicks)));
        }
        private void PasteNotes(Note[] pasted)
        {
            if (pasted.Length == 0) { return; }
            Note[] replaced = Song.Tracks[SelectedTrack].Notes.Where(note => pasted.Any(added =>
                added.Tick < (long)note.Tick + note.DurationTicks && note.Tick < (long)added.Tick + added.DurationTicks)).ToArray();
            NoteBatchEditor.Replace(document.Session, SelectedTrack, replaced, pasted);
            Selection.Replace(pasted);
            StatusText = $"{replaced.Length} 音を置き換え";
            changed();
        }
        /// <summary>起動時に最初に見せる最高音。空なら C6。</summary>
        public static int GetInitialTopPitch(Song song)
        {
            const int DefaultTopPitch = 84;
            const int MarginRows = 3;
            int highest = -1;
            foreach (Track track in song.Tracks)
            {
                foreach (Note note in track.Notes) { highest = Math.Max(highest, note.MidiNote); }
            }
            return highest < 0 ? DefaultTopPitch : Math.Min(MaximumMidiNote, highest + MarginRows);
        }
        /// <summary>既定グリッドで丸める。Alt 時は整数 tick 単位。</summary>
        public static int Snap(double tick, bool bypassSnap) => SnapGrid.Snap(tick, SnapResolution.Sixteenth, bypassSnap);
    }
}
