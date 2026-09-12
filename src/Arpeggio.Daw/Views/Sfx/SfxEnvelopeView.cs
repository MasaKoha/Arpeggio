using System;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Curves;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Arpeggio.Daw.Views.Sfx
{
    /// <summary>要求時間と60Hz量子化後の静的包絡線を並べて表示する。</summary>
    internal sealed class SfxEnvelopeView : UserControl
    {
        private readonly double graphWidth;
        private readonly double graphHeight;
        private const double PeakVolume = 15;
        private readonly TextBlock duration = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly Polyline curve = new Polyline();

        internal SfxEnvelopeView()
        {
            graphWidth = (double)Application.Current!.FindResource("Arpeggio.Sfx.EnvelopeWidth")!;
            graphHeight = (double)Application.Current!.FindResource("Arpeggio.Sfx.EnvelopeHeight")!;
            curve.Stroke = (IBrush)Application.Current!.FindResource("Arpeggio.Accent")!;
            curve.StrokeThickness = (double)Application.Current!.FindResource("Arpeggio.Sfx.EnvelopeStroke")!;
            var canvas = new Canvas { Width = graphWidth, Height = graphHeight };
            canvas.Children.Add(curve);
            var panel = new StackPanel();
            panel.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
            panel.Children.Add(duration);
            Content = panel;
        }

        internal void Show(SfxEnvelopeCurve? envelope)
        {
            IsVisible = envelope is not null;
            if (envelope is null) { return; }
            var points = new Points();
            for (int frame = 0; frame < envelope.VolumeMacro.Values.Length; frame++)
            {
                points.Add(new Point(graphWidth * frame / envelope.EnvelopeFrames,
                    graphHeight * (1 - envelope.VolumeMacro.Values[frame] / PeakVolume)));
            }
            curve.Points = points;
            duration.Text = FormattableString.Invariant($"要求 {envelope.RequestedEnvelopeSeconds:0.######} 秒 → 実効 {envelope.EnvelopeDurationSeconds:0.######} 秒（{envelope.EnvelopeFrames} フレーム）。終端にゼロ音量を1フレーム保持。");
        }
    }
}
