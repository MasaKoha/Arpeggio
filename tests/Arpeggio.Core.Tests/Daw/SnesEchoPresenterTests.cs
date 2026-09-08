using System;
using System.Collections.Generic;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>エコー設定の保存・履歴と、停止後の DSP 再構築を検証する。</summary>
    public sealed class SnesEchoPresenterTests
    {
        private const int BufferFrames = 2048;
        private const int StereoChannels = 2;
        private const int SampleRate = 44100;
        private const int EchoDelayMilliseconds = 16;
        private const double EchoFeedback = 0.5;
        private const double EchoVolume = 0.75;

        /// <summary>再生中の変更は停止・先頭リセット・再開を行い、再構築した DSP と同じ PCM を返す。</summary>
        [Fact]
        public void EchoChangeStopsResetsAndResumesInOneHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            fixture.Presenter.Instruments.Apply("エコー対象", new Dictionary<string, string> { ["echoSend"] = "1" });
            fixture.Presenter.PianoRoll.Add(0, 60);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            fixture.Presenter.Transport.TogglePlayback();
            fixture.Audio.RequestFrames(BufferFrames);
            fixture.Presenter.Poll();
            Assert.True(fixture.View.PositionTick > 0);
            int starts = fixture.Audio.StartCount;
            int stops = fixture.Audio.StopCount;
            int history = fixture.Document.Session.History.UndoCount;

            fixture.Presenter.SnesEcho.Apply(EchoDelayMilliseconds, EchoFeedback, EchoVolume, nameof(SnesEchoFirPresets.LowPass));

            Assert.Equal(starts + 1, fixture.Audio.StartCount);
            Assert.Equal(stops + 1, fixture.Audio.StopCount);
            Assert.True(fixture.Audio.IsRunning);
            Assert.True(fixture.View.IsPlaying);
            Assert.Equal(0d, fixture.View.PositionTick);
            Assert.Equal(history + 1, fixture.Document.Session.History.UndoCount);
            Assert.Equal(EchoDelayMilliseconds, fixture.Document.Song.SnesEcho.DelayMilliseconds);
            Assert.Equal(EchoFeedback, fixture.Document.Song.SnesEcho.Feedback);
            Assert.Equal(EchoVolume, fixture.Document.Song.SnesEcho.Volume);
            Assert.Equal(SnesEchoFirPresets.LowPass, fixture.Document.Song.SnesEcho.FirCoefficients);
            Assert.Equal(nameof(SnesEchoFirPresets.LowPass), fixture.Presenter.SnesEcho.CurrentFirPreset);

            Song snapshot = SongSerializer.Deserialize(SongSerializer.Serialize(fixture.Document.Song));
            SongRenderer expectedRenderer = new SongRenderer(snapshot, new RenderSettings(SampleRate));
            float[] expected = new float[BufferFrames * StereoChannels];
            expectedRenderer.Render(expected);
            Assert.Contains(expected, sample => sample != 0f);
            Assert.Equal(expected, fixture.Audio.RequestSamples(BufferFrames));

            fixture.Presenter.Undo();
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.True(fixture.Audio.IsRunning);
            fixture.Presenter.Redo();
            Assert.Equal(SnesEchoFirPresets.LowPass, fixture.Document.Song.SnesEcho.FirCoefficients);
        }

        /// <summary>一時停止中の変更も先頭へ戻し、ユーザーが再生するまでは出力を開始しない。</summary>
        [Fact]
        public void EchoChangeWhileStoppedResetsWithoutStartingAudio()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            fixture.Presenter.Transport.TogglePlayback();
            fixture.Audio.RequestFrames(BufferFrames);
            fixture.Presenter.Transport.TogglePlayback();
            Assert.True(fixture.View.PositionTick > 0);
            int starts = fixture.Audio.StartCount;

            fixture.Presenter.SnesEcho.Apply(EchoDelayMilliseconds, EchoFeedback, EchoVolume, nameof(SnesEchoFirPresets.Wide));

            Assert.False(fixture.Audio.IsRunning);
            Assert.Equal(starts, fixture.Audio.StartCount);
            Assert.Equal(0d, fixture.View.PositionTick);
            fixture.Presenter.Save();
            SnesEchoSettings saved = SongSerializer.Load(fixture.Path).SnesEcho;
            Assert.Equal(EchoDelayMilliseconds, saved.DelayMilliseconds);
            Assert.Equal(SnesEchoFirPresets.Wide, saved.FirCoefficients);
        }

        /// <summary>同じ設定の再適用で履歴や再生位置を変えない。</summary>
        [Fact]
        public void ReapplyingSettingsDoesNotRestartOrRecordHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            fixture.Presenter.SnesEcho.Apply(EchoDelayMilliseconds, EchoFeedback, EchoVolume, nameof(SnesEchoFirPresets.HighPass));
            fixture.Presenter.Transport.TogglePlayback();
            fixture.Audio.RequestFrames(BufferFrames);
            fixture.Presenter.Poll();
            double position = fixture.View.PositionTick;
            int starts = fixture.Audio.StartCount;
            int stops = fixture.Audio.StopCount;
            int history = fixture.Document.Session.History.UndoCount;

            fixture.Presenter.SnesEcho.Apply(EchoDelayMilliseconds, EchoFeedback, EchoVolume, nameof(SnesEchoFirPresets.HighPass));

            Assert.Equal(position, fixture.View.PositionTick);
            Assert.Equal(starts, fixture.Audio.StartCount);
            Assert.Equal(stops, fixture.Audio.StopCount);
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>不正入力は再生・履歴・ソングを変更する前に拒否する。</summary>
        [Theory]
        [InlineData(-16, 0.5, 0.5)]
        [InlineData(17, 0.5, 0.5)]
        [InlineData(256, 0.5, 0.5)]
        [InlineData(16, -1, 0.5)]
        [InlineData(16, 1, 0.5)]
        [InlineData(16, double.NaN, 0.5)]
        [InlineData(16, 0.5, -0.1)]
        [InlineData(16, 0.5, 1.1)]
        [InlineData(16, 0.5, double.PositiveInfinity)]
        public void InvalidSettingsLeavePlaybackAndHistoryUnchanged(int delay, double feedback, double volume)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            fixture.Presenter.Transport.TogglePlayback();
            string before = SongSerializer.Serialize(fixture.Document.Song);
            int starts = fixture.Audio.StartCount;
            int stops = fixture.Audio.StopCount;
            int history = fixture.Document.Session.History.UndoCount;

            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Presenter.SnesEcho.Apply(delay, feedback, volume, nameof(SnesEchoFirPresets.Flat)));

            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(starts, fixture.Audio.StartCount);
            Assert.Equal(stops, fixture.Audio.StopCount);
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>既存のカスタム FIR はプリセットを選ぶまで維持する。</summary>
        [Fact]
        public void CustomFirIsPreservedWhenNoPresetIsSelected()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            Song custom = SongSerializer.Load(fixture.Path);
            int[] coefficients = { 32, 16, 8, 4, 2, 1, 0, 0 };
            custom.SnesEcho.FirCoefficients = coefficients;
            SongSerializer.Save(custom, fixture.Path);
            fixture.Presenter.Open(fixture.Path);
            Assert.Null(fixture.Presenter.SnesEcho.CurrentFirPreset);

            fixture.Presenter.SnesEcho.Apply(EchoDelayMilliseconds, EchoFeedback, EchoVolume, null);

            Assert.Equal(coefficients, fixture.Document.Song.SnesEcho.FirCoefficients);
            Assert.NotSame(coefficients, fixture.Document.Song.SnesEcho.FirCoefficients);
            Assert.Equal(1, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>SNES 以外では公開操作からもエコー編集を拒否する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        public void OtherChipsCannotEditEcho(ChipKind chip)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(chip);
            Assert.False(fixture.Presenter.SnesEcho.IsAvailable);
            Assert.Throws<InvalidOperationException>(() => fixture.Presenter.SnesEcho.Apply(
                EchoDelayMilliseconds, EchoFeedback, EchoVolume, nameof(SnesEchoFirPresets.Flat)));
            Assert.Equal(0, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>作業ファイルの置換失敗では設定と履歴を公開せず、元の設定で再生を再開する。</summary>
        [Fact]
        public void FailedSavePreservesSettingsAndHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture(ChipKind.Snes);
            string workingPath = fixture.Document.Session.Path!;
            string before = SongSerializer.Serialize(fixture.Document.Song);
            fixture.Presenter.Transport.TogglePlayback();
            File.Delete(workingPath);
            Directory.CreateDirectory(workingPath);
            try
            {
                fixture.Presenter.Execute(() => fixture.Presenter.SnesEcho.Apply(
                    EchoDelayMilliseconds, EchoFeedback, EchoVolume, nameof(SnesEchoFirPresets.LowPass)));
                Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
                Assert.Equal(0, fixture.Document.Session.History.UndoCount);
                Assert.True(fixture.Audio.IsRunning);
                Assert.False(File.Exists(workingPath));
            }
            finally
            {
                Directory.Delete(workingPath);
            }
        }
    }
}
