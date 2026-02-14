IViewDefinitions.NewCustomElementDef メソッド
=========================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したモデルクラスのカスタムエディタ要素定義を生成します。

引数​
-----------------------

名前

型

説明

editor

ICustomEditorDefinition

カスタムエディタ定義  
null は指定できません。

name

string

カスタムエディタ要素定義名  
null、または空文字は指定できません。

modelClass

IClass

モデルのメタクラス  
null は指定できません。

elementTypeId

string

エディタ要素種類識別子  
null、または空文字は指定できません。

path

string

対応するメタクラスのパス文字列  
フィールドに対応しない要素の場合はnullを指定します。  
既定値は null です。

戻り値​
--------------------------

*   [ICustomElementDefinition](_extension_api_NextDesign.Core_ICustomElementDefinition.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

editor に null を指定した場合  
name に null、または空文字列 を指定した場合  
name に 使用できない文字列 を指定した場合  
modelClass に null を指定した場合  
elementTypeId に null、または空文字列 を指定した場合

フィールドが見つからない

ExtensionFieldNotFoundException

パスで指定されたフィールドが見つからない場合

サポート外

ExtensionNotSupportedException

アプリケーションの現在のエディションが対応していない場合