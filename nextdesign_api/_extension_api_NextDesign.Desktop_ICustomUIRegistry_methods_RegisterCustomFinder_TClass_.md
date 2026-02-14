ICustomUIRegistry.RegisterCustomFinder<TClass> メソッド
===================================================

名前空間: NextDesign.Desktop

説明​
-----------------------

独自拡張したファインダを登録します。

*   TClass : カスタムファインダの型

引数​
-----------------------

名前

型

説明

extensionName

string

エクステンション名  
マニフェストで定義したエクステンション名を指定します。  
null、または空文字列は指定できません。

descriptor

CustomFinderDescriptor

タイプ記述子。  
nullは指定できません。

戻り値​
--------------------------

*   void

例外​
-------------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

extensionName に null、または空文字列 を指定した場合  
descriptor に null を指定した場合

カスタムUIの識別子が重複

ExtensionDuplicationException

カスタムUIの識別子(descriptor.Id)が重複して指定された場合