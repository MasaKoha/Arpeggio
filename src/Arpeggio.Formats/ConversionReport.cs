using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Arpeggio.Core.Document;

namespace Arpeggio.Formats
{
    /// <summary>変換全体の診断・恒常的制限・集計を保持する。警告の表示上限は成功判定に影響しない。</summary>
    public sealed class ConversionReport
    {
        private readonly ConversionDiagnosticCollection _warnings;
        private readonly ConversionDiagnosticCollection _errors;
        private readonly List<string> _limitations = new List<string>();
        private readonly Dictionary<string, long> _statistics = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>形式・チップ・strict と明細保持上限を固定する。保持上限は規定値以下だけを許可する。</summary>
        public ConversionReport(ConversionFormat format, ChipKind chip, bool strict = false,
            int diagnosticDetailLimit = ConversionLimits.MaximumDiagnosticDetails)
        {
            Format = format;
            Chip = chip;
            Strict = strict;
            _warnings = new ConversionDiagnosticCollection(diagnosticDetailLimit);
            _errors = new ConversionDiagnosticCollection(diagnosticDetailLimit);
            Limitations = _limitations.AsReadOnly();
            Statistics = new ReadOnlyDictionary<string, long>(_statistics);
        }

        /// <summary>対象形式。</summary>
        public ConversionFormat Format { get; }
        /// <summary>対象チップ。</summary>
        public ChipKind Chip { get; }
        /// <summary>全変換警告を保存拒否の理由とするか。</summary>
        public bool Strict { get; }
        /// <summary>展開済みの演奏秒数。</summary>
        public double DurationSeconds { get; private set; }
        /// <summary>検証時に算定した出力バイト数。</summary>
        public long OutputBytes { get; private set; }
        /// <summary>保持上限内の変換警告。</summary>
        public IReadOnlyList<ConversionDiagnostic> Warnings => _warnings.Details;
        /// <summary>保持上限内の変換エラー。</summary>
        public IReadOnlyList<ConversionDiagnostic> Errors => _errors.Details;
        /// <summary>strict の失敗理由に含めない恒常的な方式の制限。</summary>
        public IReadOnlyList<string> Limitations { get; }
        /// <summary>採用数などの名前付き整数統計。</summary>
        public IReadOnlyDictionary<string, long> Statistics { get; }
        /// <summary>未保持分を含む全警告発生数。</summary>
        public long WarningCount => _warnings.TotalCount;
        /// <summary>未保持分を含む全エラー発生数。</summary>
        public long ErrorCount => _errors.TotalCount;
        /// <summary>明細へ保持できなかった警告発生数。</summary>
        public long DroppedWarningCount => _warnings.DroppedCount;
        /// <summary>明細へ保持できなかったエラー発生数。</summary>
        public long DroppedErrorCount => _errors.DroppedCount;
        /// <summary>未保持分を含むコード別警告発生数。</summary>
        public IReadOnlyDictionary<string, long> WarningCountsByCode => _warnings.CodeCounts;
        /// <summary>未保持分を含むコード別エラー発生数。</summary>
        public IReadOnlyDictionary<string, long> ErrorCountsByCode => _errors.CodeCounts;
        /// <summary>全診断に基づく保存可否。方式の制限だけでは失敗にしない。</summary>
        public bool CanWrite => ErrorCount == 0 && (!Strict || WarningCount == 0);

        /// <summary>変換警告を登録し、同じ原因・元位置の明細へ集約する。</summary>
        public void AddWarning(ConversionDiagnostic diagnostic) => _warnings.Add(diagnostic);
        /// <summary>変換エラーを登録し、同じ原因・元位置の明細へ集約する。</summary>
        public void AddError(ConversionDiagnostic diagnostic) => _errors.Add(diagnostic);

        /// <summary>恒常的な制限の説明を重複なく追加する。</summary>
        public void AddLimitation(string limitation)
        {
            if (string.IsNullOrWhiteSpace(limitation))
            {
                throw new ArgumentException("制限の説明を指定してください。", nameof(limitation));
            }
            if (!_limitations.Contains(limitation))
            {
                _limitations.Add(limitation);
            }
        }

        /// <summary>名前付きの集計結果を設定する。</summary>
        public void SetStatistic(string name, long value)
        {
            if (string.IsNullOrWhiteSpace(name) || value < 0)
            {
                throw new ArgumentException("統計には名前と非負の値が必要です。");
            }
            _statistics[name] = value;
        }

        /// <summary>演奏時間と予定出力サイズを設定する。資源上限は ConversionLimits で別途検証する。</summary>
        public void SetOutputMetrics(double durationSeconds, long outputBytes)
        {
            if (!double.IsFinite(durationSeconds) || durationSeconds < 0 || outputBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            }
            DurationSeconds = durationSeconds;
            OutputBytes = outputBytes;
        }
    }
}
