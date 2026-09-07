using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>選択ノートの効果を一操作ずつ検証して公開する。</summary>
    public sealed class NotePanelPresenter
    {
        /// <summary>アルペジオの各半音入力の上限。</summary>
        public const int MaximumArpeggioSemitones = 15;
        private const int ArpeggioShift = 4;
        private readonly DawDocument document;
        private readonly PianoRollPresenter pianoRoll;
        private readonly Action changed;

        /// <summary>ピアノロールと同じ選択状態・編集セッションを受け取る。</summary>
        public NotePanelPresenter(DawDocument document, PianoRollPresenter pianoRoll, Action changed)
        {
            this.document = document;
            this.pianoRoll = pianoRoll;
            this.changed = changed;
        }

        /// <summary>表示対象のノート。未選択なら null。</summary>
        public Note? SelectedNote => pianoRoll.SelectedNote;

        /// <summary>効果を末尾へ追加する。</summary>
        public void AddEffect(NoteEffectKind kind, int value)
        {
            Note note = RequireNote();
            List<NoteEffect> effects = new List<NoteEffect>(note.Effects) { new NoteEffect(kind, value) };
            Apply(note, effects.ToArray());
        }

        /// <summary>指定行の種類と値を一履歴で更新する。</summary>
        public void UpdateEffect(int index, NoteEffectKind kind, int value)
        {
            Note note = RequireNote();
            NoteEffect[] effects = (NoteEffect[])note.Effects.Clone();
            ValidateIndex(index, effects.Length);
            effects[index] = new NoteEffect(kind, value);
            Apply(note, effects);
        }

        /// <summary>指定行の効果を一履歴で削除する。</summary>
        public void RemoveEffect(int index)
        {
            Note note = RequireNote();
            List<NoteEffect> effects = new List<NoteEffect>(note.Effects);
            ValidateIndex(index, effects.Count);
            effects.RemoveAt(index);
            Apply(note, effects.ToArray());
        }

        /// <summary>二つの正の半音差を上位・下位の 4 bit に畳む。</summary>
        public static int PackArpeggio(int firstSemitones, int secondSemitones)
        {
            if (firstSemitones < 0 || firstSemitones > MaximumArpeggioSemitones ||
                secondSemitones < 0 || secondSemitones > MaximumArpeggioSemitones)
            {
                throw new ArgumentOutOfRangeException(nameof(firstSemitones), "アルペジオの半音差はそれぞれ +0〜+15 です。");
            }
            return (firstSemitones << ArpeggioShift) | secondSemitones;
        }

        /// <summary>保存値から第 2 音の半音差を読む。</summary>
        public static int FirstArpeggioSemitones(int value) => (value >> ArpeggioShift) & MaximumArpeggioSemitones;

        /// <summary>保存値から第 3 音の半音差を読む。</summary>
        public static int SecondArpeggioSemitones(int value) => value & MaximumArpeggioSemitones;

        private Note RequireNote()
        {
            pianoRoll.EndDrag();
            return SelectedNote ?? throw new InvalidOperationException("ノートを選択してください。");
        }

        private void Apply(Note note, NoteEffect[] effects)
        {
            Note replacement = new Note
            {
                Tick = note.Tick, DurationTicks = note.DurationTicks, MidiNote = note.MidiNote,
                Volume = note.Volume, InstrumentId = note.InstrumentId, Effects = effects
            };
            document.Session.Notes.Update(pianoRoll.SelectedTrack, note.Tick, replacement);
            changed();
        }

        private static void ValidateIndex(int index, int count)
        {
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "効果を選択してください。");
            }
        }
    }
}
