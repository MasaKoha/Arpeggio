using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>内蔵／埋め込みサンプルの線形補間再生とサンプル単位の ADSR。</summary>
    public sealed class SnesVoiceSynthesizer : ChannelSynthesizer
    {
        private const int WaveformCount = 6;
        private const double SilenceEpsilon = 0.0000001;
        private readonly float[][] _waveforms = new float[WaveformCount][];
        private float[] _waveform = Array.Empty<float>();
        private AdsrEnvelope _envelope;
        private bool _loop;
        private bool _hasEmbeddedSample;
        private int _sourceSampleRate;
        private int _rootMidiNote;
        private int _loopStart;
        private int _loopEnd;
        private double _samplePosition;
        private bool _releasing;
        private long _ageSamples;
        private long _releaseSamples;
        private double _level;
        private double _releaseLevel;

        /// <summary>全内蔵波形を事前確保して指定レートのボイスを作る。</summary>
        public SnesVoiceSynthesizer(int sampleRate = 44100) : base(sampleRate, ChipKind.Snes, ChannelKind.Sample)
        {
            for (int index = 0; index < _waveforms.Length; index++)
            {
                _waveforms[index] = SnesWaveformBuilder.Build((SnesWaveformKind)(index + 1));
            }
        }

        /// <summary>音色別のエコー送り量。</summary>
        public double EchoSend { get; private set; }
        /// <summary>音色別の左右定位。</summary>
        public double Pan { get; private set; }

        /// <summary>事前生成した波形を選び、ADSR を先頭へ戻す。</summary>
        protected override void ConfigureInstrument(Instrument instrument)
        {
            var sample = (SnesSampleInstrument)instrument;
            ConfigureMacros(null, sample.ArpeggioMacro, sample.PitchMacro);
            _hasEmbeddedSample = sample.SampleData != null;
            _waveform = _hasEmbeddedSample ? sample.DecodedSamples : _waveforms[(int)sample.Waveform - 1];
            _sourceSampleRate = sample.SampleRate;
            _rootMidiNote = sample.RootMidiNote;
            _loopStart = sample.LoopStart;
            _loopEnd = sample.LoopEnd == 0 ? _waveform.Length : sample.LoopEnd;
            _samplePosition = 0;
            _envelope = sample.Envelope;
            _loop = sample.Loop;
            _releasing = false;
            _ageSamples = 0;
            _releaseSamples = 0;
            _level = 0;
            EchoSend = sample.EchoSend;
            Pan = sample.Pan;
        }

        /// <summary>現在の音量を起点にリリースへ移る。</summary>
        public override void NoteOff()
        {
            if (_releasing || !IsActive)
            {
                return;
            }
            _releasing = true;
            _releaseLevel = _level;
            _releaseSamples = 0;
            if (_envelope.ReleaseSeconds <= SilenceEpsilon)
            {
                IsActive = false;
            }
        }

        /// <summary>補間波形と ADSR を掛け合わせる。</summary>
        protected override double ReadSample()
        {
            // perf: NoteOn を含めて既存の波形配列を参照するだけで再生成しない。
            if (_hasEmbeddedSample)
            {
                return ReadEmbeddedSample();
            }
            double position = Phase * _waveform.Length;
            int index = (int)position;
            int next = (index + 1) % _waveform.Length;
            double interpolated = _waveform[index] + (_waveform[next] - _waveform[index]) * (position - index);
            _level = GetEnvelopeLevel();
            _ageSamples++;
            if (!_loop && Phase + PhaseIncrement >= 1)
            {
                IsActive = false;
            }
            return interpolated * _level * Volume;
        }

        /// <summary>元サンプルのレートと基準音を再生位置の増分に織り込む。</summary>
        protected override double GetPhaseIncrement()
        {
            return _hasEmbeddedSample
                ? PitchTable.GetSnesSampleRatio(MidiNote, _rootMidiNote) * _sourceSampleRate / SampleRate
                : base.GetPhaseIncrement();
        }

        private double ReadEmbeddedSample()
        {
            // perf: 位置・補間・ループの更新は既存配列と値型だけで完結する。
            int end = _loop ? _loopEnd : _waveform.Length;
            int index = (int)_samplePosition;
            int next = index + 1;
            if (next >= end)
            {
                next = _loop ? _loopStart : index;
            }
            double interpolated = _waveform[index] + (_waveform[next] - _waveform[index]) * (_samplePosition - index);
            _samplePosition += PhaseIncrement;
            if (_samplePosition >= end)
            {
                if (_loop)
                {
                    _samplePosition = _loopStart + (_samplePosition - _loopStart) % (_loopEnd - _loopStart);
                }
                else
                {
                    IsActive = false;
                }
            }
            _level = GetEnvelopeLevel();
            _ageSamples++;
            return interpolated * _level * Volume;
        }

        private double GetEnvelopeLevel()
        {
            if (_releasing)
            {
                double remaining = 1 - _releaseSamples++ / Math.Max(1, _envelope.ReleaseSeconds * SampleRate);
                if (remaining <= SilenceEpsilon)
                {
                    IsActive = false;
                    return 0;
                }
                return _releaseLevel * remaining;
            }
            double seconds = (double)_ageSamples / SampleRate;
            if (seconds < _envelope.AttackSeconds)
            {
                return seconds / _envelope.AttackSeconds;
            }
            double decayTime = seconds - _envelope.AttackSeconds;
            if (decayTime < _envelope.DecaySeconds)
            {
                return 1 - (1 - _envelope.SustainLevel) * decayTime / _envelope.DecaySeconds;
            }
            return _envelope.SustainLevel;
        }
    }
}
