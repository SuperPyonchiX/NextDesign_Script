IEvent インタフェース
==============

名前空間: NextDesign.Desktop

説明​
-----------------------

イベント定義情報を提供します。  
マニフェストファイルで定義したイベント定義情報を参照することができます。

プロパティ​
--------------------------------

名前

説明

[Area](_extension_api_NextDesign.Desktop_IEvent_properties_Area.md)

イベントエリア名  
\- application：アプリケーション  
\- project：プロジェクト  
\- models：モデル  
\- commands：コマンド  
\- editors：エディタ  
\- pages：ページ  
\- navigators：ナビゲータ  
\- informations：情報ウィンドウ

[EventName](_extension_api_NextDesign.Desktop_IEvent_properties_EventName.md)

イベント名  
マニフェストで定義するイベント名が取得できます。  
先頭は大文字に変換されます。  
  
例：  
OnAfterStart / OnBeforeQuit

[FuncName](_extension_api_NextDesign.Desktop_IEvent_properties_FuncName.md)

ハンドラ関数名  
マニフェストで定義する関数名が取得できます。

注釈​
-----------------------

**イベント定義情報の例**

例えば、マニフェストの拡張ポイント定義に次のようなイベントが定義されている場合、

    {  // ～（省略）～  "extensionPoints": {    "events": {      "projects" : {        "onAfterNew" : "Project_OnAfterNew",        "onAfterOpen" : "Project_OnAfterOpen"      },      "models": [        {           "class" : "*",           "onAfterNew" : "Model_OnNew",           "OnFieldChanged" : "Model_OnFieldChanged",        },        {           "class" : "Actor",           "onAfterNew" : "Model_Actor_OnNew"        }      ]    }  }}

それぞれの対応するイベント発生時には、次のようにイベント定義情報が展開されます。

    // プロジェクトを開いた際のイベントの展開内容IEvent projectOpen;projectOpen.Area = "project";projectOpen.EventName = "OnAfterOpen";　　　　　// イベント名は先頭が大文字となりますprojectOpen.FuncName = "Project_OnAfterOpen";// モデルの新規作成イベントの展開内容IEvent modelNewAll;modelNewAll.Area = "models";modelNewAll.EventName = "OnAfterNew";modelNewAll.FuncName = "Model_OnNew";// Actorの新規作成イベントの展開内容IEvent modelNewActor;modelNewActor.Area = "models";modelNewAll.EventName = "OnAfterNew";modelNewAll.FuncName = "Model_Actor_OnNew";