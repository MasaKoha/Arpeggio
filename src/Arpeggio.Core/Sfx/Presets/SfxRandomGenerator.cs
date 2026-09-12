namespace Arpeggio.Core.Sfx.Presets
{
    /// <summary>アルゴリズム版1のxorshift32。OSやSystem.Randomの実装に依存しない。</summary>
    public sealed class SfxRandomGenerator
    {
        private const uint ZeroSeedState = 0x6D2B79F5;
        private const int FirstLeftShift = 13;
        private const int RightShift = 17;
        private const int LastLeftShift = 5;
        private const double UnsignedIntegerRange = 4294967296.0;
        private uint _state;

        /// <summary>符号なし32bitのseedで初期化する。0だけ規定の非ゼロ状態へ置換する。</summary>
        public SfxRandomGenerator(uint seed)
        {
            _state = seed == 0 ? ZeroSeedState : seed;
        }

        /// <summary>状態を一回進め、符号なし32bitの出力を返す。</summary>
        public uint NextUInt32()
        {
            unchecked
            {
                _state ^= _state << FirstLeftShift;
                _state ^= _state >> RightShift;
                _state ^= _state << LastLeftShift;
            }
            return _state;
        }

        /// <summary>状態を一回進め、0以上1未満の実数を返す。</summary>
        public double NextDouble() => NextUInt32() / UnsignedIntegerRange;
    }
}
