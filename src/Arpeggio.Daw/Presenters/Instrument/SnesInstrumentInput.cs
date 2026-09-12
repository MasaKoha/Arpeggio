using System;
using System.Globalization;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Daw.Presenters.Instrument
{
    /// <summary>SNES 専用入力の範囲と空欄による秒指定への復帰を検証する。</summary>
    public sealed class SnesInstrumentInput
    {
        /// <summary>Attack レジスタの上限。</summary>
        public const int MaximumAttack = 15;
        /// <summary>Decay レジスタの上限。</summary>
        public const int MaximumDecay = 7;
        /// <summary>Sustain level レジスタの上限。</summary>
        public const int MaximumSustainLevel = 7;
        /// <summary>Sustain rate とノイズレートの上限。</summary>
        public const int MaximumRate = 31;
        /// <summary>Slider.Maximum は double のため、AXAML の x:Static 用に同じ値を double で公開する。</summary>
        public const double MaximumRateSliderValue = MaximumRate;

        /// <summary>Attack の整数入力。</summary>
        public string Attack { get; init; } = string.Empty;
        /// <summary>Decay の整数入力。</summary>
        public string Decay { get; init; } = string.Empty;
        /// <summary>Sustain level の整数入力。</summary>
        public string SustainLevel { get; init; } = string.Empty;
        /// <summary>Sustain rate の整数入力。</summary>
        public string SustainRate { get; init; } = string.Empty;
        /// <summary>ピッチ変調を有効にするか。</summary>
        public bool PitchModulation { get; init; }
        /// <summary>DSP ノイズを有効にするか。</summary>
        public bool NoiseEnabled { get; init; }
        /// <summary>ノイズレート。</summary>
        public int NoiseRate { get; init; } = MaximumRate;

        /// <summary>全欄が空なら秒指定を使う。</summary>
        public bool UsesSeconds => string.IsNullOrWhiteSpace(Attack) && string.IsNullOrWhiteSpace(Decay) &&
            string.IsNullOrWhiteSpace(SustainLevel) && string.IsNullOrWhiteSpace(SustainRate);
        /// <summary>Attack 欄を適用できるか。</summary>
        public bool IsAttackValid => UsesSeconds || IsRegisterValid(Attack, MaximumAttack);
        /// <summary>Decay 欄を適用できるか。</summary>
        public bool IsDecayValid => UsesSeconds || IsRegisterValid(Decay, MaximumDecay);
        /// <summary>Sustain level 欄を適用できるか。</summary>
        public bool IsSustainLevelValid => UsesSeconds || IsRegisterValid(SustainLevel, MaximumSustainLevel);
        /// <summary>Sustain rate 欄を適用できるか。</summary>
        public bool IsSustainRateValid => UsesSeconds || IsRegisterValid(SustainRate, MaximumRate);

        internal void ApplyTo(SnesSampleInstrument instrument)
        {
            if (!IsAttackValid || !IsDecayValid || !IsSustainLevelValid || !IsSustainRateValid)
            {
                throw new ArgumentException("ADSR レジスタは attack 0〜15、decay 0〜7、sustain level 0〜7、sustain rate 0〜31 の整数です。秒指定へ戻すには全欄を空にしてください。");
            }
            if (NoiseRate < 0 || NoiseRate > MaximumRate)
            {
                throw new ArgumentOutOfRangeException(nameof(NoiseRate), "ノイズレートは 0〜31 です。");
            }
            instrument.AdsrRegisters = UsesSeconds ? null : new SnesAdsrRegisters(
                int.Parse(Attack, CultureInfo.InvariantCulture), int.Parse(Decay, CultureInfo.InvariantCulture),
                int.Parse(SustainLevel, CultureInfo.InvariantCulture), int.Parse(SustainRate, CultureInfo.InvariantCulture));
            instrument.PitchModulation = PitchModulation;
            instrument.NoiseEnabled = NoiseEnabled;
            instrument.NoiseRate = NoiseRate;
        }

        private static bool IsRegisterValid(string text, int maximum) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0 && value <= maximum;
    }
}
