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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ICSharpCode.Decompiler.Metadata
{
	internal sealed class ResolutionException : Exception
	{
		public ResolutionException(IAssemblyReference reference, string? resolvedPath, Exception? innerException)
			: base($"Failed to resolve assembly: '{reference}'{Environment.NewLine}" +
			       $"Resolve result: {resolvedPath ?? "<not found>"}", innerException)
		{
			this.Reference = reference ?? throw new ArgumentNullException(nameof(reference));
		}

		public ResolutionException(string mainModule, string? moduleName, string? resolvedPath,
			Exception? innerException)
			: base($"Failed to resolve module: '{moduleName} of {mainModule}'{Environment.NewLine}" +
			       $"Resolve result: {resolvedPath ?? "<not found>"}", innerException)
		{
			this.MainModuleFullPath = mainModule ?? throw new ArgumentNullException(nameof(mainModule));
			this.ModuleName = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
		}

		public IAssemblyReference? Reference { get; }

		public string? ModuleName { get; }

		public string? MainModuleFullPath { get; }
	}

	public interface IAssemblyResolver
	{
#if !VSADDIN
		PEFile? Resolve(IAssemblyReference reference);
		PEFile? ResolveModule(PEFile? mainModule, string? moduleName);
		Task<PEFile?> ResolveAsync(IAssemblyReference reference);
		Task<PEFile?> ResolveModuleAsync(PEFile? mainModule, string? moduleName);
#endif
	}

	public class AssemblyReferenceClassifier
	{
		/// <summary>
		/// For GAC assembly references, the WholeProjectDecompiler will omit the HintPath in the
		/// generated .csproj file.
		/// </summary>
		public virtual bool IsGacAssembly(IAssemblyReference reference)
		{
			return UniversalAssemblyResolver.GetAssemblyInGac(reference) != null;
		}

		/// <summary>
		/// For .NET Core framework references, the WholeProjectDecompiler will omit the
		/// assembly reference if the runtimePack is already included as an SDK.
		/// </summary>
		public virtual bool IsSharedAssembly(IAssemblyReference reference, [NotNullWhen(true)] out string? runtimePack)
		{
			runtimePack = null;
			return false;
		}
	}

	public interface IAssemblyReference
	{
		string Name { get; }
		string FullName { get; }
		Version? Version { get; }
		string? Culture { get; }
		byte[]? PublicKeyToken { get; }

		bool IsWindowsRuntime { get; }
		bool IsRetargetable { get; }
	}

	public sealed class AssemblyNameReference : IAssemblyReference
	{
		string? fullName;

		public string Name { get; private set; } = string.Empty;

		public string FullName {
			get {
				if (fullName != null)
					return fullName;

				const string sep = ", ";

				var builder = new StringBuilder();
				builder.Append(Name);
				builder.Append(sep);
				builder.Append("Version=");
				builder.Append((Version ?? UniversalAssemblyResolver.ZeroVersion).ToString(fieldCount: 4));
				builder.Append(sep);
				builder.Append("Culture=");
				builder.Append(string.IsNullOrEmpty(Culture) ? "neutral" : Culture);
				builder.Append(sep);
				builder.Append("PublicKeyToken=");

				var pk_token = PublicKeyToken;
				if (pk_token is { Length: > 0 })
				{
					foreach (var t in pk_token)
					{
						builder.Append(t.ToString("x2"));
					}
				}
				else
					builder.Append("null");

				if (IsRetargetable)
				{
					builder.Append(sep);
					builder.Append("Retargetable=Yes");
				}

				return fullName = builder.ToString();
			}
		}

		public Version? Version { get; private set; }

		public string? Culture { get; private set; }

		public byte[]? PublicKeyToken { get; private set; }

		public bool IsWindowsRuntime { get; private set; }

		public bool IsRetargetable { get; private set; }

		public static AssemblyNameReference Parse(string? fullName)
		{
			ArgumentNullException.ThrowIfNull(fullName);
			if (fullName.Length == 0)
				throw new ArgumentException("Name can not be empty");

			var name = new AssemblyNameReference();
			var tokens = fullName.Split(',');
			for (int i = 0; i < tokens.Length; i++)
			{
				var token = tokens[i].Trim();

				if (i == 0)
				{
					name.Name = token;
					continue;
				}

				var parts = token.Split('=');
				if (parts.Length != 2)
					throw new ArgumentException("Malformed name");

				switch (parts[0].ToLowerInvariant())
				{
					case "version":
						name.Version = new Version(parts[1]);
						break;
					case "culture":
						name.Culture = parts[1] == "neutral" ? "" : parts[1];
						break;
					case "publickeytoken":
						var pk_token = parts[1];
						if (pk_token == "null")
							break;

						name.PublicKeyToken = new byte[pk_token.Length / 2];
						for (int j = 0; j < name.PublicKeyToken.Length; j++)
							name.PublicKeyToken[j] = Byte.Parse(pk_token.Substring(j * 2, 2),
								System.Globalization.NumberStyles.HexNumber);

						break;
				}
			}

			return name;
		}

		public override string ToString()
		{
			return FullName;
		}
	}

#if !VSADDIN
	public sealed class AssemblyReference : IAssemblyReference
	{
		static readonly SHA1 sha1 = SHA1.Create();

		readonly System.Reflection.Metadata.AssemblyReference entry;
		string? fullName;

		string? name;

		public AssemblyReference(MetadataReader metadata, AssemblyReferenceHandle handle)
		{
			if (handle.IsNil)
				throw new ArgumentNullException(nameof(handle));
			Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
			Handle = handle;
			entry = metadata.GetAssemblyReference(handle);
		}

		public AssemblyReference(PEFile? module, AssemblyReferenceHandle handle)
		{
			ArgumentNullException.ThrowIfNull(module);
			if (handle.IsNil)
				throw new ArgumentNullException(nameof(handle));
			Metadata = module.Metadata;
			Handle = handle;
			entry = Metadata.GetAssemblyReference(handle);
		}

		public MetadataReader Metadata { get; }
		public AssemblyReferenceHandle Handle { get; }

		public bool IsWindowsRuntime => (entry.Flags & AssemblyFlags.WindowsRuntime) != 0;
		public bool IsRetargetable => (entry.Flags & AssemblyFlags.Retargetable) != 0;

		public string Name {
			get {
				if (name == null)
				{
					try
					{
						name = Metadata.GetString(entry.Name);
					}
					catch (BadImageFormatException)
					{
						name = $"AR:{Handle}";
					}
				}

				return name;
			}
		}

		public string FullName {
			get {
				if (fullName == null)
				{
					try
					{
						fullName = entry.GetFullAssemblyName(Metadata);
					}
					catch (BadImageFormatException)
					{
						fullName = $"fullname(AR:{Handle})";
					}
				}

				return fullName;
			}
		}

		public Version Version => entry.Version;
		public string Culture => Metadata.GetString(entry.Culture);
		byte[]? IAssemblyReference.PublicKeyToken => GetPublicKeyToken();

		public byte[]? GetPublicKeyToken()
		{
			if (entry.PublicKeyOrToken.IsNil)
				return null;
			var bytes = Metadata.GetBlobBytes(entry.PublicKeyOrToken);
			if ((entry.Flags & AssemblyFlags.PublicKey) != 0)
			{
				return sha1.ComputeHash(bytes).Skip(12).ToArray();
			}

			return bytes;
		}

		public override string ToString()
		{
			return FullName;
		}
	}
#endif
}