IModelInitializationProvider.InitializeFields メソッド
==================================================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

モデルに対する初期化を行います。

性能面のボトルネックとならないように注意して実装してください。  
本メソッドの実装において、さらにフィールドにモデルを追加した場合、追加したモデルに該当する  
プロバイダの登録があれば、その時点でIModelInitializationProvider.InitializeFieldsが実行されます。

プロバイダの実装では、呼び出しが再帰しないように注意してください。  
例えば、コンポジット構造のモデルに対して、プロバイダ内で子要素を作成するように実装した場合  
IModelInitializationProvider.InitializeFieldsの呼び出しが無限ループします。

このメソッドが例外をスローした場合は、新しいモデルの作成自体が失敗したものとして処理全体がロールバックされます。

引数​
-----------------------

名前

型

説明

initializeParams

ModelInitializationParams

モデルのインスタンスの初期化対象を提供するパラメータ

戻り値​
--------------------------

*   void