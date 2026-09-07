using System;
using System.IO;
using System.Threading;
using Arpeggio.Codecs;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;

namespace Arpeggio.Daw.Audio
{
    /// <summary>全曲の合成と形式別保存を行い、成功したファイルだけを保存先へ移動する。</summary>
    internal static class SongFileExporter
    {
        private const float OggQuality = 0.5f;

        internal static void Write(Song song, string path, CancellationToken cancellationToken)
        {
            string extension = Path.GetExtension(path);
            bool isOgg = extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
            if (!isOgg && !extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("書き出し先の拡張子は .wav または .ogg にしてください。", nameof(path));
            }
            cancellationToken.ThrowIfCancellationRequested();
            RenderSettings settings = new RenderSettings();
            SongRenderer renderer = new SongRenderer(song, settings);
            float[] samples = renderer.RenderAll();
            cancellationToken.ThrowIfCancellationRequested();
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (isOgg) { OggWriter.Write(temporaryPath, samples, settings.SampleRate, OggQuality); }
                else { WavWriter.Write(temporaryPath, samples, settings.SampleRate); }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
