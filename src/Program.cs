using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;

namespace TeyvatGuide
{
    internal static class Program
    {
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        public static string Resource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }
        public static void ApplyTheme(Application app)
        {
            app.Resources = (ResourceDictionary)XamlReader.Parse(Resource("Theme.xaml"));
        }
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        public static void UseDarkCaption(Window window)
        {
            window.SourceInitialized += (s,e) => {
                var handle = new WindowInteropHelper(window).Handle;
                int dark = 1;
                if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
            };
        }
        [STAThread]
        private static void Main()
        {
            bool first;
            using (var instance = new Mutex(true, @"Local\TeyvatGuide.Desktop.1", out first))
            {
                if (!first) { MessageBox.Show("提瓦特随行已打开，可从任务栏切回控制台。", "提瓦特随行"); return; }
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                ApplyTheme(app);
                var storage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TeyvatGuide");
                app.Run(new MainWindow(storage));
            }
        }
    }
}
