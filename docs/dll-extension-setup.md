# DLL 形式エクステンションの開発環境（Visual Studio なし）

Next Design V3 の DLL 形式エクステンションを、Visual Studio を入れずに .NET SDK と VS Code でビルドする手順。対象は `HelloDll/` と、今後 DLL 化する拡張。

2026年9月26日に、Visual Studio の入っていない会社PCで .NET 10 SDK（10.0.401）を使って `HelloDll` をビルドし、Next Design V3.1.9 での読み込みとコマンド実行を確認した。デバッガのアタッチは未確認。

## 使うものとライセンス

| 用途 | ツール | ライセンス | 社内での商用開発 |
|---|---|---|---|
| ビルド | .NET 10 SDK（dotnet CLI） | Windows 版は .NET Library License（ソースコードは MIT） | 可。費用なし |
| 参照パック（net6.0 向け） | Microsoft.NETCore.App.Ref ほか（NuGet） | MIT | 可 |
| エディタ | VS Code | Microsoft Software License Terms | 可。費用なし |
| 補完・デバッグ | VS Code の「C#」拡張（ms-dotnettools.csharp） | ソースは MIT、配布物は Microsoft C# Extension のライセンス条項 | 可。費用なし |
| 使わない | VS Code の「C# Dev Kit」（ms-dotnettools.csdevkit） | Community License | 商用で開発者6名以上は Visual Studio Professional 以上のサブスクリプションが必要 |

公式手順は .NET 6 SDK だが、6 は 2024年11月12日にサポートが終わっている。.NET 8 も 2026年11月10日に終わるので、LTS の .NET 10 SDK（2028年11月14日まで）を使う。新しい SDK でも net6.0-windows 向けの DLL を作れる。

## ライセンス上問題ないと判断した根拠

2026年9月時点で各ライセンスの原文を確認した。以下は条文を読んだうえでの判断で、社内の法務・ソフトウェア管理部門の判断に代わるものではない。

### .NET SDK

- 無償で商用利用できる。公式サイト「.NET is free」に "There are no licensing costs, including for commercial use." とある
- Windows 版の SDK は .NET Library License（dotnet/core の license-information.md に "On Windows: .NET Library License" とある）。第1条は "You may install and use any number of copies of the software to develop and test your applications."。社内向けの拡張 DLL を開発する用途はこれに当たり、台数の制限もない
- 作った DLL の配布も制限されない。license-information.md に "Binaries produced by .NET SDK compilers can be redistributed without additional restrictions" とある
- 禁止事項（第5条）は、リバースエンジニアリング、技術的制限の回避、SDK そのものを第三者へ共有・公開・貸与すること。SDK は各自が Microsoft から入手すれば当たらない
- 第4条で利用状況の送信（テレメトリ）が定められている。止め方は手順 2 に書いた

### 参照パック（nuget.org に接続できない場合に持ち込む nupkg）

- 3つの nupkg（Microsoft.NETCore.App.Ref / Microsoft.WindowsDesktop.App.Ref / Microsoft.AspNetCore.App.Ref の 6.0.36）は、いずれもパッケージ内の定義で MIT。WindowsDesktop は同梱の LICENSE ファイルが MIT License 本文。自分のPC間で持ち込んで使うことに制限はない

### VS Code

- ライセンス条項第1条は "You may use any number of copies of the software to develop and test your applications, including deployment within your internal corporate network."。組織内での利用が明記されていて、台数の制限もない

### C# 拡張

- 配布物は「Microsoft Software License Terms - Microsoft C# Extension for Visual Studio Code」。"You may only use the C# Extension for Visual Studio Code with Visual Studio Code, Visual Studio or Xamarin Studio software to help you develop and test your applications." とあり、VS Code でアプリを開発する用途なら使える。有償の条件はない

### C# Dev Kit（使わない理由）

- FAQ に "For commercial purposes, teams of up to 5 can also use the C# Dev Kit at no cost. For 6+ developers, those users will need a Visual Studio Professional (or higher) subscription." とある。会社で使うと有償ライセンスが要る可能性が高いので入れない
- C# 拡張だけで、補完・定義ジャンプ・エラー表示・デバッガは動く

### Next Design の DLL（未確認）

- 直接参照する NextDesign.Core.dll / NextDesign.Desktop.dll は、社内で導入済みの Next Design に含まれるもの。公式マニュアルの DLL 開発手順がこれらを参照する前提なので、拡張開発での利用は想定された使い方と考えられる。ただし Next Design の使用許諾契約そのものは確認していない
- NuGet の NextDesign.Core / NextDesign.Desktop にはライセンス表記がなく、"DENSO CREATE INC. All rights reserved." だけ。会社PCではインストール先の DLL を直接参照するので、NuGet パッケージは使わない

## 1. 事前に確かめること

