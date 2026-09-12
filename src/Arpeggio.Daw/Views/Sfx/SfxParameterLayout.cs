using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Daw.Views.Sfx
{
    /// <summary>単位・範囲を再定義せず、設計書の表示順と日本語名だけを持つ。</summary>
    internal static class SfxParameterLayout
    {
        internal static string Group(string path)
        {
            if (path.EndsWith(".enabled") || path == "snes.waveform") { return "音の構成"; }
            if (path.StartsWith("tone.envelope.")) { return "トーン音量"; }
            if (path.Contains("pitchChange") || path.Contains("repeatPeriod")) { return "音程変化・反復"; }
            if (path.StartsWith("tone.")) { return "トーン音程"; }
            if (path.Contains("duty")) { return "チップ固有音色"; }
            return "ノイズ";
        }

        internal static int Order(string path) => path.EndsWith(".decaySeconds") ? 2 : path.EndsWith(".punch") ? 1 : 0;

        internal static string Label(SfxParameterDescription description)
        {
            string path = description.Path;
            string name = path[(path.LastIndexOf('.') + 1)..] switch
            {
                "enabled" => path.StartsWith("tone.") ? "トーン" : "ノイズ",
                "baseFrequencyHz" => "基準周波数",
                "slideSemitonesPerSecond" => "スライド",
                "deltaSlideSemitonesPerSecondSquared" => "スライド加速度",
                "vibratoDepthCents" => "ビブラート深さ",
                "vibratoSpeedHz" => "ビブラート速度",
                "volume" => "ピーク音量",
                "attackSeconds" => "立ち上がり",
                "sustainSeconds" => "保持時間",
                "decaySeconds" => "減衰時間",
                "punch" => "保持冒頭の強調",
                "pitchChangeSemitones" => "音程ジャンプ",
                "pitchChangeTimeSeconds" => "ジャンプ待ち時間",
                "repeatPeriodSeconds" => "反復周期（0 は無効）",
                "dutyPercent" => "デューティ比",
                "dutySweepPercentPerSecond" => "デューティ変化",
                "noiseMode" => "ノイズモード",
                "noisePeriodIndex" => "ノイズ周期",
                "noiseSlideIndicesPerSecond" or "noiseSlideSelectionsPerSecond" => "ノイズ変化",
                "noiseWidth" => "ノイズ幅",
                "noiseSelection" => "ノイズ選択値",
                "waveform" => "トーン波形",
                "noiseRate" => "DSP ノイズ速度",
                _ => path
            };
            return description.Unit.Length == 0 ? name : name + "（" + description.Unit + "）";
        }
    }
}
