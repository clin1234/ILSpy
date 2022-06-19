﻿// Copyright (c) 2018 Siegfried Pammer
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ICSharpCode.Decompiler.Metadata
{
	public enum TargetRuntime
	{
		Unknown,
		Net_1_0,
		Net_1_1,
		Net_2_0,
		Net_4_0
	}

	public enum TargetFrameworkIdentifier
	{
		NETFramework,
		NETCoreApp,
		NETStandard,
		Silverlight,
		NET
	}

	enum DecompilerRuntime
	{
		NETFramework,
		NETCoreApp,
		Mono
	}

	/// <summary>
	/// Used to resolve assemblies referenced by an assembly.
	/// </summary>
	public class UniversalAssemblyResolver : AssemblyReferenceClassifier, IAssemblyResolver
	{
		static readonly List<string> gac_paths = GetGacPaths();
		static readonly DecompilerRuntime decompilerRuntime;
		readonly List<string?> directories = new();

		readonly Lazy<DotNetCorePathFinder> dotNetCorePathFinder;
		readonly string? mainAssemblyFileName;
		readonly MetadataReaderOptions metadataOptions;
		readonly string runtimePack;
		readonly PEStreamOptions streamOptions;

		readonly string targetFramework;
		readonly TargetFrameworkIdentifier targetFrameworkIdentifier;
		readonly Version targetFrameworkVersion;
		readonly bool throwOnError;

		static UniversalAssemblyResolver()
		{
			// TODO : test whether this works with Mono on *Windows*, not sure if we'll
			// ever need this...
			if (Type.GetType("Mono.Runtime") != null)
				decompilerRuntime = DecompilerRuntime.Mono;
			else if (typeof(object).Assembly.GetName().Name == "System.Private.CoreLib")
				decompilerRuntime = DecompilerRuntime.NETCoreApp;
			else if (Environment.OSVersion.Platform == PlatformID.Unix)
				decompilerRuntime = DecompilerRuntime.Mono;
		}

		/// <summary>
		/// Creates a new instance of the <see cref="UniversalAssemblyResolver"/>.
		/// </summary>
		/// <param name="mainAssemblyFileName">
		/// The full path to the "main assembly" (i.e., the assembly being decompiled). This is used to
		/// resolve assemblies that are located next the main assembly. If no full path is used, the resolver
		/// falls back to using <see cref="Environment.CurrentDirectory"/>.
		/// </param>
		/// <param name="throwOnError">
		/// If <see langword="true"/> an <see cref="ResolutionException"/> is thrown, in case the
		/// assembly reference cannot be resolved.
		/// </param>
		/// <param name="targetFramework">
		/// The target framework name as used by <see cref="System.Runtime.Versioning.TargetFrameworkAttribute"/>.
		/// That is, "{framework},Version={version}": currently it supports ".NETCoreApp", ".NETStandard" and
		/// "Silverlight", if the string doesn't match any of these, the resolver falls back to ".NET Framework",
		/// which is "classic" .NET &lt;= 4.8.
		/// </param>
		/// <param name="runtimePack">
		/// Identifier of the runtime pack this assembly was compiled for.
		/// If omitted, falling back to "Microsoft.NETCore.App" and this is ignored in case of classic .NET</param>
		/// <param name="streamOptions">Options used for the <see cref="PEReader"/>.</param>
		/// <param name="metadataOptions">Options used for the <see cref="MetadataReader"/>.</param>
		public UniversalAssemblyResolver(string? mainAssemblyFileName, bool throwOnError, string? targetFramework,
			string? runtimePack = null, PEStreamOptions streamOptions = PEStreamOptions.Default,
			MetadataReaderOptions metadataOptions = MetadataReaderOptions.Default)
		{
			this.mainAssemblyFileName = mainAssemblyFileName;
			this.throwOnError = throwOnError;
			this.streamOptions = streamOptions;
			this.metadataOptions = metadataOptions;
			this.targetFramework = targetFramework ?? string.Empty;
			this.runtimePack = runtimePack ?? "Microsoft.NETCore.App";
			(targetFrameworkIdentifier, targetFrameworkVersion) = ParseTargetFramework(this.targetFramework);
			this.dotNetCorePathFinder = new Lazy<DotNetCorePathFinder>(InitDotNetCorePathFinder);
			if (mainAssemblyFileName != null)
			{
				string? baseDirectory = Path.GetDirectoryName(mainAssemblyFileName);
				if (string.IsNullOrWhiteSpace(baseDirectory))
					baseDirectory = Environment.CurrentDirectory;
				AddSearchDirectory(baseDirectory);
			}
		}

		public void AddSearchDirectory(string? directory)
		{
			directories.Add(directory);
			if (dotNetCorePathFinder.IsValueCreated)
			{
				dotNetCorePathFinder.Value.AddSearchDirectory(directory);
			}
		}

		public void RemoveSearchDirectory(string? directory)
		{
			directories.Remove(directory);
			if (dotNetCorePathFinder.IsValueCreated)
			{
				dotNetCorePathFinder.Value.RemoveSearchDirectory(directory);
			}
		}

		public string?[] GetSearchDirectories()
		{
			return directories.ToArray();
		}

		internal static (TargetFrameworkIdentifier, Version) ParseTargetFramework(string targetFramework)
		{
			if (string.IsNullOrEmpty(targetFramework))
				return (TargetFrameworkIdentifier.NETFramework, ZeroVersion);
			string[] tokens = targetFramework.Split(',');

			TargetFrameworkIdentifier identifier = tokens[0].Trim().ToUpperInvariant() switch {
				".NETCOREAPP" => TargetFrameworkIdentifier.NETCoreApp,
				".NETSTANDARD" => TargetFrameworkIdentifier.NETStandard,
				"SILVERLIGHT" => TargetFrameworkIdentifier.Silverlight,
				_ => TargetFrameworkIdentifier.NETFramework
			};

			Version? version = null;

			for (int i = 1; i < tokens.Length; i++)
			{
				var pair = tokens[i].Trim().Split('=');

				if (pair.Length != 2)
					continue;

				switch (pair[0].Trim().ToUpperInvariant())
				{
					case "VERSION":
						var versionString = pair[1].TrimStart('v', ' ', '\t');
						if (identifier is TargetFrameworkIdentifier.NETCoreApp or TargetFrameworkIdentifier.NETStandard)
						{
							if (versionString.Length == 3)
								versionString += ".0";
						}

						if (!Version.TryParse(versionString, out version))
							version = null;
						// .NET 5 or greater still use ".NETCOREAPP" as TargetFrameworkAttribute value...
						if (version?.Major >= 5 && identifier == TargetFrameworkIdentifier.NETCoreApp)
							identifier = TargetFrameworkIdentifier.NET;
						break;
				}
			}

			return (identifier, version ?? ZeroVersion);
		}

		public override bool IsSharedAssembly(IAssemblyReference reference, [NotNullWhen(true)] out string? runtimePack)
		{
			return dotNetCorePathFinder.Value.TryResolveDotNetCoreShared(reference, out runtimePack) != null;
		}

		public string? FindAssemblyFile(IAssemblyReference name)
		{
			if (name.IsWindowsRuntime)
			{
				return FindWindowsMetadataFile(name);
			}

			string? file;
			switch (targetFrameworkIdentifier)
			{
				case TargetFrameworkIdentifier.NET:
				case TargetFrameworkIdentifier.NETCoreApp:
				case TargetFrameworkIdentifier.NETStandard:
					if (IsZeroOrAllOnes(targetFrameworkVersion))
						goto default;
					file = dotNetCorePathFinder.Value.TryResolveDotNetCore(name);
					if (file != null)
						return file;
					goto default;
				case TargetFrameworkIdentifier.Silverlight:
					if (IsZeroOrAllOnes(targetFrameworkVersion))
						goto default;
					file = ResolveSilverlight(name, targetFrameworkVersion);
					if (file != null)
						return file;
					goto default;
				default:
					return ResolveInternal(name);
			}
		}

		DotNetCorePathFinder InitDotNetCorePathFinder()
		{
			DotNetCorePathFinder dotNetCorePathFinder = mainAssemblyFileName == null
				? new DotNetCorePathFinder(targetFrameworkIdentifier,
					targetFrameworkVersion,
					runtimePack)
				: new DotNetCorePathFinder(mainAssemblyFileName,
					targetFramework,
					runtimePack,
					targetFrameworkIdentifier,
					targetFrameworkVersion);
			foreach (var directory in directories)
			{
				dotNetCorePathFinder.AddSearchDirectory(directory);
			}

			return dotNetCorePathFinder;
		}

		string? FindWindowsMetadataFile(IAssemblyReference name)
		{
			// Finding Windows Metadata (winmd) is currently only supported on Windows.
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
				return null;

			// TODO : Find a way to detect the base directory for the required Windows SDK.
			string? basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
				"Windows Kits", "10", "References");

			if (!Directory.Exists(basePath))
				return FindWindowsMetadataInSystemDirectory(name);

			// TODO : Find a way to detect the required Windows SDK version.
			var di = new DirectoryInfo(basePath);
			basePath = null;
			foreach (var versionFolder in di.EnumerateDirectories())
			{
				basePath = versionFolder.FullName;
			}

			if (basePath == null)
				return FindWindowsMetadataInSystemDirectory(name);

			basePath = Path.Combine(basePath, name.Name);

			if (!Directory.Exists(basePath))
				return FindWindowsMetadataInSystemDirectory(name);

			basePath = Path.Combine(basePath, FindClosestVersionDirectory(basePath, name.Version));

			if (!Directory.Exists(basePath))
				return FindWindowsMetadataInSystemDirectory(name);

			string file = Path.Combine(basePath, name.Name + ".winmd");

			if (!File.Exists(file))
				return FindWindowsMetadataInSystemDirectory(name);

			return file;
		}

		string? FindWindowsMetadataInSystemDirectory(IAssemblyReference name)
		{
			string file = Path.Combine(Environment.SystemDirectory, "WinMetadata", name.Name + ".winmd");
			if (File.Exists(file))
				return file;
			return null;
		}

		/// <summary>
		/// This only works on Windows
		/// </summary>
		string? ResolveSilverlight(IAssemblyReference name, Version? version)
		{
			string[] targetFrameworkSearchPaths = {
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
					"Microsoft Silverlight"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
					"Microsoft Silverlight")
			};

			foreach (var baseDirectory in targetFrameworkSearchPaths)
			{
				if (!Directory.Exists(baseDirectory))
					continue;
				var versionDirectory = Path.Combine(baseDirectory, FindClosestVersionDirectory(baseDirectory, version));
				var file = SearchDirectory(name, versionDirectory);
				if (file != null)
					return file;
			}

			return null;
		}

		string FindClosestVersionDirectory(string basePath, Version? version)
		{
			string? path = null;
			foreach (var folder in new DirectoryInfo(basePath).GetDirectories()
				         .Select(static d => DotNetCorePathFinder.ConvertToVersion(d.Name))
				         .Where(static v => v.Item1 != null)
				         .OrderByDescending(static v => v.Item1).Where(folder => path == null || version == null || folder.Item1 >= version))
			{
				path = folder.Item2;
			}

			return path ?? version?.ToString() ?? ".";
		}

		string? ResolveInternal(IAssemblyReference name)
		{
			if (name == null) throw new ArgumentNullException(nameof(name));

			var assembly = SearchDirectory(name, directories);
			if (assembly != null)
				return assembly;

			var framework_dir = Path.GetDirectoryName(typeof(object).Module.FullyQualifiedName)!;
			var framework_dirs = decompilerRuntime == DecompilerRuntime.Mono
				? new[] { framework_dir, Path.Combine(framework_dir, "Facades") }
				: new[] { framework_dir };

			if (IsSpecialVersionOrRetargetable(name))
			{
				assembly = SearchDirectory(name, framework_dirs);
				if (assembly != null)
					return assembly;
			}

			if (name.Name == "mscorlib")
			{
				assembly = GetCorlib(name);
				if (assembly != null)
					return assembly;
			}

			assembly = GetAssemblyInGac(name);
			if (assembly != null)
				return assembly;

			// when decompiling assemblies that target frameworks prior to 4.0, we can fall back to the 4.0 assemblies in case the target framework is not installed.
			// but when looking for Microsoft.Build.Framework, Version=15.0.0.0 we should not use the version 4.0 assembly here so that the LoadedAssembly logic can instead fall back to version 15.1.0.0
			if (name.Version <= new Version(4, 0, 0, 0))
			{
				assembly = SearchDirectory(name, framework_dirs);
				if (assembly != null)
					return assembly;
			}

			if (throwOnError)
				throw new ResolutionException(name, null, null);
			return null;
		}

