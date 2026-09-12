using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Synthesis;

namespace Arpeggio.Core.Sfx.Curves
{
    /// <summary>保存する整数マクロ全域を、再生と同じ PitchTable で診断する。</summary>
    internal static class SfxPitchDiagnostics
    {
        private const double CentsPerSemitone = 100;
        private const double MidiRoundTripTolerance = 1e-9;

        internal static IReadOnlyList<SfxPitchFrame> Generate(SfxToneCurve? tone, ChipKind chip,
            List<SfxGenerationWarning> warnings)
        {
            if (tone is null)
            {
                return Array.Empty<SfxPitchFrame>();
            }
            ChannelKind channel = chip == ChipKind.Snes ? ChannelKind.Sample : ChannelKind.Pulse;
            var frames = new SfxPitchFrame[tone.Envelope.BodyFrames];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                // VoiceModulation と同じ加算順にして、丸め境界の診断と再生を一致させる。
                double requestedMidiNote = (double)tone.AnchorMidiNote + tone.ArpeggioMacro.Values[frame]
                    + tone.PitchMacro.Values[frame] / CentsPerSemitone;
                double requestedFrequency = PitchTable.GetFrequency(requestedMidiNote);
                double clampedMidiNote = PitchTable.ClampMidiNote(chip, channel, requestedMidiNote);
                var result = new SfxPitchFrame
                {
                    Frame = frame, RequestedMidiNote = requestedMidiNote,
                    RequestedFrequencyHz = double.IsFinite(requestedFrequency) && requestedFrequency > 0 ? requestedFrequency : null,
                    ActualFrequencyHz = PitchTable.Quantize(chip, channel, requestedMidiNote),
                    IsClamped = Math.Abs(requestedMidiNote - clampedMidiNote) > MidiRoundTripTolerance
                };
                frames[frame] = result;
                if (result.IsClamped)
                {
                    SfxFrameWarningCollector.Add(warnings, new SfxGenerationWarning
                    {
                        Code = "PitchClamped", ParameterPath = "tone.baseFrequencyHz", Layer = "tone",
                        FromFrame = frame, ToFrame = frame,
                        Requested = result.RequestedFrequencyHz, Actual = result.ActualFrequencyHz,
                        Message = "曲線適用後の音程をチップの音域へ制限しました。Hzは範囲先頭の代表値です。"
                    });
                }
            }
            return Array.AsReadOnly(frames);
        }
    }
}
