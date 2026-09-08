using System;
using System.Collections.Generic;

namespace Arpeggio.Core.Instruments.Snes
{
    /// <summary>プリセットの名前・分類・説明・推奨値の正本。</summary>
    public static class SnesInstrumentCatalog
    {
        private static readonly IReadOnlyList<SnesInstrumentPreset> Presets = Array.AsReadOnly(new[]
        {
            new SnesInstrumentPreset("strings", "持続系", "柔らかな鋸歯状倍音と二声の微小デチューン", 128, 59, true, new SnesAdsrRegisters(9, 3, 6, 0), 0.35),
            new SnesInstrumentPreset("brass", "持続系", "矩形波寄りの倍音が揺れる金管", 128, 59, true, new SnesAdsrRegisters(14, 3, 6, 0), 0.2),
            new SnesInstrumentPreset("organ", "持続系", "1・2・3・4 倍音のドローバー", 128, 59, true, new SnesAdsrRegisters(15, 0, 7, 0), 0.15),
            new SnesInstrumentPreset("choir", "持続系", "正弦波と約 1 kHz のフォルマント", 128, 59, true, new SnesAdsrRegisters(8, 2, 6, 0), 0.4),
            new SnesInstrumentPreset("flute", "持続系", "弱い第二倍音と息のノイズ", 128, 59, true, new SnesAdsrRegisters(13, 2, 6, 0), 0.25),
            new SnesInstrumentPreset("lead", "持続系", "25 % パルスのシンセリード", 128, 59, true, new SnesAdsrRegisters(15, 3, 6, 0), 0.15),
            new SnesInstrumentPreset("bass", "持続系", "三角波と第二倍音の低音", 128, 59, true, new SnesAdsrRegisters(15, 4, 5, 0), 0.05),
            new SnesInstrumentPreset("piano", "減衰系", "高次倍音が先に減衰する二段のピアノ", 25600, 60, false, new SnesAdsrRegisters(15, 0, 7, 0), 0.2),
            new SnesInstrumentPreset("pluck", "減衰系", "短い倍音豊かな弦のアタック", 16000, 60, false, new SnesAdsrRegisters(15, 0, 7, 0), 0.2),
            new SnesInstrumentPreset("bell", "減衰系", "非整数倍音 1 : 2.76 : 5.4 のベル", 32000, 60, false, new SnesAdsrRegisters(15, 0, 7, 0), 0.35),
            new SnesInstrumentPreset("kick", "ドラム", "150→50 Hz のピッチ下降とクリック", 9600, 60, false, new SnesAdsrRegisters(15, 0, 7, 0), 0),
            new SnesInstrumentPreset("snare", "ドラム", "200 Hz の胴鳴りと 150 ms のノイズ", 4800, 60, false, new SnesAdsrRegisters(15, 0, 7, 0), 0.1),
            new SnesInstrumentPreset("hat", "ドラム", "40 ms の高域ノイズ（closed）", 1280, 60, false, new SnesAdsrRegisters(15, 0, 7, 0), 0.05),
            new SnesInstrumentPreset("openhat", "ドラム", "200 ms の高域ノイズ（open）", 6400, 60, false, new SnesAdsrRegisters(15, 0, 7, 0), 0.1),
            new SnesInstrumentPreset("tom", "ドラム", "中域のピッチ下降、200 ms", 6400, 60, false, new SnesAdsrRegisters(15, 0, 7, 0), 0.15),
            new SnesInstrumentPreset("crash", "ドラム", "800 ms の高域ノイズ", 25600, 60, false, new SnesAdsrRegisters(15, 0, 7, 0), 0.3),
        });

        /// <summary>表示順を固定した全プリセット。</summary>
        public static IReadOnlyList<SnesInstrumentPreset> All => Presets;

        /// <summary>小文字の正式名だけを受け付ける。</summary>
        public static bool TryGet(string name, out SnesInstrumentPreset? preset)
        {
            foreach (SnesInstrumentPreset candidate in Presets)
            {
                if (candidate.Name == name)
                {
                    preset = candidate;
                    return true;
                }
            }
            preset = null;
            return false;
        }

        /// <summary>未知のプリセット名を拒否して推奨値を返す。</summary>
        public static SnesInstrumentPreset Get(string name)
        {
            if (!TryGet(name, out SnesInstrumentPreset? preset))
            {
                throw new ArgumentException($"未知の SNES プリセットです: {name}", nameof(name));
            }
            return preset!;
        }
    }
}
