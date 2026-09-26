# HelloDll

Next Design V3 で DLL 形式のエクステンションが動くかを確かめる最小構成。スクリプト形式の拡張を DLL へ移す前の足場として使う。

## ビルド

```powershell
dotnet publish HelloDll -c Release -o work/hellodll-publish
```

- `-p:NextDesignDir="<Next Design のインストール先>"` を付けると、本体の DLL を直接参照する。付けなければ NuGet の 3.1.3 を参照する
- 実行時は Next Design 本体の DLL を使うので、どちらの場合も出力には含めない
- Visual Studio なしでの環境構築（会社PC向け）は [DLL 形式エクステンションの開発環境](../docs/dll-extension-setup.md)

## 配置

`work/hellodll-publish` の中身を次のフォルダへコピーする。

```
%LOCALAPPDATA%\DENSO CREATE\Next Design\extensions\HelloDll\
    manifest.json
    HelloDll.dll
    HelloDll.deps.json
    HelloDll.pdb
```

エクステンションは Next Design の起動時にしか読み込まれない。DLL を差し替えるときは Next Design を終了してからコピーする。

## 参照

- [スクリプトと DLL](https://docs.nextdesign.app/extension/v3.x/docs/overview/script-and-dlls)
- [プロジェクトの作成](https://docs.nextdesign.app/extension/v3.x/docs/getting-started/dev-with-vs/create-vs-project)
- [実行とデバッグ](https://docs.nextdesign.app/extension/v3.x/docs/getting-started/dev-with-vs/debugging)
