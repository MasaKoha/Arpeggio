using System;
using System.IO;

namespace Arpeggio.Core.Tests.Brief
{
    /// <summary>作曲指示書テストごとの作業ファイルを隔離する。</summary>
    internal sealed class BriefFileFixture : IDisposable
    {
        internal BriefFileFixture()
        {
            DirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "arpeggio-brief-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        internal string DirectoryPath { get; }
        internal string PathFor(string name) => Path.Combine(DirectoryPath, name);

        /// <summary>このテストで作成したファイルだけを破棄する。</summary>
        public void Dispose()
        {
            Directory.Delete(DirectoryPath, true);
        }
    }
}
