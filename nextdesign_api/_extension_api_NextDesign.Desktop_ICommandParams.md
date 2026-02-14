ICommandParams インタフェース
======================

名前空間: NextDesign.Desktop

説明​
-------------------------

コマンドのパラメータを提供します。

所属エリア​
--------------------------------

名前

説明

[コマンド](_extension_api_overview_commands.md)

コマンドハンドラで受け取ったコマンドにアクセスするAPI群です。

所属イベントエリア​
--------------------------------------------

名前

説明

[コマンド](_extension_api_overview_events_commands.md)

コマンドの実行を通知します。

プロパティ​
--------------------------------

名前

説明

[Item\[int\]](/extension/api/NextDesign.Desktop/ICommandParams/properties/Item__int__)

指定したインデックスの値を取得します。

[Item\[string\]](/extension/api/NextDesign.Desktop/ICommandParams/properties/Item__string__)

指定したキーに関連付けられている値を取得します。

メソッド​
-----------------------------

名前

説明

[AddParam](_extension_api_NextDesign.Desktop_ICommandParams_methods_AddParam.md)

パラメータに値オブジェクトを追加します。

[AddParamWithName](_extension_api_NextDesign.Desktop_ICommandParams_methods_AddParamWithName.md)

パラメータに名前付きの値を追加します。

[GetByIndex](_extension_api_NextDesign.Desktop_ICommandParams_methods_GetByIndex.md)

指定パラメータの値をインデックスで取得します。

[GetByName](_extension_api_NextDesign.Desktop_ICommandParams_methods_GetByName.md)

指定パラメータの値をパラメータ名で取得します。

[ToArray](_extension_api_NextDesign.Desktop_ICommandParams_methods_ToArray.md)

パラメータの内容をオブジェクト配列に変換します。

[ToCollection](_extension_api_NextDesign.Desktop_ICommandParams_methods_ToCollection.md)

パラメータの内容をオブジェクトコレクションに変換します。

注釈​
-----------------------

コマンドパラメータの各要素の値は、次のようなインデックス指定またはパラメータ名指定でも取得できます。

    // パラメータの値をインデックス指定で取得object param0 = commandParams[0];// パラメータの値をパラメータ名指定で取得object param1 = commandParams["param1"];