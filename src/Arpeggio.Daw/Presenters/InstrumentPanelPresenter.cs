using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Import;
using Arpeggio.Core.Session;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>選択音とトラックの既定音色を編集セッションへ接続する。</summary>
    public sealed class InstrumentPanelPresenter
    {
        private readonly DawDocument document;
        private readonly PianoRollPresenter pianoRoll;
        private readonly Action changed;
        private readonly Dictionary<int, int> defaultInstruments = new Dictionary<int, int>();

        /// <summary>明示的に編集対象と選択状態、変更通知を受け取る。</summary>
        public InstrumentPanelPresenter(DawDocument document, PianoRollPresenter pianoRoll, Action changed)
        {
            this.document = document;
            this.pianoRoll = pianoRoll;
            this.changed = changed;
        }

        /// <summary>参照交換による音色一覧の変更検知に使う公開スナップショット。</summary>
        public IReadOnlyList<Instrument> InstrumentSnapshot => document.Song.Instruments;

        /// <summary>選択チャンネルと互換のある音色。</summary>
        public IReadOnlyList<Instrument> Instruments => document.Song.Instruments
            .Where(instrument => instrument.Kind == RequiredKind).ToArray();

        /// <summary>選択音があればその音色、それ以外はトラックの既定。</summary>
        public Instrument? CurrentInstrument
        {
            get
            {
                Note? note = pianoRoll.SelectedNote;
                if (note != null)
                {
                    return document.Song.Instruments.Find(instrument => instrument.Id == note.InstrumentId);
                }
                if (defaultInstruments.TryGetValue(pianoRoll.SelectedTrack, out int instrumentId))
                {
                    Instrument? remembered = document.Song.Instruments.Find(instrument =>
                        instrument.Id == instrumentId && instrument.Kind == RequiredKind);
                    if (remembered != null)
                    {
                        return remembered;
                    }
                }
                return document.Song.Instruments.Find(instrument => instrument.Kind == RequiredKind);
            }
        }

        /// <summary>チャンネルに対応する音色の種類。</summary>
        public InstrumentKind RequiredKind => (document.Song.Chip, document.Song.Tracks[pianoRoll.SelectedTrack].Channel) switch
        {
            (ChipKind.Nes, ChannelKind.Pulse) => InstrumentKind.NesPulse,
            (ChipKind.Nes, ChannelKind.Triangle) => InstrumentKind.NesTriangle,
            (ChipKind.Nes, ChannelKind.Noise) => InstrumentKind.NesNoise,
            (ChipKind.Nes, ChannelKind.Dpcm) => InstrumentKind.NesDpcm,
            (ChipKind.GameBoy, ChannelKind.Pulse) => InstrumentKind.GbPulse,
            (ChipKind.GameBoy, ChannelKind.Wave) => InstrumentKind.GbWave,
            (ChipKind.GameBoy, ChannelKind.Noise) => InstrumentKind.GbNoise,
            (ChipKind.Snes, ChannelKind.Sample) => InstrumentKind.SnesSample,
            _ => throw new InvalidOperationException("未対応のチャンネルです。")
        };

        /// <summary>ノート追加に使う音色 ID を取得する。</summary>
        public int ResolveInstrumentId() => CurrentInstrument?.Id
            ?? throw new InvalidOperationException("このトラックに使う音色を追加してください。");

        /// <summary>文書切替時にトラックごとの既定音色を忘れる。</summary>
        public void ResetSelection() => defaultInstruments.Clear();

        /// <summary>WAV を複製音色へ取り込み、成功時だけ一履歴で公開する。</summary>
        public void ImportWav(string path, string rootNote, bool loop)
        {
            pianoRoll.EndDrag();
            if (CurrentInstrument is not SnesSampleInstrument current)
            {
                throw new InvalidOperationException("WAV の取り込みには SNES 音色を選択してください。");
            }
            SnesSampleInstrument replacement = (SnesSampleInstrument)InstrumentJson.Deserialize(InstrumentJson.Serialize(current));
            WavSampleImporter.Import(replacement, path, NoteName.Parse(rootNote), null, null, loop);
            document.Session.Instruments.Update(replacement);
            changed();
        }

        /// <summary>表示対象の kind に実在する項目だけを返す。</summary>
        public IReadOnlyList<InstrumentParameter> GetParameters() => CurrentInstrument is Instrument instrument
            ? InstrumentParameterEditor.Describe(instrument) : Array.Empty<InstrumentParameter>();

        /// <summary>選択音へ割り当て、後続ノートの既定として記憶する。</summary>
        public void SelectInstrument(int instrumentId)
        {
            Instrument selected = document.Song.Instruments.Find(instrument =>
                instrument.Id == instrumentId && instrument.Kind == RequiredKind)
                ?? throw new ArgumentException("選択トラックでは使えない音色です。");
            pianoRoll.EndDrag();
            Note? note = pianoRoll.SelectedNote;
            if (note != null && note.InstrumentId != selected.Id)
            {
                Note replacement = new Note
                {
                    Tick = note.Tick, DurationTicks = note.DurationTicks, MidiNote = note.MidiNote,
                    Volume = note.Volume, InstrumentId = selected.Id, Effects = note.Effects
                };
                document.Session.Notes.Update(pianoRoll.SelectedTrack, note.Tick, replacement);
            }
            defaultInstruments[pianoRoll.SelectedTrack] = selected.Id;
            changed();
        }

        /// <summary>同じチャンネル用の既定音色を追加する。</summary>
        public void AddInstrument()
        {
            pianoRoll.EndDrag();
            Instrument created = CreateInstrument();
            created.Id = checked(document.Song.Instruments.Select(instrument => instrument.Id).DefaultIfEmpty(0).Max() + 1);
            created.Name = $"{RequiredKind} {created.Id}";
            document.Session.Instruments.Add(created);
            // 追加は一履歴とし、ノートへの割当は音色一覧の選択で明示する。
            pianoRoll.ClearSelection();
            defaultInstruments[pianoRoll.SelectedTrack] = created.Id;
            changed();
        }

        /// <summary>参照中の音色は Core の検証で拒否して削除する。</summary>
        public void RemoveInstrument()
        {
            pianoRoll.EndDrag();
            document.Session.Instruments.Remove(ResolveInstrumentId());
            changed();
        }

        /// <summary>名前とパラメータを一操作で検証して公開する。</summary>
        public void Apply(string name, IReadOnlyDictionary<string, string> values)
        {
            pianoRoll.EndDrag();
            Instrument current = CurrentInstrument ?? throw new InvalidOperationException("音色を追加してください。");
            Instrument replacement = InstrumentParameterEditor.Apply(current, name, values);
            document.Session.Instruments.Update(replacement);
            changed();
        }

        private Instrument CreateInstrument() => RequiredKind switch
        {
            InstrumentKind.NesPulse => new NesPulseInstrument(),
            InstrumentKind.NesTriangle => new NesTriangleInstrument(),
            InstrumentKind.NesNoise => new NesNoiseInstrument(),
            InstrumentKind.NesDpcm => new NesDpcmInstrument(),
            InstrumentKind.GbPulse => new GbPulseInstrument(),
            InstrumentKind.GbWave => new GbWaveInstrument(),
            InstrumentKind.GbNoise => new GbNoiseInstrument(),
            InstrumentKind.SnesSample => new SnesSampleInstrument(),
            _ => throw new InvalidOperationException("未対応の音色です。")
        };
    }
}
