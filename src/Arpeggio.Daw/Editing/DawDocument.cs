using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;

namespace Arpeggio.Daw.Editing
{
    /// <summary>Core の自動保存を作業ファイルへ隔離し、正本の明示保存を管理する。</summary>
    public sealed class DawDocument : IDisposable
    {
        private readonly string workingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"arpeggio-{Guid.NewGuid():N}.json");
        private readonly EditSession savedSession = new EditSession();
        private string savedSnapshot = string.Empty;

        /// <summary>自動保存先を隔離した編集セッション。</summary>
        public EditSession Session { get; } = new EditSession();
        /// <summary>正本の絶対パス。</summary>
        public string Path { get; private set; } = string.Empty;
        /// <summary>現在の公開ソング。</summary>
        public Song Song => Session.Song ?? throw new InvalidOperationException("ソングが開かれていません。");
        /// <summary>正本保存時から編集内容が変わっているか。</summary>
        public bool IsDirty => SongSerializer.Serialize(Song) != savedSnapshot;

        /// <summary>文書の読み込み成功後に正本パスを通知する。</summary>
        public event Action<string>? Opened;
        /// <summary>正本の明示保存成功後に保存先パスを通知する。</summary>
        public event Action<string>? Saved;

        /// <summary>読めることを確認してから作業セッションへ切り替える。</summary>
        public void Open(string path)
        {
            Song loaded = SongSerializer.Load(path);
            SongSerializer.Save(loaded, workingPath);
            savedSession.Open(path);
            Session.Open(workingPath);
            Path = System.IO.Path.GetFullPath(path);
            savedSnapshot = SongSerializer.Serialize(Song);
            Opened?.Invoke(Path);
        }

        /// <summary>正本の EditSession.Save を成功させたときだけ保存基準を進める。</summary>
        public void Save()
        {
            Song target = savedSession.Song ?? throw new InvalidOperationException("保存先がありません。");
            target.Title = Song.Title;
            target.Chip = Song.Chip;
            target.TempoBpm = Song.TempoBpm;
            target.LengthTicks = Song.LengthTicks;
            target.LoopStartTick = Song.LoopStartTick;
            target.Instruments = Song.Instruments;
            target.Tracks = Song.Tracks;
            target.SnesEcho = Song.SnesEcho;
            savedSession.Save();
            savedSnapshot = SongSerializer.Serialize(Song);
            Saved?.Invoke(Path);
        }

        /// <summary>自分の保存・重複通知を内容比較で判別する。</summary>
        public bool HasExternalChange() => SongSerializer.Serialize(SongSerializer.Load(Path)) != savedSnapshot;

        /// <summary>Core に公開編集 API がないエコー設定を、保存成功後だけ一履歴で公開する。</summary>
        internal void UpdateSnesEcho(SnesEchoSettings settings)
        {
            Song candidate = SongSerializer.Deserialize(SongSerializer.Serialize(Song));
            candidate.SnesEcho = new SnesEchoSettings
            {
                DelayMilliseconds = settings.DelayMilliseconds,
                Feedback = settings.Feedback,
                Volume = settings.Volume,
                FirCoefficients = (int[])settings.FirCoefficients.Clone()
            };
            // 公開ソングの先行変更を避け、検証や保存の失敗では履歴と表示を維持する。
            SongSerializer.Save(candidate, workingPath);
            Session.History.Record(Song);
            Song.SnesEcho = candidate.SnesEcho;
        }

        /// <summary>終了時に一時作業ファイルを削除する。</summary>
        public void Dispose()
        {
            try { File.Delete(workingPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