1. Next Design のインストール先を調べる。スタートメニューの Next Design を右クリックし、「ファイルの場所を開く」でショートカットのリンク先を見る。そのフォルダに `NextDesign.Core.dll` と `NextDesign.Desktop.dll` があることを確かめる
2. ブラウザで `https://api.nuget.org/v3/index.json` が開けるか試す。開ければ手順 4 はそのまま進める。開けなければ手順 4 の「nuget.org に接続できない場合」を使う

## 2. .NET 10 SDK を入れる

管理者権限がある場合は、次のどちらか。

- `https://dotnet.microsoft.com/download/dotnet/10.0` から「SDK 10.0.x」の「Windows x64 インストーラー」をダウンロードして実行する
- PowerShell で `winget install Microsoft.DotNet.SDK.10`

管理者権限がない場合は、公式のインストールスクリプトでユーザーフォルダに入れる。

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File $env:TEMP\dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
[Environment]::SetEnvironmentVariable("PATH", "$env:LOCALAPPDATA\Microsoft\dotnet;" + [Environment]::GetEnvironmentVariable("PATH", "User"), "User")
```

dotnet CLI は既定で利用状況を Microsoft へ送る。社内PCで止めたい場合は次を設定する。

```powershell
[Environment]::SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1", "User")
```

新しいターミナルを開き、`dotnet --list-sdks` に `10.0.x` が出れば完了。

## 3. VS Code に「C#」拡張を入れる

拡張機能ビューで「C#」を検索し、発行元が Microsoft の `C#`（`ms-dotnettools.csharp`）を入れる。`C# Dev Kit` は入れない。コマンドラインなら次のとおり。

```powershell
code --install-extension ms-dotnettools.csharp
```

### 「spawn UNKNOWN」の通知が出る場合

C# 拡張を入れた直後に、次の通知が出ることがある。

- An error occurred while installing .NET (10.0.12): spawn UNKNOWN
- An error occurred while installing .NET: spawn UNKNOWN
- .NET SDKが見つかりません: Error running dotnet --info: spawn UNKNOWN .NET デバッグは有効になりません。

C# 拡張は、言語サーバー用の .NET ランタイムを「.NET Install Tool」経由で自動ダウンロードして起動する。その起動が失敗している。ターミナルからの dotnet は動くので、ビルドと配置には影響しない。影響を受けるのは補完・エラー表示・デバッガ。

ユーザー設定（Ctrl+Shift+P →「基本設定: ユーザー設定を開く (JSON)」）に次を追加し、自動ダウンロードの代わりに手順 2 で入れた SDK を使わせる。path は SDK を入れた場所に合わせる。

```json
"dotnetAcquisitionExtension.existingDotnetPath": [
  {
    "extensionId": "ms-dotnettools.csharp",
    "path": "C:\Program Files\dotnet\dotnet.exe"
  }
]
```

2026年9月26日、会社PC（SDK は C:\Program Files\dotnet）でこの設定を入れ、3つとも通知が出なくなった。自動ダウンロードしたランタイムの起動が失敗した原因（セキュリティソフトによるブロックか、SDK 導入前から開いていた VS Code の PATH か）は特定していない。この設定でも同じ通知が出る場合は、Windows セキュリティの「保護の履歴」にブロックの記録がないか確認する。

## 4. ビルドする

リポジトリ直下で、インストール先の DLL を直接参照してビルドする。`NextDesignDir` には手順 1 で調べたフォルダを渡す。

```powershell
dotnet publish HelloDll -c Release -o work/hellodll-publish -p:NextDesignDir="C:\Program Files\DENSO CREATE\Next Design"
```

`NextDesignDir` を省くと NuGet の `NextDesign.Core` / `NextDesign.Desktop` 3.1.3 を参照する。社内の実行環境と版を揃えるため、会社PCでは直接参照を使う。

初回は `net6.0` 向けの参照パックを nuget.org から取得する。

### nuget.org に接続できない場合

必要なのは次の3ファイル（合計 13MB 程度）。nuget.org に接続できるPCで一度ビルドすると `%USERPROFILE%\.nuget\packages\` の下にできるので、それを持ち込む。

| ファイル | 取得元 |
|---|---|
| `microsoft.netcore.app.ref.6.0.36.nupkg` | `%USERPROFILE%\.nuget\packages\microsoft.netcore.app.ref\6.0.36\` |
| `microsoft.windowsdesktop.app.ref.6.0.36.nupkg` | `%USERPROFILE%\.nuget\packages\microsoft.windowsdesktop.app.ref\6.0.36\` |
| `microsoft.aspnetcore.app.ref.6.0.36.nupkg` | `%USERPROFILE%\.nuget\packages\microsoft.aspnetcore.app.ref\6.0.36\` |

会社PCの任意のフォルダ（例 `C:\nuget-local`）に置き、取得元をそのフォルダに限定してビルドする。

```powershell
dotnet publish HelloDll -c Release -o work/hellodll-publish -p:NextDesignDir="C:\Program Files\DENSO CREATE\Next Design" -p:RestoreSources="C:\nuget-local"
```

この3ファイルでビルドが通ることは .NET 9 SDK で確認した。.NET 10 SDK で別のパッケージを求められたら、同じ要領で追加する。

## 5. 配置して動かす

1. Next Design を終了する。エクステンションは起動時にしか読み込まれず、実行中は DLL を差し替えられない
2. `work/hellodll-publish` の中身を `%LOCALAPPDATA%\DENSO CREATE\Next Design\extensions\HelloDll\` へコピーする
3. Next Design を起動し、リボンの「DLL確認」タブの「Hello」を押す

`NextDesign.Core.dll` / `NextDesign.Desktop.dll` を配置先に置いてはいけない（本体の DLL と競合する）。csproj で出力から外してあるので、publish の出力をそのままコピーすればよい。

## 6. デバッグ（未確認）

Next Design が .NET 6 上で動いていれば、VS Code の「C#」拡張で起動中の Next Design にアタッチできるはず。`.vscode/launch.json` の例：

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Next Design にアタッチ",
      "type": "coreclr",
      "request": "attach",
      "processId": "${command:pickProcess}"
    }
  ]
}
```

