using System;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Arpeggio.Daw.Views
{
    /// <summary>ノートごとのコントロールを作らず、ピアノロール全体を描画する。</summary>
    public sealed class PianoRollControl : Control
    {
        /// <summary>半音一段の高さ。</summary>
        public const double NoteHeight = 18;
        private const double InitialPixelsPerTick = 3;
        private const double MinimumPixelsPerTick = 0.5;
        private const double MaximumPixelsPerTick = 16;
        private const double ZoomFactor = 1.2;
        private const double ResizeHandleWidth = 7;
        private const double NoteInset = 1;
        private const int BeatsPerBar = 4;
        private readonly IBrush background = new SolidColorBrush(Color.Parse("#151B24"));
        private readonly IBrush blackKeyBackground = new SolidColorBrush(Color.Parse("#11161E"));
        private readonly IBrush noteBrush = new SolidColorBrush(Color.Parse("#69C7AD"));
        private readonly IBrush selectedBrush = new SolidColorBrush(Color.Parse("#FFE09A"));
        private readonly IBrush ghostBrush = new SolidColorBrush(Color.Parse("#364A57"));
        private readonly Pen gridPen = new Pen(new SolidColorBrush(Color.Parse("#242E3A")));
        private readonly Pen beatPen = new Pen(new SolidColorBrush(Color.Parse("#394657")));
        private readonly Pen barPen = new Pen(new SolidColorBrush(Color.Parse("#647488")));
        private readonly Pen cursorPen = new Pen(new SolidColorBrush(Color.Parse("#FF8594")), 2);
        private PianoRollPresenter? presenter;
        private Action<Action>? execute;
        private Song? song;
        private int selectedTrack;
        private int? selectedTick;
        private double positionTick;
        private Rect viewport;

        /// <summary>横方向の tick 拡大率。</summary>
        public double PixelsPerTick { get; private set; } = InitialPixelsPerTick;
        /// <summary>ズーム前後でポインター直下の tick を維持するための通知。</summary>
        public event Action<double, double>? ZoomChanged;
        /// <summary>入力の判断先を明示的に接続する。</summary>
        public void Bind(PianoRollPresenter pianoRoll, Action<Action> executeAction)
        {
            presenter = pianoRoll;
            execute = executeAction;
            Focusable = true;
            ClipToBounds = true;
        }
        /// <summary>編集データと選択だけを受け取る。</summary>
        public void ShowSong(Song current, int trackIndex, int? tick)
        {
            song = current;
            selectedTrack = trackIndex;
            selectedTick = tick;
            Width = current.LengthTicks * PixelsPerTick;
            Height = (PianoRollPresenter.MaximumMidiNote + 1) * NoteHeight;
            InvalidateVisual();
        }
        /// <summary>可視領域外のノート描画を省く。</summary>
        public void SetViewport(Rect visible) { viewport = visible; InvalidateVisual(); }
        /// <summary>合成済みフレーム位置を表示へ反映する。</summary>
        public void SetPosition(double tick) { positionTick = tick; InvalidateVisual(); }
        /// <summary>保持したブラシと Pen でグリッド・ゴースト・選択ノートを描く。</summary>
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (song == null) { return; }
            // perf: Rect と Point は値型。描画中にノート用オブジェクトを生成しない。
            Rect visible = viewport.Width > 0 ? viewport : new Rect(Bounds.Size);
            context.DrawRectangle(background, null, visible);
            DrawPitchRows(context, visible);
            DrawTickLines(context, visible);
            for (int trackIndex = 0; trackIndex < song.Tracks.Count; trackIndex++)
            {
                if (trackIndex != selectedTrack) { DrawNotes(context, song.Tracks[trackIndex], ghostBrush, visible, false); }
            }
            DrawNotes(context, song.Tracks[selectedTrack], noteBrush, visible, true);
            double cursorPosition = positionTick * PixelsPerTick;
            context.DrawLine(cursorPen, new Point(cursorPosition, visible.Top), new Point(cursorPosition, visible.Bottom));
        }
        /// <summary>ピクセル座標を tick と MIDI 音高に変換して操作を開始する。</summary>
        protected override void OnPointerPressed(PointerPressedEventArgs arguments)
        {
            base.OnPointerPressed(arguments);
            if (presenter == null || execute == null) { return; }
            Focus();
            Point position = arguments.GetPosition(this);
            int pitch = GetPitch(position.Y);
            bool bypassSnap = arguments.KeyModifiers.HasFlag(KeyModifiers.Alt);
            if (arguments.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                execute(() => presenter.DeleteAt(position.X / PixelsPerTick, pitch));
            }
            else if (arguments.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                execute(() => presenter.Press(position.X / PixelsPerTick, pitch, ResizeHandleWidth / PixelsPerTick, bypassSnap));
                arguments.Pointer.Capture(this);
            }
            arguments.Handled = true;
        }
        /// <summary>キャプチャ中は画面外でもドラッグを継続する。</summary>
        protected override void OnPointerMoved(PointerEventArgs arguments)
        {
            base.OnPointerMoved(arguments);
            if (presenter == null || execute == null || presenter.DragMode == PianoRollDragMode.None) { return; }
            Point position = arguments.GetPosition(this);
            execute(() => presenter.Drag(position.X / PixelsPerTick, GetPitch(position.Y), arguments.KeyModifiers.HasFlag(KeyModifiers.Alt)));
            arguments.Handled = true;
        }
        /// <summary>履歴を確定してキャプチャを解放する。</summary>
        protected override void OnPointerReleased(PointerReleasedEventArgs arguments)
        {
            base.OnPointerReleased(arguments);
            presenter?.EndDrag();
            arguments.Pointer.Capture(null);
        }
        /// <summary>OS 側でキャプチャを失っても操作を残さない。</summary>
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs arguments)
        {
            base.OnPointerCaptureLost(arguments);
            presenter?.EndDrag();
        }
        /// <summary>Ctrl ホイールで横方向の拡大率を変更する。</summary>
        protected override void OnPointerWheelChanged(PointerWheelEventArgs arguments)
        {
            if (!arguments.KeyModifiers.HasFlag(KeyModifiers.Control)) { base.OnPointerWheelChanged(arguments); return; }
            double previousScale = PixelsPerTick;
            double anchorTick = arguments.GetPosition(this).X / previousScale;
            PixelsPerTick = Math.Clamp(previousScale * Math.Pow(ZoomFactor, arguments.Delta.Y), MinimumPixelsPerTick, MaximumPixelsPerTick);
            if (song != null) { Width = song.LengthTicks * PixelsPerTick; }
            ZoomChanged?.Invoke(anchorTick, previousScale);
            InvalidateVisual();
            arguments.Handled = true;
        }
        private static int GetPitch(double verticalPosition) => Math.Clamp(PianoRollPresenter.MaximumMidiNote - (int)Math.Floor(verticalPosition / NoteHeight), 0, PianoRollPresenter.MaximumMidiNote);
        private void DrawPitchRows(DrawingContext context, Rect visible)
        {
            int firstRow = Math.Max(0, (int)(visible.Top / NoteHeight));
            int lastRow = Math.Min(PianoRollPresenter.MaximumMidiNote, (int)(visible.Bottom / NoteHeight));
            for (int row = firstRow; row <= lastRow; row++)
            {
                double top = row * NoteHeight;
                if (KeyboardStripControl.IsBlackKey(PianoRollPresenter.MaximumMidiNote - row))
                {
                    context.DrawRectangle(blackKeyBackground, null, new Rect(visible.Left, top, visible.Width, NoteHeight));
                }
                context.DrawLine(gridPen, new Point(visible.Left, top), new Point(visible.Right, top));
            }
        }
        private void DrawTickLines(DrawingContext context, Rect visible)
        {
            int firstTick = (int)(visible.Left / PixelsPerTick / PianoRollPresenter.GridTicks) * PianoRollPresenter.GridTicks;
            double lastTick = Math.Min(song!.LengthTicks, visible.Right / PixelsPerTick);
            for (long tick = firstTick; tick <= lastTick; tick += PianoRollPresenter.GridTicks)
            {
                Pen pen = gridPen;
                if (tick % (Song.FixedTicksPerBeat * BeatsPerBar) == 0) { pen = barPen; }
                else if (tick % Song.FixedTicksPerBeat == 0) { pen = beatPen; }
                double left = tick * PixelsPerTick;
                context.DrawLine(pen, new Point(left, visible.Top), new Point(left, visible.Bottom));
            }
        }
        private void DrawNotes(DrawingContext context, Track track, IBrush brush, Rect visible, bool isSelectedTrack)
        {
            foreach (Note note in track.Notes)
            {
                Rect rectangle = new Rect(note.Tick * PixelsPerTick, (PianoRollPresenter.MaximumMidiNote - note.MidiNote) * NoteHeight + NoteInset,
                    Math.Max(1, note.DurationTicks * PixelsPerTick - NoteInset), NoteHeight - NoteInset * 2);
                if (!rectangle.Intersects(visible)) { continue; }
                context.DrawRectangle(isSelectedTrack && note.Tick == selectedTick ? selectedBrush : brush, null, rectangle);
            }
        }
    }
}
