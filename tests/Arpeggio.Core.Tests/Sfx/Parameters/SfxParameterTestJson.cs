using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Tests.Sfx.Parameters
{
    /// <summary>仕様テスト用に、実装の Catalog を経由せずパスと型の対応を読む。</summary>
    internal static class SfxParameterTestJson
    {
        internal static string Patch(string path, object value)
        {
            string[] segments = path.Split('.');
            object nested = value;
            for (int index = segments.Length - 1; index >= 0; index--)
            {
                nested = new Dictionary<string, object> { [segments[index]] = nested };
            }
            return JsonSerializer.Serialize(nested);
        }

        internal static JsonElement Read(SfxParameters parameters, string path)
        {
            JsonSerializerOptions options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            JsonElement element = JsonSerializer.SerializeToElement(parameters, options);
            foreach (string segment in path.Split('.'))
            {
                element = element.GetProperty(segment);
            }
            return element;
        }
    }
}
