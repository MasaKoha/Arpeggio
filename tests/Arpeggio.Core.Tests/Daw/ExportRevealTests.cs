using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arpeggio.Daw.Presenters;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>書き出し後の「フォルダを開く」の表示条件と呼び出しを検証する。</summary>
    public sealed class ExportRevealTests
    {
        [Fact]
        public async Task SuccessfulExportPublishesPathAndRevealOpensIt()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            List<string> revealed = new List<string>();
            using ExportPresenter presenter = new ExportPresenter(fixture.Document, fixture.View, () => { },
                (song, path, cancellationToken) => File.WriteAllBytes(path, Array.Empty<byte>()),
                path => revealed.Add(path));
            string target = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "out.wav");

            presenter.RevealLastExport();
            Assert.Empty(revealed);
            Assert.Null(fixture.View.LastExportedPath);

            await presenter.RunAsync(target);
            Assert.Equal(target, presenter.LastExportedPath);
            Assert.Equal(target, fixture.View.LastExportedPath);

            presenter.RevealLastExport();
            Assert.Equal(new[] { target }, revealed);
        }

        [Fact]
        public async Task FailedExportKeepsPreviousPath()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            int calls = 0;
            using ExportPresenter presenter = new ExportPresenter(fixture.Document, fixture.View, () => { },
                (song, path, cancellationToken) =>
                {
                    calls++;
                    if (calls > 1) { throw new IOException("書き込み失敗"); }
                    File.WriteAllBytes(path, Array.Empty<byte>());
                },
                path => { });
            string first = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "first.wav");
            await presenter.RunAsync(first);
            await presenter.RunAsync(Path.Combine(Path.GetDirectoryName(fixture.Path)!, "second.wav"));
            Assert.Equal(first, presenter.LastExportedPath);
            Assert.Contains("書き出し失敗", fixture.View.ExportStatus);
        }
    }
}
