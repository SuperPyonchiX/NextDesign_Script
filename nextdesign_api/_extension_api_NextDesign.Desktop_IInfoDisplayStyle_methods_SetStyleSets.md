IInfoDisplayStyle.SetStyleSets メソッド
===================================

名前空間: NextDesign.Desktop

説明​
-----------------------

ビュー上での表示スタイルを設定します。

このメソッドで指定したスタイルは、ダイアグラム上でのみ有効となります。  
また、アプリケーションのビュー上で適用中のスタイルを更新しても、ビューの表示にはリアルタイムで反映されません（次回適用時に反映されます）。

引数​
-----------------------

名前

型

説明

viewName

string

スタイル適用対象のビュー定義名  
nullまたは空文字列が指定された場合はすべてのビュー定義を適用対象とします。

styleValues

IDictionary<string, string>

表示スタイル名とその値の組み合わせ  
指定方法の詳細は注釈を参照してください。

戻り値​
--------------------------

*   void

注釈​
-----------------------

引数 "styleValues" では、次のように表示スタイル名とその値を組み合わせてスタイルを指定できます。

次のKey-Valueの組み合わせ辞書オブジェクト。

*   Key: ビュー上で適用可能な表示スタイル名  
    　　　　 ビューが対応しないスタイル名を指定されている場合、その設定は無視されます。
*   Value:該当スタイル名において有効な値

表示スタイル名と有効な値の例。

*   "ForeColor" ... "Blue" などのColor名
*   "BackColor" ... "Red" などのColor名
*   "BorderColor" ... "Red" などのColor名
*   "BorderThickness" ... "1.5"などのDouble値を表現する文字列
*   "LineStyle" ... "Solid","Dot","Dash",DashDot","DashDotDot"

有効なColor名の詳細は次のURLを参照。  
[https://docs.microsoft.com/ja-jp/dotnet/api/system.windows.media.colors?view=netframework-4.6.2](https://docs.microsoft.com/ja-jp/dotnet/api/system.windows.media.colors?view=netframework-4.6.2)  
Color名の代わりに "#FF0000FF" などの ARGB 値も指定可能。