using System;
using System.Text;

namespace Arpeggio.Core.Document
{
    /// <summary>CLI と MCP が共用するチップ制約と入力単位のリファレンス。</summary>
    public static class ChipReference
    {
        /// <summary>CLI と MCP のチップ名を大文字小文字を区別せず解釈する。</summary>
        public static ChipKind ParseChip(string text)
        {
            return text.Trim().ToLowerInvariant() switch
            {
                "nes" => ChipKind.Nes,
                "gameboy" => ChipKind.GameBoy,
                "snes" => ChipKind.Snes,
                _ => throw new ArgumentException("chip は nes・gameboy・snes を指定してください。", nameof(text))
            };
        }

        /// <summary>設計書のチップ仕様と M1-A の境界・単位に対応する説明を返す。</summary>
        public static string Get(ChipKind chip)
        {
            StringBuilder reference = new StringBuilder();
            switch (chip)
            {
                case ChipKind.Nes:
                    AppendNes(reference);
                    break;
                case ChipKind.GameBoy:
                    AppendGameBoy(reference);
                    break;
                case ChipKind.Snes:
                    AppendSnes(reference);
                    break;
                default:
                    throw new ArgumentException("chip は Nes・GameBoy・Snes を指定してください。", nameof(chip));
            }
            AppendCommon(reference);
            return reference.ToString();
        }

        private static void AppendNes(StringBuilder reference)
        {
            reference.AppendLine("NES / 2A03");
            reference.AppendLine("トラック（0 始まり、固定順）: 0 Pulse 1、1 Pulse 2、2 Triangle 1、3 Noise 1、4 Dpcm 1。");
            reference.AppendLine("音域: Pulse は約 54.6 Hz〜12.4 kHz、Triangle は約 27.3 Hz〜6.2 kHz。周期レジスタは 11 bit。");
            reference.AppendLine("Pulse 音量は 0〜15。Triangle は 32 段階の階段波で音量 15 固定、他の音量指定は無視して警告する。");
            reference.AppendLine("Noise は 15 bit LFSR。MIDI 番号は音階でなく周期選択: マクロ・効果適用後の音程を 0〜127 に丸め、下位 4 bit で 16 周期表を選ぶ。");
            reference.AppendLine("Dpcm は M1 ではノートを無視して無音。サンプル入力は未対応。");
            reference.AppendLine("音色 kind と JSON パラメータ:");
            reference.AppendLine("  NesPulse (Pulse): duty、volumeMacro、arpeggioMacro、pitchMacro、dutyMacro。");
            reference.AppendLine("  NesTriangle (Triangle): arpeggioMacro、pitchMacro。");
            reference.AppendLine("  NesNoise (Noise): noiseMode=Long|Short、volumeMacro、pitchMacro。");
            reference.AppendLine("  NesDpcm (Dpcm): 共通 id・name のみ。");
            AppendDuty(reference);
            reference.AppendLine("非線形 NES ミキサーを使用する。実機向けファイル出力は M1 対象外。");
        }

        private static void AppendGameBoy(StringBuilder reference)
        {
            reference.AppendLine("Game Boy / DMG");
            reference.AppendLine("トラック（0 始まり、固定順）: 0 Pulse 1、1 Pulse 2、2 Wave 1、3 Noise 1。");
            reference.AppendLine("音域: Pulse の周期制約は 64〜131072 Hz、Wave は 32〜65536 Hz。入力 MIDI は 0〜127 で、下限外をクランプする。");
            reference.AppendLine("Pulse / Noise 音量は 0〜15。Wave は outputLevel=0|25|50|100 (%) にノート音量を乗算する。");
            reference.AppendLine("Noise の MIDI 番号は音階でなく周期選択。マクロ・効果適用後の音程を 0〜127 に丸め、下位 3 bit + 1 が分周比、(127 - 選択値) / 8 が整数シフト数。");
            reference.AppendLine("音色 kind と JSON パラメータ:");
            reference.AppendLine("  GbPulse (Pulse): duty、initialVolume=0〜15、envelopeIncreasing=true|false、envelopeStepFrames=0 以上、volumeMacro、arpeggioMacro、pitchMacro。");
            reference.AppendLine("    envelopeStepFrames は 60 Hz のフレーム数で音量を 1 段階変える間隔。0 はハードウェアエンベロープ無効。");
            reference.AppendLine("  GbWave (Wave): waveform=[0〜15 の整数を 32 要素]、outputLevel=0|25|50|100、arpeggioMacro、pitchMacro。");
            reference.AppendLine("  GbNoise (Noise): lfsrWidth=7|15、volumeMacro、pitchMacro。");
            AppendDuty(reference);
            reference.AppendLine("M1 はモノラル合成へトラック単位のステレオパンを適用する。");
        }

