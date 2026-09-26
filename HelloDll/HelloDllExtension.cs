using System;
using System.Reflection;
using NextDesign.Core;
using NextDesign.Desktop;
using NextDesign.Extension;

namespace HelloDll
{
    // エントリポイントの DLL には IExtension を実装したクラスが 1 つだけ要る
    public class HelloDllExtension : IExtension
    {
        private DateTime activatedAt;

        public void Activate(IContext context)
        {
            activatedAt = DateTime.Now;
        }

        public void Deactivate(IContext context)
        {
        }

        // manifest.json の execFunc と名前を一致させる
        public void SayHello(ICommandContext context, ICommandParams commandParams)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var message = string.Join(Environment.NewLine,
                "DLL 形式で動いています。",
                "",
                "DLL: " + assembly.Location,
                "版: " + assembly.GetName().Version,
                "Activate: " + activatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                "プロジェクト: " + (context.App.Workspace.CurrentProject?.Name ?? "（未オープン）"));
            context.App.Window.UI.ShowInformationDialog(message, "HelloDll");
        }
    }
}
