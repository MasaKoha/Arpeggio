using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>SPC700 DSP の ADSR レジスタ値。範囲は音色検証時に確認する。</summary>
    public readonly record struct SnesAdsrRegisters
    {
        /// <summary>立ち上がり・減衰・保持レベル・保持減衰のレジスタを指定する。</summary>
        [JsonConstructor]
        public SnesAdsrRegisters(int attack, int decay, int sustainLevel, int sustainRate)
        {
            Attack = attack;
            Decay = decay;
            SustainLevel = sustainLevel;
            SustainRate = sustainRate;
        }

        /// <summary>立ち上がり速度（0〜15）。</summary>
        public int Attack { get; init; }
        /// <summary>減衰速度（0〜7）。</summary>
        public int Decay { get; init; }
        /// <summary>保持レベル（0〜7、1/8〜8/8）。</summary>
        public int SustainLevel { get; init; }
        /// <summary>保持中の減衰速度（0〜31、0 は保持）。</summary>
        public int SustainRate { get; init; }
    }
}
