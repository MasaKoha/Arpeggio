using System;
using System.IO;
using System.Text;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Storage;

namespace Arpeggio.Daw.Editing.Sfx
{
    /// <summary>候補の新規保存と、保存済み内容を開けるかの判定を担当する。</summary>
    public sealed class SfxCandidateFile
    {
        private string? savedRevision;

        /// <summary>最後に新規保存できた絶対パス。保存失敗時には更新しない。</summary>
        public string? SavedPath { get; private set; }

        /// <summary>現在候補が保存済み内容と一致するか。</summary>
        public bool IsCurrent(Song candidate) => savedRevision == SfxHash.ComputeRevision(candidate);

        /// <summary>隣接一時ファイルから上書きしない移動で保存する。</summary>
        public void SaveNew(Song candidate, string path)
        {
            string destination = Path.GetFullPath(path);
            if (File.Exists(destination))
            {
                throw new SfxEditException("DestinationExists", "保存先が既に存在します。");
            }
            string content = SongSerializer.Serialize(candidate);
            string revision = SfxHash.ComputeRevision(candidate);
            string temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
                File.Move(temporaryPath, destination, false);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            SavedPath = destination;
            savedRevision = revision;
        }

        /// <summary>現在候補とディスクの両方を検証し、古い保存内容を開かせない。</summary>
        public string RequireCurrentPath(Song candidate)
        {
            if (SavedPath is null || !IsCurrent(candidate))
            {
                throw new InvalidOperationException("保存した SFX は古い値です。最新候補を新規保存してください。");
            }
            if (SfxHash.ComputeRevision(SongSerializer.Load(SavedPath)) != savedRevision)
            {
                throw new SfxEditException("RevisionConflict", "保存した SFX が外部で変更されています。");
            }
            return SavedPath;
        }
    }
}
