using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

[assembly: AssemblyTitle("摸鱼电视机")]
[assembly: AssemblyProduct("摸鱼电视机")]
[assembly: AssemblyCompany("MoyuTV")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace MoyuTv
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            CoreWebView2Environment.LoaderDllFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            Uri start = null;
            if (args.Length == 1)
            {
                Uri.TryCreate(args[0], UriKind.Absolute, out start);
                if (start != null && start.Scheme != Uri.UriSchemeHttp && start.Scheme != Uri.UriSchemeHttps)
                    start = null;
            }
            Application.Run(new Television(start));
        }
    }
}
