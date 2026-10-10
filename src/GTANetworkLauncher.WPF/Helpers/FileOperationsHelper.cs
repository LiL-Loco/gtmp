using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GrandTheftMultiplayer.Launcher.Services;
using Microsoft.Win32;

namespace GrandTheftMultiplayer.Launcher.Helpers
{
	public class FileOperationsHelper
	{
		public static long CalculateTotalFileSizes(IEnumerable<string> filePaths)
		{
			long totalSize = 0;
			filePaths.ToList().ForEach(filePath => totalSize += new FileInfo(filePath).Length);
			return totalSize;
		}

		public static string BytesToReadableString(long bytes)
		{
			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			var order = 0;
			while (bytes >= 1024 && order < sizes.Length - 1)
			{
				order++;
				bytes = bytes / 1024;
			}

			return $"{bytes:0.##} {sizes[order]}";
		}

		/// <summary>
		/// Copies the given relative paths from source to destination.
		/// </summary>
		/// <returns>
		/// The relative paths that could not be copied. Callers must treat a non empty
		/// result as a failed operation instead of reporting success.
		/// </returns>
		public static IReadOnlyCollection<string> CopyFiles(string sourcePath, string destinationPath, IEnumerable<string> relativeFilePaths)
		{
			var failedFilePaths = new List<string>();

			foreach (var relativeFilePath in relativeFilePaths)
			{
				if (!CopyFile(Path.Combine(sourcePath, relativeFilePath), Path.Combine(destinationPath, relativeFilePath)))
					failedFilePaths.Add(relativeFilePath);
			}

			return failedFilePaths;
		}

		/// <summary>
		/// Copies a single file. Returns false instead of swallowing the error, because
		/// the callers use the result to decide whether a backup is usable.
		/// </summary>
		public static bool CopyFile(string absoluteSourcePath, string absoluteDestinationPath)
		{
			if (!File.Exists(absoluteSourcePath)) return false;

			try
			{
				if (File.Exists(absoluteDestinationPath))
					File.Delete(absoluteDestinationPath);

				EnsureFileDirectory(absoluteDestinationPath);
				File.Copy(absoluteSourcePath, absoluteDestinationPath);

				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public static void EnsureDirectory(string directoryPath)
		{
			Directory.CreateDirectory(directoryPath);
		}

		public static void EnsureFileDirectory(string filePath)
		{
			var directoryPath = Path.GetDirectoryName(filePath);
			if (string.IsNullOrWhiteSpace(directoryPath)) return;

			EnsureDirectory(directoryPath);
		}

		public static string BrowseForGta5Exe()
		{
			var openFileDialog = new OpenFileDialog
			{
				Filter = $"{Constants.GTA5ExecutableFileName}|{Constants.GTA5ExecutableFileName}",
				FileName = Constants.GTA5ExecutableFileName,
				DefaultExt = ".exe",
				CheckFileExists = true,
				CheckPathExists = true,
				InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
			};

			return openFileDialog.ShowDialog() != true ? null : openFileDialog.FileName;
		}

		public static string BrowseForGtMpServerExe()
		{
			var openFileDialog = new OpenFileDialog
			{
				Filter = $"{Constants.ServerExeFileName}|{Constants.ServerExeFileName}",
				FileName = Constants.ServerExeFileName,
				DefaultExt = ".exe",
				CheckFileExists = true,
				CheckPathExists = true,
				InitialDirectory = Environment.CurrentDirectory
			};

			return openFileDialog.ShowDialog() != true ? null : openFileDialog.FileName;
		}

		public static void MoveFileIgnoreReadonly(string sourceFilePath, string destinationPath)
		{
			MakeFileNotReadonly(sourceFilePath);

			if (File.Exists(destinationPath))
				MakeFileNotReadonly(destinationPath);

			File.Copy(sourceFilePath, destinationPath);
			File.SetAttributes(sourceFilePath, FileAttributes.Normal);
			File.Delete(sourceFilePath);
		}

		private static void MakeFileNotReadonly(string filePath)
		{
			new FileInfo(filePath).IsReadOnly = false;
		}
	}
}