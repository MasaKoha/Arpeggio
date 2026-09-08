using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Daw.Audio;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>再生操作と構造編集時の停止・リセットを管理する。</summary>
    public sealed class TransportPresenter
    {
        private const int BeatsPerBar = 4;
        private readonly DawDocument document;
        private readonly PlaybackEngine playback;
        private readonly Action changed;

        /// <summary>ドキュメントと再生機を明示的に接続する。</summary>
        public TransportPresenter(DawDocument document, PlaybackEngine playback, Action changed)
        {
            this.document = document;
            this.playback = playback;
            this.changed = changed;
        }
        /// <summary>音声出力の現在の再生状態。ポーリングや合成は行わない。</summary>
        public bool IsPlaying => playback.IsPlaying;
        /// <summary>音声出力へ供給済みの再生位置。取得による再生の進行はない。</summary>
        public double PositionTick => playback.PositionTick;

        /// <summary>再生と停止を切り替える。</summary>
        public void TogglePlayback()
        {
            if (playback.IsPlaying) { playback.Stop(); }
            else { playback.Play(); }
            changed();
        }
        /// <summary>停止して先頭へ戻す。</summary>
        public void Stop() { playback.Stop(); playback.Reset(); changed(); }
        /// <summary>ループを切り替える。</summary>
        public void ToggleLoop() { playback.SetLoop(!playback.IsLooping); changed(); }
        /// <summary>テンポ変更はコールバックを終了させてから確定する。</summary>
        public void SetTempo(int tempoBpm)
        {
            ChangeStructure(() => BatchOperationApplier.Apply(document.Session,
                new[] { new BatchOperation { Kind = BatchOperationKind.SetTempo, TempoBpm = tempoBpm } }));
        }
        /// <summary>曲の長さ変更は既存ノートとの整合性を検証する。</summary>
        public void SetLength(int lengthTicks)
        {
            ChangeStructure(() => BatchOperationApplier.Apply(document.Session,
                new[] { new BatchOperation { Kind = BatchOperationKind.SetLength, LengthTicks = lengthTicks } }));
        }
        /// <summary>履歴もテンポと長さを変えうるため、停止中に復元する。</summary>
        public void ChangeStructure(Action edit)
        {
            bool resume = playback.IsPlaying;
            playback.Stop();
            try { edit(); }
            finally
            {
                playback.Reset();
                if (resume) { playback.Play(); }
                changed();
            }
        }
        /// <summary>4/4 拍子の小節:拍:tick 表示へ変換する。</summary>
        public static string FormatPosition(double positionTick)
        {
            long tick = Math.Max(0, (long)positionTick);
            long beat = tick / Song.FixedTicksPerBeat;
            return $"{beat / BeatsPerBar + 1}:{beat % BeatsPerBar + 1}:{tick % Song.FixedTicksPerBeat:00}";
        }
    }
}
