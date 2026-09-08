using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>BRR キャッシュ・ガウス補間・ADSR・ノイズを 32 kHz で合成するボイス。</summary>
    public sealed class SnesVoiceSynthesizer : ChannelSynthesizer
    {
        private const int WaveformCount = 6;
        private const double PcmScale = 32768.0;
        private const int MaximumVolume = 127;
        private const int ModulationShift = 15;
        private const double SemitonesPerOctave = 12;
        private readonly BrrSample[] _waveforms = new BrrSample[WaveformCount];
        private readonly SnesEnvelope _envelope = new SnesEnvelope();
        private readonly SnesNoiseGenerator _noise = new SnesNoiseGenerator();
        private BrrSample? _sample;
        private bool _loop;
        private bool _looped;
        private bool _sampleEnded;
        private bool _hasSampleSource;
        private bool _noiseEnabled;
        private bool _pitchModulation;
        private bool _externalClock;
        private int _noiseRate;
        private int _sourceSampleRate;
        private int _rootMidiNote;
        private int _loopStart;
        private int _loopEnd;
        private int _pitchRegister;
        private double _samplePosition;
        private long _outputClock;
        private double _previousOutput;
        private double _currentOutput;

        /// <summary>内蔵波形を BRR 往復して事前確保する。</summary>
        public SnesVoiceSynthesizer(int sampleRate = 44100) : base(sampleRate, ChipKind.Snes, ChannelKind.Sample)
        {
            for (int index = 0; index < _waveforms.Length; index++)
            {
                _waveforms[index] = BrrSample.Create(SnesWaveformBuilder.Build((SnesWaveformKind)(index + 1), PitchTable.SnesWaveformLength));
            }
        }

        /// <summary>音色別のエコー送り量。</summary>
        public double EchoSend { get; private set; }
        /// <summary>音色別の左右定位。</summary>
        public double Pan { get; private set; }
        /// <summary>変調前の 14 bit ピッチレジスタ。</summary>
        public int PitchRegister => _pitchRegister;

        internal void UseExternalClock() => _externalClock = true;

        /// <summary>準備済みキャッシュを選び、発音状態を初期化する。</summary>
        protected override void ConfigureInstrument(Instrument instrument)
        {
            var sample = (SnesSampleInstrument)instrument;
            ConfigureMacros(null, sample.ArpeggioMacro, sample.PitchMacro);
            _hasSampleSource = sample.SampleData != null || sample.Preset != null;
            _sample = sample.SampleData != null ? sample.PreparedSample : sample.PreparedPreset;
            if (!_hasSampleSource)
            {
                _sample = _waveforms[(int)sample.Waveform - 1];
            }
            _sourceSampleRate = sample.SampleRate;
            _rootMidiNote = sample.RootMidiNote;
            _loop = sample.Loop;
            _loopStart = _hasSampleSource ? _sample!.LoopStart : 0;
            _loopEnd = _hasSampleSource && _loop ? _sample!.LoopEnd : _sample!.Samples.Length;
            _samplePosition = 0;
            _looped = false;
            _sampleEnded = false;
            _envelope.Start(sample.AdsrRegisters ?? SnesEnvelope.Quantize(sample.Envelope));
            _noise.Reset();
            _noiseEnabled = sample.NoiseEnabled;
            _noiseRate = sample.NoiseRate;
            _pitchModulation = sample.PitchModulation;
            _outputClock = SampleRate;
            _previousOutput = 0;
            _currentOutput = 0;
            EchoSend = sample.EchoSend;
            Pan = sample.Pan;
        }

        /// <summary>現在音量から DSP 固定速度のリリースへ移る。</summary>
        public override void NoteOff() => _envelope.Release();

        /// <summary>同じ DSP 時刻の前ボイス出力を受け、一サンプル進める。単独時は変調源を 0 とする。</summary>
        public short ReadDspSample(short previousVoiceOutput = 0)
        {
            // perf: BRR は音色設定時に処理済み。音声処理中はキャッシュと整数状態だけを使う。
            if (!IsActive || _sampleEnded)
            {
                IsActive = false;
                return 0;
            }
            double level = _envelope.ReadSample();
            if (_envelope.IsSilent)
            {
                IsActive = false;
                return 0;
            }
            double sample = _noiseEnabled ? _noise.ReadSample(_noiseRate) : ReadWaveform();
            if (!_noiseEnabled)
            {
                int pitch = _pitchModulation ? ModulatePitch(_pitchRegister, previousVoiceOutput) : _pitchRegister;
                AdvanceSample(pitch);
            }
            int volume = (int)Math.Round(Volume * MaximumVolume);
            return (short)Math.Clamp(Math.Round(sample * level * volume / MaximumVolume * PcmScale), short.MinValue, short.MaxValue);
        }

        /// <summary>前ボイスの signed 16 bit 出力でピッチを変調し、14 bit 範囲へ飽和する。</summary>
        public static int ModulatePitch(int pitch, short previousVoiceOutput)
            => Math.Clamp(pitch + (pitch * previousVoiceOutput >> ModulationShift), 0, PitchTable.MaximumSnesPitch);

        /// <summary>単独ボイスの出力時間軸へ線形補間する。全曲時はミキサーの共通クロックに任せる。</summary>
        protected override double ReadSample()
        {
            // perf: 二点の履歴と整数クロックを保持し、バッファ分割に依存しない補間を行う。
            if (_externalClock)
            {
                return 0;
            }
            while (_outputClock >= SampleRate)
            {
                _outputClock -= SampleRate;
                _previousOutput = _currentOutput;
                _currentOutput = ReadDspSample() / PcmScale;
            }
            double result = _previousOutput + (_currentOutput - _previousOutput) * _outputClock / SampleRate;
            _outputClock += SnesRateTable.SampleRate;
            return result;
        }

        /// <summary>元レートを含む増分を 32 kHz の 14 bit レジスタに量子化する。</summary>
        protected override double GetPhaseIncrement()
        {
            double step = _hasSampleSource
                ? Math.Pow(2, (MidiNote - _rootMidiNote) / SemitonesPerOctave) * _sourceSampleRate / SnesRateTable.SampleRate
                : PitchTable.Quantize(ChipKind.Snes, ChannelKind.Sample, MidiNote) * PitchTable.SnesWaveformLength / SnesRateTable.SampleRate;
            _pitchRegister = PitchTable.GetSnesPitchRegister(step);
            return 0;
        }

        private double ReadWaveform()
        {
            int position = (int)_samplePosition;
            return GaussianInterpolator.Interpolate(ReadAt(position - 1), ReadAt(position), ReadAt(position + 1),
                ReadAt(position + 2), _samplePosition - position);
        }

        private float ReadAt(int position)
        {
            if (_loop && (position >= _loopEnd || (_looped && position < _loopStart)))
            {
                int length = _loopEnd - _loopStart;
                position = _loopStart + ((position - _loopStart) % length + length) % length;
            }
            if (position < 0)
            {
                return 0;
            }
            return _sample!.Samples[Math.Min(position, _loopEnd - 1)];
        }

        private void AdvanceSample(int pitch)
        {
            _samplePosition += pitch / (double)PitchTable.SnesUnityPitch;
            if (_samplePosition < _loopEnd)
            {
                return;
            }
            if (_loop)
            {
                _samplePosition = _loopStart + (_samplePosition - _loopStart) % (_loopEnd - _loopStart);
                _looped = true;
            }
            else
            {
                _sampleEnded = true;
            }
        }
    }
}
