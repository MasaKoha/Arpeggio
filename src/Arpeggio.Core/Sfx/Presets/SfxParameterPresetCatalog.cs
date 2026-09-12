using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Presets
{
    /// <summary>設計で固定した八種類の完全初期値を、従来のソング雛形とは別に提供する。</summary>
    public static class SfxParameterPresetCatalog
    {
        private const double LowFrequencyHz = 196;
        private const double NotificationFrequencyHz = 523.251131;
        private const double LaserFrequencyHz = 880;
        private const double JumpSlide = 128;
        private const double HitSlide = -80;
        private const double LaserSlide = -240;
        private const double PowerUpSlide = 24;
        private const double PowerUpDeltaSlide = 48;
        private const int CoinPitchChange = 7;
        private const int PowerUpPitchChange = 12;
        private const int SelectPitchChange = 5;
        private const double CoinPunch = 0.25;
        private const int HitNesPeriod = 3;
        private const int HitGameBoyWidth = 7;
        private const int HitGameBoySelection = 120;
        private const int HitSnesRate = 31;
        private const int ExplosionSnesRate = 18;

        /// <summary>指定チップの八種類を jump から select までの固定表示順で返す。</summary>
        public static IReadOnlyList<SfxParameterPresetDescription> GetAll(ChipKind chip)
        {
            SfxParameters defaults = SfxParameterCatalog.CreateDefaults(chip);
            return Array.AsReadOnly(new[]
            {
                Describe(chip, SfxPresetKind.Jump, "jump", "一声の上昇スライド", defaults with
                {
                    Tone = defaults.Tone with { BaseFrequencyHz = LowFrequencyHz,
                        Envelope = Envelope(1, 8), SlideSemitonesPerSecond = JumpSlide }
                }),
                Describe(chip, SfxPresetKind.Coin, "coin", "保持中に七半音上がる通知音", defaults with
                {
                    Tone = defaults.Tone with { BaseFrequencyHz = NotificationFrequencyHz,
                        Envelope = Envelope(3, 6) with { Punch = CoinPunch }, PitchChangeSemitones = CoinPitchChange }
                }),
                Describe(chip, SfxPresetKind.Hit, "hit", "下降トーンと短いノイズの衝撃音", CreateHit(defaults, chip)),
                Describe(chip, SfxPresetKind.Explosion, "explosion", "ノイズ単独の長い減衰", CreateExplosion(defaults, chip)),
                Describe(chip, SfxPresetKind.PowerUp, "powerup", "加速する上昇と一オクターブのジャンプ", defaults with
                {
                    Tone = defaults.Tone with { BaseFrequencyHz = LowFrequencyHz, Envelope = Envelope(6, 18),
                        SlideSemitonesPerSecond = PowerUpSlide, DeltaSlideSemitonesPerSecondSquared = PowerUpDeltaSlide,
                        PitchChangeSemitones = PowerUpPitchChange, PitchChangeTimeSeconds = Seconds(12) }
                }),
                Describe(chip, SfxPresetKind.Laser, "laser", "一声の高速下降スライド", defaults with
                {
                    Tone = defaults.Tone with { BaseFrequencyHz = LaserFrequencyHz,
                        Envelope = Envelope(0, 9), SlideSemitonesPerSecond = LaserSlide }
                }),
                Describe(chip, SfxPresetKind.Blip, "blip", "一声の短い通知音", defaults with
                {
                    Tone = defaults.Tone with { BaseFrequencyHz = NotificationFrequencyHz, Envelope = Envelope(1, 1) }
                }),
                Describe(chip, SfxPresetKind.Select, "select", "保持後に五半音上がる選択音", defaults with
                {
                    Tone = defaults.Tone with { BaseFrequencyHz = NotificationFrequencyHz,
                        Envelope = Envelope(3, 3), PitchChangeSemitones = SelectPitchChange }
                })
            });
        }

        /// <summary>識別子から指定チップの初期値を返す。None と未知値は拒否する。</summary>
        public static SfxParameterPresetDescription Get(SfxPresetKind kind, ChipKind chip)
        {
            foreach (SfxParameterPresetDescription preset in GetAll(chip))
            {
                if (preset.Kind == kind)
                {
                    return preset;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(kind), "有効なパラメータプリセットを指定してください。");
        }

        /// <summary>名前を大小文字非依存で解釈する。pickup と power-up を正式な用途へ写像する。</summary>
        public static SfxPresetKind Parse(string? name)
        {
            string normalized = name?.Trim() ?? string.Empty;
            if (string.Equals(normalized, "pickup", StringComparison.OrdinalIgnoreCase))
            {
                return SfxPresetKind.Coin;
            }
            if (string.Equals(normalized, "power-up", StringComparison.OrdinalIgnoreCase))
            {
                return SfxPresetKind.PowerUp;
            }
            foreach (SfxParameterPresetDescription preset in GetAll(ChipKind.Nes))
            {
                if (string.Equals(normalized, preset.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return preset.Kind;
                }
            }
            throw SfxParameterException.Invalid("preset", "未知のパラメータプリセットです。");
        }

        private static SfxParameterPresetDescription Describe(ChipKind chip, SfxPresetKind kind,
            string name, string description, SfxParameters parameters)
        {
            return new SfxParameterPresetDescription
            {
                Chip = chip, Kind = kind, Name = name, Description = description,
                Parameters = SfxParameterValidator.Normalize(parameters, chip)
            };
        }

        private static SfxParameters CreateHit(SfxParameters defaults, ChipKind chip)
        {
            SfxParameters parameters = defaults with
            {
                Tone = defaults.Tone with { BaseFrequencyHz = LowFrequencyHz,
                    Envelope = Envelope(0, 9), SlideSemitonesPerSecond = HitSlide },
                Noise = defaults.Noise with { Enabled = true, Envelope = Envelope(0, 4) }
            };
            return chip switch
            {
                ChipKind.Nes => parameters with { Nes = defaults.Nes! with
                    { NoiseMode = NoiseMode.Short, NoisePeriodIndex = HitNesPeriod } },
                ChipKind.GameBoy => parameters with { GameBoy = defaults.GameBoy! with
                    { NoiseWidth = HitGameBoyWidth, NoiseSelection = HitGameBoySelection } },
                _ => parameters with { Snes = defaults.Snes! with { NoiseRate = HitSnesRate } }
            };
        }

        private static SfxParameters CreateExplosion(SfxParameters defaults, ChipKind chip)
        {
            SfxParameters parameters = defaults with
            {
                Tone = defaults.Tone with { Enabled = false },
                Noise = defaults.Noise with { Enabled = true, Envelope = Envelope(1, 23) }
            };
            return chip == ChipKind.Snes
                ? parameters with { Snes = defaults.Snes! with { NoiseRate = ExplosionSnesRate } } : parameters;
        }

        private static SfxEnvelopeParameters Envelope(int sustainFrames, int decayFrames)
            => new SfxEnvelopeParameters { SustainSeconds = Seconds(sustainFrames), DecaySeconds = Seconds(decayFrames) };

        private static double Seconds(int frames)
            => Math.Round((double)frames / SfxParameterValidator.ControlFramesPerSecond,
                SfxParameterValidator.DecimalPlaces, MidpointRounding.AwayFromZero);
    }
}
