using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GrandTheftMultiplayer.Launcher.Helpers;
using GrandTheftMultiplayer.Launcher.Models.DllInjectionService;
using GrandTheftMultiplayer.Launcher.Models.Logger;
using GrandTheftMultiplayer.Launcher.Services.Interfaces;

namespace GrandTheftMultiplayer.Launcher.Services.Runtime
{
	/// <summary>
	/// Injects the GT-MP client into the running game process.
	/// </summary>
	public class GameInjectionService : IGameInjectionService
	{
		private const string ClientScriptsFolder = @"bin\scripts";
		private const string ClientDllFileName = "GrandTheftMultiplayer.Client.dll";

		private readonly ILogger _logger;
		private readonly INotificationService _notificationService;

		public GameInjectionService(ILogger logger, INotificationService notificationService)
		{
			_logger = logger;
			_notificationService = notificationService;
		}

		public bool InjectIntoProcess(Process process)
		{
			if (process == null)
			{
				_logger.Write("Injection aborted: no game process supplied.", LogMessageSeverity.Error);
				return false;
			}

			var clientDllPath = GetClientDllPath();

			if (!File.Exists(clientDllPath))
			{
				_logger.Write($"Injection aborted, client dll missing: {clientDllPath}", LogMessageSeverity.Error);
				_notificationService.ShowNotification($"Could not find \"{clientDllPath}\". Please re-install GT-MP.", true);
				return false;
			}

			_logger.Write($"Injecting {clientDllPath} into process {process.Id}.");

			var result = DllInjector.Inject(process, clientDllPath);

			if (result != DllInjectionResult.Success)
				_logger.Write($"Injection failed: {result}", LogMessageSeverity.Error);

			return result == DllInjectionResult.Success;
		}

		/// <summary>
		/// Injects any additional .asi plugins shipped in the client scripts folder.
		/// These are optional, so a failure is logged but does not fail the launch.
		/// </summary>
		public void InjectCustomAsiFiles(Process targetProcess, string gamePath)
		{
			var scriptsFolderPath = GetClientScriptsFolderPath();

			if (!Directory.Exists(scriptsFolderPath))
				return;

			var asiFiles = Directory.GetFiles(scriptsFolderPath, "*.asi", SearchOption.TopDirectoryOnly)
				.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (!asiFiles.Any())
				return;

			foreach (var asiFilePath in asiFiles)
			{
				_logger.Write($"Injecting asi file {asiFilePath} into process {targetProcess.Id}.");

				var result = DllInjector.Inject(targetProcess, asiFilePath);

				if (result != DllInjectionResult.Success)
					_logger.Write($"Failed to inject asi file {asiFilePath}: {result}", LogMessageSeverity.Warning);
			}
		}

		private static string GetClientScriptsFolderPath()
		{
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ClientScriptsFolder);
		}

		private static string GetClientDllPath()
		{
			return Path.Combine(GetClientScriptsFolderPath(), ClientDllFileName);
		}
	}
}