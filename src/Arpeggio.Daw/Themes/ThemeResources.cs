using System;
using Avalonia;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace Arpeggio.Daw.Themes
{
    /// <summary>カスタム描画の初期化時に AXAML のテーマリソースを解決する。</summary>
    internal static class ThemeResources
    {
        internal static IBrush GetBrush(string name) => Get<IBrush>(name);
        internal static IDataTemplate GetContentTemplate(string name) => Get<IDataTemplate>(name);
        internal static FontFamily NumericFont => Get<FontFamily>("Arpeggio.Font.Numeric");

        private static TResource Get<TResource>(string name)
        {
            Application application = Application.Current
                ?? throw new InvalidOperationException("テーマの読み込み後に描画コントロールを生成してください。");
            // インデクサーでは探索されない MergedDictionaries 内のリソースも解決する。
            return application.Resources.TryGetResource(name, null, out object? resource) && resource is TResource typedResource
                ? typedResource
                : throw new InvalidOperationException($"テーマリソース {name} がありません。");
        }
    }
}
