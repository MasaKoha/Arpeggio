using System;
using System.Collections.Generic;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Formats.Export
{
    /// <summary>レジスタ変換に必要な音色の固定パラメーター。マクロは制御値へ適用済みとする。</summary>
    public sealed class ControlInstrument
    {
        internal ControlInstrument(Instrument instrument)
        {
            Id = instrument.Id;
            Kind = instrument.Kind;
            Waveform = Array.AsReadOnly(Array.Empty<int>());
            switch (instrument)
            {
                case NesNoiseInstrument noise:
                    NoiseMode = noise.NoiseMode;
                    break;
                case GbPulseInstrument pulse:
                    InitialVolume = pulse.InitialVolume;
                    EnvelopeIncreasing = pulse.EnvelopeIncreasing;
                    EnvelopeStepFrames = pulse.EnvelopeStepFrames;
                    break;
                case GbWaveInstrument wave:
                    OutputLevel = wave.OutputLevel;
                    Waveform = Array.AsReadOnly((int[])wave.Waveform.Clone());
                    break;
                case GbNoiseInstrument noise:
                    LfsrWidth = noise.LfsrWidth;
                    break;
            }
        }

        /// <summary>元ソングの音色識別子。</summary>
        public int Id { get; }
        /// <summary>音色の種類。</summary>
        public InstrumentKind Kind { get; }
        /// <summary>NES Noise の周期モード。</summary>
        public NoiseMode NoiseMode { get; }
        /// <summary>GB Noise の LFSR 幅。</summary>
        public int LfsrWidth { get; }
        /// <summary>GB Pulse の初期エンベロープ音量。</summary>
        public int InitialVolume { get; }
        /// <summary>GB Pulse のエンベロープが増加方向か。</summary>
        public bool EnvelopeIncreasing { get; }
        /// <summary>GB Pulse の音量変化間隔。</summary>
        public int EnvelopeStepFrames { get; }
        /// <summary>GB Wave の出力百分率。</summary>
        public int OutputLevel { get; }
        /// <summary>GB Wave の 32 個の 4 bit 値。それ以外は空。</summary>
        public IReadOnlyList<int> Waveform { get; }
    }
}
