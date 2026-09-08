using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Synthesis;
using Arpeggio.Core.Synthesis.GameBoy;
using Arpeggio.Core.Synthesis.Nes;
using Arpeggio.Core.Synthesis.Snes;

namespace Arpeggio.Core.Render
{
    /// <summary>チップ別のミキサーへ定位と送り音を配線する。</summary>
    internal sealed class RenderMixer
    {
        private const float SnesPcmScale = 32768;
        private readonly ChipKind _chip;
        private readonly Track[] _tracks;
        private readonly SnesEcho _echo;
        private readonly float[] _leftLevels;
        private readonly float[] _rightLevels;
        private readonly SnesVoiceSynthesizer[] _snesVoices;
        private readonly int _sampleRate;
        private long _dspClock;
        private float _previousLeft;
        private float _previousRight;
        private float _currentLeft;
        private float _currentRight;

        internal RenderMixer(Song song, int sampleRate, Track[] tracks, IChannelSynthesizer[] synthesizers)
        {
            _chip = song.Chip;
            _tracks = tracks;
            _sampleRate = sampleRate;
            _dspClock = sampleRate;
            _echo = new SnesEcho(SnesRateTable.SampleRate, song.SnesEcho);
            _snesVoices = new SnesVoiceSynthesizer[_chip == ChipKind.Snes ? synthesizers.Length : 0];
            for (int index = 0; index < _snesVoices.Length; index++)
            {
                _snesVoices[index] = (SnesVoiceSynthesizer)synthesizers[index];
                _snesVoices[index].UseExternalClock();
            }
            _leftLevels = new float[tracks.Length];
            _rightLevels = new float[tracks.Length];
        }

        internal void Mix(ReadOnlySpan<float> samples, out float left, out float right)
        {
            if (_chip == ChipKind.Snes)
            {
                MixSnes(out left, out right);
                return;
            }
            left = 0;
            right = 0;
            // perf: 配線と作業領域を構築時に固定し、合成時は値だけを渡す。
            for (int index = 0; index < samples.Length; index++)
            {
                float sample = _tracks[index].Muted ? 0 : samples[index];
                double pan = _tracks[index].Pan;
                if (_chip == ChipKind.GameBoy)
                {
                    GbMixer.Add(sample, pan, ref left, ref right);
                }
                else
                {
                    _leftLevels[index] = (float)(sample * Math.Min(1, 1 - pan));
                    _rightLevels[index] = (float)(sample * Math.Min(1, 1 + pan));
                }
            }
            if (_chip == ChipKind.Nes)
            {
                left = MixNes(_leftLevels);
                right = MixNes(_rightLevels);
            }
            left = Math.Clamp(left, -1, 1);
            right = Math.Clamp(right, -1, 1);
        }

        private void MixSnes(out float left, out float right)
        {
            // perf: 全ボイスを同じ DSP 時刻で順に進め、ミックス後の二点だけを補間する。
            while (_dspClock >= _sampleRate)
            {
                _dspClock -= _sampleRate;
                _previousLeft = _currentLeft;
                _previousRight = _currentRight;
                RenderSnesFrame(out _currentLeft, out _currentRight);
            }
            double fraction = (double)_dspClock / _sampleRate;
            left = SnesMixer.Clamp16(_previousLeft + (_currentLeft - _previousLeft) * fraction);
            right = SnesMixer.Clamp16(_previousRight + (_currentRight - _previousRight) * fraction);
            _dspClock += SnesRateTable.SampleRate;
        }

        private void RenderSnesFrame(out float left, out float right)
        {
            left = 0;
            right = 0;
            float sendLeft = 0;
            float sendRight = 0;
            short previousVoiceOutput = 0;
            for (int index = 0; index < _snesVoices.Length; index++)
            {
                SnesVoiceSynthesizer voice = _snesVoices[index];
                short output = voice.ReadDspSample(previousVoiceOutput);
                previousVoiceOutput = output;
                float sample = _tracks[index].Muted ? 0 : output / SnesPcmScale;
                double pan = Math.Clamp(_tracks[index].Pan + voice.Pan, -1, 1);
                SnesMixer.Add(sample, pan, ref left, ref right);
                SnesMixer.Add((float)(sample * voice.EchoSend), pan, ref sendLeft, ref sendRight);
            }
            _echo.Process(sendLeft, sendRight, out float wetLeft, out float wetRight);
            left = SnesMixer.Clamp16(left + wetLeft);
            right = SnesMixer.Clamp16(right + wetRight);
        }

        private static float MixNes(float[] samples) => NesMixer.Mix(samples[0], samples[1], samples[2], samples[3], samples[4]);
    }
}
