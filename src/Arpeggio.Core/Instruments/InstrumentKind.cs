namespace Arpeggio.Core.Instruments
{
    /// <summary>InstrumentKind の種別。</summary>
    public enum InstrumentKind
    {
        /// <summary>None 音色。</summary>
        None = 0,
        /// <summary>NesPulse 音色。</summary>
        NesPulse = 1,
        /// <summary>NesTriangle 音色。</summary>
        NesTriangle = 2,
        /// <summary>NesNoise 音色。</summary>
        NesNoise = 3,
        /// <summary>NesDpcm 音色。</summary>
        NesDpcm = 4,
        /// <summary>GbPulse 音色。</summary>
        GbPulse = 5,
        /// <summary>GbWave 音色。</summary>
        GbWave = 6,
        /// <summary>GbNoise 音色。</summary>
        GbNoise = 7,
        /// <summary>SnesSample 音色。</summary>
        SnesSample = 8,
    }
}
