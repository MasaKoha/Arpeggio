using System;
using System.Text.Json.Serialization;
using Arpeggio.Core.Synthesis.Snes;

namespace Arpeggio.Core.Instruments
{
    /// <summary>SnesSample チャンネルの音色。</summary>
    public sealed class SnesSampleInstrument : Instrument
    {
        private string? _sampleData;
        private int _sampleCount;

        private bool _loop = true;
        private int _loopStart;
        private int _loopEnd;

        internal BrrSample? PreparedSample { get; private set; }
        internal string? SampleDataError { get; private set; }

        /// <summary>音色の種類。</summary>
        [JsonIgnore]
        public override InstrumentKind Kind => InstrumentKind.SnesSample;

        /// <summary>生成する波形種別。</summary>
        [JsonPropertyOrder(0)]
        public SnesWaveformKind Waveform { get; set; } = SnesWaveformKind.Sine;

        /// <summary>サンプルをループ再生するか。</summary>
        [JsonPropertyOrder(1)]
        public bool Loop
        {
            get => _loop;
            set
            {
                _loop = value;
                RefreshLoop();
            }
        }

        /// <summary>ADSR エンベロープ。</summary>
        [JsonPropertyOrder(2)]
        public AdsrEnvelope Envelope { get; set; } = new AdsrEnvelope(0, 0, 1, 0.05);

        /// <summary>エコー送り量（0〜1）。</summary>
        [JsonPropertyOrder(3)]
        public double EchoSend { get; set; }

        /// <summary>左右定位（-1〜1）。</summary>
        [JsonPropertyOrder(4)]
        public double Pan { get; set; }

        /// <summary>任意のアルペジオマクロ。</summary>
        [JsonPropertyOrder(5)]
        public Macro? ArpeggioMacro { get; set; }

        /// <summary>任意のピッチマクロ。</summary>
        [JsonPropertyOrder(6)]
        public Macro? PitchMacro { get; set; }

        /// <summary>little-endian PCM 16 bit モノラルの Base64。null は合成波形。代入時に再生キャッシュを準備する。</summary>
        [JsonPropertyOrder(7)]
        public string? SampleData
        {
            get => _sampleData;
            set
            {
                float[] decoded = Array.Empty<float>();
                string? error = null;
                try
                {
                    if (value != null)
                    {
                        decoded = SampleDataCodec.ToFloat(SampleDataCodec.Decode(value));
                    }
                }
                catch (Exception exception) when (exception is FormatException or ArgumentException)
                {
                    // JSON のプロパティ順に依存せず、音色全体が揃ってから Validator が拒否する。
                    error = exception.Message;
                }
                _sampleData = value;
                _sampleCount = decoded.Length;
                SampleDataError = error;
                PreparedSample = decoded.Length == 0 ? null : BrrSample.Create(decoded, _loop, _loopStart, _loopEnd);
            }
        }

        /// <summary>埋め込みサンプルの元レート（Hz）。</summary>
        [JsonPropertyOrder(8)]
        public int SampleRate { get; set; } = 44100;

        /// <summary>元サンプルの MIDI 音程。既定は C4。</summary>
        [JsonPropertyOrder(9)]
        public int RootMidiNote { get; set; } = 60;

        /// <summary>ループ開始位置。モノラルのサンプル単位。</summary>
        [JsonPropertyOrder(10)]
        public int LoopStart
        {
            get => _loopStart;
            set
            {
                _loopStart = value;
                RefreshLoop();
            }
        }

        /// <summary>ループ終端（含まない）。0 はサンプル末尾。</summary>
        [JsonPropertyOrder(11)]
        public int LoopEnd
        {
            get => _loopEnd;
            set
            {
                _loopEnd = value;
                RefreshLoop();
            }
        }

        /// <summary>任意の DSP ADSR レジスタ。指定時は秒指定より優先する。</summary>
        [JsonPropertyOrder(12)]
        public SnesAdsrRegisters? AdsrRegisters { get; set; }

        /// <summary>直前ボイスの出力でピッチを変調する（ボイス 1〜7）。</summary>
        [JsonPropertyOrder(13)]
        public bool PitchModulation { get; set; }

        /// <summary>サンプルの代わりに DSP ノイズを使う。</summary>
        [JsonPropertyOrder(14)]
        public bool NoiseEnabled { get; set; }

        /// <summary>ノイズ速度（0〜31、0 は停止）。</summary>
        [JsonPropertyOrder(15)]
        public int NoiseRate { get; set; } = SnesRateTable.MaximumRate;

        /// <summary>Base64 を含まない表示用の短いサンプル説明。</summary>
        [JsonIgnore]
        public string SampleSummary => SampleData is null ? string.Empty : $"sample {SampleCount} smp @ {SampleRate} Hz";

        /// <summary>検証済み埋め込みサンプルの要素数。合成波形または不正データでは 0。</summary>
        [JsonIgnore]
        public int SampleCount => _sampleCount;

        private void RefreshLoop()
        {
            if (PreparedSample != null)
            {
                PreparedSample = PreparedSample.WithLoop(_loop, _loopStart, _loopEnd);
            }
        }
    }
}
