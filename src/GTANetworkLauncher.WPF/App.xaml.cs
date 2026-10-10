using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MahApps.Metro;

namespace GrandTheftMultiplayer.Launcher
{
    public partial class App
    {
	    protected override void OnStartup(StartupEventArgs e)
		{
		    AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
		    DispatcherUnhandledException += OnDispatcherUnhandledException;

		    try
			{
			    // Must match the dictionaries merged in App.xaml.
			    //
			    // ChangeAppStyle replaces the application's resources with the accent
			    // and theme pair named here, so a mismatch does not merely look wrong: it
			    // removes every resource the views reference, and the next StaticResource
			    // lookup throws a XamlParseException before the window ever appears.
			    ThemeManager.AddAccent("Modern", new Uri("pack://application:,,,/Resources/Accents/Modern.xaml"));
			    ThemeManager.AddAppTheme("Modern", new Uri("pack://application:,,,/Resources/Themes/ModernDark.xaml"));

                // now change app style to the custom accent and current theme
                ThemeManager.ChangeAppStyle(Current,
		            ThemeManager.GetAccent("Modern"),
		            ThemeManager.GetAppTheme("Modern"));

                base.OnStartup(e);
		    }
		    catch (Exception ex)
		    {
			    ReportFatal(ex);
			    Shutdown(1);
		    }
		}

		/// <summary>
		/// Reports a failure that would otherwise terminate the process without a word.
		/// </summary>
		/// <remarks>
		/// A malformed resource dictionary or a missing StaticResource kills the
		/// launcher during XAML parsing. WPF surfaces that only as "CLR20r3" in the
		/// Windows event log, naming neither the file nor the line. The released
		/// launcher had no handler at all, so a broken theme was indistinguishable
		/// from "it does not start".
		/// </remarks>
		private static void ReportFatal(Exception exception)
		{
			var root = exception;
			while (root.InnerException != null)
			{
				root = root.InnerException;
			}

			var report = $"{root.GetType().Name}: {root.Message}";

			try
			{
				var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.log");

				File.AppendAllText(
					path,
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

		private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			ReportFatal(e.Exception);

			e.Handled = true;
			Shutdown(1);
		}

		private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			ReportFatal(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
		}
	}
}