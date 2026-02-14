ICustomFinder.GetSelectedModels メソッド
====================================

名前空間: NextDesign.Desktop.CustomUI

説明​
-----------------------

関連付け対象のモデル一覧を取得します。

引数​
-----------------------

名前

型

説明

model

IModel

関連付け対象のモデル。

field

string

関連付け対象フィールド名。

parentWindow

Window

ファインダ呼び出しを行ったビューのウィンドウ。  
ダイアログ表示する際には、このウィンドウを親ウィンドウとします。

selectedModels

IEnumerable<IModel>

現在指定されたモデルの指定されたフィールドで関連付けられているモデルの一覧。  
新しい関連付けを追加することのみが許容される場合は null となります。

戻り値​
--------------------------

*   IEnumerable<IModel>