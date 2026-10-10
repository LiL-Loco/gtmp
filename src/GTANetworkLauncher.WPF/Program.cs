using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace GrandTheftMultiplayer.Launcher
{
    public class Program
    {
        [STAThread]
        public static void Main()
        {
            // ReSharper disable once UnusedVariable
            var mutex = new Mutex(true, "GrandTheftMultiplayerLauncher", out bool createdNew);

            if (!createdNew)
            {
                ShowWindow();
                Application.Current.Shutdown();
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;

            // App.Main loads App.xaml and its merged resource dictionaries before
            // OnStartup runs, so a malformed theme dies here - outside every handler
            // the application installs. WPF reports it only as "CLR20r3" with no
            // file and no line, which is indistinguishable from "it does not start".
            try
            {
                App.Main();
            }
            catch (Exception ex)
            {
                var root = ex;
                while (root.InnerException != null) root = root.InnerException;

                var report = $"{root.GetType().Name}: {root.Message}";

                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.log"),
                        $"[{DateTime.Now:HH:mm:ss}] {report}{Environment.NewLine}{root.StackTrace}{Environment.NewLine}{Environment.NewLine}");
                }
                catch (Exception)
                {
                    // Reporting the failure must not become a second failure.
                }

                try
                {
                    MessageBox.Show(report, "Launcher failed to start", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception)
                {
                }
            }
        }

        private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
        {
            var executingAssembly = Assembly.GetExecutingAssembly();
            var assemblyName = new AssemblyName(args.Name);

            var path = assemblyName.Name + ".dll";
            if (assemblyName.CultureInfo.Equals(CultureInfo.InvariantCulture) == false)
            {
                path = $@"{assemblyName.CultureInfo}\{path}";
            }

            using (var stream = executingAssembly.GetManifestResourceStream(path))
            {
                if (stream == null)
                    return null;

                var assemblyRawBytes = new byte[stream.Length];
                stream.Read(assemblyRawBytes, 0, assemblyRawBytes.Length);
                return Assembly.Load(assemblyRawBytes);
            }
        }

        [DllImport("User32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static void ShowWindow()
        {
            foreach (var process in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
            {
                if (process.MainWindowHandle == IntPtr.Zero) continue;

                ShowWindow(process.MainWindowHandle, 1);
                SetForegroundWindow(process.MainWindowHandle);
                return;
            }
        }
    }
}
