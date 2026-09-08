using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis
{
    /// <summary>チップ共通のマクロとノート効果を発音単位で計算する。</summary>
    public sealed class VoiceModulation
    {
        private const int MaximumVolume = 15;
        private const int CentsPerSemitone = 100;
        private const int FramesPerSecond = 60;
        private const int VibratoHertz = 6;
        private readonly MacroRunner _volume = new MacroRunner();
        private readonly MacroRunner _arpeggio = new MacroRunner();
        private readonly MacroRunner _pitch = new MacroRunner();
        private readonly MacroRunner _duty = new MacroRunner();
        private int _midiNote;
        private int _noteVolume;
        private int _pitchSlide;
        private int _volumeSlide;
        private int _vibrato;
        private int _effectArpeggio;
        private long _frame;
        private double _durationFrames = FramesPerSecond;

        /// <summary>発音ごとに独立したマクロと効果の状態を作る。</summary>
        public VoiceModulation() { }

        /// <summary>マクロとノート効果を合成した連続 MIDI 音程。</summary>
        public double MidiNote { get; private set; }
        /// <summary>マクロとノート効果を合成した 0〜1 の音量。</summary>
        public double Volume { get; private set; }
        /// <summary>マクロ適用後のデューティ選択値。</summary>
        public int Duty => _duty.Value;

        /// <summary>効果を発音先頭へ戻す。続けて Configure、SetDuration の順に呼ぶ。</summary>
        public void Start(int midiNote, int volume, ReadOnlySpan<NoteEffect> effects)
        {
            _midiNote = midiNote;
            _noteVolume = volume;
            _frame = 0;
            _pitchSlide = 0;
            _volumeSlide = 0;
            _vibrato = 0;
            _effectArpeggio = 0;
            _durationFrames = FramesPerSecond;
            for (int index = 0; index < effects.Length; index++)
            {
                NoteEffect effect = effects[index];
                switch (effect.Kind)
                {
                    case NoteEffectKind.PitchSlide: _pitchSlide = effect.Value; break;
                    case NoteEffectKind.VolumeSlide: _volumeSlide = effect.Value; break;
                    case NoteEffectKind.Vibrato: _vibrato = effect.Value; break;
                    case NoteEffectKind.Arpeggio: _effectArpeggio = effect.Value; break;
                }
            }
        }

        /// <summary>マクロ参照を保持し、先頭の制御値を適用する。使用中のマクロは変更しない。</summary>
        public void Configure(Macro? volume, Macro? arpeggio, Macro? pitch, Macro? duty = null, int initialDuty = 0)
        {
            _volume.Reset(volume, MaximumVolume);
            _arpeggio.Reset(arpeggio);
            _pitch.Reset(pitch);
            _duty.Reset(duty, initialDuty);
            Recalculate();
        }

        /// <summary>実際の発音残り秒数をスライド期間に設定する。進行前に呼ぶ。</summary>
        public void SetDuration(double seconds)
        {
            _durationFrames = Math.Max(1, seconds * FramesPerSecond);
        }

        /// <summary>曲全体の 60 Hz 境界に合わせてマクロと効果を一段進める。</summary>
        public void AdvanceFrame()
        {
            // perf: マクロと効果は事前に用意した状態だけを書き換える。
            _frame++;
            _volume.AdvanceFrame();
            _arpeggio.AdvanceFrame();
            _pitch.AdvanceFrame();
            _duty.AdvanceFrame();
            Recalculate();
        }

        private void Recalculate()
        {
            const int ArpeggioCycle = 3;
            const int NibbleBits = 4;
            const int NibbleMask = 15;
            double progress = Math.Min(1, _frame / _durationFrames);
            int offset = 0;
            if (_frame % ArpeggioCycle == 1)
            {
                offset = (_effectArpeggio >> NibbleBits) & NibbleMask;
            }
            else if (_frame % ArpeggioCycle == 2)
            {
                offset = _effectArpeggio & NibbleMask;
            }
            double vibrato = _vibrato * Math.Sin(2 * Math.PI * VibratoHertz * _frame / FramesPerSecond);
            MidiNote = (double)_midiNote + _arpeggio.Value + offset + (_pitch.Value + vibrato) / CentsPerSemitone + _pitchSlide * progress;
            Volume = Math.Clamp(_noteVolume + _volumeSlide * progress, 0, MaximumVolume) * Math.Clamp(_volume.Value, 0, MaximumVolume) / (MaximumVolume * MaximumVolume);
        }
    }
}
