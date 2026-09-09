using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>音色の optional マクロが通常編集・履歴・正本保存で失われないことを検証する。</summary>
    public sealed class OptionalInstrumentMacroTests
    {
        /// <summary>GB の音色切替・名前編集・マクロ編集を一履歴で保持し、保存し直しても消失しない。</summary>
        [Fact]
        public void GameBoyDutySurvivesSelectionEditingHistoryAndSave()
        {
            using var fixture = new DawPresenterFixture(ChipKind.GameBoy);
            const int InstrumentId = 2;
            var original = new GbPulseInstrument
            {
                Id = InstrumentId,
                DutyMacro = new Macro { Values = new[] { 1, 2, 3, 4 }, LoopIndex = 1 }
            };
            fixture.Document.Session.Instruments.Add(original);
            original.DutyMacro.Values[0] = 4;
            fixture.Presenter.Instruments.SelectInstrument(InstrumentId);
            Assert.Equal(1, Assert.IsType<GbPulseInstrument>(fixture.Presenter.Instruments.CurrentInstrument).DutyMacro!.Values[0]);
            fixture.Presenter.Instruments.Apply("保持", new Dictionary<string, string>());
            Assert.Equal(new[] { 1, 2, 3, 4 }, Assert.IsType<GbPulseInstrument>(fixture.Presenter.Instruments.CurrentInstrument).DutyMacro!.Values);
            int history = fixture.Document.Session.History.UndoCount;
            fixture.Presenter.Instruments.Apply("編集", new Dictionary<string, string> { ["dutyMacro"] = "4,3,2,1/2" });
            Assert.Equal(history + 1, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Undo();
            Assert.Equal(new[] { 1, 2, 3, 4 }, Assert.IsType<GbPulseInstrument>(fixture.Presenter.Instruments.CurrentInstrument).DutyMacro!.Values);
            fixture.Presenter.Redo();
            fixture.Presenter.Save();
            Song restored = SongSerializer.Load(fixture.Path);
            var saved = Assert.IsType<GbPulseInstrument>(Assert.Single(restored.Instruments, instrument => instrument.Id == InstrumentId));
            Assert.NotNull(saved.DutyMacro);
            Assert.Equal(new[] { 4, 3, 2, 1 }, saved.DutyMacro.Values);
            Assert.Equal(2, saved.DutyMacro.LoopIndex);
        }

        /// <summary>SNES 音量マクロをコピー・選択・通常編集し、履歴と正本へ保持する。</summary>
        [Fact]
        public void SnesVolumeSurvivesSelectionEditingHistoryAndSave()
        {
            using var fixture = new DawPresenterFixture(ChipKind.Snes);
            const int InstrumentId = 2;
            var original = new SnesSampleInstrument
            {
                Id = InstrumentId,
                VolumeMacro = new Macro { Values = new[] { 12, 8, 4, 0 }, LoopIndex = 1 }
            };
            fixture.Document.Session.Instruments.Add(original);
            original.VolumeMacro.Values[0] = 0;
            fixture.Presenter.Instruments.SelectInstrument(InstrumentId);
            Assert.Equal(12, Assert.IsType<SnesSampleInstrument>(fixture.Presenter.Instruments.CurrentInstrument).VolumeMacro!.Values[0]);
            fixture.Presenter.Instruments.Apply("保持", new Dictionary<string, string>());
            Assert.Equal(new[] { 12, 8, 4, 0 }, Assert.IsType<SnesSampleInstrument>(fixture.Presenter.Instruments.CurrentInstrument).VolumeMacro!.Values);
            int history = fixture.Document.Session.History.UndoCount;
            fixture.Presenter.Instruments.Apply("編集", new Dictionary<string, string> { ["volumeMacro"] = "15,10,5,0/2" });
            Assert.Equal(history + 1, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Undo();
            Assert.Equal(new[] { 12, 8, 4, 0 }, Assert.IsType<SnesSampleInstrument>(fixture.Presenter.Instruments.CurrentInstrument).VolumeMacro!.Values);
            fixture.Presenter.Redo();
            fixture.Presenter.Save();
            Song restored = SongSerializer.Load(fixture.Path);
            var saved = Assert.IsType<SnesSampleInstrument>(Assert.Single(restored.Instruments, instrument => instrument.Id == InstrumentId));
            Assert.NotNull(saved.VolumeMacro);
            Assert.Equal(new[] { 15, 10, 5, 0 }, saved.VolumeMacro.Values);
            Assert.Equal(2, saved.VolumeMacro.LoopIndex);
        }
    }
}
