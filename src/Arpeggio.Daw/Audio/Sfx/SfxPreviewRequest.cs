using Arpeggio.Core.Document;

namespace Arpeggio.Daw.Audio.Sfx
{
    /// <summary>非同期完了が属する世代と、所有権を切り離した入力を保持する。</summary>
    internal sealed record SfxPreviewRequest(long Generation, Song? Song);
}
