using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Arpeggio.Formats
{
    /// <summary>明細を上限内で集約し、未保持分も含めて発生数を数える。</summary>
    internal sealed class ConversionDiagnosticCollection
    {
        private readonly int _detailLimit;
        private readonly List<ConversionDiagnostic> _details = new List<ConversionDiagnostic>();
        private readonly Dictionary<ConversionDiagnosticKey, int> _detailIndices = new Dictionary<ConversionDiagnosticKey, int>();
        private readonly Dictionary<string, long> _codeCounts = new Dictionary<string, long>(StringComparer.Ordinal);

        internal ConversionDiagnosticCollection(int detailLimit)
        {
            if (detailLimit < 0 || detailLimit > ConversionLimits.MaximumDiagnosticDetails)
            {
                throw new ArgumentOutOfRangeException(nameof(detailLimit));
            }
            _detailLimit = detailLimit;
            Details = _details.AsReadOnly();
            CodeCounts = new ReadOnlyDictionary<string, long>(_codeCounts);
        }

        internal ConversionDiagnosticCollection(ConversionDiagnosticCollection source) : this(source._detailLimit)
        {
            _details.AddRange(source._details);
            foreach (var entry in source._detailIndices)
            {
                _detailIndices.Add(entry.Key, entry.Value);
            }
            foreach (var entry in source._codeCounts)
            {
                _codeCounts.Add(entry.Key, entry.Value);
            }
            TotalCount = source.TotalCount;
            DroppedCount = source.DroppedCount;
        }

        internal IReadOnlyList<ConversionDiagnostic> Details { get; }
        internal IReadOnlyDictionary<string, long> CodeCounts { get; }
        internal long TotalCount { get; private set; }
        internal long DroppedCount { get; private set; }

        internal void Add(ConversionDiagnostic diagnostic)
        {
            if (string.IsNullOrWhiteSpace(diagnostic.Code) || string.IsNullOrWhiteSpace(diagnostic.Message) ||
                diagnostic.OccurrenceCount <= 0 || diagnostic.SourceChannel is < 1 or > ConversionLimits.MaximumMidiChannel ||
                (diagnostic.MaximumError.HasValue && (!double.IsFinite(diagnostic.MaximumError.Value) || diagnostic.MaximumError.Value < 0)))
            {
                throw new ArgumentException("診断にはコード・説明・正の発生数・有効な位置と誤差が必要です。", nameof(diagnostic));
            }
            TotalCount = checked(TotalCount + diagnostic.OccurrenceCount);
            _codeCounts.TryGetValue(diagnostic.Code, out long codeCount);
            _codeCounts[diagnostic.Code] = checked(codeCount + diagnostic.OccurrenceCount);
            var key = new ConversionDiagnosticKey(diagnostic);
            if (_detailIndices.TryGetValue(key, out int detailIndex))
            {
                ConversionDiagnostic previous = _details[detailIndex];
                bool hasLargerError = diagnostic.MaximumError.HasValue &&
                    (!previous.MaximumError.HasValue || diagnostic.MaximumError.Value > previous.MaximumError.Value);
                ConversionDiagnostic representative = hasLargerError ? diagnostic : previous;
                _details[detailIndex] = representative with { OccurrenceCount = checked(previous.OccurrenceCount + diagnostic.OccurrenceCount) };
                return;
            }
            if (_details.Count >= _detailLimit)
            {
                DroppedCount = checked(DroppedCount + diagnostic.OccurrenceCount);
                return;
            }
            _detailIndices.Add(key, _details.Count);
            _details.Add(diagnostic);
        }
    }
}
