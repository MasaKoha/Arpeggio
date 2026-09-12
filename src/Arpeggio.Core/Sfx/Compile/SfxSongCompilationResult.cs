using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx.Curves;

namespace Arpeggio.Core.Sfx.Compile
{
    /// <summary>通常の再生・書き出しへ渡す Song と、生成時点の曲線・診断。</summary>
    public sealed class SfxSongCompilationResult
    {
        internal SfxSongCompilationResult(Song song, SfxCurveGenerationResult curves,
            IReadOnlyList<SfxPitchFrame> tonePitchFrames, IReadOnlyList<SfxGenerationWarning> warnings)
        {
            Song = song;
            Curves = curves;
            TonePitchFrames = tonePitchFrames;
            Warnings = warnings;
            ToneTrackIndex = curves.Tone is null ? null : SfxSongCompiler.ToneTrackIndex;
            NoiseTrackIndex = curves.Noise is null ? null : SfxSongCompiler.GetNoiseTrackIndex(song.Chip);
        }

        /// <summary>新しく組み立てた検証済み Song。SFX 定義・出自の適用は編集側が担う。</summary>
        public Song Song { get; }

        /// <summary>正規化パラメータと有効レイヤーの包絡・時間・ピッチ列。マクロは Song と共有する。</summary>
        public SfxCurveGenerationResult Curves { get; }

        /// <summary>全トーンフレームの要求値と実効値。トーン無効時は空。</summary>
        public IReadOnlyList<SfxPitchFrame> TonePitchFrames { get; }

        /// <summary>時間・無効設定に続いて、duty・noise・pitch の順の生成診断。</summary>
        public IReadOnlyList<SfxGenerationWarning> Warnings { get; }

        /// <summary>有効トーンの配置先。無効時は null。</summary>
        public int? ToneTrackIndex { get; }

        /// <summary>有効ノイズの配置先。無効時は null。</summary>
        public int? NoiseTrackIndex { get; }
    }
}
