using System;
using System.Collections.Generic;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Formats;

namespace Arpeggio.Daw.Presenters.Export
{
    /// <summary>チップに適合する書き出し候補と手入力された拡張子を同じ規則で検証する。</summary>
    public static class ExportFileTypes
    {
        private static readonly IReadOnlyList<string> nesExtensions = Array.AsReadOnly(new[] { ".wav", ".ogg", ".nsf", ".vgm" });
        private static readonly IReadOnlyList<string> gameBoyExtensions = Array.AsReadOnly(new[] { ".wav", ".ogg", ".vgm" });
        private static readonly IReadOnlyList<string> audioExtensions = Array.AsReadOnly(new[] { ".wav", ".ogg" });

        /// <summary>既存音声形式を先頭に、現在チップで保存可能な拡張子だけを返す。</summary>
        public static IReadOnlyList<string> GetExtensions(ChipKind chip) => chip switch
        {
            ChipKind.Nes => nesExtensions,
            ChipKind.GameBoy => gameBoyExtensions,
            ChipKind.Snes => audioExtensions,
            _ => throw new ArgumentException("対応するチップを指定してください。", nameof(chip))
        };

        /// <summary>拡張子を大小文字によらず解釈する。音声形式は None、未知の拡張子は例外にする。</summary>
        public static ConversionFormat GetChipFormat(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".nsf" => ConversionFormat.Nsf,
                ".vgm" => ConversionFormat.Vgm,
                ".wav" or ".ogg" => ConversionFormat.None,
                _ => throw new ArgumentException("書き出し先は .wav / .ogg / .nsf / .vgm を指定してください。", nameof(path))
            };
        }

        /// <summary>手入力を含む候補がチップに適合することを確認する。</summary>
        public static void Validate(string path, ChipKind chip)
        {
            string extension = Path.GetExtension(path);
            foreach (string candidate in GetExtensions(chip))
            {
                if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase)) { return; }
            }
            throw new ArgumentException($"{chip} には {extension} を書き出せません。", nameof(path));
        }
    }
}
