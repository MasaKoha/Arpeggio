using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis
{
    /// <summary>チャンネルの発音寿命・位相・共通効果を管理する合成基底。</summary>
    public abstract class ChannelSynthesizer : IChannelSynthesizer
    {
        private readonly VoiceModulation _modulation = new VoiceModulation();
        private readonly ChipKind _chip;
        private readonly ChannelKind _channel;

        /// <summary>出力サンプルレート。</summary>
        protected int SampleRate { get; }
        /// <summary>発音またはリリースが継続しているか。</summary>
        protected bool IsActive { get; set; }
        /// <summary>一周期を 0〜1 とする位相。</summary>
        protected double Phase { get; set; }
        /// <summary>制約適用後の一サンプル当たりの位相増分。</summary>
        protected double PhaseIncrement { get; private set; }
        /// <summary>マクロと効果を含めた正規化音量。</summary>
        protected double Volume => _modulation.Volume;
        /// <summary>マクロ適用後のデューティ選択値。</summary>
        protected int Duty => _modulation.Duty;
        /// <summary>マクロ適用後の MIDI 音程。</summary>
        protected double MidiNote => _modulation.MidiNote;

        /// <summary>チップの周期制約と出力レートを固定する。</summary>
        protected ChannelSynthesizer(int sampleRate, ChipKind chip, ChannelKind channel)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }
            SampleRate = sampleRate;
            _chip = chip;
            _channel = channel;
        }

        /// <summary>位相・効果・音色状態を初期化して発音する。</summary>
        public void NoteOn(int midiNote, int volume, Instrument instrument, ReadOnlySpan<NoteEffect> effects)
        {
            _modulation.Start(midiNote, volume, effects);
            Phase = 0;
            IsActive = true;
            ConfigureInstrument(instrument);
            RefreshPitch();
        }

        /// <summary>即座に消音する。リリースを持つチャンネルは上書きする。</summary>
        public virtual void NoteOff() => IsActive = false;

        /// <summary>シーケンサーが発音期間を渡し、スライドの終点を決める。</summary>
        public void SetNoteDuration(double seconds) => _modulation.SetDuration(seconds);

        /// <summary>呼び出し側のモノラルバッファへ確保せず加算する。</summary>
        public void Render(Span<float> buffer)
        {
            // perf: 波形生成は既存状態と値型だけを使い、任意のバッファ長を処理する。
            for (int index = 0; index < buffer.Length; index++)
            {
                if (!IsActive)
                {
                    break;
                }
                buffer[index] += (float)ReadSample();
                Phase += PhaseIncrement;
                Phase -= Math.Floor(Phase);
            }
        }

        /// <summary>共通マクロとチップ固有エンベロープを一段進める。</summary>
        public virtual void AdvanceFrame()
        {
            // perf: 周波数の指数計算はサンプルごとでなく 60 Hz に限定する。
            _modulation.AdvanceFrame();
            RefreshPitch();
        }

        /// <summary>音色のマクロ参照を発音状態に結び付ける。</summary>
        protected void ConfigureMacros(Macro? volume, Macro? arpeggio, Macro? pitch, Macro? duty = null, int initialDuty = 0)
            => _modulation.Configure(volume, arpeggio, pitch, duty, initialDuty);

        /// <summary>チップ固有の音色状態を初期化する。</summary>
        protected abstract void ConfigureInstrument(Instrument instrument);
        /// <summary>現在位相の一サンプルを求める。</summary>
        protected abstract double ReadSample();

        /// <summary>デューティ選択を High の時間比率へ変換する。</summary>
        protected static double GetDutyRatio(int duty)
        {
            return (DutyCycle)duty switch
            {
                DutyCycle.Percent12_5 => 0.125,
                DutyCycle.Percent25 => 0.25,
                DutyCycle.Percent75 => 0.75,
                _ => 0.5
            };
        }

        /// <summary>音色固有の基準音がある場合に位相増分の計算を差し替える。</summary>
        protected virtual double GetPhaseIncrement() => PitchTable.Quantize(_chip, _channel, MidiNote) / SampleRate;

        private void RefreshPitch() => PhaseIncrement = GetPhaseIncrement();
    }
}
