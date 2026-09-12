using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Tests.Mcp.Sfx;
using Arpeggio.Mcp.Brief;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp.Brief
{
    /// <summary>別インスタンスでも共有ロックを使い、作曲指示の更新を失わないことを検証する。</summary>
    public sealed class BriefConcurrencyToolsTests
    {
        private const int TimeoutSeconds = 10;
        private const int ObservationMilliseconds = 100;
        private const int ConcurrentCreators = 8;

        /// <summary>全四操作が既存の Song ツールと同じ共有セッションロックを待つ。</summary>
        [Theory]
        [InlineData("create")]
        [InlineData("tweak")]
        [InlineData("show")]
        [InlineData("text")]
        public async Task AllToolsWaitForSharedSessionLock(string operation)
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.PathFor("source.brief.json");
            CompositionBriefFile.Create(new CompositionBrief { Title = "曲" }, path);
            var tools = new McpBriefTools(fixture.Session);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            TimeSpan timeout = TimeSpan.FromSeconds(TimeoutSeconds);
            Task holder = Task.Run(() =>
            {
                lock (fixture.Session)
                {
                    entered.SetResult();
                    if (!release.Wait(timeout))
                    {
                        throw new TimeoutException("共有ロックの解放待ちが終了しませんでした。");
                    }
                }
            });
            Task<string>? invocation = null;
            try
            {
                await entered.Task.WaitAsync(timeout);
                invocation = Task.Run(() =>
                {
                    attempted.SetResult();
                    return operation switch
                    {
                        "create" => tools.CreateBrief(fixture.PathFor("created.brief.json")),
                        "tweak" => tools.TweakBrief(path, notes: "追記"),
                        "show" => tools.ShowBrief(path),
                        "text" => tools.BriefText(path),
                        _ => throw new ArgumentException("未知の操作です。", nameof(operation))
                    };
                });
                await attempted.Task.WaitAsync(timeout);
                Task observation = Task.Delay(ObservationMilliseconds);
                Assert.Same(observation, await Task.WhenAny(invocation, observation));
            }
            finally
            {
                release.Set();
                Task completion = invocation == null ? holder : Task.WhenAll(holder, invocation);
                await completion.WaitAsync(timeout);
            }
            string reply = await invocation!;
            if (operation == "text")
            {
                Assert.Equal(CompositionBriefTextRenderer.Render(CompositionBriefFile.Load(path)), reply);
            }
            else
            {
                SfxToolFixture.Success(reply);
            }
        }

        /// <summary>同時の部分編集でも各呼び出しが最新ファイルを読み、他項目の更新を失わない。</summary>
        [Fact]
        public async Task ConcurrentTweaksPreserveBothChanges()
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.PathFor("source.brief.json");
            CompositionBriefFile.Create(new CompositionBrief { Title = "曲" }, path);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string> title = Task.Run(async () =>
            {
                await start.Task;
                return new McpBriefTools(fixture.Session).TweakBrief(path, title: "新しい曲名");
            });
            Task<string> mood = Task.Run(async () =>
            {
                await start.Task;
                return new McpBriefTools(fixture.Session).TweakBrief(path, mood: "穏やか");
            });
            start.SetResult();
            string[] replies = await Task.WhenAll(title, mood);
            foreach (string reply in replies)
            {
                SfxToolFixture.Success(reply);
            }
            CompositionBrief brief = CompositionBriefFile.Load(path);
            Assert.Equal("新しい曲名", brief.Title);
            Assert.Equal("穏やか", brief.Mood);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>同一宛先の同時新規作成は一つだけ成功し、保存済み内容を上書きしない。</summary>
        [Fact]
        public async Task ConcurrentCreatesHaveExactlyOneWinner()
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.PathFor("source.brief.json");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string>[] pending = Enumerable.Range(0, ConcurrentCreators).Select(number => Task.Run(async () =>
            {
                await start.Task;
                return new McpBriefTools(fixture.Session).CreateBrief(path, title: "候補" + number);
            })).ToArray();
            start.SetResult();
            string[] replies = await Task.WhenAll(pending);
            JsonElement[] results = replies.Select(reply =>
            {
                using JsonDocument document = JsonDocument.Parse(reply);
                return document.RootElement.Clone();
            }).ToArray();
            JsonElement winner = Assert.Single(results, result => !result.TryGetProperty("error", out _));
            Assert.Equal(winner.GetProperty("brief").GetProperty("title").GetString(), CompositionBriefFile.Load(path).Title);
            foreach (JsonElement failure in results.Where(result => result.TryGetProperty("error", out _)))
            {
                Assert.Equal("DestinationExists", failure.GetProperty("code").GetString());
                Assert.Equal(1, failure.GetProperty("exitCode").GetInt32());
            }
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }
    }
}
