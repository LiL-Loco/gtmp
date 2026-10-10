using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GrandTheftMultiplayer.Launcher.Models.DllInjectionService;

namespace GrandTheftMultiplayer.Launcher.Helpers
{
	/// <summary>
	/// Injects a DLL into a running process by allocating memory in the target,
	/// writing the DLL path and starting a remote thread on LoadLibraryA.
	/// </summary>
	/// <remarks>
	/// This requires the launcher and the target process to have the same architecture.
	/// The launcher is built as AnyCPU and the game is 64 bit, so both run as 64 bit;
	/// that is what makes the local LoadLibraryA address a valid remote entry point.
	/// </remarks>
	public class DllInjector
	{
		private const uint ProcessCreateThread = 0x0002;
		private const uint ProcessVmOperation = 0x0008;
		private const uint ProcessVmRead = 0x0010;
		private const uint ProcessVmWrite = 0x0020;
		private const uint ProcessQueryInformation = 0x0400;

		private const uint MemCommit = 0x1000;
		private const uint MemReserve = 0x2000;
		private const uint MemRelease = 0x8000;
		private const uint PageExecuteReadWrite = 0x40;

		private const uint ThreadWaitTimeout = 15000;
		private const uint WaitObject0 = 0x00000000;
		private const uint WaitTimeout = 0x00000102;

		private static readonly IntPtr IntptrZero = (IntPtr)0;

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CloseHandle(IntPtr hObject);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
		private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
		private static extern IntPtr GetModuleHandle(string lpModuleName);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint dwFreeType);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] buffer,
			IntPtr nSize, out IntPtr lpNumberOfBytesWritten);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttribute, IntPtr dwStackSize,
			IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

		public static DllInjectionResult Inject(Process process, string dllPath)
		{
			if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
				return DllInjectionResult.DllNotFound;

			if (process == null)
				return DllInjectionResult.GameProcessNotFound;

			try
			{
				process.Refresh();
			}
			catch (Exception)
			{
				// The process object may refer to an exited process.
				return DllInjectionResult.GameProcessNotFound;
			}

			if (process.HasExited)
				return DllInjectionResult.GameProcessNotFound;

			return PerformInjection((uint)process.Id, dllPath)
				? DllInjectionResult.Success
				: DllInjectionResult.InjectionFailed;
		}

		private static bool PerformInjection(uint targetProcess, string dllPath)
		{
			var hndProc = OpenProcess(
				ProcessCreateThread | ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation,
				false,
				targetProcess);

			if (hndProc == IntptrZero)
				return false;

			var lpAddress = IntptrZero;

			try
			{
				var lpLlAddress = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
				if (lpLlAddress == IntptrZero)
					return false;

				// One extra byte for the null terminator: LoadLibraryA expects a C string,
				// and relying on the zero filled allocation would be an implicit contract
				// with the memory manager rather than with the API.
				var pathBytes = Encoding.ASCII.GetBytes(dllPath);
				var bufferSize = pathBytes.Length + 1;

				lpAddress = VirtualAllocEx(hndProc, IntptrZero, (IntPtr)bufferSize, MemCommit | MemReserve, PageExecuteReadWrite);
				if (lpAddress == IntptrZero)
					return false;

				var pathBuffer = new byte[bufferSize];
				Buffer.BlockCopy(pathBytes, 0, pathBuffer, 0, pathBytes.Length);
				pathBuffer[pathBytes.Length] = 0;

				IntPtr bytesWritten;
				if (!WriteProcessMemory(hndProc, lpAddress, pathBuffer, (IntPtr)bufferSize, out bytesWritten))
					return false;

				var lpThreadId = IntptrZero;
				var remoteThread = CreateRemoteThread(hndProc, IntptrZero, IntptrZero, lpLlAddress, lpAddress, 0, out lpThreadId);
				if (remoteThread == IntptrZero)
					return false;

				try
				{
					// LoadLibraryA is asynchronous, so success can only be judged by the
					// module handle it returns as its exit code.
					var waitResult = WaitForSingleObject(remoteThread, ThreadWaitTimeout);
					if (waitResult != WaitObject0)
						return false;

					uint exitCode;
					if (!GetExitCodeThread(remoteThread, out exitCode))
						return false;

					return exitCode != 0;
				}
				finally
				{
					CloseHandle(remoteThread);
				}
			}
			finally
			{
				if (lpAddress != IntptrZero)
					VirtualFreeEx(hndProc, lpAddress, IntPtr.Zero, MemRelease);

				CloseHandle(hndProc);
			}
		}
	}
}