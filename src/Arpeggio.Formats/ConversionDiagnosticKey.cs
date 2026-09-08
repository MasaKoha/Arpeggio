namespace Arpeggio.Formats
{
    /// <summary>元ノート単位で原因を識別する。変調ごとの出力時刻や値は集約キーに含めない。</summary>
    internal readonly record struct ConversionDiagnosticKey
    {
        internal ConversionDiagnosticKey(ConversionDiagnostic diagnostic)
        {
            Code = diagnostic.Code;
            SourceTrack = diagnostic.SourceTrack;
            SourceChannel = diagnostic.SourceChannel;
            SourceEvent = diagnostic.SourceEvent;
            SourceTick = diagnostic.SourceTick;
            OutputTrack = diagnostic.OutputTrack;
        }

        internal string Code { get; }
        internal int? SourceTrack { get; }
        internal int? SourceChannel { get; }
        internal long? SourceEvent { get; }
        internal long? SourceTick { get; }
        internal int? OutputTrack { get; }
    }
}
