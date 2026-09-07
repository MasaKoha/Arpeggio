using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Arpeggio.Core.Session
{
    /// <summary>バッチ配列と保存形式の音色 JSON を読み取る。</summary>
    public static class BatchOperationJson
    {
        /// <summary>全要素を編集前に解析する。未知のキー・種別を拒否する。</summary>
        public static BatchOperation[] Deserialize(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                List<BatchOperation> operations = new List<BatchOperation>();
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    JsonObject input = JsonNode.Parse(element.GetRawText()) as JsonObject
                        ?? throw new ArgumentException("操作は JSON オブジェクトで指定してください。");
                    JsonNode? instrument = input["instrument"];
                    input.Remove("instrument");
                    BatchOperation operation = JsonSerializer.Deserialize<BatchOperation>(input.ToJsonString(), InstrumentJson.CreateOptions())
                        ?? throw new ArgumentException("操作は null にできません。");
                    if (instrument != null)
                    {
                        operation.Instrument = InstrumentJson.Deserialize(instrument.ToJsonString());
                    }
                    if (operation.Kind == BatchOperationKind.None || !Enum.IsDefined(typeof(BatchOperationKind), operation.Kind))
                    {
                        throw new ArgumentException("操作 kind が未指定または未対応です。");
                    }
                    operations.Add(operation);
                }
                return operations.ToArray();
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
            {
                throw new ArgumentException("operations は操作オブジェクトの JSON 配列で指定してください。", exception);
            }
        }
    }
}
