# 配布ビルドと署名

Arpeggio の配布物を作る手順と、未署名配布に伴う各 OS での扱いをまとめる。
日常の開発ビルドは README の「試す」を参照する。


`.NET 10 SDK` と NuGet への接続がある環境で実行する。配布物には .NET ランタイム、Avalonia、SDL3 のネイティブ依存を含めるため、利用者の SDK／ランタイムのインストールは不要。単一ファイル化・トリミング・AOT は使わない。

```sh
# macOS 上で実行。Apple Silicon と Intel は別々の配布物にする。
bash tools/build_app.sh osx-arm64 1.0.0 1
bash tools/build_app.sh osx-x64 1.0.0 1

# Windows 用も同じスクリプトでクロス publish できる。
bash tools/build_app.sh win-x64 1.0.0 1
bash tools/build_app.sh win-arm64 1.0.0 1
```

引数は RID・バージョン・ビルド番号。後ろ二つは省略すると `1.0.0`・`1` になる。macOS のパッケージ化には macOS 標準の `plutil`・`codesign`・`ditto` を使う。Windows ZIP の生成には `zip` と `curl` が必要で、macOS／Linux／Windows の Git Bash で実行できる。Windows 上でビルドする場合も .NET 10 SDK・`zip`・`curl` を PATH に用意する。

| 対象 | 展開済みの出力 | 配布用 ZIP（上の指定の場合） |
|---|---|---|
| macOS Apple Silicon | `artifacts/osx-arm64/Arpeggio.app` | `artifacts/Arpeggio-1.0.0-1-osx-arm64.zip` |
| macOS Intel | `artifacts/osx-x64/Arpeggio.app` | `artifacts/Arpeggio-1.0.0-1-osx-x64.zip` |
| Windows x64 | `artifacts/win-x64/Arpeggio/` | `artifacts/Arpeggio-1.0.0-1-win-x64.zip` |
| Windows ARM64 | `artifacts/win-arm64/Arpeggio/` | `artifacts/Arpeggio-1.0.0-1-win-arm64.zip` |

再実行すると同じ RID の展開済み出力と同名 ZIP を置き換える。publish・パッケージ化に失敗した場合は前回の配布物を保持する。macOS は .NET 10 の対応範囲に合わせて **macOS 14 以降**を対象とする。[.NET 10 対応 OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)

macOS は ZIP を展開し、`Arpeggio.app` を `/Applications` に移して起動する。バンドルは `Contents/MacOS/` に publish 出力一式、`Contents/Resources/arpeggio.icns` にアイコン、`Contents/Info.plist` にアプリ情報を持つ。Windows は ZIP **全体**を展開して `Arpeggio/arpeggio-daw.exe` を起動する。exe だけを取り出さない。Windows アイコンは既存の `ApplicationIcon` を apphost に反映する。

Windows の初回起動前に、同梱の `Arpeggio/Prerequisites/vc_redist.x64.exe`（ARM64 版では `vc_redist.arm64.exe`）を実行する。SDL3 が必要とする **Visual C++ v14 ランタイム**の導入用で、新しい対応ランタイムが導入済みなら不要。ビルドスクリプトは Microsoft の公式 URL からビルド時の最新版インストーラーを取得して ZIP に含める。導入時は管理者権限を求められる場合がある。[Microsoft のランタイム配布情報](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist)

```powershell
# Windows の PowerShell。ファイルを exe へドロップしても同じ起動引数になる。
& .\Arpeggio\arpeggio-daw.exe "C:\Music\song.arpeggio.json"
```

