using System;
using System.Text.Json;
using Arpeggio.Core.Session;

namespace Arpeggio.Cli.Sfx
{
    /// <summary>SFX の共通結果を通常表示へ整形し、警告だけを標準エラーへ送る。</summary>
    internal static class SfxOutput
    {
        internal static void WriteText(object output)
        {
            using JsonDocument document = JsonDocument.Parse(SessionOutput.Serialize(output));
            JsonElement root = document.RootElement;
            Console.WriteLine($"{root.GetProperty("operation").GetString()}: 完了");
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name == "warnings")
                {
                    foreach (JsonElement warning in property.Value.EnumerateArray())
                    {
                        Console.Error.WriteLine($"{warning.GetProperty("code").GetString()}: {warning.GetProperty("message").GetString()}");
                    }
                    continue;
                }
                if (property.Name != "operation")
                {
                    Console.WriteLine($"{property.Name}: {property.Value}");
                }
            }
        }
    }
}
