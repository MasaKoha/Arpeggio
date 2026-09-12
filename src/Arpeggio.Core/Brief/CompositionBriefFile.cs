using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arpeggio.Core.Brief
{
    /// <summary>作曲指示書の JSON 変換と原子的なファイル保存。</summary>
    public static class CompositionBriefFile
    {
        /// <summary>検証後に規定順・camelCase・文字列 enum の JSON を返す。</summary>
        public static string Serialize(CompositionBrief brief)
        {
            CompositionBriefValidator.Validate(brief);
            return JsonSerializer.Serialize(brief, CreateOptions());
        }

        /// <summary>版と各項目を検証し、未指定の自由記述を空文字に正規化する。</summary>
        public static CompositionBrief Deserialize(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                RequireStructure(document.RootElement);
                CompositionBrief brief = JsonSerializer.Deserialize<CompositionBrief>(json, CreateOptions())
                    ?? throw new CompositionBriefException("InvalidBrief", "document", "作曲指示書はオブジェクトで指定してください。");
                CompositionBriefValidator.Validate(brief);
                return brief with
                {
                    Mood = brief.Mood ?? string.Empty,
                    Structure = brief.Structure ?? string.Empty,
                    Instrumentation = brief.Instrumentation ?? string.Empty,
                    References = brief.References ?? string.Empty,
                    Constraints = brief.Constraints ?? string.Empty,
                    Notes = brief.Notes ?? string.Empty
                };
            }
            catch (CompositionBriefException exception) when (exception.Code != "InvalidBrief")
            {
                throw new CompositionBriefException("InvalidBrief", exception.ParameterPath, exception.Message, exception);
            }
            catch (JsonException exception)
            {
                throw new CompositionBriefException("InvalidBrief", "document", "作曲指示書 JSON の形式が不正です。", exception);
            }
        }

        /// <summary>ファイルから検証済みの作曲指示書を復元する。</summary>
        public static CompositionBrief Load(string path)
        {
            RequirePath(path);
            return Deserialize(File.ReadAllText(path));
        }

        /// <summary>既存の宛先を上書きせず、新しい作曲指示書を保存する。</summary>
        public static void Create(CompositionBrief brief, string path)
        {
            RequirePath(path);
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw DestinationExists();
            }
            Write(Serialize(brief), path, false);
        }

        /// <summary>検証後に同じディレクトリの一時ファイルを置換して保存する。</summary>
        public static void Save(CompositionBrief brief, string path)
        {
            RequirePath(path);
            Write(Serialize(brief), path, true);
        }

        private static void Write(string content, string path, bool overwrite)
        {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
                try
                {
                    // create は存在確認後に別の書き手が保存しても上書きしない。
                    File.Move(temporaryPath, path, overwrite);
                }
                catch (IOException) when (!overwrite && (File.Exists(path) || Directory.Exists(path)))
                {
                    throw DestinationExists();
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void RequireStructure(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new CompositionBriefException("InvalidBrief", "document", "作曲指示書はオブジェクトで指定してください。");
            }
            bool hasVersion = false;
            var remaining = new HashSet<string>(StringComparer.Ordinal)
            {
                "version", "title", "chip", "tempoBpm", "mood", "structure", "instrumentation", "references", "constraints", "notes"
            };
            foreach (JsonProperty property in root.EnumerateObject())
            {
                // Song も version・title・chip を持つため、別種の文書を部分的に読み込まない。
                if (!remaining.Remove(property.Name))
                {
                    throw new CompositionBriefException("InvalidBrief", property.Name, $"未知または重複する項目です: {property.Name}");
                }
                if (property.Name != "version")
                {
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetInt32(out int version) || version != CompositionBrief.CurrentVersion)
                {
                    throw InvalidVersion();
                }
                hasVersion = true;
            }
            if (!hasVersion)
            {
                throw InvalidVersion();
            }
        }

        private static CompositionBriefException InvalidVersion()
        {
            return new CompositionBriefException("InvalidBrief", "version", $"version は {CompositionBrief.CurrentVersion} を一つ指定してください。");
        }

        private static CompositionBriefException DestinationExists()
        {
            return new CompositionBriefException("DestinationExists", "path", "保存先が既に存在します。");
        }

        private static void RequirePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new CompositionBriefException("InvalidParameter", "path", "ファイルのパスを指定してください。");
            }
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));
            return options;
        }
    }
}
