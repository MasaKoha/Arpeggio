Arpeggio — Windows 配布版

1. ZIP 全体を展開してください。exe だけを移動しないでください。
2. 初回は Prerequisites フォルダの vc_redist.x64.exe または vc_redist.arm64.exe を実行し、
   Microsoft Visual C++ v14 ランタイムを導入してください。管理者権限を求められる場合があります。
   新しい対応ランタイムが導入済みなら、この手順は不要です。
3. arpeggio-daw.exe を起動してください。.NET の追加インストールは不要です。

曲を開くには .arpeggio.json ファイルを exe へドロップするか、PowerShell から指定してください。
  & .\arpeggio-daw.exe "C:\Music\song.arpeggio.json"

引数なしでは同梱デモを %LOCALAPPDATA%\Arpeggio\welcome.arpeggio.json へ初回コピーして開きます。
その後の保存内容は次回起動時にも残ります。一般の .json の関連付けは変更しません。
Arpeggio 自体は未署名のため、Windows が初回起動時に警告する場合があります。

同梱ランタイムの取得元（配布ビルド時の最新版）:
https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist
https://aka.ms/vc14/vc_redist.x64.exe
https://aka.ms/vc14/vc_redist.arm64.exe
