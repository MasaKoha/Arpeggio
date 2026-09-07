using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sequencing;
using Arpeggio.Core.Synthesis;

namespace Arpeggio.Core.Render
{
    /// <summary>編集可能なソングを確保なしのステレオ PCM ストリームへ変換する。</summary>
    public sealed class SongRenderer
    {
        private const int StereoChannels = 2;
        private const int SeekBufferFrames = 1024;
        private const int MaximumVolume = 15;
        private const double PitchWarningEpsilon = 0.000001;
        private readonly Song _song;
        private readonly RenderSettings _settings;
        private readonly FrameClock _frameClock;
        private readonly float[] _seekBuffer = new float[SeekBufferFrames * StereoChannels];
        private TickClock _tickClock;
        private Track[] _tracks = Array.Empty<Track>();
        private TrackSequencer[] _sequencers = Array.Empty<TrackSequencer>();
        private IChannelSynthesizer[] _synthesizers = Array.Empty<IChannelSynthesizer>();
        private Instrument?[] _activeInstruments = Array.Empty<Instrument?>();
        private Note?[] _activeNotes = Array.Empty<Note?>();
        private float[] _channelSamples = Array.Empty<float>();
        private List<Instrument> _instruments;
        private RenderMixer _mixer;
        private int _lengthTicks;
        private int _loopStartTick;
        private long _totalSamples;
        private long _lastFrame;

        /// <summary>ソング参照を保持し、合成器・バッファを事前確保する。</summary>
        public SongRenderer(Song song, RenderSettings settings)
        {
            ValidateSettings(settings);
            SongValidator.Validate(song);
            _song = song;
            _settings = settings;
            _frameClock = new FrameClock(settings.SampleRate);
            _tickClock = new TickClock(song.TempoBpm, settings.SampleRate);
            _instruments = song.Instruments;
            _mixer = CreatePipeline();
        }

        /// <summary>音域クランプや音量無視の記録。</summary>
        public RenderReport Report { get; } = new RenderReport();
        /// <summary>先頭から書き出したステレオフレーム数。</summary>
        public long PositionSamples { get; private set; }
        /// <summary>末尾余白も含めて書き出し終えたか。</summary>
        public bool IsFinished => PositionSamples >= _totalSamples;

        /// <summary>左右一組を一フレームとして書き込み、書いたフレーム数を返す。</summary>
        public int Render(Span<float> interleavedStereo)
        {
            if (interleavedStereo.Length % StereoChannels != 0)
            {
                throw new ArgumentException("ステレオ PCM バッファは偶数長にしてください。", nameof(interleavedStereo));
            }
            // perf: 参照更新も発音も警告記録も、生成時に確保した領域だけを使う。
            interleavedStereo.Clear();
            RefreshDocument();
            int frames = (int)Math.Min(interleavedStereo.Length / StereoChannels, _totalSamples - PositionSamples);
            for (int index = 0; index < frames; index++)
            {
                AdvanceFrameBoundary();
                RenderChannels();
                _mixer.Mix(_channelSamples, out float left, out float right);
                interleavedStereo[index * StereoChannels] = left;
                interleavedStereo[index * StereoChannels + 1] = right;
                PositionSamples++;
            }
            return frames;
        }

        /// <summary>現在位置から末尾余白までを一括で書き出す。</summary>
        public float[] RenderAll()
        {
            long values = checked((_totalSamples - PositionSamples) * StereoChannels);
            if (values > int.MaxValue)
            {
                throw new InvalidOperationException("一括出力の配列上限を超えます。Render で分割してください。");
            }
            var samples = new float[(int)values];
            Render(samples);
            return samples;
        }

        /// <summary>先頭から捨て読みして位相・マクロ・残響も含めて位置を復元する。</summary>
        public void Seek(long positionSamples)
        {
            if (positionSamples < 0 || positionSamples > _totalSamples)
            {
                throw new ArgumentOutOfRangeException(nameof(positionSamples));
            }
            Reset();
            while (PositionSamples < positionSamples)
            {
                int frames = (int)Math.Min(SeekBufferFrames, positionSamples - PositionSamples);
                Render(_seekBuffer.AsSpan(0, frames * StereoChannels));
            }
        }

        /// <summary>現在のソング構成から合成状態を再構築する。コールバック外で呼ぶ。</summary>
        public void Reset()
        {
            SongValidator.Validate(_song);
            _mixer = CreatePipeline();
            PositionSamples = 0;
            _lastFrame = 0;
            Report.Clear();
        }