#if !VSADDIN
		public PEFile? Resolve(IAssemblyReference name)
		{
			var file = FindAssemblyFile(name);
			return CreatePEFileFromFileName(file, ex => new ResolutionException(name, file, ex));
		}

		public PEFile? ResolveModule(PEFile? mainModule, string moduleName)
		{
			string? baseDirectory = Path.GetDirectoryName(mainModule?.FileName);
			if (baseDirectory == null)
				return null;
			string moduleFileName = Path.Combine(baseDirectory, moduleName);
			return CreatePEFileFromFileName(moduleFileName,
				ex => new ResolutionException(mainModule.FileName, moduleName, moduleFileName, ex));
		}

		private PEFile? CreatePEFileFromFileName(string? fileName, Func<Exception?, Exception> makeException)
		{
			if (fileName == null)
			{
				if (throwOnError)
					throw makeException(null);
				return null;
			}

			try
			{
				FileStream stream = new(fileName, FileMode.Open, FileAccess.Read);
				return new PEFile(fileName, stream, streamOptions, metadataOptions);
			}
			catch (BadImageFormatException ex)
			{
				if (throwOnError)
					throw makeException(ex);
			}
			catch (IOException ex)
			{
				if (throwOnError)
					throw makeException(ex);
			}

			return null;
		}

		public Task<PEFile?> ResolveAsync(IAssemblyReference name)
		{
			return Task.Run(() => Resolve(name));
		}

		public Task<PEFile?> ResolveModuleAsync(PEFile? mainModule, string? moduleName)
		{
			return Task.Run(() => ResolveModule(mainModule, moduleName));
		}
