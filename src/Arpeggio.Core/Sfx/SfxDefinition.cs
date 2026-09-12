using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arpeggio.Core.Sfx.Storage;

namespace Arpeggio.Core.Sfx
{
    /// <summary>既知版の編集定義、または解釈せず保持する未知版 JSON。</summary>
    [JsonConverter(typeof(SfxDefinitionJsonConverter))]
    public sealed class SfxDefinition
    {
        /// <summary>既知版の定義を保持する。値の検証はソングの保存境界で行う。</summary>
        public SfxDefinition(SfxDefinitionData data)
        {
            Known = data ?? throw new ArgumentNullException(nameof(data));
        }

        internal SfxDefinition(JsonElement unsupportedJson)
        {
            // 読み込み元 JsonDocument の破棄後も再保存できるよう、所有権を切り離す。
            UnsupportedJson = unsupportedJson.Clone();
        }

        /// <summary>既知版の定義。未知版を部分復元して公開しない。</summary>
        public SfxDefinitionData? Known { get; }

        /// <summary>未知版の定義全体。既知版では null。</summary>
        public JsonElement? UnsupportedJson { get; }
    }
}
