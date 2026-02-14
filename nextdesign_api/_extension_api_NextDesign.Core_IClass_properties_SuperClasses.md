IClass.SuperClasses プロパティ
=========================

名前空間: NextDesign.Core

説明​
-----------------------

このクラスの直接のスーパークラス  
直接のスーパークラスを持たない場合は、空のコレクションを返します。

    IClassCollection SuperClasses { get; }

型​
--------------------

*   IClassCollection

注釈​
-----------------------

**Ver.1.1 での API 仕様変更と移行方法について**

以前の API 仕様では、このプロパティは、スーパークラスが継承するクラスを含む、継承関係にあるすべてのスーパークラスを取得できました。Ver.1.1 以降では、このクラスと直接継承関係にあるスーパークラスのみを取得するように変更されました。  
（以前の動作は、GetAllSuperClasses()メソッドで代替できます）。  
本 API を利用している場合は、Ver.1.1 以降へのバージョンアップと合わせてエクステンション内の該当箇所を変更する必要があります。

次の例を参考に変更してください。

**変更前**

    IClass myClass = ...;foreach (var superClasse in myClass.SuperClasses){    // do something with the superclass...}

**変更後**

    IClass myClass = ...;foreach (var superClasse in myClass.GetAllSuperClasses()){    // do something with the superclass...}