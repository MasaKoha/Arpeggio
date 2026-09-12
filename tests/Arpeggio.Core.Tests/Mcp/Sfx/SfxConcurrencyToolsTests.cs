using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Sfx;
using Arpeggio.Mcp;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp.Sfx
{
    /// <summary>別ツールインスタンスでも共有セッションの直列実行と競合拒否を維持する。</summary>
    public sealed class SfxConcurrencyToolsTests
    {
        private const int TimeoutSeconds = 10;
        private const int ObservationMilliseconds = 100;
        private const int ConcurrentWriters = 8;

        /// <summary>読み取り・作成も含む全追加ツールが、既存ツールと同じセッションロックを待つ。</summary>
        [Theory]
        [InlineData("create")]
        [InlineData("list")]
        [InlineData("params")]
        [InlineData("tweak")]
        [InlineData("randomize")]
        [InlineData("mutate")]
        [InlineData("regenerate")]
        [InlineData("detach")]
        public async Task NewToolsWaitForTheSharedSessionLock(string operation)
        {
            using var fixture = new SfxToolFixture();
            fixture.CreateAndOpen();
            var another = new ArpeggioTools(fixture.Session);
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
                    return Invoke(another, fixture.PathFor("created.json"), operation);
                });
                await attempted.Task.WaitAsync(timeout);
                Task observation = Task.Delay(ObservationMilliseconds);
                Assert.Same(observation, await Task.WhenAny(invocation, observation));
            }
            finally
            {
                release.Set();
                Task completion = invocation is null ? holder : Task.WhenAll(holder, invocation);
                await completion.WaitAsync(timeout);
            }
            SfxToolFixture.Success(await invocation!);
        }

        /// <summary>同じ revision の同時編集は一件だけ成功し、残りは競合、一履歴だけが残る。</summary>
        [Fact]
        public async Task ConcurrentWritersCommitOnlyOneRevision()
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.CreateAndOpen();
            byte[] before = File.ReadAllBytes(path);
            string revision = SfxHash.ComputeRevision(fixture.Session.Song!);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string>[] pending = Enumerable.Range(0, ConcurrentWriters).Select(_ => Task.Run(async () =>
            {
                await start.Task;
                return new ArpeggioTools(fixture.Session).TweakSfx("{\"tone\":{\"baseFrequencyHz\":660}}", revision);
            })).ToArray();
            start.SetResult();
            string[] replies = await Task.WhenAll(pending);
            JsonElement[] results = replies.Select(reply =>
            {
                using JsonDocument document = JsonDocument.Parse(reply);
                return document.RootElement.Clone();
            }).ToArray();
            JsonElement success = Assert.Single(results, result => !result.TryGetProperty("error", out _));
            Assert.Equal(SfxHash.ComputeRevision(fixture.Session.Song!), success.GetProperty("revision").GetString());
            foreach (JsonElement failure in results.Where(result => result.TryGetProperty("error", out _)))
            {
                Assert.Equal("RevisionConflict", failure.GetProperty("code").GetString());
                Assert.Equal(1, failure.GetProperty("exitCode").GetInt32());
            }
            Assert.Equal(1, fixture.Session.History.UndoCount);
            SfxToolFixture.Success(fixture.Tools.Undo());
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        private static string Invoke(ArpeggioTools tools, string path, string operation)
        {
            return operation switch
            {
                "create" => tools.CreateSfx(path),
                "list" => tools.SfxParameterPresets(),
                "params" => tools.SfxParameters(),
                _ => SfxEditingToolsTests.Invoke(tools, operation)
            };
        }
    }
}
