namespace Arpeggio.Core.Render
{
    /// <summary>PCM の出力レート・再生回数・末尾余白を指定する。</summary>
    public readonly record struct RenderSettings
    {
        /// <summary>標準設定を明示し、構造体のゼロ初期化と区別する。</summary>
        public RenderSettings() : this(44100, 1, 0.5) { }

        /// <summary>指定レートで全曲とループ区間を再生し、末尾余白を追加する。</summary>
        public RenderSettings(int SampleRate = 44100, int LoopCount = 1, double TailSeconds = 0.5)
        {
            this.SampleRate = SampleRate;
            this.LoopCount = LoopCount;
            this.TailSeconds = TailSeconds;
        }
        /// <summary>一秒当たりのステレオフレーム数。</summary>
        public int SampleRate { get; init; }
        /// <summary>最初の全曲再生を含む再生回数。</summary>
        public int LoopCount { get; init; }
        /// <summary>ノート終了後の残響用余白。</summary>
        public double TailSeconds { get; init; }
    }
}
