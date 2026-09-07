using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Arpeggio.Daw.Platform
{
    /// <summary>書き出したファイルを OS のファイルブラウザ（Finder / Explorer）で選択状態にして開く。</summary>
    public static class FileRevealer
    {
        /// <summary>ファイルが存在すればそれを選択して開き、無ければ親フォルダだけを開く。</summary>
        public static void Reveal(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath) ?? fullPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // -R は Finder で対象を選択状態にする。ファイルが無いときはフォルダを開く
                Start("open", File.Exists(fullPath) ? new[] { "-R", fullPath } : new[] { directory });
                return;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Start("explorer.exe", File.Exists(fullPath) ? new[] { "/select,", fullPath } : new[] { directory });
                return;
            }
            Start("xdg-open", new[] { directory });
        }

        private static void Start(string fileName, string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            using Process? process = Process.Start(startInfo);
        }
    }
}
