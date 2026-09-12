using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sequencing;

namespace Arpeggio.Formats.Export.Control
{
    /// <summary>各トラックの次境界とグローバルフレーム境界の和集合を走査する。</summary>
    internal sealed class ControlTimelineBuilder
    {
        private readonly Song _snapshot;
        private readonly ChipExportOptions _options;
        private readonly ConversionReport _report;
        private readonly TickClock _clock;
        private readonly FrameClock _frameClock = new FrameClock(ConversionLimits.ControlSampleRate);

        internal ControlTimelineBuilder(Song snapshot, ChipExportOptions options, ConversionReport report)
        {
            _snapshot = snapshot;
            _options = options;
            _report = report;
            _clock = new TickClock(snapshot.TempoBpm, ConversionLimits.ControlSampleRate);
        }

        internal ControlTimeline? Build()
        {
            var cursors = new List<ControlTrackCursor>(_snapshot.Tracks.Count);
            try
            {
                var instruments = new Dictionary<int, Instrument>();
                foreach (Instrument instrument in _snapshot.Instruments)
                {
                    instruments.Add(instrument.Id, instrument);
                }
                for (int trackIndex = 0; trackIndex < _snapshot.Tracks.Count; trackIndex++)
                {
                    cursors.Add(new ControlTrackCursor(_snapshot, trackIndex, _options.Loops, _clock, instruments, _report));
                }
                return BuildEvents(cursors);
            }
            finally
            {
                foreach (ControlTrackCursor cursor in cursors)
                {
                    cursor.Dispose();
                }
            }
        }

        private ControlTimeline? BuildEvents(List<ControlTrackCursor> cursors)
        {
            double totalTicks = _snapshot.LengthTicks + (_options.Loops - 1.0) * (_snapshot.LengthTicks - _snapshot.LoopStartTick);
            long endSamples = _clock.TickToSamples(totalTicks);
            long positionSamples = 0;
            long previousFrame = 0;
            long boundaryCount = 0;
            var events = new List<ControlEvent>();
            while (true)
            {
                long frame = _frameClock.GetFrame(positionSamples);
                foreach (ControlTrackCursor cursor in cursors)
                {
                    cursor.Prepare(positionSamples, frame > previousFrame, positionSamples == endSamples);
                }
                if (!_report.CanWrite)
                {
                    return null;
                }
                AppendEvents(cursors, events);
                boundaryCount++;
                if (positionSamples == endSamples)
                {
                    break;
                }
                previousFrame = frame;
                positionSamples = FindNextBoundary(cursors, positionSamples, endSamples);
            }
            _report.SetStatistic("controlBoundaries", boundaryCount);
            _report.SetStatistic("controlEvents", events.Count);
            return new ControlTimeline(_snapshot, endSamples, events);
        }

        private long FindNextBoundary(List<ControlTrackCursor> cursors, long positionSamples, long endSamples)
        {
            long nextBoundary = Math.Min(endSamples, _frameClock.GetNextBoundary(positionSamples));
            foreach (ControlTrackCursor cursor in cursors)
            {
                nextBoundary = Math.Min(nextBoundary, cursor.NextBoundary);
            }
            return nextBoundary;
        }

        private static void AppendEvents(List<ControlTrackCursor> cursors, List<ControlEvent> events)
        {
            // 全声の停止を先に確定し、共有ゲートを後続の発音が上書きする順序を守る。
            foreach (ControlTrackCursor cursor in cursors)
            {
                if (cursor.PendingStop is ControlEvent stop)
                {
                    events.Add(stop);
                }
            }
            foreach (ControlTrackCursor cursor in cursors)
            {
                if (cursor.PendingState is ControlEvent state)
                {
                    events.Add(state);
                }
            }
        }
    }
}