        private RenderMixer CreatePipeline()
        {
            _lengthTicks = _song.LengthTicks;
            _loopStartTick = _song.LoopStartTick;
            _tickClock = new TickClock(_song.TempoBpm, _settings.SampleRate);
            double totalTicks = _lengthTicks + (_settings.LoopCount - 1.0) * (_lengthTicks - _loopStartTick);
            long tailSamples = checked((long)Math.Round(_settings.TailSeconds * _settings.SampleRate, MidpointRounding.AwayFromZero));
            _totalSamples = checked(_tickClock.TickToSamples(totalTicks) + tailSamples);
            _tracks = _song.Tracks.ToArray();
            _instruments = _song.Instruments;
            _sequencers = new TrackSequencer[_tracks.Length];
            _synthesizers = new IChannelSynthesizer[_tracks.Length];
            _activeInstruments = new Instrument?[_tracks.Length];
            _activeNotes = new Note?[_tracks.Length];
            _channelSamples = new float[_tracks.Length];
            for (int index = 0; index < _tracks.Length; index++)
            {
                _sequencers[index] = new TrackSequencer(_tracks[index]);
                _synthesizers[index] = ChannelSynthesizerFactory.Create(_song.Chip, _tracks[index].Channel, _settings.SampleRate);
            }
            return new RenderMixer(_song, _settings.SampleRate, _tracks, _synthesizers);
        }

        private void RefreshDocument()
        {
            _instruments = _song.Instruments;
            for (int index = 0; index < _sequencers.Length; index++)
            {
                _sequencers[index].Refresh();
                Note? note = _activeNotes[index];
                if (note is null)
                {
                    continue;
                }
                Instrument? instrument = FindInstrument(note.InstrumentId);
                if (instrument != null && !ReferenceEquals(instrument, _activeInstruments[index]))
                {
                    // フレーム更新後のイベントとして再発音し、先頭マクロの飛ばしを防ぐ。
                    _sequencers[index].Reset();
                }
            }
        }

        private void AdvanceFrameBoundary()
        {
            long frame = _frameClock.GetFrame(PositionSamples);
            while (_lastFrame < frame)
            {
                for (int index = 0; index < _synthesizers.Length; index++)
                {
                    _synthesizers[index].AdvanceFrame();
                }
                _lastFrame++;
            }
        }

        private void RenderChannels()
        {
            for (int index = 0; index < _synthesizers.Length; index++)
            {
                if (_sequencers[index].TryGetEvent(PositionSamples, _tickClock, _lengthTicks, _loopStartTick, _settings.LoopCount, out NoteEvent noteEvent))
                {
                    ApplyEvent(index, noteEvent);
                }
                Span<float> sample = _channelSamples.AsSpan(index, 1);
                sample.Clear();
                _synthesizers[index].Render(sample);
            }
        }

        private void ApplyEvent(int index, NoteEvent noteEvent)
        {
            _synthesizers[index].NoteOff();
            _activeNotes[index] = noteEvent.Note;
            _activeInstruments[index] = null;
            Note? note = noteEvent.Note;
            if (note is null)
            {
                return;
            }
            Instrument? instrument = FindInstrument(note.InstrumentId);
            if (instrument != null)
            {
                double remainingTicks = Math.Max(0, noteEvent.DurationTicks - noteEvent.ElapsedTicks);
                double durationSeconds = (double)_tickClock.TickToSamples(remainingTicks) / _settings.SampleRate;
                StartNote(index, note, instrument, durationSeconds);
            }
        }

        private void StartNote(int index, Note note, Instrument instrument, double durationSeconds)
        {
            _synthesizers[index].NoteOn(note.MidiNote, note.Volume, instrument, note.Effects);
            if (_synthesizers[index] is ChannelSynthesizer channel)
            {
                channel.SetNoteDuration(durationSeconds);
            }
            _activeInstruments[index] = instrument;
            RecordWarnings(index, note);
        }

        private Instrument? FindInstrument(int instrumentId)
        {
            for (int index = 0; index < _instruments.Count; index++)
            {
                Instrument instrument = _instruments[index];
                if (instrument.Id == instrumentId)
                {
                    return instrument;
                }
            }
            return null;
        }

        private void RecordWarnings(int index, Note note)
        {
            ChannelKind channel = _tracks[index].Channel;
            if (channel == ChannelKind.Noise || channel == ChannelKind.Dpcm)
            {
                return;
            }
            Instrument? instrument = FindInstrument(note.InstrumentId);
            double actual = instrument is SnesSampleInstrument sample && sample.SampleData != null
                ? PitchTable.ClampSnesSampleMidiNote(note.MidiNote, sample.RootMidiNote)
                : PitchTable.ClampMidiNote(_song.Chip, channel, note.MidiNote);
            if (Math.Abs(actual - note.MidiNote) > PitchWarningEpsilon)
            {
                Report.Add(new RenderWarning(RenderWarningKind.PitchClamped, index, note.Tick, note.MidiNote, actual));
            }
            if (channel == ChannelKind.Triangle && note.Volume != MaximumVolume)
            {
                Report.Add(new RenderWarning(RenderWarningKind.TriangleVolumeIgnored, index, note.Tick, note.Volume, MaximumVolume));
            }
        }

        private static void ValidateSettings(RenderSettings settings)
        {
            if (settings.SampleRate <= 0 || settings.LoopCount <= 0 || settings.TailSeconds < 0 ||
                double.IsNaN(settings.TailSeconds) || double.IsInfinity(settings.TailSeconds) ||
                settings.TailSeconds * settings.SampleRate >= long.MaxValue)
            {
                throw new ArgumentException("サンプルレート・ループ回数は正、末尾余白は有限の非負値にしてください。", nameof(settings));
            }
        }
    }
}
