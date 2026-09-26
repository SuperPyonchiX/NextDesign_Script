# DLL 形式エクステンションの開発環境（Visual Studio なし）

Next Design V3 の DLL 形式エクステンションを、Visual Studio を入れずに .NET SDK と VS Code でビルドする手順。対象は `HelloDll/` と、今後 DLL 化する拡張。

## 使うものとライセンス

| 用途 | ツール | ライセンス |
|---|---|---|
| ビルド | .NET 8 SDK（dotnet CLI） | MIT。Visual Studio とは別配布 |
| エディタ | VS Code | 無償 |
| 補完・デバッグ | VS Code の「C#」拡張（`ms-dotnettools.csharp`） | MIT |

VS Code の「C# Dev Kit」（`ms-dotnettools.csdevkit`）は入れない。Visual Studio Community と同系統の条件で、商用利用かつ開発者6名以上だと Visual Studio サブスクリプションが要る。「C#」拡張だけで補完・定義ジャンプ・エラー表示は動く。

公式手順は .NET 6 SDK だが、6 はサポートが終わっている。.NET 8 SDK でも `net6.0-windows` 向けの DLL を作れる。

## 1. 事前に確かめること

1. Next Design のインストール先を調べる。スタートメニューの Next Design を右クリックし、「ファイルの場所を開く」でショートカットのリンク先を見る。そのフォルダに `NextDesign.Core.dll` と `NextDesign.Desktop.dll` があることを確かめる
2. ブラウザで `https://api.nuget.org/v3/index.json` が開けるか試す。開ければ手順 4 はそのまま進める。開けなければ手順 4 の「nuget.org に接続できない場合」を使う

## 2. .NET 8 SDK を入れる

管理者権限がある場合は、次のどちらか。

- `https://dotnet.microsoft.com/download/dotnet/8.0` から「SDK 8.0.x」の「Windows x64 インストーラー」をダウンロードして実行する
- PowerShell で `winget install Microsoft.DotNet.SDK.8`

管理者権限がない場合は、公式のインストールスクリプトでユーザーフォルダに入れる。

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File $env:TEMP\dotnet-install.ps1 -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
[Environment]::SetEnvironmentVariable("PATH", "$env:LOCALAPPDATA\Microsoft\dotnet;" + [Environment]::GetEnvironmentVariable("PATH", "User"), "User")
```

dotnet CLI は既定で利用状況を Microsoft へ送る。社内PCで止めたい場合は次を設定する。

```powershell
[Environment]::SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1", "User")
```

新しいターミナルを開き、`dotnet --list-sdks` に `8.0.x` が出れば完了。

## 3. VS Code に「C#」拡張を入れる

拡張機能ビューで「C#」を検索し、発行元が Microsoft の `C#`（`ms-dotnettools.csharp`）を入れる。`C# Dev Kit` は入れない。コマンドラインなら次のとおり。

```powershell
code --install-extension ms-dotnettools.csharp
```

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

この3ファイルでビルドが通ることは .NET 9 SDK で確認した。.NET 8 SDK で別のパッケージを求められたら、同じ要領で追加する。

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

## 参照

- [スクリプトと DLL](https://docs.nextdesign.app/extension/v3.x/docs/overview/script-and-dlls)
- [プロジェクトの作成](https://docs.nextdesign.app/extension/v3.x/docs/getting-started/dev-with-vs/create-vs-project)
- [実行とデバッグ](https://docs.nextdesign.app/extension/v3.x/docs/getting-started/dev-with-vs/debugging)
- [C# Dev Kit FAQ（ライセンス）](https://code.visualstudio.com/docs/csharp/cs-dev-kit-faq)
