using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Arpeggio.Core.Render
{
    /// <summary>合成中の補正を事前確保した領域に記録する。</summary>
    public sealed class RenderReport
    {
        /// <summary>オーディオ処理中に保持できる警告件数の上限。</summary>
        public const int WarningCapacity = 4096;
        private readonly List<RenderWarning> _warnings = new List<RenderWarning>(WarningCapacity);
        private readonly ReadOnlyCollection<RenderWarning> _readOnlyWarnings;

        /// <summary>読み取り用ビューも合成開始前に生成する。</summary>
        public RenderReport() => _readOnlyWarnings = _warnings.AsReadOnly();
        /// <summary>同じトラック・tick・種別で重複を除いた警告。</summary>
        public IReadOnlyList<RenderWarning> Warnings => _readOnlyWarnings;
        /// <summary>事前確保領域を超えた警告の通知回数。</summary>
        public long DroppedWarningCount { get; private set; }

        internal void Add(RenderWarning warning)
        {
            // perf: 重複判定も既存リスト上で行い、ハッシュ表の拡張を起こさない。
            for (int index = 0; index < _warnings.Count; index++)
            {
                RenderWarning existing = _warnings[index];
                if (existing.Kind == warning.Kind && existing.TrackIndex == warning.TrackIndex && existing.Tick == warning.Tick)
                {
                    return;
                }
            }
            if (_warnings.Count == WarningCapacity)
            {
                DroppedWarningCount++;
                return;
            }
            _warnings.Add(warning);
        }

        internal void Clear()
        {
            _warnings.Clear();
            DroppedWarningCount = 0;
        }
    }
}
