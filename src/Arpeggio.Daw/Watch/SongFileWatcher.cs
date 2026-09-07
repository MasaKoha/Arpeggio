using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Arpeggio.Daw.Watch
{
    /// <summary>ファイルの置換も検知し、通知の連続をまとめる。</summary>
    public sealed class SongFileWatcher : IDisposable
    {
        private const int DebounceMilliseconds = 150;
        private readonly object synchronization = new object();
        private readonly FileSystemWatcher watcher;
        private readonly Timer debounceTimer;
        private readonly Action reload;
        private long lastChangeTimestamp;
        private bool isDisposed;

        /// <summary>指定ファイルを監視し、ワーカースレッドから再読み込みを通知する。</summary>
        public SongFileWatcher(string path, Action reload)
        {
            this.reload = reload;
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath)
                ?? throw new ArgumentException("監視するファイルの親ディレクトリがありません。", nameof(path));
            watcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            try
            {
                watcher.EnableRaisingEvents = true;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>監視とタイマーを止め、購読を解除する。</summary>
        public void Dispose()
        {
            lock (synchronization)
            {
                if (isDisposed)
                {
                    return;
                }
                isDisposed = true;
                debounceTimer.Dispose();
            }
            watcher.Changed -= OnChanged;
            watcher.Created -= OnChanged;
            watcher.Deleted -= OnChanged;
            watcher.Renamed -= OnRenamed;
            watcher.Error -= OnError;
            watcher.Dispose();
        }

        private void OnChanged(object sender, FileSystemEventArgs arguments) => ScheduleReload();
        private void OnRenamed(object sender, RenamedEventArgs arguments) => ScheduleReload();
        private void OnError(object sender, ErrorEventArgs arguments) => ScheduleReload();

        private void ScheduleReload()
        {
            lock (synchronization)
            {
                if (isDisposed)
                {
                    return;
                }
                lastChangeTimestamp = Stopwatch.GetTimestamp();
                debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
            }
        }

        private void OnDebounceElapsed(object? state)
        {
            lock (synchronization)
            {
                if (isDisposed)
                {
                    return;
                }
                // Change 前にキューへ入ったコールバックでも最後の変更から待つ。
                double remainingMilliseconds = DebounceMilliseconds -
                    Stopwatch.GetElapsedTime(lastChangeTimestamp).TotalMilliseconds;
                if (remainingMilliseconds > 0)
                {
                    debounceTimer.Change((int)Math.Ceiling(remainingMilliseconds), Timeout.Infinite);
                    return;
                }
            }
            reload();
        }
    }
}
