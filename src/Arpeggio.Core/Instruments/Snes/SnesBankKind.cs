namespace Arpeggio.Core.Instruments.Snes
{
    /// <summary>八ボイスへ割り当てる内蔵音色の編成。</summary>
    public enum SnesBankKind
    {
        /// <summary>従来の単一音色。</summary>
        None = 0,
        /// <summary>管弦楽と三種のドラム。</summary>
        Orchestral = 1,
        /// <summary>バンド編成。</summary>
        Band = 2,
        /// <summary>シンセ中心の編成。</summary>
        Chip = 3
    }
}
