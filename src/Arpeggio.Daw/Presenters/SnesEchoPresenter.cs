using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>ソング単位のエコー設定を、一履歴と再生再構築の境界で編集する。</summary>
    public sealed class SnesEchoPresenter
    {
        /// <summary>エコー遅延の刻み幅。</summary>
        public const int DelayStepMilliseconds = 16;
        /// <summary>エコー遅延の最大値。</summary>
        public const int MaximumDelayMilliseconds = 240;
        /// <summary>NumericUpDown の Maximum / Increment は decimal のため、AXAML の x:Static 用に同じ値を decimal で公開する。</summary>
        public const decimal MaximumDelayMillisecondsInput = MaximumDelayMilliseconds;
        /// <summary>NumericUpDown の Increment 用の decimal 値。</summary>
        public const decimal DelayStepMillisecondsInput = DelayStepMilliseconds;
        private readonly DawDocument document;
        private readonly PianoRollPresenter pianoRoll;
        private readonly TransportPresenter transport;

        /// <summary>文書・ドラッグ確定・再生制御を明示的に接続する。</summary>
        public SnesEchoPresenter(DawDocument document, PianoRollPresenter pianoRoll, TransportPresenter transport)
        {
            this.document = document;
            this.pianoRoll = pianoRoll;
            this.transport = transport;
        }

        /// <summary>SNES ソングのときだけ編集を許可する。</summary>
        public bool IsAvailable => document.Song.Chip == ChipKind.Snes;
        /// <summary>現在公開されているエコー設定。</summary>
        public SnesEchoSettings Settings => document.Song.SnesEcho;
        /// <summary>FIR の選択肢。</summary>
        public IReadOnlyList<string> FirPresetNames { get; } = Array.AsReadOnly(new[]
        {
            nameof(SnesEchoFirPresets.Flat), nameof(SnesEchoFirPresets.LowPass),
            nameof(SnesEchoFirPresets.HighPass), nameof(SnesEchoFirPresets.Wide)
        });
        /// <summary>任意係数なら null とし、プリセットへ暗黙変換しない。</summary>
        public string? CurrentFirPreset => FirPresetNames.FirstOrDefault(name =>
            Settings.FirCoefficients.SequenceEqual(SnesEchoFirPresets.Get(name)));

        /// <summary>Flyout の確定値を適用する。FIR 未選択なら既存係数を保持する。</summary>
        public void Apply(int delayMilliseconds, double feedback, double volume, string? firPreset)
        {
            if (!IsAvailable)
            {
                throw new InvalidOperationException("エコー設定は SNES ソングで使用できます。");
            }
            Validate(delayMilliseconds, feedback, volume);
            SnesEchoSettings replacement = new SnesEchoSettings
            {
                DelayMilliseconds = delayMilliseconds,
                Feedback = feedback,
                Volume = volume,
                FirCoefficients = firPreset == null ? (int[])Settings.FirCoefficients.Clone() : SnesEchoFirPresets.Get(firPreset)
            };
            if (Settings.DelayMilliseconds == replacement.DelayMilliseconds && Settings.Feedback == replacement.Feedback &&
                Settings.Volume == replacement.Volume && Settings.FirCoefficients.SequenceEqual(replacement.FirCoefficients))
            {
                return;
            }
            pianoRoll.EndDrag();
            transport.ChangeStructure(() => document.UpdateSnesEcho(replacement));
        }

        private static void Validate(int delayMilliseconds, double feedback, double volume)
        {
            if (delayMilliseconds < 0 || delayMilliseconds > MaximumDelayMilliseconds || delayMilliseconds % DelayStepMilliseconds != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delayMilliseconds), "エコー遅延は 0〜240 ms の 16 ms 刻みです。");
            }
            if (!double.IsFinite(feedback) || Math.Abs(feedback) >= 1)
            {
                throw new ArgumentOutOfRangeException(nameof(feedback), "エコーフィードバックの絶対値は 1 未満です。");
            }
            if (!double.IsFinite(volume) || volume < 0 || volume > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(volume), "エコー音量は 0〜1 です。");
            }
        }
    }
}
