using System;
using System.Collections.Generic;
using System.Globalization;
using Arpeggio.Core.Document;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Arpeggio.Daw.Views
{
    /// <summary>横スクロールに同期する 4/4 拍子のルーラー。</summary>
    public sealed class TimeRulerControl : Control
    {
        private const int BeatsPerBar = 4;
        private const double LabelSize = 11;
        private readonly Typeface labelTypeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
        private const double LabelInset = 4;
        private readonly IBrush background = new SolidColorBrush(Color.Parse("#202B38"));
        private readonly Pen line = new Pen(Brushes.SlateGray);
        private readonly Dictionary<int, FormattedText> labels = new Dictionary<int, FormattedText>();
        private double horizontalOffset;
        private double pixelsPerTick;
        private int firstBeat;
        private int lastBeat;
        /// <summary>文字キャッシュは描画前に可視範囲だけ準備する。</summary>
        public void SetViewport(double offset, double scale, double width, int lengthTicks)
        {
            horizontalOffset = offset;
            pixelsPerTick = scale;
            int start = Math.Max(0, (int)(offset / scale / Song.FixedTicksPerBeat));
            int end = (int)Math.Min(lengthTicks / Song.FixedTicksPerBeat, (offset + width) / scale / Song.FixedTicksPerBeat + 1);
            if (start != firstBeat || end != lastBeat || labels.Count == 0)
            {
                labels.Clear();
                for (int beat = start; beat <= end; beat++)
                {
                    labels.Add(beat, new FormattedText($"{beat / BeatsPerBar + 1}:{beat % BeatsPerBar + 1}",
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight, labelTypeface, LabelSize, Brushes.LightGray));
                }
            }
            firstBeat = start;
            lastBeat = end;
            InvalidateVisual();
        }
        /// <summary>事前生成した小節・拍ラベルを描く。</summary>
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            using (context.PushClip(new Rect(Bounds.Size)))
            {
                context.DrawRectangle(background, null, new Rect(Bounds.Size));
                foreach (KeyValuePair<int, FormattedText> label in labels)
                {
                    double left = label.Key * Song.FixedTicksPerBeat * pixelsPerTick - horizontalOffset;
                    context.DrawLine(line, new Point(left, 0), new Point(left, Bounds.Height));
                    context.DrawText(label.Value, new Point(left + LabelInset, LabelInset));
                }
            }
        }
    }
}
