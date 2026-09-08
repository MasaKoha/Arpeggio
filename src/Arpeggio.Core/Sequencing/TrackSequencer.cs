using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sequencing
{
    /// <summary>トラックのノートを Delay とループ込みの発音遷移へ展開する。</summary>
    public sealed class TrackSequencer
    {
        private readonly Track _track;
        private List<Note> _notes;
        private Note? _activeNote;
        private long _activeCycle;

        /// <summary>編集時にノート列の参照が差し替わるトラックを受け取る。</summary>
        public TrackSequencer(Track track)
        {
            _track = track;
            _notes = track.Notes;
        }

        /// <summary>バッファの描画前に呼び、編集で公開されたノート列を取り込む。</summary>
        public void Refresh()
        {
            // perf: 編集側がリストを差し替えるため、コピーもロックも要らない。
            _notes = _track.Notes;
        }

        /// <summary>履歴を消去し、次の問い合わせで現在のノートを再発音できる状態に戻す。</summary>
        public void Reset()
        {
            _activeNote = null;
            _activeCycle = 0;
            Refresh();
        }

        /// <summary>ループ展開前の tick で発音中のノートと Delay 後の経過 tick を返す。</summary>
        public bool TryGetActive(double tick, out Note? note, out double elapsedTicks)
        {
            note = null;
            elapsedTicks = 0.0;
            // ミュートはミキサー段で行う。SPC700 はボリューム 0 でも前ボイス出力がピッチモジュレーションへ乗るため、発音自体は止めない
            int noteIndex = FindNoteIndex(tick);
            if (noteIndex < 0)
            {
                return false;
            }
            Note candidate = _notes[noteIndex];
            double startTick = candidate.Tick + (double)GetDelay(candidate);
            if (tick < startTick || tick >= candidate.Tick + (double)candidate.DurationTicks)
            {
                return false;
            }
            note = candidate;
            elapsedTicks = tick - startTick;
            return true;
        }

        /// <summary>絶対サンプル位置で遷移があれば返す。同時刻の交代は停止と発音を一つにまとめる。</summary>
        public bool TryGetEvent(long absoluteSample, TickClock clock, int lengthTicks, int loopStartTick,
            int loopCount, out NoteEvent noteEvent)
        {
            // perf: 値型イベントと二分探索により、イベント列を事前生成せずに描画する。
            Note? note = null;
            long cycle = 0;
            double elapsedTicks = 0.0;
            double durationTicks = 0.0;
            double totalTicks = lengthTicks + (loopCount - 1.0) * (lengthTicks - loopStartTick);
            if (absoluteSample < clock.TickToSamples(totalTicks))
            {
                cycle = GetCycle(absoluteSample, clock, lengthTicks, loopStartTick);
                double tickOffset = cycle * (double)(lengthTicks - loopStartTick);
                note = FindActiveAtSample(absoluteSample, clock, tickOffset, out elapsedTicks, out durationTicks);
            }
            bool hasTransition = !ReferenceEquals(note, _activeNote) || (note != null && cycle != _activeCycle);
            noteEvent = default;
            if (!hasTransition)
            {
                return false;
            }
            _activeNote = note;
            _activeCycle = cycle;
            noteEvent = new NoteEvent(absoluteSample, note, elapsedTicks, durationTicks, note != null);
            return true;
        }

        private Note? FindActiveAtSample(long absoluteSample, TickClock clock, double tickOffset,
            out double elapsedTicks, out double durationTicks)
        {
            elapsedTicks = 0.0;
            durationTicks = 0.0;
            int noteIndex = FindNoteIndexAtSample(absoluteSample, clock, tickOffset);
            if (noteIndex < 0)
            {
                return null;
            }
            Note note = _notes[noteIndex];
            int delay = GetDelay(note);
            double startTick = tickOffset + note.Tick + delay;
            double endTick = tickOffset + note.Tick + note.DurationTicks;
            if (absoluteSample < clock.TickToSamples(startTick) || absoluteSample >= clock.TickToSamples(endTick))
            {
                return null;
            }
            elapsedTicks = Math.Max(0.0, clock.SamplesToTick(absoluteSample) - startTick);
            durationTicks = note.DurationTicks - (double)delay;
            return note;
        }

        private int FindNoteIndex(double tick)
        {
            int lowerBound = 0;
            int upperBound = _notes.Count;
            while (lowerBound < upperBound)
            {
                int middle = lowerBound + (upperBound - lowerBound) / 2;
                if (_notes[middle].Tick <= tick)
                {
                    lowerBound = middle + 1;
                }
                else
                {
                    upperBound = middle;
                }
            }
            return lowerBound - 1;
        }

        private int FindNoteIndexAtSample(long absoluteSample, TickClock clock, double tickOffset)
        {
            int lowerBound = 0;
            int upperBound = _notes.Count;
            while (lowerBound < upperBound)
            {
                int middle = lowerBound + (upperBound - lowerBound) / 2;
                long startSample = clock.TickToSamples(tickOffset + _notes[middle].Tick);
                if (startSample <= absoluteSample)
                {
                    lowerBound = middle + 1;
                }
                else
                {
                    upperBound = middle;
                }
            }
            return lowerBound - 1;
        }

        private static long GetCycle(long absoluteSample, TickClock clock, int lengthTicks, int loopStartTick)
        {
            if (absoluteSample < clock.TickToSamples(lengthTicks))
            {
                return 0;
            }
            int loopLength = lengthTicks - loopStartTick;
            double absoluteTick = clock.SamplesToTick(absoluteSample);
            long cycle = Math.Max(1L, (long)Math.Floor((absoluteTick - lengthTicks) / loopLength) + 1L);
            double nextCycleTick = lengthTicks + cycle * (double)loopLength;
            // tick の実数境界より半サンプル早く丸められた周回開始も取りこぼさない。
            if (absoluteSample >= clock.TickToSamples(nextCycleTick))
            {
                cycle++;
            }
            return cycle;
        }

        private static int GetDelay(Note note)
        {
            NoteEffect[] effects = note.Effects;
            for (int effectIndex = 0; effectIndex < effects.Length; effectIndex++)
            {
                if (effects[effectIndex].Kind == NoteEffectKind.Delay)
                {
                    return effects[effectIndex].Value;
                }
            }
            return 0;
        }
    }
}
