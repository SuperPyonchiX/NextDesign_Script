# はじめに

本文書は HelloDll（リボン表示名「DLL 形式の動作確認」）の紹介である。Next Design V3 で DLL 形式のエクステンションが読み込まれ、コマンドが動くかを確かめる最小構成である。

スクリプト形式の拡張を DLL へ移す前に、ビルド環境と配置の手順だけを切り分けて確かめるために作った。

# 何ができるようになるか

配置すると、リボンに「DLL確認」タブが出る。「確認」グループの「Hello」を押すと、読み込まれた DLL の場所・版・Activate した時刻・開いているプロジェクト名をダイアログに表示する。

2026年9月26日に、Visual Studio の入っていない会社PC で .NET 10 SDK を使ってビルドし、Next Design V3.1.9 での読み込みとコマンド実行を確認した（NuGet の 3.1.3 を参照してビルド）。経緯は [DLL 形式エクステンションの開発環境](../docs/dll-extension-setup.md) にある。

## できないこと

- **モデルや図には触らない。** DLL が読み込まれてコマンドが動くことを確かめるだけである。
- **デバッガのアタッチは未確認。** 手順案は [開発環境の「デバッグ」](../docs/dll-extension-setup.md) にあるが、実機では試していない。

# 全体像

```
HelloDll/
├── HelloDll.csproj        ← ビルドするソースの列挙。共通設定は ../Directory.Build.props
├── HelloDllExtension.cs   ← IExtension の実装と「Hello」のハンドラ
└── manifest.json          ← リボンとコマンドの定義（lifecycle は application）
```

# 環境構築

.NET SDK の入れ方（Visual Studio なし、会社PC 向け）は [DLL 形式エクステンションの開発環境](../docs/dll-extension-setup.md) にある。

| 必要なもの | 用途 |
|---|---|
| .NET SDK | ビルド |
| Next Design V3.x | 動作確認 |

リポジトリ直下で実行する。配置先（`extensions\HelloDll`）ではビルドしない。

```powershell
# ビルドして work/hellodll-publish に出力する（NuGet の NextDesign 3.1.3 を参照）
dotnet publish HelloDll -c Release -o work/hellodll-publish
```

- `-p:NextDesignDir="[Next Design のインストール先]"` を付けると、NuGet の代わりにインストール先の DLL を直接参照する
- 実行時は Next Design 本体の DLL を使うので、どちらの場合も出力には含めない

続けて、Next Design を終了してから `work/hellodll-publish` の中身だけを次のフォルダへコピーする。ソース（`.csproj`、`.cs`、`bin`、`obj`）は置かない。

```
%LOCALAPPDATA%\DENSO CREATE\Next Design\extensions\HelloDll\
    manifest.json
    HelloDll.dll
    HelloDll.deps.json
    HelloDll.pdb
```

エクステンションは Next Design の起動時にしか読み込まれず、起動中は DLL を差し替えられない。HelloDll は `Tools/Publish-Extensions.ps1` の既定の対象に入っていないので、手でコピーする。

# 困ったとき

| 症状 | まず見るところ |
|---|---|
| 「DLL確認」タブが出ない | 配置先のフォルダ名と、`manifest.json`・`HelloDll.dll` が直下にあるか。コピー後に Next Design を再起動したか |
| DLL をコピーできない | Next Design が起動したままになっていないか |
| 本体の DLL と競合する | 配置先に `NextDesign.Core.dll` / `NextDesign.Desktop.dll` を置いていないか |
| nuget.org に接続できない | [開発環境の「nuget.org に接続できない場合」](../docs/dll-extension-setup.md) |

# 関連

- [スクリプトと DLL](https://docs.nextdesign.app/extension/v3.x/docs/overview/script-and-dlls)
- [プロジェクトの作成](https://docs.nextdesign.app/extension/v3.x/docs/getting-started/dev-with-vs/create-vs-project)
- [実行とデバッグ](https://docs.nextdesign.app/extension/v3.x/docs/getting-started/dev-with-vs/debugging)
