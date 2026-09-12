using System;
using System.Globalization;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Arpeggio.Daw.Presenters.PianoRoll;
using Arpeggio.Daw.Presenters.PianoRoll.Selection;

namespace Arpeggio.Daw.Views.PianoRoll
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
        private const int SemitonesPerOctave = 12;
        private const int MaximumVolume = 15;
        private const double MinimumBrightness = 0.45;
        private const double GhostOpacity = 0.25;
        private const double NoteCornerRadius = 2;
        private const double LabelInset = 4;
        private const double LabelSize = 11;
        private const double EffectMarkSize = 6;
        private const double SelectionThickness = 2;
        private readonly IBrush background = ThemeResources.GetBrush("Arpeggio.Background");
        private readonly IBrush blackKeyBackground = ThemeResources.GetBrush("Arpeggio.Grid.BlackKey");
        private readonly IBrush accent = ThemeResources.GetBrush("Arpeggio.Accent");
        private readonly IBrush playhead = ThemeResources.GetBrush("Arpeggio.Playhead");
        private readonly IBrush labelBrush = ThemeResources.GetBrush("Arpeggio.Background");
        private readonly Pen noteEdge = new Pen(ThemeResources.GetBrush("Arpeggio.Background"));
        private readonly Pen selectedPen = new Pen(ThemeResources.GetBrush("Arpeggio.Accent"), SelectionThickness);
        private readonly Pen gridPen = new Pen(ThemeResources.GetBrush("Arpeggio.Grid.Sixteenth"));
        private readonly Pen beatPen = new Pen(ThemeResources.GetBrush("Arpeggio.Grid.Beat"));
        private readonly Pen barPen = new Pen(ThemeResources.GetBrush("Arpeggio.Grid.Bar"));
        private readonly Pen octavePen = new Pen(ThemeResources.GetBrush("Arpeggio.Border"));
        private readonly Pen cursorPen = new Pen(ThemeResources.GetBrush("Arpeggio.Playhead"), SelectionThickness);
        private readonly Typeface labelTypeface = new Typeface(ThemeResources.NumericFont, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
        private readonly StreamGeometry effectMark = StreamGeometry.Parse("M0 0 H6 V6 Z");
        private readonly StreamGeometry cursorMark = StreamGeometry.Parse("M-4 0 H4 L0 6 Z");
        private IBrush[][] volumeBrushes = Array.Empty<IBrush[]>();
        private IBrush[] ghostBrushes = Array.Empty<IBrush>();
        private IBrush[] channelBrushes = Array.Empty<IBrush>();
        private readonly IBrush ghostLabelBrush = ThemeResources.GetBrush("Arpeggio.TextPrimary");
        private FormattedText[] channelLabels = Array.Empty<FormattedText>();
        private FormattedText[] ghostLabels = Array.Empty<FormattedText>();
        private string[] shortLabels = Array.Empty<string>();
        private string[] trackToolTips = Array.Empty<string>();
        private int hoveredTrack = -1;
        private PianoRollPresenter? presenter;
        private Action<Action>? execute;
        private Song? song;
        private int selectedTrack;
        private NoteSelection? selection;
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
            PrepareChannelVisuals(current);
            song = current;
            selectedTrack = trackIndex;
            selection = presenter?.Selection;
            hoveredTrack = -1;
            ToolTip.SetTip(this, trackToolTips[trackIndex]);
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
                if (trackIndex != selectedTrack) { DrawNotes(context, trackIndex, visible, false); }
            }
            DrawNotes(context, selectedTrack, visible, true);
            DrawSelectionRectangle(context);
            double cursorPosition = positionTick * PixelsPerTick;
            context.DrawLine(cursorPen, new Point(cursorPosition, visible.Top), new Point(cursorPosition, visible.Bottom));
            using (context.PushTransform(Matrix.CreateTranslation(cursorPosition, visible.Top)))
            {
                context.DrawGeometry(playhead, null, cursorMark);
            }
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
                execute(() => presenter.BeginErase(position.X / PixelsPerTick, pitch));
                arguments.Pointer.Capture(this);
            }
            else if (arguments.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                NotePointerModifiers modifiers = NotePointerModifiers.None;
                if (arguments.KeyModifiers.HasFlag(KeyModifiers.Control)) { modifiers |= NotePointerModifiers.Control; }
                if (arguments.KeyModifiers.HasFlag(KeyModifiers.Shift)) { modifiers |= NotePointerModifiers.Shift; }
                execute(() => presenter.Press(position.X / PixelsPerTick, pitch, ResizeHandleWidth / PixelsPerTick, bypassSnap, modifiers));
                arguments.Pointer.Capture(this);
            }
            arguments.Handled = true;
        }
        /// <summary>キャプチャ中は画面外でもドラッグを継続する。</summary>
        protected override void OnPointerMoved(PointerEventArgs arguments)
        {
            base.OnPointerMoved(arguments);
            UpdateNoteToolTip(arguments.GetPosition(this));
            if (presenter == null || execute == null || presenter.DragMode == PianoRollDragMode.None) { return; }
            Point position = arguments.GetPosition(this);
            execute(() => presenter.Drag(position.X / PixelsPerTick, GetPitch(position.Y), arguments.KeyModifiers.HasFlag(KeyModifiers.Alt)));
            arguments.Handled = true;
        }
        /// <summary>履歴を確定してキャプチャを解放する。</summary>
        protected override void OnPointerReleased(PointerReleasedEventArgs arguments)
        {
            base.OnPointerReleased(arguments);
            if (presenter != null && execute != null) { execute(presenter.EndDrag); }
            InvalidateVisual();
            arguments.Pointer.Capture(null);
        }
        /// <summary>OS 側でキャプチャを失っても操作を残さない。</summary>
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs arguments)
        {
            base.OnPointerCaptureLost(arguments);
            if (presenter != null && execute != null) { execute(presenter.EndDrag); }
            InvalidateVisual();
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
        private void DrawSelectionRectangle(DrawingContext context)
        {
            if (presenter?.SelectionRectangle is not NoteSelectionRectangle selectionRectangle) { return; }
            const double SelectionOpacity = 0.2;
            Rect rectangle = new Rect(selectionRectangle.Left * PixelsPerTick,
                (PianoRollPresenter.MaximumMidiNote - selectionRectangle.TopPitch) * NoteHeight,
                (selectionRectangle.Right - selectionRectangle.Left) * PixelsPerTick,
                (selectionRectangle.TopPitch - selectionRectangle.BottomPitch + 1) * NoteHeight);
            using (context.PushOpacity(SelectionOpacity)) { context.DrawRectangle(accent, null, rectangle); }
            context.DrawRectangle(null, selectedPen, rectangle);
        }
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
                if ((PianoRollPresenter.MaximumMidiNote - row) % SemitonesPerOctave == 0)
                {
                    context.DrawLine(octavePen, new Point(visible.Left, top + NoteHeight), new Point(visible.Right, top + NoteHeight));
                }
            }
        }
        private void DrawTickLines(DrawingContext context, Rect visible)
        {
            int gridTicks = presenter?.SnapTicks ?? PianoRollPresenter.GridTicks;
            int firstTick = (int)(visible.Left / PixelsPerTick / gridTicks) * gridTicks;
            double lastTick = Math.Min(song!.LengthTicks, visible.Right / PixelsPerTick);
            for (long tick = firstTick; tick <= lastTick; tick += gridTicks)
            {
                Pen pen = gridPen;
                if (tick % (Song.FixedTicksPerBeat * BeatsPerBar) == 0) { pen = barPen; }
                else if (tick % Song.FixedTicksPerBeat == 0) { pen = beatPen; }
                double left = tick * PixelsPerTick;
                context.DrawLine(pen, new Point(left, visible.Top), new Point(left, visible.Bottom));
            }
        }
        private void UpdateNoteToolTip(Point position)
        {
            if (song == null) { return; }
            int trackIndex = FindHoveredTrack(position);
            if (trackIndex == hoveredTrack) { return; }
            hoveredTrack = trackIndex;
            ToolTip.SetTip(this, trackToolTips[trackIndex]);
        }

        private int FindHoveredTrack(Point position)
        {
            if (ContainsNoteAt(song!.Tracks[selectedTrack], position)) { return selectedTrack; }
            for (int trackIndex = song.Tracks.Count - 1; trackIndex >= 0; trackIndex--)
            {
                if (ContainsNoteAt(song.Tracks[trackIndex], position)) { return trackIndex; }
            }
            return selectedTrack;
        }

        private bool ContainsNoteAt(Track track, Point position)
        {
            int pitch = GetPitch(position.Y);
            double tick = position.X / PixelsPerTick;
            foreach (Note note in track.Notes)
            {
                if (note.MidiNote == pitch && tick >= note.Tick && tick < (long)note.Tick + note.DurationTicks)
                {
                    return true;
                }
            }
            return false;
        }

        private void PrepareChannelVisuals(Song current)
        {
            if (channelBrushes.Length != current.Tracks.Count)
            {
                channelBrushes = new IBrush[current.Tracks.Count];
                volumeBrushes = new IBrush[current.Tracks.Count][];
                ghostBrushes = new IBrush[current.Tracks.Count];
                channelLabels = new FormattedText[current.Tracks.Count];
                ghostLabels = new FormattedText[current.Tracks.Count];
                shortLabels = new string[current.Tracks.Count];
                trackToolTips = new string[current.Tracks.Count];
            }
            for (int trackIndex = 0; trackIndex < current.Tracks.Count; trackIndex++)
            {
                Track track = current.Tracks[trackIndex];
                IBrush channelBrush = ChannelPalette.GetBrush(track.Channel, track.ChannelIndex);
                string shortLabel = ChannelPalette.GetShortLabel(track.Channel, track.ChannelIndex);
                trackToolTips[trackIndex] = $"{shortLabel} · {track.Name}";
                if (ReferenceEquals(channelBrushes[trackIndex], channelBrush) && shortLabels[trackIndex] == shortLabel)
                {
                    continue;
                }
                channelBrushes[trackIndex] = channelBrush;
                shortLabels[trackIndex] = shortLabel;
                Color color = ((ISolidColorBrush)channelBrush).Color;
                ghostBrushes[trackIndex] = new ImmutableSolidColorBrush(color, GhostOpacity);
                volumeBrushes[trackIndex] = CreateVolumeBrushes(color);
                channelLabels[trackIndex] = new FormattedText(shortLabel, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeface, LabelSize, labelBrush);
                ghostLabels[trackIndex] = new FormattedText(shortLabel, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeface, LabelSize, ghostLabelBrush);
            }
        }

        private static IBrush[] CreateVolumeBrushes(Color color)
        {
            IBrush[] brushes = new IBrush[MaximumVolume + 1];
            for (int volume = 0; volume <= MaximumVolume; volume++)
            {
                double brightness = MinimumBrightness + (1 - MinimumBrightness) * volume / MaximumVolume;
                brushes[volume] = new ImmutableSolidColorBrush(Color.FromRgb(
                    (byte)Math.Round(color.R * brightness), (byte)Math.Round(color.G * brightness), (byte)Math.Round(color.B * brightness)));
            }
            return brushes;
        }

        private void DrawNotes(DrawingContext context, int trackIndex, Rect visible, bool isSelectedTrack)
        {
            foreach (Note note in song!.Tracks[trackIndex].Notes)
            {
                Rect rectangle = new Rect(note.Tick * PixelsPerTick, (PianoRollPresenter.MaximumMidiNote - note.MidiNote) * NoteHeight + NoteInset,
                    Math.Max(1, note.DurationTicks * PixelsPerTick - NoteInset), NoteHeight - NoteInset * 2);
                if (!rectangle.Intersects(visible)) { continue; }
                if (!isSelectedTrack)
                {
                    context.DrawRectangle(ghostBrushes[trackIndex], null, rectangle, NoteCornerRadius, NoteCornerRadius);
                    DrawGhostLabel(context, trackIndex, rectangle);
                    continue;
                }
                context.DrawRectangle(volumeBrushes[trackIndex][note.Volume], null, rectangle, NoteCornerRadius, NoteCornerRadius);
                double edgeInset = Math.Min(NoteCornerRadius, rectangle.Width / 2);
                context.DrawLine(noteEdge, new Point(rectangle.Left + edgeInset, rectangle.Bottom - NoteInset),
                    new Point(rectangle.Right - edgeInset, rectangle.Bottom - NoteInset));
                DrawChannelLabel(context, trackIndex, rectangle);
                if (note.Effects.Length > 0) { DrawEffectMark(context, rectangle); }
                if (selection != null && selection.Contains(note.Tick))
                {
                    context.DrawRectangle(null, selectedPen, rectangle, NoteCornerRadius, NoteCornerRadius);
                }
            }
        }

        private void DrawChannelLabel(DrawingContext context, int trackIndex, Rect rectangle)
        {
            FormattedText label = channelLabels[trackIndex];
            if (rectangle.Width < label.Width + LabelInset * 2 + EffectMarkSize) { return; }
            // 音量の低いノートでも文字のコントラストを保つため、記号の面は元のチャンネル色にする。
            Rect badge = new Rect(rectangle.Left + LabelInset, rectangle.Top + NoteInset,
                label.Width + LabelInset, rectangle.Height - NoteInset * 2);
            context.DrawRectangle(channelBrushes[trackIndex], null, badge, NoteCornerRadius, NoteCornerRadius);
            context.DrawText(label, new Point(badge.Left + LabelInset / 2, rectangle.Top + (rectangle.Height - label.Height) / 2));
        }

        private void DrawGhostLabel(DrawingContext context, int trackIndex, Rect rectangle)
        {
            FormattedText label = ghostLabels[trackIndex];
            if (rectangle.Width < label.Width + LabelInset * 2) { return; }
            // 複数ゴーストが重なっても識別記号の背景輝度を一定に保つ。
            context.DrawRectangle(background, null, new Rect(rectangle.Left + LabelInset / 2, rectangle.Top + NoteInset,
                label.Width + LabelInset, rectangle.Height - NoteInset * 2), NoteCornerRadius, NoteCornerRadius);
            context.DrawText(label, new Point(rectangle.Left + LabelInset, rectangle.Top + (rectangle.Height - label.Height) / 2));
        }

        private void DrawEffectMark(DrawingContext context, Rect rectangle)
        {
            using (context.PushClip(rectangle))
            using (context.PushTransform(Matrix.CreateTranslation(rectangle.Right - EffectMarkSize, rectangle.Top)))
            {
                context.DrawGeometry(accent, null, effectMark);
            }
        }
    }
}
