using System;
using System.IO;

namespace Arpeggio.Formats.Export
{
    /// <summary>検証済みの不変な列とメタデータを所有し、診断時と同じ演奏だけを保存する。</summary>
    public sealed class ChipExportPlan
    {
        private readonly Action<Stream>? _writePrepared;

        internal ChipExportPlan(Action<Stream>? writePrepared, ConversionReport report)
        {
            _writePrepared = writePrepared;
            Report = report;
        }

        /// <summary>変換・サイズ・CPU 予算を含む保存前の全診断。</summary>
        public ConversionReport Report { get; }
        /// <summary>確定済み内容があり、strict を含む診断が保存を許可するか。</summary>
        public bool CanWrite => _writePrepared != null && Report.CanWrite;

        internal bool WriteTo(Stream destination)
        {
            if (!CanWrite)
            {
                return false;
            }
            _writePrepared!(destination);
            return true;
        }
    }
}
