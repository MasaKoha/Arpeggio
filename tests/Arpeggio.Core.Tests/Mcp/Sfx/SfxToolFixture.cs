using System;
using System.IO;
using System.Text.Json;
using Arpeggio.Core.Session;
using Arpeggio.Mcp;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp.Sfx
{
    /// <summary>MCP のセッションと保存先をテストごとに隔離し、文字列 JSON の応答を検証する。</summary>
    internal sealed class SfxToolFixture : IDisposable
    {
        internal SfxToolFixture()
        {
            DirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "arpeggio-sfx-mcp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Tools = new ArpeggioTools(Session);
        }

        internal string DirectoryPath { get; }
        internal EditSession Session { get; } = new EditSession();
        internal ArpeggioTools Tools { get; }
        internal string PathFor(string name) => Path.Combine(DirectoryPath, name);

        internal string CreateAndOpen(string chip = "nes")
        {
            string path = PathFor("current.json");
            Success(Tools.CreateSfx(path, chip));
            Success(Tools.OpenSong(path));
            return path;
        }

        internal static JsonElement Success(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            Assert.False(document.RootElement.TryGetProperty("error", out _), json);
            return document.RootElement.Clone();
        }

        internal static JsonElement Failure(string json, int exitCode, string code, string? parameterPath = null)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.Equal(exitCode, root.GetProperty("exitCode").GetInt32());
            Assert.Equal(code, root.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));
            if (parameterPath is not null)
            {
                Assert.Equal(parameterPath, root.GetProperty("parameterPath").GetString());
            }
            return root.Clone();
        }

        /// <summary>テストが作成した保存先と一時ファイルを破棄する。</summary>
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
}
