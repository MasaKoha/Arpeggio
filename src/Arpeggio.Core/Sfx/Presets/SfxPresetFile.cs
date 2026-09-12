using System;
using System.IO;
using System.Text;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx.Presets
{
    /// <summary>完成した効果音プリセットを既存ファイルの上書きなしで保存する。</summary>
    public static class SfxPresetFile
    {
        /// <summary>生成・検証後に同じディレクトリの一時ファイルを移動して新規保存する。</summary>
        public static void Create(string path, ChipKind chip, SfxPresetKind kind, string? title = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("保存先を指定してください。", nameof(path));
            }
            if (File.Exists(path))
            {
                throw new ArgumentException("保存先が既に存在します。別のパスを指定してください。", nameof(path));
            }
            Song song = SfxPresetFactory.Create(chip, kind);
            song.Title = title ?? song.Title;
            string content = SongSerializer.Serialize(song);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
                // 存在確認後の競合でも上書きしないため、置換ではなく新規移動を使う。
                File.Move(temporaryPath, path, false);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