#endif

		#region .NET / mono GAC handling

		string? SearchDirectory(IAssemblyReference name, IEnumerable<string?> directories)
		{
			foreach (var directory in directories)
			{
				if (directory == null)
					continue;
				string? file = SearchDirectory(name, directory);
				if (file != null)
					return file;
			}

			return null;
		}

		static bool IsSpecialVersionOrRetargetable(IAssemblyReference reference)
		{
			return IsZeroOrAllOnes(reference.Version) || reference.IsRetargetable;
		}

		string? SearchDirectory(IAssemblyReference name, string directory)
		{
			var extensions = name.IsWindowsRuntime ? new[] { ".winmd", ".dll" } : new[] { ".dll", ".exe" };
			foreach (var extension in extensions)
			{
				string file = Path.Combine(directory, name.Name + extension);
				if (!File.Exists(file))
					continue;
				try
				{
					return file;
				}
				catch (BadImageFormatException)
				{
				}
			}

			return null;
		}

		static bool IsZeroOrAllOnes(Version? version)
		{
			return version == null
			       || (version.Major == 0 && version.Minor == 0 && version.Build == 0 && version.Revision == 0)
			       || (version.Major == 65535 && version.Minor == 65535 && version.Build == 65535 &&
			           version.Revision == 65535);
		}

		internal static readonly Version ZeroVersion = new(0, 0, 0, 0);

		string? GetCorlib(IAssemblyReference reference)
		{
			var version = reference.Version;
			var corlib = typeof(object).Assembly.GetName();

			if (decompilerRuntime != DecompilerRuntime.NETCoreApp)
			{
				if (corlib.Version == version || IsSpecialVersionOrRetargetable(reference))
					return typeof(object).Module.FullyQualifiedName;
			}

			if (reference.PublicKeyToken == null)
				return null;
			if (version == null)
				return null;

			string? path = decompilerRuntime == DecompilerRuntime.Mono
				? GetMonoMscorlibBasePath(version)
				: GetMscorlibBasePath(version,
					reference.PublicKeyToken.ToHexString(8));

			if (path == null)
				return null;

			var file = Path.Combine(path, "mscorlib.dll");
			if (File.Exists(file))
				return file;

			return null;
		}

		string? GetMscorlibBasePath(Version version, string publicKeyToken)
		{
			string? GetSubFolderForVersion()
			{
				switch (version.Major)
				{
					case 1:
						if (version.MajorRevision == 3300)
							return "v1.0.3705";
						return "v1.1.4322";
					case 2:
						return "v2.0.50727";
					case 4:
						return "v4.0.30319";
					default:
						if (throwOnError)
							throw new NotSupportedException("Version not supported: " + version);
						return null;
				}
			}

			if (publicKeyToken == "969db8053d3322ac")
			{
				string programFiles = Environment.Is64BitOperatingSystem
					? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
					: Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
				string cfPath = $@"Microsoft.NET\SDK\CompactFramework\v{version.Major}.{version.Minor}\WindowsCE\";
				string cfBasePath = Path.Combine(programFiles, cfPath);
				if (Directory.Exists(cfBasePath))
					return cfBasePath;
			}
			else
			{
				string rootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
					"Microsoft.NET");
				string[] frameworkPaths = {
					Path.Combine(rootPath, "Framework"),
					Path.Combine(rootPath, "Framework64")
				};

				string? folder = GetSubFolderForVersion();

				if (folder != null)
				{
					foreach (var path in frameworkPaths)
					{
						var basePath = Path.Combine(path, folder);
						if (Directory.Exists(basePath))
							return basePath;
					}
				}
			}

			if (throwOnError)
				throw new NotSupportedException("Version not supported: " + version);
			return null;
		}

		string? GetMonoMscorlibBasePath(Version version)
		{
			var path = Directory.GetParent(typeof(object).Module.FullyQualifiedName)!.Parent!.FullName;
			switch (version.Major)
			{
				case 1:
					path = Path.Combine(path, "1.0");
					break;
				case 2:
					path = Path.Combine(path, version.MajorRevision == 5 ? "2.1" : "2.0");
					break;
				case 4:
					path = Path.Combine(path, "4.0");
					break;
				default:
				{
					if (throwOnError)
						throw new NotSupportedException("Version not supported: " + version);
					return null;
				}
			}

			return path;
		}

		public static List<string> GetGacPaths()
		{
			if (decompilerRuntime == DecompilerRuntime.Mono)
				return GetDefaultMonoGacPaths();

			var paths = new List<string>(2);
			var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
			if (windir == "")
				return paths;

			paths.Add(Path.Combine(windir, "assembly"));
			paths.Add(Path.Combine(windir, "Microsoft.NET", "assembly"));
			return paths;
		}

		static List<string> GetDefaultMonoGacPaths()
		{
			var paths = new List<string>(1);
			var gac = GetCurrentMonoGac();
			paths.Add(gac);

			var gac_paths_env = Environment.GetEnvironmentVariable("MONO_GAC_PREFIX");
			if (string.IsNullOrEmpty(gac_paths_env))
				return paths;

			var prefixes = gac_paths_env.Split(Path.PathSeparator);
			foreach (var prefix in prefixes)
			{
				if (string.IsNullOrEmpty(prefix))
					continue;

				var gac_path = Path.Combine(Path.Combine(Path.Combine(prefix, "lib"), "mono"), "gac");
				if (Directory.Exists(gac_path) && !paths.Contains(gac_path))
					paths.Add(gac_path);
			}

			return paths;
		}

		static string GetCurrentMonoGac()
		{
			return Path.Combine(
				Directory.GetParent(
					Path.GetDirectoryName(typeof(object).Module.FullyQualifiedName)!)!.FullName,
				"gac");
		}

		public static string? GetAssemblyInGac(IAssemblyReference reference)
		{
			if (reference.PublicKeyToken == null || reference.PublicKeyToken.Length == 0)
				return null;

			if (decompilerRuntime == DecompilerRuntime.Mono)
				return GetAssemblyInMonoGac(reference);

			return GetAssemblyInNetGac(reference);
		}

		static string? GetAssemblyInMonoGac(IAssemblyReference reference)
		{
			foreach (var gac_path in gac_paths)
			{
				var file = GetAssemblyFile(reference, string.Empty, gac_path);
				if (File.Exists(file))
					return file;
			}

			return null;
		}

		static string? GetAssemblyInNetGac(IAssemblyReference reference)
		{
			var gacs = new[] { "GAC_MSIL", "GAC_32", "GAC_64", "GAC" };
			var prefixes = new[] { string.Empty, "v4.0_" };

			for (int i = 0; i < gac_paths.Count; i++)
			{
				foreach (var t in gacs)
				{
					var gac = Path.Combine(gac_paths[i], t);
					var file = GetAssemblyFile(reference, prefixes[i], gac);
					if (File.Exists(file))
						return file;
				}
			}

			return null;
		}

		static string GetAssemblyFile(IAssemblyReference reference, string prefix, string gac)
		{
			var gac_folder = new StringBuilder()
				.Append(prefix)
				.Append(reference.Version);
			gac_folder.Append("__");
			for (int i = 0; i < reference.PublicKeyToken!.Length; i++)
				gac_folder.Append(reference.PublicKeyToken[i].ToString("x2"));
			return Path.Combine(gac, reference.Name, gac_folder.ToString(), reference.Name + ".dll");
		}

		/// <summary>
		/// Gets the names of all assemblies in the GAC.
		/// </summary>
		public static IEnumerable<AssemblyNameReference> EnumerateGac()
		{
			var gacs = new[] { "GAC_MSIL", "GAC_32", "GAC_64", "GAC" };
			foreach (var path in GetGacPaths())
			{
				foreach (var gac in gacs)
				{
					string rootPath = Path.Combine(path, gac);
					if (!Directory.Exists(rootPath))
						continue;
					foreach (var item in new DirectoryInfo(rootPath).EnumerateFiles("*.dll",
						         SearchOption.AllDirectories))
					{
						string[]? name = Path.GetDirectoryName(item.FullName)?[(rootPath.Length + 1)..]
							?.Split(new[] {
									"\\"
								},
								StringSplitOptions.RemoveEmptyEntries);
						if (name?.Length != 2)
							continue;
						var match = Regex.Match(name[1],
							$"(v4.0_)?(?<version>[^_]+)_(?<culture>[^_]+)?_(?<publicKey>[^_]+)");
						if (!match.Success)
							continue;
						string culture = match.Groups["culture"].Value;
						if (string.IsNullOrEmpty(culture))
							culture = "neutral";
						yield return AssemblyNameReference.Parse(name[0] + ", Version=" +
						                                         match.Groups["version"].Value + ", Culture=" +
						                                         culture + ", PublicKeyToken=" +
						                                         match.Groups["publicKey"].Value);
					}
				}
			}
		}

		#endregion
	}
}