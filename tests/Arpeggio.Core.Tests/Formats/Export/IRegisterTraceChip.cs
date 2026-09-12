namespace Arpeggio.Core.Tests.Formats.Export
{
    /// <summary>生成実装から独立したテスト音源のクロック・書き込み・観測境界。</summary>
    internal interface IRegisterTraceChip
    {
        /// <summary>サンプル時刻から整数 cycles へ換算するクロック。</summary>
        int ClockRate { get; }
        /// <summary>一つの実機レジスタ書き込みを適用する。</summary>
        void Apply(int address, int value);
        /// <summary>指定 cycles だけ状態を進める。</summary>
        void AdvanceCycles(int cycles);
        /// <summary>現在の検証用左右出力。</summary>
        (double Left, double Right) Output { get; }
    }
}
