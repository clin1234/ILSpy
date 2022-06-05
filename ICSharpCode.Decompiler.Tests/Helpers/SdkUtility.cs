// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System.IO;

using Microsoft.Win32;

namespace ICSharpCode.Decompiler.Tests.Helpers
{
	internal static class SdkUtility
	{
		static string GetPathFromRegistry(string key, string valueName)
		{
			using RegistryKey installRootKey = Registry.LocalMachine.OpenSubKey(key);
			object o = installRootKey?.GetValue(valueName);
			if (o != null)
			{
				string r = o.ToString();
				if (!string.IsNullOrEmpty(r))
					return r;
			}

			return null;
		}

		static string GetPathFromRegistryX86(string key, string valueName)
		{
			using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
			using RegistryKey installRootKey = baseKey.OpenSubKey(key);
			object o = installRootKey?.GetValue(valueName);
			if (o != null)
			{
				string r = o.ToString();
				if (!string.IsNullOrEmpty(r))
					return r;
			}

			return null;
		}

		/// <summary>
		/// Searches all the .net sdk bin folders and return the path of the
		/// exe from the latest sdk.
		/// </summary>
		/// <param name="exeName">The EXE to search for.</param>
		/// <returns>The path of the executable, or null if the exe is not found.</returns>
		public static string GetSdkPath(string exeName)
		{
			string execPath;
			if (!string.IsNullOrEmpty(WindowsSdk461NetFxTools))
			{
				execPath = Path.Combine(WindowsSdk461NetFxTools, exeName);
				if (File.Exists(execPath))
				{
					return execPath;
				}
			}

			if (!string.IsNullOrEmpty(WindowsSdk80NetFxTools))
			{
				execPath = Path.Combine(WindowsSdk80NetFxTools, exeName);
				if (File.Exists(execPath))
				{
					return execPath;
				}
			}

			if (!string.IsNullOrEmpty(WindowsSdk71InstallRoot))
			{
				execPath = Path.Combine(WindowsSdk71InstallRoot, "bin\\" + exeName);
				if (File.Exists(execPath))
				{
					return execPath;
				}
			}

			if (!string.IsNullOrEmpty(WindowsSdk70InstallRoot))
			{
				execPath = Path.Combine(WindowsSdk70InstallRoot, "bin\\" + exeName);
				if (File.Exists(execPath))
				{
					return execPath;
				}
			}

			if (!string.IsNullOrEmpty(WindowsSdk61InstallRoot))
			{
				execPath = Path.Combine(WindowsSdk61InstallRoot, "bin\\" + exeName);
				if (File.Exists(execPath))
				{
					return execPath;
				}
			}

			if (!string.IsNullOrEmpty(WindowsSdk60aInstallRoot))
			{
				execPath = Path.Combine(WindowsSdk60aInstallRoot, "bin\\" + exeName);
				if (File.Exists(execPath))
				{
					return execPath;
				}
			}

			if (!string.IsNullOrEmpty(WindowsSdk60InstallRoot))
			{
				execPath = Path.Combine(WindowsSdk60InstallRoot, "bin\\" + exeName);
				if (File.Exists(execPath))
				{
					return execPath;
				}
			}

			if (!string.IsNullOrEmpty(NetSdk20InstallRoot))
			{
				execPath = Path.Combine(NetSdk20InstallRoot, "bin\\" + exeName);
				if (File.Exists(execPath))
				{
					return execPath;
				}
			}

			return null;
		}

		#region InstallRoot Properties

		static string netFrameworkInstallRoot;

		static string netSdk20InstallRoot;

		/// <summary>
		/// Location of the .NET 2.0 SDK install root.
		/// </summary>
		public static string NetSdk20InstallRoot {
			get {
				return netSdk20InstallRoot ??=
					GetPathFromRegistry(@"SOFTWARE\Microsoft\.NETFramework", "sdkInstallRootv2.0") ?? string.Empty;
			}
		}

		static string windowsSdk60InstallRoot;

		/// <summary>
		/// Location of the .NET 3.0 SDK (Windows SDK 6.0) install root.
		/// </summary>
		public static string WindowsSdk60InstallRoot {
			get {
				return windowsSdk60InstallRoot ??=
					GetPathFromRegistry(@"SOFTWARE\Microsoft\Microsoft SDKs\Windows\v6.0", "InstallationFolder") ??
					string.Empty;
			}
		}

		static string windowsSdk60aInstallRoot;

		/// <summary>
		/// Location of the Windows SDK Components in Visual Studio 2008 (.NET 3.5; Windows SDK 6.0a).
		/// </summary>
		public static string WindowsSdk60aInstallRoot {
			get {
				return windowsSdk60aInstallRoot ??=
					GetPathFromRegistry(@"SOFTWARE\Microsoft\Microsoft SDKs\Windows\v6.0a", "InstallationFolder") ??
					string.Empty;
			}
		}

		static string windowsSdk61InstallRoot;

		/// <summary>
		/// Location of the .NET 3.5 SDK (Windows SDK 6.1) install root.
		/// </summary>
		public static string WindowsSdk61InstallRoot {
			get {
				return windowsSdk61InstallRoot ??=
					GetPathFromRegistry(@"SOFTWARE\Microsoft\Microsoft SDKs\Windows\v6.1", "InstallationFolder") ??
					string.Empty;
			}
		}

		static string windowsSdk70InstallRoot;

		/// <summary>
		/// Location of the .NET 3.5 SP1 SDK (Windows SDK 7.0) install root.
		/// </summary>
		public static string WindowsSdk70InstallRoot {
			get {
				return windowsSdk70InstallRoot ??=
					GetPathFromRegistry(@"SOFTWARE\Microsoft\Microsoft SDKs\Windows\v7.0", "InstallationFolder") ??
					string.Empty;
			}
		}

		static string windowsSdk71InstallRoot;

		/// <summary>
		/// Location of the .NET 4.0 SDK (Windows SDK 7.1) install root.
		/// </summary>
		public static string WindowsSdk71InstallRoot {
			get {
				return windowsSdk71InstallRoot ??=
					GetPathFromRegistry(@"SOFTWARE\Microsoft\Microsoft SDKs\Windows\v7.1", "InstallationFolder") ??
					string.Empty;
			}
		}

		static string windowsSdk80InstallRoot;

		/// <summary>
		/// Location of the .NET 4.5 SDK (Windows SDK 8.0) install root.
		/// </summary>
		public static string WindowsSdk80NetFxTools {
			get {
				return windowsSdk80InstallRoot ??= GetPathFromRegistryX86(
					@"SOFTWARE\Microsoft\Microsoft SDKs\Windows\v8.0A\WinSDK-NetFx40Tools",
					"InstallationFolder") ?? string.Empty;
			}
		}

		static string WindowsSdk461InstallRoot;

		/// <summary>
		/// Location of the .NET 4.6.1 SDK install root.
		/// </summary>
		public static string WindowsSdk461NetFxTools {
			get {
				return WindowsSdk461InstallRoot ??= GetPathFromRegistryX86(
					@"SOFTWARE\Wow6432Node\Microsoft\Microsoft SDKs\NETFXSDK\4.6.1\WinSDK-NetFx40Tools",
					"InstallationFolder") ?? string.Empty;
			}
		}

		#endregion
	}
}