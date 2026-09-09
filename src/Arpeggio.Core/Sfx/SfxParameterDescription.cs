using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx
{
    /// <summary>全フロントエンドで共有する入力型・単位・範囲・操作刻み。</summary>
    public sealed class SfxParameterDescription
    {
        internal SfxParameterDescription(string path, ChipKind chip,
            Func<SfxParameters, object> read, Func<SfxParameters, object, SfxParameters> replace)
        {
            Path = path;
            Chip = chip;
            Read = read;
            Replace = replace;
            DefaultValue = read(SfxParameterCatalog.CreateDefaults(chip == ChipKind.None ? ChipKind.Nes : chip));
        }

        /// <summary>parameters を基点とする正規 JSON パス。</summary>
        public string Path { get; }

        /// <summary>対象チップ。None は三チップ共通。</summary>
        public ChipKind Chip { get; }

        /// <summary>受理する JSON 値の種類。</summary>
        public SfxParameterValueKind ValueKind { get; internal init; }

        /// <summary>保存値の単位。無次元・真偽値・文字列選択は空文字。</summary>
        public string Unit { get; internal init; } = string.Empty;

        /// <summary>共通仕様の初期値。文字列選択は正式名。</summary>
        public object DefaultValue { get; }

        /// <summary>数値の下限（含む）。非数値は null。</summary>
        public double? Minimum { get; internal init; }

        /// <summary>数値の上限（含む）。非数値は null。</summary>
        public double? Maximum { get; internal init; }

        /// <summary>連続範囲の外で、無効値としてゼロを受理するか。</summary>
        public bool AllowsZero { get; internal init; }

        /// <summary>離散的な許可値。空なら値の種類と範囲だけで制限する。</summary>
        public IReadOnlyList<object> Choices { get; internal init; } = Array.Empty<object>();

        /// <summary>UI キー操作の刻み。選択項目は選択肢一段。入力の量子化には使わない。</summary>
        public double Step { get; internal init; } = 1;

        /// <summary>刻みの単位。基準周波数だけ保存単位と異なる。</summary>
        public string StepUnit { get; internal init; } = string.Empty;

        /// <summary>微調整の刻み。周波数では0.01半音、整数と選択値では一段。</summary>
        public double FineStep { get; internal init; } = 1;

        /// <summary>対数軸で表示するか。</summary>
        public bool IsLogarithmic { get; internal init; }

        /// <summary>意味と条件付き制限の説明。</summary>
        public string Description { get; internal init; } = string.Empty;

        /// <summary>指定チップで入力可能か。レイヤー無効時も保存と検証は可能。</summary>
        public bool IsSupported(ChipKind chip)
        {
            return (chip == ChipKind.Nes || chip == ChipKind.GameBoy || chip == ChipKind.Snes)
                && (Chip == ChipKind.None || Chip == chip);
        }

        internal Func<SfxParameters, object> Read { get; }
        internal Func<SfxParameters, object, SfxParameters> Replace { get; }
    }
}
