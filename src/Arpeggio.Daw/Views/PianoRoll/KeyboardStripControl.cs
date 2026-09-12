using System.Globalization;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Arpeggio.Daw.Presenters.PianoRoll;

namespace Arpeggio.Daw.Views.PianoRoll
{
    /// <summary>ピアノロールと縦位置が一致する鍵盤とオクターブ表示。</summary>
    public sealed class KeyboardStripControl : Control
    {
        private const int SemitonesPerOctave = 12;
        private const double LabelSize = 11;
        private readonly Typeface labelTypeface = new Typeface(ThemeResources.NumericFont, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
        private const double LabelInset = 4;
        private readonly IBrush labelBrush = ThemeResources.GetBrush("Arpeggio.Background");
        private readonly IBrush whiteKey = ThemeResources.GetBrush("Arpeggio.TextPrimary");
        private readonly IBrush blackKey = ThemeResources.GetBrush("Arpeggio.PanelRaised");
        private readonly Pen edge = new Pen(ThemeResources.GetBrush("Arpeggio.Border"));
        private readonly FormattedText?[] labels = new FormattedText?[PianoRollPresenter.MaximumMidiNote + 1];
        private double verticalOffset;
        /// <summary>描画ループで文字列を生成しないよう音名を用意する。</summary>
        public KeyboardStripControl()
        {
            ClipToBounds = true;
            for (int pitch = 0; pitch <= PianoRollPresenter.MaximumMidiNote; pitch += SemitonesPerOctave)
            {
                labels[pitch] = new FormattedText(NoteName.Format(pitch), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeface, LabelSize, labelBrush);
            }
        }
        /// <summary>スクロールに鍵盤表示を同期させる。</summary>
        public void SetOffset(double offset) { verticalOffset = offset; InvalidateVisual(); }
        /// <summary>可視範囲の鍵盤と C 音のラベルを描く。</summary>
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            for (int pitch = 0; pitch <= PianoRollPresenter.MaximumMidiNote; pitch++)
            {
                double top = (PianoRollPresenter.MaximumMidiNote - pitch) * PianoRollControl.NoteHeight - verticalOffset;
                if (top + PianoRollControl.NoteHeight < 0 || top > Bounds.Height) { continue; }
                context.DrawRectangle(IsBlackKey(pitch) ? blackKey : whiteKey, edge,
                    new Rect(0, top, Bounds.Width, PianoRollControl.NoteHeight));
                FormattedText? label = labels[pitch];
                if (label != null) { context.DrawText(label, new Point(LabelInset, top + (PianoRollControl.NoteHeight - label.Height) / 2)); }
            }
        }
        /// <summary>半音位置が黒鍵に属するか。</summary>
        public static bool IsBlackKey(int pitch) => pitch % SemitonesPerOctave is 1 or 3 or 6 or 8 or 10;
    }
}
