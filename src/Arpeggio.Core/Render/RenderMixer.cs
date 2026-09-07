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
        private readonly ChipKind _chip;
        private readonly Track[] _tracks;
        private readonly IChannelSynthesizer[] _synthesizers;
        private readonly SnesEcho _echo;
        private readonly float[] _leftLevels;
        private readonly float[] _rightLevels;

        internal RenderMixer(Song song, int sampleRate, Track[] tracks, IChannelSynthesizer[] synthesizers)
        {
            _chip = song.Chip;
            _tracks = tracks;
            _synthesizers = synthesizers;
            _echo = new SnesEcho(sampleRate, song.SnesEcho);
            _leftLevels = new float[tracks.Length];
            _rightLevels = new float[tracks.Length];
        }

        internal void Mix(ReadOnlySpan<float> samples, out float left, out float right)
        {
            left = 0;
            right = 0;
            float sendLeft = 0;
            float sendRight = 0;
            // perf: 配線と作業領域を構築時に固定し、合成時は値だけを渡す。
            for (int index = 0; index < samples.Length; index++)
            {
                float sample = _tracks[index].Muted ? 0 : samples[index];
                double pan = _tracks[index].Pan;
                if (_synthesizers[index] is SnesVoiceSynthesizer voice)
                {
                    pan = Math.Clamp(pan + voice.Pan, -1, 1);
                    SnesMixer.Add(sample, pan, ref left, ref right);
                    SnesMixer.Add((float)(sample * voice.EchoSend), pan, ref sendLeft, ref sendRight);
                }
                else if (_chip == ChipKind.GameBoy)
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
            if (_chip == ChipKind.Snes)
            {
                _echo.Process(sendLeft, sendRight, out float wetLeft, out float wetRight);
                left += wetLeft;
                right += wetRight;
            }
            left = Math.Clamp(left, -1, 1);
            right = Math.Clamp(right, -1, 1);
        }

        private static float MixNes(float[] samples) => NesMixer.Mix(samples[0], samples[1], samples[2], samples[3], samples[4]);
    }
}
