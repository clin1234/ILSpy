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
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.Metadata
{
	/// <summary>
	/// PEFile is the main class the decompiler uses to represent a metadata assembly/module.
	/// Every file on disk can be loaded into a standalone PEFile instance.
	/// 
	/// A PEFile can be combined with its referenced assemblies/modules to form a type system,
	/// in that case the <see cref="MetadataModule"/> class is used instead.
	/// </summary>
	/// <remarks>
	/// In addition to wrapping a <c>System.Reflection.Metadata.PEReader</c>, this class
	/// contains a few decompiler-specific caches to allow efficiently constructing a type
	/// system from multiple PEFiles. This allows the caches to be shared across multiple
	/// decompiled type systems.
	/// </remarks>
	[DebuggerDisplay("{" + nameof(FileName) + "}")]
	public sealed class PEFile : IDisposable, IModuleReference
	{
		ImmutableArray<AssemblyReference> assemblyReferences;

		string? fullName;

		MethodSemanticsLookup? methodSemanticsLookup;

		string? name;

		Dictionary<FullTypeName, ExportedTypeHandle>? typeForwarderLookup;

		Dictionary<TopLevelTypeName, TypeDefinitionHandle>? typeLookup;

		public PEFile(string fileName, PEStreamOptions streamOptions = PEStreamOptions.Default,
			MetadataReaderOptions metadataOptions = MetadataReaderOptions.Default)
			: this(fileName, new PEReader(new FileStream(fileName, FileMode.Open, FileAccess.Read), streamOptions),
				metadataOptions)
		{
		}

		public PEFile(string fileName, Stream stream, PEStreamOptions streamOptions = PEStreamOptions.Default,
			MetadataReaderOptions metadataOptions = MetadataReaderOptions.Default)
			: this(fileName, new PEReader(stream, streamOptions), metadataOptions)
		{
		}

		public PEFile(string fileName, PEReader reader,
			MetadataReaderOptions metadataOptions = MetadataReaderOptions.Default)
		{
			this.FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
			this.Reader = reader ?? throw new ArgumentNullException(nameof(reader));
			if (!reader.HasMetadata)
				throw new PEFileNotSupportedException("PE file does not contain any managed metadata.");
			this.Metadata = reader.GetMetadataReader(metadataOptions);
		}

		public string FileName { get; }
		public PEReader Reader { get; }
		public MetadataReader Metadata { get; }

		public bool IsAssembly => Metadata.IsAssembly;

		public string Name {
			get {
				var value = LazyInit.VolatileRead(ref name);
				if (value == null)
				{
					value = Metadata.IsAssembly
						? Metadata.GetString(Metadata.GetAssemblyDefinition().Name)
						: Metadata.GetString(Metadata.GetModuleDefinition().Name);
					value = LazyInit.GetOrSet(ref name, value);
				}

				return value;
			}
		}

		public string FullName {
			get {
				var value = LazyInit.VolatileRead(ref fullName);
				if (value == null)
				{
					value = Metadata.IsAssembly ? Metadata.GetFullAssemblyName() : Name;
					value = LazyInit.GetOrSet(ref fullName, value);
				}

				return value;
			}
		}

		public ImmutableArray<AssemblyReference> AssemblyReferences {
			get {
				var value = assemblyReferences;
				if (value.IsDefault)
				{
					value = Metadata.AssemblyReferences.Select(r => new AssemblyReference(this, r)).ToImmutableArray();
					assemblyReferences = value;
				}

				return value;
			}
		}

		public ImmutableArray<Resource> Resources => GetResources().ToImmutableArray();

		internal MethodSemanticsLookup MethodSemanticsLookup {
			get {
				var r = LazyInit.VolatileRead(ref methodSemanticsLookup);
				if (r != null)
					return r;
				return LazyInit.GetOrSet(ref methodSemanticsLookup, new MethodSemanticsLookup(Metadata));
			}
		}

		public void Dispose()
		{
			Reader.Dispose();
		}

		IModule IModuleReference.Resolve(ITypeResolveContext context)
		{
			return new MetadataModule(context.Compilation, this, TypeSystemOptions.Default);
		}

		public TargetRuntime GetRuntime()
		{
			string version = Metadata.MetadataVersion;
			if (version is not { Length: > 1 })
				return TargetRuntime.Unknown;
			switch (version[1])
			{
				case '1':
					if (version.Length <= 3)
						return TargetRuntime.Unknown;
					if (version[3] == 1)
						return TargetRuntime.Net_1_0;
					return TargetRuntime.Net_1_1;
				case '2':
					return TargetRuntime.Net_2_0;
				case '4':
					return TargetRuntime.Net_4_0;
				default:
					return TargetRuntime.Unknown;
			}
		}

		IEnumerable<Resource> GetResources()
		{
			return Metadata.ManifestResources.Select(h => new MetadataResource(this, h));
		}

		/// <summary>
		/// Finds the top-level-type with the specified name.
		/// </summary>
		public TypeDefinitionHandle GetTypeDefinition(TopLevelTypeName typeName)
		{
			var lookup = LazyInit.VolatileRead(ref typeLookup);
			if (lookup == null)
			{
				lookup = new Dictionary<TopLevelTypeName, TypeDefinitionHandle>();
				foreach (var handle in Metadata.TypeDefinitions)
				{
					var td = Metadata.GetTypeDefinition(handle);
					if (!td.GetDeclaringType().IsNil)
					{
						continue; // nested type
					}

					var nsHandle = td.Namespace;
					string ns = nsHandle.IsNil ? string.Empty : Metadata.GetString(nsHandle);
					string name =
						ReflectionHelper.SplitTypeParameterCountFromReflectionName(Metadata.GetString(td.Name),
							out int typeParameterCount);
					lookup[new TopLevelTypeName(ns, name, typeParameterCount)] = handle;
				}

				lookup = LazyInit.GetOrSet(ref typeLookup, lookup);
			}

			if (lookup.TryGetValue(typeName, out var resultHandle))
				return resultHandle;
			return default;
		}

		/// <summary>
		/// Finds the type forwarder with the specified name.
		/// </summary>
		public ExportedTypeHandle GetTypeForwarder(FullTypeName typeName)
		{
			var lookup = LazyInit.VolatileRead(ref typeForwarderLookup);
			if (lookup == null)
			{
				lookup = new Dictionary<FullTypeName, ExportedTypeHandle>();
				foreach (var handle in Metadata.ExportedTypes)
				{
					var td = Metadata.GetExportedType(handle);
					lookup[td.GetFullTypeName(Metadata)] = handle;
				}

				lookup = LazyInit.GetOrSet(ref typeForwarderLookup, lookup);
			}

			if (lookup.TryGetValue(typeName, out var resultHandle))
				return resultHandle;
			return default;
		}

		public IModuleReference WithOptions(TypeSystemOptions options)
		{
			return new PEFileWithOptions(this, options);
		}

		private sealed class PEFileWithOptions : IModuleReference
		{
			readonly TypeSystemOptions options;
			readonly PEFile peFile;

			public PEFileWithOptions(PEFile peFile, TypeSystemOptions options)
			{
				this.peFile = peFile;
				this.options = options;
			}

			IModule IModuleReference.Resolve(ITypeResolveContext context)
			{
				return new MetadataModule(context.Compilation, peFile, options);
			}
		}
	}
}