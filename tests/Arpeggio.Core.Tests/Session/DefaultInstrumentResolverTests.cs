using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;
using Xunit;

namespace Arpeggio.Core.Tests.Session
{
    /// <summary>音色 ID 省略時の解決規則を検証する。</summary>
    public sealed class DefaultInstrumentResolverTests
    {
        [Fact]
        public void ExplicitIdIsReturnedAsIs()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            Assert.Equal(42, DefaultInstrumentResolver.Resolve(song, 0, 42));
        }

        [Fact]
        public void FirstCompatibleInstrumentIsChosenForChannel()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Instruments.Add(new NesTriangleInstrument { Id = 2, Name = "bass" });
            song.Instruments.Add(new NesTriangleInstrument { Id = 3, Name = "bass2" });
            Assert.Equal(1, DefaultInstrumentResolver.Resolve(song, 0, null));
            Assert.Equal(2, DefaultInstrumentResolver.Resolve(song, 2, null));
        }

        [Fact]
        public void MissingCompatibleInstrumentExplainsWhatToAdd()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            ArgumentException exception = Assert.Throws<ArgumentException>(() => DefaultInstrumentResolver.Resolve(song, 2, null));
            Assert.Contains("NesTriangle", exception.Message);
        }
    }
}