実行後にプロセス一覧から `NextDesign.exe` を選ぶ。配置した `.pdb` がビルド時のものと一致していれば、ブレークポイントで止まる。

## 上司・情シスへの説明

### 口頭での答え方

> .NET SDK は Microsoft が無償で出している開発キットで、ライセンスは .NET Library License。条項に「プログラムの設計・開発・テストのためなら何台に入れてもよい」とあり、公式サイトにも商用利用で費用がかからないと明記されている。Visual Studio とは別の製品で、Visual Studio のライセンスには依存しない。

### エビデンス（強い順）

| # | 資料 | 示すこと | 入手方法 |
|---|---|---|---|
| 1 | インストールした SDK に同梱の LICENSE.txt | 実際に同意したライセンスそのもの。版ごとに固定で、Web の記載が変わっても影響しない | C:\Program Files\dotnet\LICENSE.txt（ユーザーフォルダに入れた場合は %LOCALAPPDATA%\Microsoft\dotnet\LICENSE.txt） |
| 2 | 同 第1条 a 項 | "You may install and use any number of copies of the software to design, develop and test your programs." | 上のファイルの冒頭（.NET 9 SDK 同梱版で確認。10 は各自で開いて確認する） |
| 3 | 公式ページ「.NET is free」 | "There are no licensing costs, including for commercial use." | https://dotnet.microsoft.com/platform/free |
| 4 | dotnet/core の license-information.md | Windows 版の配布物が .NET Library License であること、SDK でビルドしたバイナリは追加の制限なく再配布できること | https://github.com/dotnet/core/blob/main/license-information.md |
| 5 | C# Dev Kit FAQ | 有償になりうるのは C# Dev Kit で、それを入れていないこと | https://code.visualstudio.com/docs/csharp/cs-dev-kit-faq |

1 がいちばん効く。Web ページ（3〜5）は後から書き換わることがあるので、PDF かスクリーンショットに日付を付けて保存しておく。

### 聞かれそうなこと

| 質問 | 答え |
|---|---|
| Visual Studio を入れていないのに大丈夫か | SDK は Visual Studio とは別に配布されている製品で、Visual Studio のライセンスに依存しない。Microsoft の公式サイトから SDK 単体で入れている |
| データが外に出ないか | ライセンス条項に利用状況の送信が定められている。DOTNET_CLI_TELEMETRY_OPTOUT=1 で止められる。必須設定にするかは社内ポリシー次第 |
| VS Code の拡張は大丈夫か | 「C#」拡張は、VS Code でアプリを開発・テストする用途なら使える条項で、有償の条件はない。有償になりうる「C# Dev Kit」は入れていない |

### 言い切らないこと

- 「法的に100%問題ない」とは言わない。「条文上は問題ないと判断した。根拠はこれ」と伝え、最終判断は会社のソフトウェア管理部門（情シス・法務）に承認をもらう形にする
- Next Design 本体の使用許諾契約は確認していないので、その点は「未確認」と伝える

## 参照

- [スクリプトと DLL](https://docs.nextdesign.app/extension/v3.x/docs/overview/script-and-dlls)
- [プロジェクトの作成](https://docs.nextdesign.app/extension/v3.x/docs/getting-started/dev-with-vs/create-vs-project)
- [実行とデバッグ](https://docs.nextdesign.app/extension/v3.x/docs/getting-started/dev-with-vs/debugging)
- [.NET のサポート期間](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)
- [.NET のライセンス情報](https://github.com/dotnet/core/blob/main/license-information.md)
- [.NET Library License](https://dotnet.microsoft.com/dotnet_library_license.htm)
- [.NET is free](https://dotnet.microsoft.com/platform/free)
- [VS Code ライセンス](https://code.visualstudio.com/license)
- [C# 拡張のライセンス](https://github.com/dotnet/vscode-csharp/blob/main/RuntimeLicenses/license.txt)
- [C# Dev Kit FAQ](https://code.visualstudio.com/docs/csharp/cs-dev-kit-faq)