ファイル指定なしの起動では、同梱の SNES デモをユーザーデータ領域の `Arpeggio/welcome.arpeggio.json` へ初回だけコピーして開く。通常は macOS の `~/Library/Application Support/Arpeggio/`、Windows の `%LOCALAPPDATA%\Arpeggio\`。保存した編集は次回も残る。バンドルや exe の隣へ書き込まない。

## 未署名配布と初回起動

Developer ID 署名・公証、Windows の Authenticode 署名は行わない。macOS のスクリプトは Apple Silicon での実行用に **ad-hoc 署名**を付けるが、開発元を証明する署名ではなく、Gatekeeper の警告は解消しない。

信頼できる配布物であることを確認し、初回は Finder でアプリを右クリック →「開く」を試す。現行 macOS で許可できない場合は、一度起動を試した後に「システム設定 → プライバシーとセキュリティ → このまま開く」を使う。[Apple の初回起動手順](https://support.apple.com/ja-jp/102445)

手元で検証する配布物の quarantine を明示的に解除する場合は、対象のアプリだけを指定する。スクリプトからは自動解除しない。

```sh
xattr -d com.apple.quarantine "/Applications/Arpeggio.app"
# 内部ファイルにも付いている場合のみ再帰的に解除する。
xattr -dr com.apple.quarantine "/Applications/Arpeggio.app"
```

Windows でも未署名のため SmartScreen の警告が出る場合がある。

## 曲ファイルの関連付け

macOS は `CFBundleDocumentTypes` と UTI `dev.pisuke.arpeggio.song` に `.arpeggio.json` を登録する。Finder のダブルクリック／Dock のアプリアイコンへのドロップは、起動済みの場合も Avalonia のファイル通知から `MainWindowPresenter.Open` → `DawDocument.Open` に渡る。CLI の `arpeggio-daw <path>` と同じ読み込み・監視・再生準備を使う。

複合拡張子が `public.json` と判定されたり、既存の JSON アプリが優先されたりする場合に備え、JSON の代替ハンドラーも登録する。その場合は対象ファイルの「情報を見る → このアプリケーションで開く」で Arpeggio を選び、以後はそのファイルをダブルクリックする。一般の `.json` 全体の関連付けを変えないよう「すべてを変更」は押さない。アプリ側では `.arpeggio.json` 以外を拒否する。Windows ZIP は関連付けのレジストリを変更しないため、exe へのドロップか上記コマンドで開く。

現在の DAW は一文書なので、曲は一つずつ開く。複数ファイルの一括ドロップはエラー表示し、未保存の編集があるときの別文書への切り替えも拒否する。保存してから再度開く。同じパスの通知は前面化だけを行い、未保存の編集を再読み込みで失わない。

## macOS の前面化・受け入れ確認（依頼者が実行）

同じ Bundle ID の古い Arpeggio が起動していない状態で、確認対象の `.app` を `/Applications` に置く。以下は実装時には実行していない。

```sh
open "/Applications/Arpeggio.app"
```

ウィンドウが表示されたらターミナルを前面にし、次を一度に実行する。前面化の反映を待ってから同じスクリプト内で照合する。

```sh
osascript \
  -e 'tell application id "dev.pisuke.arpeggio" to activate' \
  -e 'delay 1' \
  -e 'tell application "System Events" to get bundle identifier of first application process whose frontmost is true'
```

`-1728` なしで成功し、Arpeggio のウィンドウが前面に現れ、`dev.pisuke.arpeggio` を返すことを確認する。Automation の許可を求められた場合は実行元のターミナル／自動化アプリに許可する。クリック・キー送信には、その実行元のアクセシビリティ権限も必要。

ファイル通知だけをコマンドで確認する場合は `--args` を付けずに渡す。

```sh
open -a "/Applications/Arpeggio.app" "/absolute/path/song.arpeggio.json"
```

| 確認 | 期待結果 |
|---|---|
| アプリ未起動で曲をダブルクリック | 指定した曲のタイトル・ノートが表示される |
| 起動済み／最小化中に別の曲を Dock へドロップ | ウィンドウが復帰し、指定曲へ切り替わる |
| 空白・日本語・`#` を含むパス | ファイル URL が復号され、指定曲を開ける |
| 編集中に別の曲を開く／同じ曲を開く | 別の曲は拒否、同じ曲は前面化のみ。編集内容は維持 |
| 不正 JSON・存在しないファイル・通常の `.json`・複数ドロップ | ステータスにエラー、現在の曲は維持 |
| NES／GB／SNES の再生と保存、外部更新の再読み込み | SDL3 の読み込み・音声出力・既存の編集機能が動作 |
| Windows のクリーンな環境で ZIP を展開し、同梱 VC++ ランタイムを導入して起動 | .NET の追加インストールなしで動作し、exe のアイコンを表示 |

配布前には次も実行する。今回の実装では **ビルド・テスト・publish・アプリ起動は未実行**で、警告ゼロ／既存 676 件の成功は未確認。

```sh
dotnet build Arpeggio.slnx
dotnet test Arpeggio.slnx
```

