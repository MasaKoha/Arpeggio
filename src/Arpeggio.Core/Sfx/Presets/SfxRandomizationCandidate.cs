using System;
using System.Collections.Generic;
using System.Globalization;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Presets
{
    /// <summary>抽選済みの値を範囲・ロック・レイヤー条件に従って候補へ適用する。</summary>
    internal sealed class SfxRandomizationCandidate
    {
        private readonly ChipKind _chip;
        private readonly HashSet<string> _locks;
        private readonly Dictionary<string, double> _corrections = new Dictionary<string, double>(StringComparer.Ordinal);

        internal SfxRandomizationCandidate(SfxParameters parameters, ChipKind chip, IReadOnlyList<string> locks)
        {
            Parameters = parameters;
            _chip = chip;
            _locks = new HashSet<string>(locks, StringComparer.Ordinal);
        }

        internal SfxParameters Parameters { get; private set; }

        internal double Read(string? path)
        {
            return path == null ? 0 : Convert.ToDouble(SfxParameterCatalog.Get(path, _chip).Read(Parameters),
                CultureInfo.InvariantCulture);
        }

        internal void Set(string? path, double requested)
        {
            if (path == null || _locks.Contains(path) || !IsLayerEnabled(path))
            {
                return;
            }
            SfxParameterDescription description = SfxParameterCatalog.Get(path, _chip);
            double clamped = Math.Clamp(requested, description.Minimum!.Value, description.Maximum!.Value);
            object value;
            if (description.Choices.Count > 0)
            {
                value = NearestChoice(description, clamped);
            }
            else if (description.ValueKind == SfxParameterValueKind.Integer)
            {
                value = (int)Math.Round(clamped, MidpointRounding.AwayFromZero);
            }
            else
            {
                value = clamped;
            }
            Parameters = description.Replace(Parameters, SfxParameterValidator.NormalizeValue(description, value));
        }

        internal void RepairEnvelopes()
        {
            RepairEnvelope("tone.envelope");
            RepairEnvelope("noise.envelope");
            Parameters = SfxParameterValidator.Normalize(Parameters, _chip);
        }

        internal IReadOnlyList<SfxParameterChange> Compare(SfxParameters original)
        {
            var changes = new List<SfxParameterChange>();
            foreach (SfxParameterDescription description in SfxParameterCatalog.GetAll(_chip))
            {
                object previous = description.Read(original);
                object value = description.Read(Parameters);
                bool wasCorrected = _corrections.TryGetValue(description.Path, out double correctedFrom);
                if (!Equals(previous, value) || wasCorrected)
                {
                    changes.Add(new SfxParameterChange
                    {
                        ParameterPath = description.Path, PreviousValue = previous, Value = value,
                        CorrectedFrom = wasCorrected ? correctedFrom : null
                    });
                }
            }
            return changes.AsReadOnly();
        }

        private bool IsLayerEnabled(string path)
        {
            SfxParameterDescription description = SfxParameterCatalog.Get(path, _chip);
            bool belongsToTone = path.StartsWith("tone.", StringComparison.Ordinal)
                || (description.Chip != ChipKind.None && path.Contains(".duty", StringComparison.Ordinal));
            return belongsToTone ? Parameters.Tone.Enabled : Parameters.Noise.Enabled;
        }

        private static object NearestChoice(SfxParameterDescription description, double value)
        {
            object nearest = description.Choices[0];
            double nearestDistance = double.PositiveInfinity;
            foreach (object choice in description.Choices)
            {
                double distance = Math.Abs(Convert.ToDouble(choice, CultureInfo.InvariantCulture) - value);
                // 選択肢は昇順。等距離で置換しないことで小さい比率を優先する。
                if (distance < nearestDistance)
                {
                    nearest = choice;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private void RepairEnvelope(string envelopePath)
        {
            string sustainPath = envelopePath + ".sustainSeconds";
            string punchPath = envelopePath + ".punch";
            double sustainFrames = Math.Round(Read(sustainPath) * SfxParameterValidator.ControlFramesPerSecond,
                MidpointRounding.AwayFromZero);
            if (sustainFrames != 0 || Read(punchPath) == 0)
            {
                return;
            }
            string correctionPath = _locks.Contains(punchPath) ? sustainPath : punchPath;
            double corrected = correctionPath == sustainPath ? SfxParameterValidator.ControlFrameSeconds : 0;
            _corrections.Add(correctionPath, Read(correctionPath));
            Set(correctionPath, corrected);
        }
    }
}
