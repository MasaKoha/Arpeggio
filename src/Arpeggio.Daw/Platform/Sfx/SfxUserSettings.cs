using System;
using System.IO;
using System.Text.Json;

namespace Arpeggio.Daw.Platform.Sfx
{
    /// <summary>ソングと独立した SFX モニター音量・展開状態をユーザー設定へ保存する。</summary>
    public sealed class SfxUserSettings
    {
        private const float DefaultMonitorVolume = 0.5f;

        /// <summary>試聴専用ゲイン。解析・書き出しには使用しない。</summary>
        public float MonitorVolume { get; set; } = DefaultMonitorVolume;
        /// <summary>音程変化・反復の初回表示は展開する。</summary>
        public bool RepeatExpanded { get; set; } = true;

        /// <summary>未作成なら既定値、不正な内容は明示的な例外として返す。</summary>
        public static SfxUserSettings Load(string path)
        {
            if (!File.Exists(path)) { return new SfxUserSettings(); }
            SfxUserSettings settings = JsonSerializer.Deserialize<SfxUserSettings>(File.ReadAllText(path))
                ?? throw new InvalidDataException("SFX 設定が空です。");
            settings.Validate();
            return settings;
        }

        /// <summary>隣接ファイルから置換し、途中失敗で旧設定を壊さない。</summary>
        public void Save(string path)
        {
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this));
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) { File.Delete(temporaryPath); }
            }
        }

        /// <summary>OS のユーザー設定ディレクトリ内の保存先。</summary>
        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arpeggio", "sfx-ui.json");

        private void Validate()
        {
            if (!float.IsFinite(MonitorVolume) || MonitorVolume < 0 || MonitorVolume > 1)
            {
                throw new InvalidDataException("SFX モニター音量は0〜1で指定してください。");
            }
        }
    }
}
