IModel.RelateAt メソッド
====================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスの指定したフィールドで、追加位置を指定して指定したモデルを関連づけて、追加した関連インスタンスを返します。

引数​
-----------------------

名前

型

説明

fieldName

string

フィールド名  
null、または空文字列は指定できません。

opposite

IModel

関連づけするモデル  
null は指定できません。

direction

string

関連づけフィールドにおける追加方向  
\- "first" : 先頭  
\- "last" : 末尾  
\- "before" : 前  
\- "after" : 後

index

int

追加基準位置  
先頭位置を0とするインデックスを指定します。  
direction の指定が "first"の場合は、インデックスの値に関係なく先頭で関連づけます。  
direction の指定が"last"の場合は、インデックスの値に関係なく末尾で関連づけます。  
direction の指定が "before"の場合は、指定された位置に、"after"の場合は、指定された位置の次の位置でインスタンスを関連づけます。

戻り値​
--------------------------

*   [IRelationship](_extension_api_NextDesign.Core_IRelationship.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

opposite に null を指定した場合  
fieldName に null、または空文字列 を指定した場合  
direction に既定の文字列以外の文字列が指定された場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定されたフィールドが移動先のインスタンスのメタクラスで見つからない場合

インデックス範囲不正

ExtensionOutOfRangeException

index に 負数が指定された場合  
または、index に該当フィールドの要素数以上の値が指定された場合

不正操作

ExtensionInvalidOperationException

自身が削除済みモデル、一時プロキシの場合  
フィールド名に操作不可のフィールドが指定された場合  
\- プロダクトラインのフィーチャ割り当てフィールド  
\- System.Coreタグが付与されているフィールド  
\- 所有フィールド  
無効なフィールドが指定された場合

制約違反

ExtensionIllegalFieldAccessException

関連づけするモデルが指定したフィールドのデータ型と互換しない場合  
関連づけにより、フィールドの多重度制約に違反する場合  
関連づけにより、フィールドのパス制約に違反する場合

無効なモデルを指定した

ExtensionInvalidModelException

関連づけするモデルに削除されたモデル、一時プロキシが指定された場合