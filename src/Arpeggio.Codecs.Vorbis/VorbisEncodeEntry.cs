using System;
using System.IO;
using OggVorbisEncoder;

namespace Arpeggio.Codecs.Vorbis
{
    /// <summary>
    /// OggVorbisEncoder を直接使うエンコード本体。<c>Arpeggio.Codecs</c> はこのアセンブリを呼び出しごとに
    /// 新しい <see cref="System.Runtime.Loader.AssemblyLoadContext"/> へ読み込んで実行する。
    /// 理由: OggVorbisEncoder 1.2.2 は 32 kHz 以上でエンコードすると静的テーブルを書き換え、その後の
    /// 32 kHz 未満・品質 0.5 未満のエンコードで IndexOutOfRangeException を起こす。静的状態を毎回捨てるため隔離する。
    /// </summary>
    public static class VorbisEncodeEntry
    {
        private const int StereoChannels = 2;
        private const int BufferFrames = 1024;

        /// <summary>検証済みのステレオ float PCM を Ogg Vorbis としてストリームへ書き出す。ストリームは閉じない。</summary>
        public static void Encode(Stream stream, float[] interleavedStereo, int sampleRate, float quality)
        {
            VorbisInfo information = VorbisInfo.InitVariableBitRate(StereoChannels, sampleRate, quality);
            OggStream container = new OggStream(Guid.NewGuid().GetHashCode());
            container.PacketIn(HeaderPacketBuilder.BuildInfoPacket(information));
            container.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(new Comments()));
            container.PacketIn(HeaderPacketBuilder.BuildBooksPacket(information));
            // 音声をヘッダーと別ページから開始するため、ここだけ強制排出する。
            WritePages(container, stream, true);
            ProcessingState encoder = ProcessingState.Create(information);
            float[][] channels = { new float[BufferFrames], new float[BufferFrames] };
            int totalFrames = interleavedStereo.Length / StereoChannels;
            if (totalFrames == 0)
            {
                // 音声パケットが 1 つも無いと EOS ページが出ず、再生側が不完全なファイルとして扱う。無音 1 フレームで閉じる。
                encoder.WriteData(channels, 1);
                WritePackets(encoder, container, stream);
            }
            for (int offset = 0; offset < totalFrames; offset += BufferFrames)
            {
                int count = Math.Min(BufferFrames, totalFrames - offset);
                CopyChannels(interleavedStereo, offset, count, channels);
                encoder.WriteData(channels, count);
                WritePackets(encoder, container, stream);
            }
            encoder.WriteEndOfStream();
            WritePackets(encoder, container, stream);
            WritePages(container, stream, true);
        }

        private static void CopyChannels(float[] samples, int offsetFrames, int countFrames, float[][] channels)
        {
            for (int index = 0; index < countFrames; index++)
            {
                int source = (offsetFrames + index) * StereoChannels;
                channels[0][index] = Math.Clamp(samples[source], -1, 1);
                channels[1][index] = Math.Clamp(samples[source + 1], -1, 1);
            }
        }

        private static void WritePackets(ProcessingState encoder, OggStream container, Stream stream)
        {
            while (!container.Finished && encoder.PacketOut(out OggPacket packet))
            {
                container.PacketIn(packet);
                WritePages(container, stream, false);
            }
        }

        private static void WritePages(OggStream container, Stream stream, bool force)
        {
            while (container.PageOut(out OggPage page, force))
            {
                stream.Write(page.Header, 0, page.Header.Length);
                stream.Write(page.Body, 0, page.Body.Length);
            }
        }
    }
}
