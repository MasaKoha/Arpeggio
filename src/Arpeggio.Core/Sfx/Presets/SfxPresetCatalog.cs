using System;
using System.Collections.Generic;

namespace Arpeggio.Core.Sfx.Presets
{
    /// <summary>効果音の名前・説明・長さを CLI / MCP に共通提供する。</summary>
    public static class SfxPresetCatalog
    {
        /// <summary>全八種類のカタログを返す。</summary>
        public static IReadOnlyList<SfxPresetDescription> GetAll()
        {
            return Array.AsReadOnly(new[]
            {
                new SfxPresetDescription { Kind = SfxPresetKind.Jump, Name = "jump", Description = "Pulse の上昇スライド（150 ms）", LengthTicks = 18 },
                new SfxPresetDescription { Kind = SfxPresetKind.Coin, Name = "coin", Description = "二音の上昇と短いアルペジオ（150 ms）", LengthTicks = 18 },
                new SfxPresetDescription { Kind = SfxPresetKind.Hit, Name = "hit", Description = "短周期ノイズと下降 Pulse（150 ms）", LengthTicks = 18 },
                new SfxPresetDescription { Kind = SfxPresetKind.Explosion, Name = "explosion", Description = "長周期ノイズの音量減衰（400 ms）", LengthTicks = 48 },
                new SfxPresetDescription { Kind = SfxPresetKind.PowerUp, Name = "powerup", Description = "四段の上昇アルペジオ（400 ms）", LengthTicks = 48 },
                new SfxPresetDescription { Kind = SfxPresetKind.Laser, Name = "laser", Description = "高速下降スライド（150 ms）", LengthTicks = 18 },
                new SfxPresetDescription { Kind = SfxPresetKind.Blip, Name = "blip", Description = "一音の短い通知（約 41.7 ms）", LengthTicks = 5 },
                new SfxPresetDescription { Kind = SfxPresetKind.Select, Name = "select", Description = "二音の短い上昇（100 ms）", LengthTicks = 12 }
            });
        }

        /// <summary>大小文字を区別せず名前を読む。power-up も受け付ける。</summary>
        public static SfxPresetKind Parse(string name)
        {
            string normalized = name.Trim();
            if (string.Equals(normalized, "power-up", StringComparison.OrdinalIgnoreCase))
            {
                return SfxPresetKind.PowerUp;
            }
            foreach (SfxPresetDescription preset in GetAll())
            {
                if (string.Equals(preset.Name, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return preset.Kind;
                }
            }
            throw new ArgumentException($"不明なプリセット '{name}' です。sfx list で名前を確認してください。", nameof(name));
        }

        /// <summary>識別子から名前・説明・長さを取得する。None は拒否する。</summary>
        public static SfxPresetDescription Get(SfxPresetKind kind)
        {
            foreach (SfxPresetDescription preset in GetAll())
            {
                if (preset.Kind == kind)
                {
                    return preset;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(kind), "有効な効果音プリセットを指定してください。");
        }
    }
}
