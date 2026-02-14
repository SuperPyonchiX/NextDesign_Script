ICommandContext インタフェース
=======================

名前空間: NextDesign.Desktop

説明​
-----------------------

コマンドの実行コンテキストを提供します。

所属エリア​
--------------------------------

名前

説明

[コマンド](_extension_api_overview_commands.md)

コマンドハンドラで受け取ったコマンドにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IContext](_extension_api_NextDesign.Desktop_IContext.md)

エクステンションの実行時には、コンテキストが与えられます。  
エクステンションの実装では、このコンテキストを通して共有変数や実行中のエクステンション設定情報を参照することができます。

プロパティ​
--------------------------------

名前

説明

[Command](_extension_api_NextDesign.Desktop_ICommandContext_properties_Command.md)

現在実行中のコマンド定義

[Global](_extension_api_NextDesign.Desktop_ICommandContext_properties_Global.md)

エクステンション共有コンテキスト

[SenderModel](_extension_api_NextDesign.Desktop_ICommandContext_properties_SenderModel.md)

コマンドがモデルに関連づいている場合はそのモデル