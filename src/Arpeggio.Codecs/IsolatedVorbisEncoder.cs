using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Arpeggio.Codecs
{
    /// <summary>
    /// <c>Arpeggio.Codecs.Vorbis</c> を呼び出しごとに使い捨ての <see cref="AssemblyLoadContext"/> へ読み込んで実行する。
    /// OggVorbisEncoder の静的テーブル汚染（32 kHz 以上でエンコードした後、32 kHz 未満・品質 0.5 未満で例外）を、
    /// 静的状態ごと捨てることで回避する。
    /// </summary>
    internal static class IsolatedVorbisEncoder
    {
        private const string EntryAssemblyName = "Arpeggio.Codecs.Vorbis";
        private const string EncoderAssemblyName = "OggVorbisEncoder";
        private const string EntryTypeName = "Arpeggio.Codecs.Vorbis.VorbisEncodeEntry";
        private const string EntryMethodName = "Encode";

        internal static void Encode(Stream stream, float[] interleavedStereo, int sampleRate, float quality)
        {
            string directory = Path.GetDirectoryName(typeof(IsolatedVorbisEncoder).Assembly.Location)
                ?? throw new InvalidOperationException("Codecs アセンブリの場所を特定できません。");
            var context = new VorbisLoadContext(directory);
            try
            {
                Assembly entryAssembly = context.LoadFromAssemblyName(new AssemblyName(EntryAssemblyName));
                Type entryType = entryAssembly.GetType(EntryTypeName, throwOnError: true)!;
                MethodInfo encode = entryType.GetMethod(EntryMethodName, BindingFlags.Public | BindingFlags.Static)
                    ?? throw new MissingMethodException(EntryTypeName, EntryMethodName);
                try
                {
                    encode.Invoke(null, new object[] { stream, interleavedStereo, sampleRate, quality });
                }
                catch (TargetInvocationException exception) when (exception.InnerException != null)
                {
                    throw exception.InnerException;
                }
            }
            finally
            {
                context.Unload();
            }
        }

        /// <summary>
        /// エンコーダーとその入口アセンブリだけを自分のコンテキストへ読み込む。
        /// <see cref="AssemblyLoadContext.Resolving"/> では既定コンテキスト（deps.json 由来の TPA）が先に解決してしまい共有インスタンスを掴むため、
        /// <see cref="Load"/> のオーバーライドで先回りする。
        /// </summary>
        private sealed class VorbisLoadContext : AssemblyLoadContext
        {
            private readonly string _directory;

            internal VorbisLoadContext(string directory) : base(EntryAssemblyName, isCollectible: true)
            {
                _directory = directory;
            }

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                if (assemblyName.Name != EntryAssemblyName && assemblyName.Name != EncoderAssemblyName)
                {
                    return null;
                }
                return LoadFromAssemblyPath(Path.Combine(_directory, assemblyName.Name + ".dll"));
            }
        }
    }
}
