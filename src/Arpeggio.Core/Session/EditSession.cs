using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.History;
using Arpeggio.Core.Instruments.Snes;

namespace Arpeggio.Core.Session
{
    /// <summary>単一ソングの編集・保存・履歴を原子的な操作として扱う。</summary>
    public sealed class EditSession
    {
        /// <summary>編集操作の入口をこのセッションに結び付ける。</summary>
        public EditSession()
        {
            Notes = new NoteEditor(this);
            Instruments = new InstrumentEditor(this);
            Sfx = new SfxEditor(this);
        }

        /// <summary>編集中のソング。未オープンなら null。</summary>
        public Song? Song { get; private set; }
        /// <summary>現在の保存先。</summary>
        public string? Path { get; private set; }
        /// <summary>セッション専用の履歴。</summary>
        public SongHistory History { get; } = new SongHistory();
        /// <summary>ノート編集操作。</summary>
        public NoteEditor Notes { get; }
        /// <summary>音色編集操作。</summary>
        public InstrumentEditor Instruments { get; }
        /// <summary>定義と生成列を一括適用する効果音編集操作。</summary>
        public SfxEditor Sfx { get; }

        /// <summary>新規ソングを保存して開く。既存ファイルの上書きは拒否する。</summary>
        public void New(string path, ChipKind chip, int tempoBpm, int lengthTicks)
        {
            New(path, chip, tempoBpm, lengthTicks, string.Empty);
        }

        /// <summary>曲名を含む新規ソングを一度の保存で作成する。</summary>
        public void New(string path, ChipKind chip, int tempoBpm, int lengthTicks, string title, SnesBankKind bank = SnesBankKind.None)
        {
            ValidatePath(path);
            if (File.Exists(path))
            {
                throw new ArgumentException("保存先が既に存在します。Open するか別のパスを指定してください。", nameof(path));
            }
            Song created = SongFactory.Create(chip, tempoBpm, lengthTicks, bank);
            created.Title = title;
            SongSerializer.Save(created, path);
            Song = created;
            Path = path;
            History.Clear();
        }

        /// <summary>読み込み成功時だけソング・保存先・履歴を切り替える。</summary>
        public void Open(string path)
        {
            ValidatePath(path);
            Song loaded = SongSerializer.Load(path);
            Song = loaded;
            Path = path;
            History.Clear();
        }

        /// <summary>現在の状態を検証して保存する。</summary>
        public void Save()
        {
            SongSerializer.Save(GetSong(), GetPath());
        }

        /// <summary>一操作戻して保存する。履歴が無い場合は false。</summary>
        public bool Undo()
        {
            Song current = GetSong();
            if (History.UndoCount == 0)
            {
                return false;
            }
            Song candidate = History.PeekUndo();
            SongValidator.Validate(current);
            SongSerializer.Save(candidate, GetPath());
            History.Undo(current);
            SongSnapshotPublisher.Apply(current, candidate);
            return true;
        }

        /// <summary>一操作やり直して保存する。履歴が無い場合は false。</summary>
        public bool Redo()
        {
            Song current = GetSong();
            if (History.RedoCount == 0)
            {
                return false;
            }
            Song candidate = History.PeekRedo();
            SongValidator.Validate(current);
            SongSerializer.Save(candidate, GetPath());
            History.Redo(current);
            SongSnapshotPublisher.Apply(current, candidate);
            return true;
        }

        internal Song GetSong()
        {
            return Song ?? throw new InvalidOperationException("New または Open でソングを開いてください。");
        }

        internal void Change(Action<Song> edit, Action? beforeSave = null)
        {
            Song current = GetSong();
            Song candidate = SongSerializer.Deserialize(SongSerializer.Serialize(current));
            edit(candidate);
            // 呼び出し側が渡した音色やマクロを後から変更しても、保存済み状態を変えない。
            candidate = SongSerializer.Deserialize(SongSerializer.Serialize(candidate));
            beforeSave?.Invoke();
            SongSerializer.Save(candidate, GetPath());
            History.Record(current);
            SongSnapshotPublisher.Apply(current, candidate);
        }

        internal static Track GetTrack(Song song, int trackIndex)
        {
            if (trackIndex < 0 || trackIndex >= song.Tracks.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(trackIndex), "trackIndex が範囲外です。");
            }
            return song.Tracks[trackIndex];
        }

        private string GetPath()
        {
            return Path ?? throw new InvalidOperationException("New または Open で保存先を指定してください。");
        }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("保存先を指定してください。", nameof(path));
            }
        }
    }
}
