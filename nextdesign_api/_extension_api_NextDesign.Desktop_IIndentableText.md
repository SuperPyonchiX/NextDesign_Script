IIndentableText インタフェース
=======================

名前空間: NextDesign.Desktop

説明​
-----------------------

テキストシーケンスのオブジェクトです。

このインタフェースで定義されたメソッドは、全てメソッドが呼び出されたインスタンス自身を返します。  
これにより、メソッド呼び出しをチェインしてシーケンスを構成することができます。

所属エリア​
--------------------------------

名前

説明

[ユーティリティ](_extension_api_overview_utility.md)

汎用API群です。

メソッド​
-----------------------------

名前

説明

[AppendLine(string,object\[\])](/extension/api/NextDesign.Desktop/IIndentableText/methods/AppendLine-2)

指定された文字列、および改行をこのシーケンスの末尾に追加します。

[AppendLine(string)](_extension_api_NextDesign.Desktop_IIndentableText_methods_AppendLine-1.md)

指定された文字列、および改行をこのシーケンスの末尾に追加します。

[AppendLine1](_extension_api_NextDesign.Desktop_IIndentableText_methods_AppendLine1.md)

指定された文字列、および改行をこのシーケンスの末尾に追加します。

[AppendLine2](_extension_api_NextDesign.Desktop_IIndentableText_methods_AppendLine2.md)

指定された文字列、および改行をこのシーケンスの末尾に追加します。

[AppendLine3](_extension_api_NextDesign.Desktop_IIndentableText_methods_AppendLine3.md)

指定された文字列、および改行をこのシーケンスの末尾に追加します。

[AppendLine4](_extension_api_NextDesign.Desktop_IIndentableText_methods_AppendLine4.md)

指定された文字列、および改行をこのシーケンスの末尾に追加します。

[AppendLine5](_extension_api_NextDesign.Desktop_IIndentableText_methods_AppendLine5.md)

指定された文字列、および改行をこのシーケンスの末尾に追加します。

[Indent](_extension_api_NextDesign.Desktop_IIndentableText_methods_Indent.md)

インデントレベルを増加します。  
  
このメソッドはテキストシーケンスに変更を加えず、インデントレベルを増加するのみです。  
このメソッドによりインデントレベルを増加した場合、AppendLineメソッドの呼び出しにおいて、自動的に行頭にインデントレベル数分のインデント文字列が付与されます。

[Outdent](_extension_api_NextDesign.Desktop_IIndentableText_methods_Outdent.md)

インデントレベルを減少します。  
  
このメソッドはテキストシーケンスに変更を加えず、インデントレベルを減少するのみです。  
このメソッドによりインデントレベルを減少した場合、AppendLineメソッドの呼び出しにおいて、付与されていたインデント文字列が1段階削除されます。

注釈​
-----------------------

例)

    var text = {create IIndetableText instance...};text.AppendLine("{"}.Indent().AppnedLine("first line text.").AppendLine("second line text.").Outdent().AppnedLine("}").ToString();