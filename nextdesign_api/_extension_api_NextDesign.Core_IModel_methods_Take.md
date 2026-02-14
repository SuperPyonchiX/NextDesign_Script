IModel.Take メソッド
================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスの指定したフィールドへ指定したモデルを移動します。  
移動対象のモデルの親要素がこのインスタンスとなります。  
なお、移動先フィールドの多重度上限制約が違反しても例外はスローされません。

引数​
-----------------------

名前

型

説明

fieldName

string

フィールド名  
null、または空文字列は指定できません。

target

IModel

移動するモデル  
nullを指定することはできません。

direction

string

移動先における追加方向  
\- "first" : 先頭  
\- "last" : 末尾  
\- "before" : 前  
\- "after" : 後

index

int

追加基準位置  
先頭位置を0とするインデックスを指定します。  
direction の指定が "first"の場合は、インデックスの値に関係なく先頭に移動します。  
direction の指定が"last"の場合は、インデックスの値に関係なく末尾に移動します。  
direction の指定が "before"の場合は、指定された位置に、"after"の場合は、指定された位置の次の位置にインスタンスを移動します。

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

target に null を指定した場合  
fieldName に null、または空文字列 を指定した場合  
direction に既定の文字列以外の文字列が指定された場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定されたフィールドがこのインスタンスのメタクラスで見つからない場合

インデックス範囲不正

ExtensionOutOfRangeException

index に 負数が指定された場合  
または、index に該当フィールドの要素数以上の値が指定された場合

指定フィールド不正

ExtensionInvalidTypeException

指定されたフィールドが所有フィールドでない、または、指定されたフィールドのデータ型が移動するインスタンスのメタクラスと互換しない場合

不正操作

ExtensionInvalidOperationException

自身が削除済みモデル、もしくは一時プロキシの場合  
フィールド名に操作不可のフィールドが指定された場合  
\- プロダクトラインのフィーチャ割り当てフィールド  
\- System.Coreタグが付与されているフィールド  
無効なフィールドが指定された場合

循環参照

ExtensionCircularReferenceException

targetに、このメソッドの呼び出し対象自身またはその祖先を指定した場合

無効なモデルを指定した

ExtensionInvalidModelException

移動するモデルに以下のモデルが指定された場合  
\- 削除されたモデル  
\- 一時プロキシ  
\- プロジェクト  
\- ユニットにおける基点であり、親要素が読み込まれていないモデル