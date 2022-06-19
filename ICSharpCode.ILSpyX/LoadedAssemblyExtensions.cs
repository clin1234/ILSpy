﻿using System;
using System.IO;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

using Mono.Cecil;

using IAssemblyResolver = ICSharpCode.Decompiler.Metadata.IAssemblyResolver;

namespace ICSharpCode.ILSpyX
{
	public static class LoadedAssemblyExtensions
	{
		/// <summary>
		/// This method creates a Cecil object model from a PEFile. It is intended as helper method for plugins.
		/// Note that this method is expensive and creates high memory pressure!
		/// Note that accessing the Cecil objects created by this method after an assembly has been unloaded by ILSpy
		/// might lead to <see cref="AccessViolationException"/> or similar.
		/// </summary>
		/// <remarks>Use only as last resort if there is something missing in the official ILSpy API.
		/// Consider creating an issue at https://github.com/icsharpcode/ILSpy/issues/new
		/// and discussing the problem with us.</remarks>
		public static unsafe ModuleDefinition CreateCecilObjectModel(this PEFile file)
		{
			if (!file.Reader.IsEntireImageAvailable)
				throw new InvalidOperationException("Need full image to create Cecil object model!");
			var image = file.Reader.GetEntireImage();
			return ModuleDefinition.ReadModule(new UnmanagedMemoryStream(image.Pointer, image.Length));
		}

		public static IAssemblyResolver GetAssemblyResolver(this PEFile file, bool loadOnDemand = true)
		{
			return GetLoadedAssembly(file).GetAssemblyResolver(loadOnDemand);
		}

		internal static IAssemblyResolver GetAssemblyResolver(this PEFile file, AssemblyListSnapshot snapshot,
			bool loadOnDemand = true)
		{
			return GetLoadedAssembly(file).GetAssemblyResolver(snapshot, loadOnDemand);
		}

		public static IDebugInfoProvider? GetDebugInfoOrNull(this PEFile file)
		{
			return GetLoadedAssembly(file).GetDebugInfoOrNull();
		}

		public static ICompilation? GetTypeSystemOrNull(this PEFile file)
		{
			return GetLoadedAssembly(file).GetTypeSystemOrNull();
		}

		public static ICompilation? GetTypeSystemWithDecompilerSettingsOrNull(this PEFile file,
			DecompilerSettings settings)
		{
			return GetLoadedAssembly(file).GetTypeSystemOrNull(DecompilerTypeSystem.GetOptions(settings));
		}

		public static LoadedAssembly GetLoadedAssembly(this PEFile file)
		{
			if (file == null) throw new ArgumentNullException(nameof(file));
			LoadedAssembly? loadedAssembly;
			lock (LoadedAssembly.loadedAssemblies)
			{
				if (!LoadedAssembly.loadedAssemblies.TryGetValue(file, out loadedAssembly))
					throw new ArgumentException("The specified file is not associated with a LoadedAssembly!");
			}

			return loadedAssembly;
		}
	}
}