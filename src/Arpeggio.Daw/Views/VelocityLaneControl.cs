using System;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;
using Arpeggio.Daw.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Arpeggio.Daw.Views
{
    /// <summary>音量の棒をカスタム描画し、入力座標だけを Presenter へ通知する。</summary>
    public sealed class VelocityLaneControl : Control
    {
        private readonly double barWidth = ThemeResources.GetDouble("Arpeggio.Space.Small");
        private readonly double hitTolerance = ThemeResources.GetDouble("Arpeggio.VelocityLane.HitTolerance");
        private readonly double laneInset = ThemeResources.GetDouble("Arpeggio.Space.Small");
        private readonly double minimumBarHeight = ThemeResources.GetDouble("Arpeggio.VelocityLane.MinimumBarHeight");
        private readonly IBrush background = ThemeResources.GetBrush("Arpeggio.Panel");
        private readonly IBrush accent = ThemeResources.GetBrush("Arpeggio.Accent");
        private readonly Pen border = new Pen(ThemeResources.GetBrush("Arpeggio.Border"));
        private IBrush channel = ThemeResources.GetBrush("Arpeggio.TextSecondary");
        private PianoRollPresenter? pianoRoll;
        private Action<Action>? execute;
        private double offset;
        private double pixelsPerTick = 1;
        /// <summary>入力の判断先を明示的に接続する。</summary>
        public void Bind(PianoRollPresenter roll, Action<Action> executeAction)
        {
            pianoRoll = roll;
            execute = executeAction;
            Focusable = true;
            ClipToBounds = true;
        }
        /// <summary>チャンネル色と編集済みノートを表示へ反映する。</summary>
        public void Refresh()
        {
            if (pianoRoll == null) { return; }
            Track track = pianoRoll.Song.Tracks[pianoRoll.SelectedTrack];
            channel = ChannelPalette.GetBrush(track.Channel, track.ChannelIndex);
            InvalidateVisual();
        }
        /// <summary>ピアノロールと水平スクロール・ズームを同期する。</summary>
        public void SetViewport(double horizontalOffset, double scale)
        {
            offset = horizontalOffset;
            pixelsPerTick = scale;
            InvalidateVisual();
        }
        /// <summary>保持済みブラシで可視範囲の棒だけを描画する。</summary>
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.DrawRectangle(background, null, new Rect(Bounds.Size));
            context.DrawLine(border, new Point(0, 0), new Point(Bounds.Width, 0));
            if (pianoRoll == null) { return; }
            double baseline = Bounds.Height - laneInset;
            double availableHeight = Math.Max(1, Bounds.Height - laneInset * 2);
            // perf: ノートごとの表示オブジェクトを作らず、値型の矩形だけを組み立てる。
            foreach (Note note in pianoRoll.Song.Tracks[pianoRoll.SelectedTrack].Notes)
            {
                double left = note.Tick * pixelsPerTick - offset;
                if (left + barWidth < 0 || left > Bounds.Width) { continue; }
                double height = Math.Max(minimumBarHeight, availableHeight * note.Volume / VelocityLanePresenter.MaximumVolume);
                IBrush brush = pianoRoll.Selection.Contains(note.Tick) ? accent : channel;
                context.DrawRectangle(brush, null, new Rect(left, baseline - height, barWidth, height));
            }
        }
        /// <summary>棒の位置と音量座標を通知する。</summary>
        protected override void OnPointerPressed(PointerPressedEventArgs arguments)
        {
            base.OnPointerPressed(arguments);
            if (pianoRoll == null || execute == null || !arguments.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { return; }
            Focus();
            Point position = arguments.GetPosition(this);
            execute(() => pianoRoll.PressVelocity((position.X + offset) / pixelsPerTick, GetVolume(position.Y), hitTolerance / pixelsPerTick));
            if (pianoRoll.Velocity.IsDragging) { arguments.Pointer.Capture(this); }
            arguments.Handled = true;
        }
        /// <summary>キャプチャ中の上下移動を通知する。</summary>
        protected override void OnPointerMoved(PointerEventArgs arguments)
        {
            base.OnPointerMoved(arguments);
            if (pianoRoll == null || execute == null || !pianoRoll.Velocity.IsDragging) { return; }
            execute(() => pianoRoll.Velocity.Drag(GetVolume(arguments.GetPosition(this).Y)));
            arguments.Handled = true;
        }
        /// <summary>解放時に一履歴へ確定する。</summary>
        protected override void OnPointerReleased(PointerReleasedEventArgs arguments)
        {
            base.OnPointerReleased(arguments);
            if (pianoRoll != null && execute != null) { execute(pianoRoll.EndDrag); }
            arguments.Pointer.Capture(null);
        }
        /// <summary>キャプチャ喪失時にも途中履歴をまとめる。</summary>
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs arguments)
        {
            base.OnPointerCaptureLost(arguments);
            if (pianoRoll != null && execute != null) { execute(pianoRoll.EndDrag); }
        }
        private double GetVolume(double verticalPosition) => (Bounds.Height - laneInset - verticalPosition) /
            Math.Max(1, Bounds.Height - laneInset * 2) * VelocityLanePresenter.MaximumVolume;
    }
}
