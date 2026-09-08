using System.IO;
using Arpeggio.Core.Document;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>エコー編集の作業保存と正本の明示保存を検証する。</summary>
    public sealed class SnesEchoAcceptanceTests
    {
        private const int EchoDelayMilliseconds = 32;
        private const double EchoFeedback = 0.5;
        private const double EchoVolume = 0.4;

        /// <summary>エコー編集は作業ファイルに保存され、明示保存まで正本を変更しない。</summary>
        [Fact]
        public void EchoEditUpdatesWorkingFileBeforeExplicitSave()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            string originalFile = File.ReadAllText(fixture.Path);

            fixture.Presenter.SnesEcho.Apply(EchoDelayMilliseconds, EchoFeedback, EchoVolume, nameof(SnesEchoFirPresets.LowPass));

            Assert.Equal(originalFile, File.ReadAllText(fixture.Path));
            Assert.True(fixture.Document.IsDirty);
            Song working = SongSerializer.Load(fixture.Document.Session.Path!);
            Assert.Equal(EchoDelayMilliseconds, working.SnesEcho.DelayMilliseconds);
            Assert.Equal(SnesEchoFirPresets.LowPass, working.SnesEcho.FirCoefficients);
            fixture.Presenter.Save();
            Assert.False(fixture.Document.IsDirty);
            Assert.Equal(SnesEchoFirPresets.LowPass, SongSerializer.Load(fixture.Path).SnesEcho.FirCoefficients);
        }

    }
}
