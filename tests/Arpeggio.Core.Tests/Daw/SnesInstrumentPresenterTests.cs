using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;
using Arpeggio.Core.Session;
using Xunit;
using Arpeggio.Daw.Presenters.Instrument;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>SNES 専用入力の推奨値・拒否条件と一履歴での公開を検証する。</summary>
    public sealed class SnesInstrumentPresenterTests
    {
        private const int EmbeddedSampleByteCount = 64;

        /// <summary>各カテゴリの選択は既存の上書きを推奨値へ戻し、Undo で全項目を復元する。</summary>
        [Theory]
        [InlineData("strings")]
        [InlineData("piano")]
        [InlineData("hat")]
        public void PresetSelectionAppliesRecommendationsInOneHistory(string presetName)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            SnesSampleInstrument original = CloneCurrent(fixture);
            original.Name = "保持する名前";
            original.Pan = 0.5;
            original.NoiseEnabled = true;
            original.RootMidiNote = 72;
            original.EchoSend = 1;
            original.AdsrRegisters = new SnesAdsrRegisters(0, 0, 0, 1);
            original.PitchMacro = new Macro { Values = new[] { 1, 2 }, LoopIndex = 0 };
            original.VolumeMacro = new Macro { Values = new[] { 12, 8, 4, 0 }, LoopIndex = 1 };
            fixture.Document.Session.Instruments.Update(original);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            int history = fixture.Document.Session.History.UndoCount;

            fixture.Presenter.Instruments.SelectPreset(presetName);

            SnesSampleInstrument selected = Current(fixture);
            SnesInstrumentPreset expected = SnesInstrumentCatalog.Get(presetName);
            Assert.Equal(presetName, selected.Preset);
            Assert.Equal(expected.AdsrRegisters, selected.AdsrRegisters);
            Assert.Equal(expected.RootMidiNote, selected.RootMidiNote);
            Assert.Equal(expected.SampleRate, selected.SampleRate);
            Assert.Equal(expected.Loop, selected.Loop);
            Assert.Equal(expected.EchoSend, selected.EchoSend);
            Assert.Equal(0, selected.LoopStart);
            Assert.Equal(0, selected.LoopEnd);
            Assert.False(selected.NoiseEnabled);
            Assert.Equal(original.Name, selected.Name);
            Assert.Equal(original.Pan, selected.Pan);
            Assert.Equal(original.PitchMacro!.Values, selected.PitchMacro!.Values);
            Assert.NotNull(selected.VolumeMacro);
            Assert.Equal(original.VolumeMacro!.Values, selected.VolumeMacro.Values);
            Assert.Equal(1, selected.VolumeMacro.LoopIndex);
            Assert.Null(original.Preset);
            Assert.Equal(history + 1, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Undo();
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            fixture.Presenter.Redo();
            Assert.Equal(presetName, Current(fixture).Preset);
        }

        /// <summary>埋め込み WAV のある音色ではプリセットを拒否し、ステータスを保持する。</summary>
        [Fact]
        public void EmbeddedSampleRejectsPresetWithoutChangingHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            SnesSampleInstrument embedded = CloneCurrent(fixture);
            embedded.SampleData = Convert.ToBase64String(new byte[EmbeddedSampleByteCount]);
            fixture.Document.Session.Instruments.Update(embedded);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            int history = fixture.Document.Session.History.UndoCount;

            fixture.Presenter.Execute(() => fixture.Presenter.Instruments.SelectPreset("strings"));
            fixture.Presenter.Poll();

            Assert.Contains("埋め込みサンプルを使用中。先に解除してください", fixture.View.Status);
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Instruments.ClearSample();
            Assert.Null(Current(fixture).SampleData);
            fixture.Presenter.Instruments.SelectPreset("strings");
            Assert.Equal("strings", Current(fixture).Preset);
            fixture.Presenter.Undo();
            fixture.Presenter.Undo();
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
        }

        /// <summary>同じ選択は履歴を増やさず、合成波形への復帰は一履歴になる。</summary>
        [Fact]
        public void ClearingPresetIsUndoableAndRepeatedSelectionDoesNothing()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            fixture.Presenter.Instruments.SelectPreset("strings");
            int history = fixture.Document.Session.History.UndoCount;
            fixture.Presenter.Instruments.SelectPreset("strings");
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Instruments.SelectPreset(null);
            Assert.Null(Current(fixture).Preset);
            Assert.Equal(history + 1, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Undo();
            Assert.Equal("strings", Current(fixture).Preset);
        }

        /// <summary>各レジスタの上下限逸脱・小数・欠落は、元音色と履歴を維持して拒否する。</summary>
        [Theory]
        [InlineData("-1", "0", "0", "0")]
        [InlineData("16", "0", "0", "0")]
        [InlineData("0", "-1", "0", "0")]
        [InlineData("0", "8", "0", "0")]
        [InlineData("0", "0", "-1", "0")]
        [InlineData("0", "0", "8", "0")]
        [InlineData("0", "0", "0", "-1")]
        [InlineData("0", "0", "0", "32")]
        [InlineData("1.5", "0", "0", "0")]
        [InlineData("", "0", "0", "0")]
        [InlineData("attack", "0", "0", "0")]
        public void InvalidRegistersAreRejected(string attack, string decay, string sustainLevel, string sustainRate)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            int history = fixture.Document.Session.History.UndoCount;
            SnesInstrumentInput input = new SnesInstrumentInput
            {
                Attack = attack, Decay = decay, SustainLevel = sustainLevel, SustainRate = sustainRate
            };

            Assert.Throws<ArgumentException>(() => Apply(fixture, input));

            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);
            Assert.False(input.IsAttackValid && input.IsDecayValid && input.IsSustainLevelValid && input.IsSustainRateValid);
        }

        /// <summary>上限値を適用でき、全空欄はプリセット付きでも秒指定に復帰する。</summary>
        [Fact]
        public void RegistersAcceptBoundariesAndEmptyFieldsRestoreSeconds()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            fixture.Presenter.Instruments.SelectPreset("strings");
            int history = fixture.Document.Session.History.UndoCount;
            Apply(fixture, new SnesInstrumentInput { Attack = "15", Decay = "7", SustainLevel = "7", SustainRate = "31" });
            Assert.Equal(new SnesAdsrRegisters(15, 7, 7, 31), Current(fixture).AdsrRegisters);
            Assert.Equal(history + 1, fixture.Document.Session.History.UndoCount);
            Apply(fixture, new SnesInstrumentInput());
            Assert.Null(Current(fixture).AdsrRegisters);
            Assert.Equal("strings", Current(fixture).Preset);
            fixture.Presenter.Undo();
            Assert.Equal(new SnesAdsrRegisters(15, 7, 7, 31), Current(fixture).AdsrRegisters);
        }

        /// <summary>ボイス 0 では変調を有効化できず、次のボイスでは DSP 入力を一履歴で適用できる。</summary>
        [Fact]
        public void PitchModulationCannotBeEnabledOnVoiceZero()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            SnesInstrumentInput input = new SnesInstrumentInput { PitchModulation = true, NoiseEnabled = true, NoiseRate = 0 };
            int history = fixture.Document.Session.History.UndoCount;
            Assert.False(fixture.Presenter.Instruments.CanUsePitchModulation);
            Assert.Throws<InvalidOperationException>(() => Apply(fixture, input));
            Assert.False(Current(fixture).PitchModulation);
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);

            fixture.Presenter.PianoRoll.SelectTrack(1);
            Assert.True(fixture.Presenter.Instruments.CanUsePitchModulation);
            Apply(fixture, input);
            Assert.True(Current(fixture).PitchModulation);
            Assert.True(Current(fixture).NoiseEnabled);
            Assert.Equal(0, Current(fixture).NoiseRate);
            Assert.Equal(history + 1, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>非表示の専用項目を動的な文字列経路から上書きできない。</summary>
        [Fact]
        public void DedicatedFieldsAreExcludedFromDynamicInputs()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            string[] dedicatedKeys = { "preset", "adsrRegisters", "pitchModulation", "noiseEnabled", "noiseRate" };
            foreach (string key in dedicatedKeys)
            {
                Assert.DoesNotContain(fixture.Presenter.Instruments.GetParameters(), parameter => parameter.Key == key);
            }
            fixture.Presenter.Instruments.Apply("保持", new Dictionary<string, string>
            {
                ["preset"] = "strings", ["pitchModulation"] = "true"
            });
            Assert.Null(Current(fixture).Preset);
            Assert.False(Current(fixture).PitchModulation);
        }

        /// <summary>ノイズレートは専用入力でも範囲外を拒否する。</summary>
        [Theory]
        [InlineData(-1)]
        [InlineData(32)]
        public void NoiseRateRejectsOutOfRangeValues(int noiseRate)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            Assert.Throws<ArgumentOutOfRangeException>(() => Apply(fixture, new SnesInstrumentInput { NoiseRate = noiseRate }));
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(0, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>CLI バンクのトラック既定音色を名前に依存せずパネルで選択する。</summary>
        [Fact]
        public void BankTrackDefaultsDetermineCurrentInstrument()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            Song bank = SongFactory.Create(ChipKind.Snes, bank: SnesBankKind.Band);
            SongSerializer.Save(bank, fixture.Path);
            fixture.Presenter.Open(fixture.Path);
            fixture.Presenter.PianoRoll.SelectTrack(1);
            Assert.Equal(bank.Tracks[1].DefaultInstrumentId, fixture.Presenter.Instruments.CurrentInstrument!.Id);
            Assert.Equal("organ", Current(fixture).Preset);
        }

        /// <summary>共有音色の既存の変調設定は、ボイス 0 で別項目を編集しても消さない。</summary>
        [Fact]
        public void VoiceZeroEditsPreserveExistingSharedModulation()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            fixture.Presenter.PianoRoll.SelectTrack(1);
            Apply(fixture, new SnesInstrumentInput { PitchModulation = true });
            fixture.Presenter.PianoRoll.SelectTrack(0);
            Assert.False(fixture.Presenter.Instruments.CanUsePitchModulation);
            Apply(fixture, new SnesInstrumentInput { PitchModulation = true, NoiseEnabled = true });
            Assert.True(Current(fixture).PitchModulation);
            Assert.True(Current(fixture).NoiseEnabled);
        }

        private static void Apply(DawPresenterFixture fixture, SnesInstrumentInput input) =>
            fixture.Presenter.Instruments.Apply("編集", new Dictionary<string, string>(), input);

        private static SnesSampleInstrument Current(DawPresenterFixture fixture) =>
            Assert.IsType<SnesSampleInstrument>(fixture.Presenter.Instruments.CurrentInstrument);

        private static SnesSampleInstrument CloneCurrent(DawPresenterFixture fixture) =>
            (SnesSampleInstrument)InstrumentJson.Deserialize(InstrumentJson.Serialize(Current(fixture)));
    }
}
