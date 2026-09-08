using System;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>11 bit 音量を DSP レート表で進める ADSR 状態機械。</summary>
    public sealed class SnesEnvelope
    {
        private const int MaximumLevel = 0x7FF;
        private const int AttackStep = 0x20;
        private const int FastAttackStep = 0x400;
        private const int ReleaseStep = 8;
        private const int SustainStep = 0x100;
        private const int MaximumAttack = 15;
        private const int MaximumDecay = 7;
        private const int DecayRateOffset = 16;
        private const int ExponentialShift = 8;
        private const int AttackUpdateCount = 64;
        private SnesAdsrRegisters _registers;
        private EnvelopeStage _stage;
        private int _remainingSamples;
        private int _level;

        private enum EnvelopeStage
        {
            None = 0,
            Attack = 1,
            Decay = 2,
            Sustain = 3,
            Release = 4
        }

        /// <summary>停止状態か。</summary>
        public bool IsSilent => _stage == EnvelopeStage.None;
        /// <summary>現在の 11 bit 音量。</summary>
        public int Level => _level;

        /// <summary>発音時にレジスタと更新位相を初期化する。</summary>
        public void Start(SnesAdsrRegisters registers)
        {
            _registers = registers;
            _stage = EnvelopeStage.Attack;
            _remainingSamples = SnesRateTable.GetPeriod(registers.Attack * 2 + 1);
            _level = 0;
        }

        /// <summary>固定速度のリリースへ移る。</summary>
        public void Release()
        {
            if (_stage != EnvelopeStage.None)
            {
                _stage = EnvelopeStage.Release;
            }
        }

        /// <summary>一 DSP サンプルだけ進めた正規化音量を返す。</summary>
        public double ReadSample()
        {
            // perf: 秒換算・レジスタ探索は NoteOn で済ませ、整数状態だけ進める。
            if (_stage == EnvelopeStage.None)
            {
                return 0;
            }
            if (_stage == EnvelopeStage.Release)
            {
                _level = Math.Max(0, _level - ReleaseStep);
                if (_level == 0)
                {
                    _stage = EnvelopeStage.None;
                }
                return _level / (double)MaximumLevel;
            }
            int rate = GetRate();
            if (rate == 0 || --_remainingSamples > 0)
            {
                return _level / (double)MaximumLevel;
            }
            AdvanceLevel();
            _remainingSamples = SnesRateTable.GetPeriod(GetRate());
            return _level / (double)MaximumLevel;
        }

        /// <summary>秒指定に最も近い attack / decay と保持レベルを求める。保持中の減衰は無効。</summary>
        public static SnesAdsrRegisters Quantize(AdsrEnvelope envelope)
        {
            int sustainLevel = Math.Clamp((int)Math.Round(envelope.SustainLevel * (MaximumDecay + 1)) - 1, 0, MaximumDecay);
            int attack = FindAttack(envelope.AttackSeconds);
            int decayUpdates = CountDecayUpdates(sustainLevel);
            int decay = MaximumDecay;
            double bestError = double.PositiveInfinity;
            for (int candidate = 0; candidate <= MaximumDecay; candidate++)
            {
                double seconds = decayUpdates * SnesRateTable.GetPeriod(candidate * 2 + DecayRateOffset) / (double)SnesRateTable.SampleRate;
                double error = Math.Abs(seconds - envelope.DecaySeconds);
                if (error <= bestError)
                {
                    bestError = error;
                    decay = candidate;
                }
            }
            return new SnesAdsrRegisters(attack, decay, sustainLevel, 0);
        }

        private int GetRate()
        {
            return _stage switch
            {
                EnvelopeStage.Attack => _registers.Attack * 2 + 1,
                EnvelopeStage.Decay => _registers.Decay * 2 + DecayRateOffset,
                _ => _registers.SustainRate
            };
        }

        private void AdvanceLevel()
        {
            if (_stage == EnvelopeStage.Attack)
            {
                _level += _registers.Attack == MaximumAttack ? FastAttackStep : AttackStep;
                if (_level >= MaximumLevel)
                {
                    _level = MaximumLevel;
                    _stage = _registers.SustainLevel == MaximumDecay ? EnvelopeStage.Sustain : EnvelopeStage.Decay;
                }
                return;
            }
            _level = Decrease(_level);
            if (_stage == EnvelopeStage.Decay && _level <= GetSustainThreshold(_registers.SustainLevel))
            {
                _stage = EnvelopeStage.Sustain;
            }
            if (_level == 0)
            {
                _stage = EnvelopeStage.None;
            }
        }

        private static int FindAttack(double seconds)
        {
            int best = MaximumAttack;
            double bestError = double.PositiveInfinity;
            for (int candidate = 0; candidate <= MaximumAttack; candidate++)
            {
                int updates = candidate == MaximumAttack ? 2 : AttackUpdateCount;
                double duration = updates * SnesRateTable.GetPeriod(candidate * 2 + 1) / (double)SnesRateTable.SampleRate;
                double error = Math.Abs(duration - seconds);
                if (error <= bestError)
                {
                    bestError = error;
                    best = candidate;
                }
            }
            return best;
        }

        private static int CountDecayUpdates(int sustainLevel)
        {
            int level = MaximumLevel;
            int updates = 0;
            while (level > GetSustainThreshold(sustainLevel))
            {
                level = Decrease(level);
                updates++;
            }
            return updates;
        }

        private static int GetSustainThreshold(int sustainLevel) => (sustainLevel + 1) * SustainStep - 1;
        private static int Decrease(int level) => Math.Max(0, level - 1 - ((level - 1) >> ExponentialShift));
    }
}
