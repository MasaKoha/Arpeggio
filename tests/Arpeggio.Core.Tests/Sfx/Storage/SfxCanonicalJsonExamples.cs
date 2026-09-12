namespace Arpeggio.Core.Tests.Sfx.Storage
{
    /// <summary>既存 NES 保存仕様から独立に固定した JSON。実装のシリアライザーで生成しない。</summary>
    internal static class SfxCanonicalJsonExamples
    {
        internal const string LegacyNes = """
        {
          "version": 1,
          "title": "",
          "chip": "Nes",
          "tempoBpm": 150,
          "ticksPerBeat": 48,
          "lengthTicks": 768,
          "loopStartTick": 0,
          "instruments": [
            {
              "id": 1,
              "name": "lead",
              "kind": "NesPulse",
              "duty": "Percent50",
              "volumeMacro": null,
              "arpeggioMacro": null,
              "pitchMacro": null,
              "dutyMacro": null
            }
          ],
          "tracks": [
            {
              "channel": "Pulse",
              "channelIndex": 0,
              "name": "Pulse 1",
              "muted": false,
              "pan": 0,
              "notes": []
            },
            {
              "channel": "Pulse",
              "channelIndex": 1,
              "name": "Pulse 2",
              "muted": false,
              "pan": 0,
              "notes": []
            },
            {
              "channel": "Triangle",
              "channelIndex": 0,
              "name": "Triangle 1",
              "muted": false,
              "pan": 0,
              "notes": []
            },
            {
              "channel": "Noise",
              "channelIndex": 0,
              "name": "Noise 1",
              "muted": false,
              "pan": 0,
              "notes": []
            },
            {
              "channel": "Dpcm",
              "channelIndex": 0,
              "name": "Dpcm 1",
              "muted": false,
              "pan": 0,
              "notes": []
            }
          ],
          "snesEcho": {
            "delayMilliseconds": 0,
            "feedback": 0,
            "volume": 0,
            "firCoefficients": [
              127,
              0,
              0,
              0,
              0,
              0,
              0,
              0
            ]
          }
        }
        """;
    }
}