        private static void AppendSnes(StringBuilder reference)
        {
            reference.AppendLine("SNES / SPC700 風");
            reference.AppendLine("トラック（0 始まり、固定順）: 0〜7 は Sample 1〜8。");
            reference.AppendLine("音域: MIDI 0〜127 を入力でき、MIDI 60 (C4) の周波数を基準とする倍率を 1/16384〜65535/16384 に量子化する。");
            reference.AppendLine("上限は C6 よりわずかに低い約 1046.5 Hz（4 倍未満）。基本音が上限外ならクランプして警告する。");
            reference.AppendLine("ノート音量 0〜15 を内部の 0〜127 に線形対応させ、ADSR と左右定位を適用する。");
            reference.AppendLine("音色 kind と JSON パラメータ:");
            reference.AppendLine("  SnesSample (Sample): waveform=Sine|Square|Saw|Triangle|Pulse|Noise、loop=true|false、envelope、echoSend=0〜1、pan=-1〜1、arpeggioMacro、pitchMacro。");
            reference.AppendLine("  envelope={attackSeconds,decaySeconds,sustainLevel,releaseSeconds}。attack / decay / release は有限の非負秒数、sustain は 0〜1。");
            reference.AppendLine("  waveform は一周期の内蔵サンプル。Square は 50 %、Pulse は 25 %。loop=false は一周期で終了する。");
            reference.AppendLine("ソングの snesEcho={delayMilliseconds,feedback,volume}: delayMilliseconds=0〜240 ms の 16 ms 刻み（0 は無効）、-1 < feedback < 1、volume=0〜1。");
            reference.AppendLine("echoSend は音色ごとの送り量。外部 WAV 取り込み・BRR 圧縮・ガウス補間は M1 対象外で、サンプル補間は線形。");
        }

        private static void AppendDuty(StringBuilder reference)
        {
            reference.AppendLine("duty: Percent12_5=1 (12.5 %)、Percent25=2 (25 %)、Percent50=3 (50 %)、Percent75=4 (75 %)。CLI --duty は百分率を指定する。");
        }

        private static void AppendCommon(StringBuilder reference)
        {
            reference.AppendLine();
            reference.AppendLine("共通の時間・ノート・音色:");
            reference.AppendLine("  tempoBpm は正の整数。四分音符=48 tick、十六分音符=12 tick。lengthTicks は正、0 <= loopStartTick < lengthTicks。");
            reference.AppendLine("  ノート名は C5 / C#5 / Db5 または MIDI 0〜127。MIDI 60=C4。チップ音域外はレンダリング時に最寄りの可能音へクランプし警告する。");
            reference.AppendLine("  tick >= 0、durationTicks > 0、tick + durationTicks <= lengthTicks。同一トラックの [tick,tick+durationTicks) は重複不可、隣接可。");
            reference.AppendLine("  volume=0〜15、instrumentId は対象チャンネルに対応する音色の正の id。音色は共通 id・name・kind を持つ。enum は JSON で文字列（整数も可）、None は指定不可。");
            reference.AppendLine("  Track.pan と SNES 音色 pan は -1〜1（左〜右、既定 0）。muted は true|false。");
            reference.AppendLine("  loops は最初の全曲再生を含む回数。2 回目以降は [loopStartTick,lengthTicks) を再生し、境界をまたぐノートは再発音する。");
            reference.AppendLine("マクロ（各 kind が列挙するものだけ使用可）:");
            reference.AppendLine("  JSON は {values:[整数,...],loopIndex:-1}。loopIndex は 0 始まり、-1 はループなし。null または空列は未指定。空列の loopIndex は -1。");
            reference.AppendLine("  CLI は \"15,14,12/2\" 形式で / 以降が loopIndex、省略時 -1。先頭値を発音開始時に適用し、以後 1/60 秒ごとに進める。");
            reference.AppendLine("  volumeMacro: 0〜15 をノート音量に乗算。arpeggioMacro: 基本音からの半音オフセット。pitchMacro: 基本音からのセント（100 セント=1 半音）。");
            reference.AppendLine("  dutyMacro: NES Pulse のみ。値は duty の enum 整数 1〜4（百分率ではない）。アルペジオ・ピッチ値は符号付き整数。");
            reference.AppendLine("ノート単位の effects=[{kind,value},...]（同じ kind の重複不可、value は整数）:");
            reference.AppendLine("  PitchSlide: ノート期間で value 半音スライド（正で上、負で下）。");
            reference.AppendLine("  Vibrato: value >= 0 の深さ（セント）、速度は 6 Hz 固定。");
            reference.AppendLine("  VolumeSlide: ノート期間で音量を value 段階変化、-15〜15。");
            reference.AppendLine("  Arpeggio: 0〜255。2 桁の 16 進 0xy が半音オフセット +x,+y（0x47=71 は +4,+7）。JSON では整数 71 を指定する。");
            reference.AppendLine("  Delay: value tick 遅延、0 <= value < durationTicks。終了は元のノート終端。");
            reference.AppendLine("  ピッチ・音量効果は 60 Hz 更新。PitchSlide / VolumeSlide は Delay 後の発音期間を基準にする。短いノートは効果更新前に終了しうる。");
        }
    }
}
