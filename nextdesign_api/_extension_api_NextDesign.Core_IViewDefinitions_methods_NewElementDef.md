IViewDefinitions.NewElementDef メソッド
===================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したモデルクラスのエディタ要素定義を生成します。

引数​
-----------------------

名前

型

説明

editor

IEditorDef

エディタ定義

name

string

エディタ要素定義名

modelClass

IClass

モデルのメタクラス

type

string

エディタ要素の種類  
\- "SimpleShape" : シンプルシェイプ  
\- "CompartmentShape" : コンパートメントシェイプ  
\- "Port" : ポートシェイプ  
\- "ConnectorShape" : コネクタシェイプ  
\- "TextBox" : テキストボックス  
\- "CheckBox" : チェックボックス  
\- "ComboBox" : コンボボックス  
\- "List" : リスト  
\- "Grid" : グリッド  
\- "ModelReferenceControl" : モデル参照コントロール

path

string

対応するメタクラスのパス文字列  
フィールドに対応しない要素の場合はnullを指定します。  
既定値は null です。

parent

IElementDef

親定義要素  
エディタ定義直下の要素として定義する場合は null を指定します。  
既定値は null です。

戻り値​
--------------------------

*   [IElementDef](_extension_api_NextDesign.Core_IElementDef.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

パラメータが不正な場合  
\- エディタ定義が指定されていない  
\- メタクラスが指定されていない

不正な種類

ExtensionInvalidTypeException

未サポートのエディタ要素種類が指定された場合  
\- typeにサポート外の要素種類が指定された  
\- パラメータの指定がtypeに指定した種類と矛盾する

フィールドが見つからない

ExtensionFieldNotFoundException

パスで指定されたフィールドが見つからなかった場合

定義が重複

ExtensionDuplicationException

ビュー要素定義名が重複する場合