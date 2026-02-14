IViewDefinitions.NewCustomEditorDef メソッド
========================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したモデルクラスのカスタムエディタ定義を生成します。

引数​
-----------------------

名前

型

説明

name

string

カスタムエディタ定義名  
null、または空文字は指定できません。

modelClass

IClass

モデルのメタクラス  
null は指定できません。

customEditorTypeId

string

カスタムエディタの種類識別子  
null、または空文字は指定できません。

戻り値​
--------------------------

*   [ICustomEditorDefinition](_extension_api_NextDesign.Core_ICustomEditorDefinition.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

name に null、または空文字列 を指定した場合  
name に 使用できない文字列を指定した場合  
modelClass に null を指定した場合  
customEditorTypeId に null、または空文字列 を指定した場合

サポート外

ExtensionNotSupportedException

アプリケーションの現在のエディションが対応していない場合