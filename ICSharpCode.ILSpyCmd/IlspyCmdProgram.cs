using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Solution;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.ILSpyX.PdbProvider;

using McMaster.Extensions.CommandLineUtils;

namespace ICSharpCode.ILSpyCmd
{
	[Command(Name = "ilspycmd",
		Description = "dotnet tool for decompiling .NET assemblies and generating portable PDBs",
		ExtendedHelpText = @"
Remarks:
  -o is valid with every option and required when using -p.

Examples:
    Decompile assembly to console out.
        ilspycmd sample.dll

    Decompile assembly to destination directory (single C# file).
        ilspycmd -o c:\decompiled sample.dll

    Decompile assembly to destination directory, create a project file, one source file per type.
        ilspycmd -p -o c:\decompiled sample.dll

    Decompile assembly to destination directory, create a project file, one source file per type, 
    into nicely nested directories.
        ilspycmd --nested-directories -p -o c:\decompiled sample.dll
")]
	[HelpOption("-h|--help")]
	[ProjectOptionRequiresOutputDirectoryValidation]
	[VersionOptionFromMember("-v|--version", Description = "Show version of ICSharpCode.Decompiler used.",
		MemberName = nameof(DecompilerVersion))]
	sealed class ILSpyCmdProgram
	{
		[FilesExist]
		[Required]
		[Argument(0, "Assembly file name(s)",
			"The list of assemblies that is being decompiled. This argument is mandatory.")]
		private string[] InputAssemblyNames { get; }

		[Option("-o|--outputdir <directory>",
			"The output directory, if omitted decompiler output is written to standard out.",
			CommandOptionType.SingleValue)]
		public string OutputDirectory { get; }

		[Option("-p|--project", "Decompile assembly as compilable project. This requires the output directory option.",
			CommandOptionType.NoValue)]
		public bool CreateCompilableProjectFlag { get; }

		[Option("-t|--type <type-name>", "The fully qualified name of the type to decompile.",
			CommandOptionType.SingleValue)]
		private string TypeName { get; }

		[Option("-il|--ilcode", "Show IL code.", CommandOptionType.NoValue)]
		private bool ShowILCodeFlag { get; }

		[Option("--il-sequence-points", "Show IL with sequence points. Implies -il.", CommandOptionType.NoValue)]
		private bool ShowILSequencePointsFlag { get; }

		[Option("-genpdb|--generate-pdb", "Generate PDB.", CommandOptionType.NoValue)]
		private bool CreateDebugInfoFlag { get; }

		[FileExistsOrNull]
		[Option("-usepdb|--use-varnames-from-pdb", "Use variable names from PDB.", CommandOptionType.SingleOrNoValue)]
		private (bool IsSet, string Value) InputPDBFile { get; }

		[Option("-l|--list <entity-type(s)>",
			"Lists all entities of the specified type(s). Valid types: c(lass), i(nterface), s(truct), d(elegate), e(num)",
			CommandOptionType.MultipleValue)]
		private IEnumerable<string> EntityTypes { get; } = Array.Empty<string>();

		public string DecompilerVersion => "ilspycmd: " + typeof(ILSpyCmdProgram).Assembly.GetName().Version +
		                                   Environment.NewLine
		                                   + "ICSharpCode.Decompiler: " +
		                                   typeof(FullTypeName).Assembly.GetName().Version;

		[Option("-lv|--languageversion <version>", "C# Language version: CSharp1, CSharp2, CSharp3, " +
			"CSharp4, CSharp5, CSharp6, CSharp7, CSharp7_1, CSharp7_2, CSharp7_3, CSharp8_0, CSharp9_0, " +
			"CSharp10_0, Preview or Latest", CommandOptionType.SingleValue)]
		public LanguageVersion LanguageVersion { get; } = LanguageVersion.Latest;

		[DirectoryExists]
		[Option("-r|--referencepath <path>",
			"Path to a directory containing dependencies of the assembly that is being decompiled.",
			CommandOptionType.MultipleValue)]
		private IEnumerable<string> ReferencePaths { get; } = Array.Empty<string>();

		[Option("--no-dead-code", "Remove dead code.", CommandOptionType.NoValue)]
		private bool RemoveDeadCode { get; }

		[Option("--no-dead-stores", "Remove dead stores.", CommandOptionType.NoValue)]
		private bool RemoveDeadStores { get; }

		[Option("-d|--dump-package",
			"Dump package assembiles into a folder. This requires the output directory option.",
			CommandOptionType.NoValue)]
		private bool DumpPackageFlag { get; }

		[Option("--nested-directories", "Use nested directories for namespaces.", CommandOptionType.NoValue)]
		private bool NestedDirectories { get; }

		public static int Main(string[] args) => CommandLineApplication.Execute<ILSpyCmdProgram>(args);

		private int? OnExecute(CommandLineApplication app)
		{
			TextWriter output = Console.Out;
			string? outputDirectory = ResolveOutputDirectory(OutputDirectory);

			if (outputDirectory != null)
			{
				Directory.CreateDirectory(outputDirectory);
			}

			try
			{
				if (CreateCompilableProjectFlag)
				{
					if (InputAssemblyNames.Length == 1)
					{
						string projectFileName = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(InputAssemblyNames[0]) + ".csproj");
						DecompileAsProject(InputAssemblyNames[0], projectFileName);
						return 0;
					}

					var projects = new List<ProjectItem>();
					foreach (var file in InputAssemblyNames)
					{
						string projectFileName = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(file), Path.GetFileNameWithoutExtension(file) + ".csproj");
						Directory.CreateDirectory(Path.GetDirectoryName(projectFileName));
						ProjectId projectId = DecompileAsProject(file, projectFileName);
						projects.Add(new ProjectItem(projectFileName, projectId.PlatformName, projectId.Guid,
							projectId.TypeGuid));
					}
					SolutionCreator.WriteSolutionFile(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(outputDirectory) + ".sln"), projects);
					return 0;
				}
				else
				{
					foreach (var file in InputAssemblyNames)
					{
						int? result = PerformPerFileAction(file);
						if (result != 0)
							return result;
					}

					return 0;
				}
			}
			catch (Exception ex)
			{
				app.Error.WriteLine(ex.ToString());
				return ProgramExitCodes.EX_SOFTWARE;
			}
			finally
			{
				output.Close();
			}

			int? PerformPerFileAction(string fileName)
			{
				if (EntityTypes.Any())
				{
					string[] values = EntityTypes.SelectMany(static v => v.Split(',', ';')).ToArray();
					HashSet<TypeKind> kinds = TypesParser.ParseSelection(values);
					if (outputDirectory != null)
					{
						string outputName = Path.GetFileNameWithoutExtension(fileName);
						output = File.CreateText(Path.Combine(outputDirectory, outputName) + ".list.txt");
					}

					return ListContent(fileName, output, kinds);
				}

				if (ShowILCodeFlag || ShowILSequencePointsFlag)
				{
					if (outputDirectory != null)
					{
						string outputName = Path.GetFileNameWithoutExtension(fileName);
						output = File.CreateText(Path.Combine(outputDirectory, outputName) + ".il");
					}

					return ShowIL(fileName, output);
				}

				if (CreateDebugInfoFlag)
				{
					string? pdbFileName = null;
					if (outputDirectory != null)
					{
						string outputName = Path.GetFileNameWithoutExtension(fileName);
						pdbFileName = Path.Combine(outputDirectory, outputName) + ".pdb";
					}
					else
					{
						pdbFileName = Path.ChangeExtension(fileName, ".pdb");
					}

					return GeneratePdbForAssembly(fileName, pdbFileName, app);
				}

				if (DumpPackageFlag)
				{
					return DumpPackageAssemblies(fileName, outputDirectory, app);
				}
				else
				{
					if (outputDirectory != null)
					{
						string outputName = Path.GetFileNameWithoutExtension(fileName);
						output = File.CreateText(Path.Combine(outputDirectory,
							(string.IsNullOrEmpty(TypeName) ? outputName : TypeName) + ".decompiled.cs"));
					}

					return Decompile(fileName, output, TypeName);
				}
			}
		}

		private static string? ResolveOutputDirectory(string outputDirectory)
		{
			// path is not set
			if (string.IsNullOrWhiteSpace(outputDirectory))
				return null;
			// resolve relative path, backreferences ('.' and '..') and other
			// platform-specific path elements, like '~'.
			return Path.GetFullPath(outputDirectory);
		}

		DecompilerSettings GetSettings(PEFile module)
		{
			return new DecompilerSettings(LanguageVersion) {
				ThrowOnAssemblyResolveErrors = false,
				RemoveDeadCode = RemoveDeadCode,
				RemoveDeadStores = RemoveDeadStores,
				UseSdkStyleProjectFormat = WholeProjectDecompiler.CanUseSdkStyleProjectFormat(module),
				UseNestedDirectoriesForNamespaces = NestedDirectories,
			};
		}

		CSharpDecompiler GetDecompiler(string assemblyFileName)
		{
			var module = new PEFile(assemblyFileName);
			var resolver =
				new UniversalAssemblyResolver(assemblyFileName, false, module.Metadata.DetectTargetFrameworkId());
			foreach (var path in ReferencePaths)
			{
				resolver.AddSearchDirectory(path);
			}

			return new CSharpDecompiler(assemblyFileName, resolver, GetSettings(module)) {
				DebugInfoProvider = TryLoadPDB(module)
			};
		}

		int ListContent(string assemblyFileName, TextWriter output, ISet<TypeKind> kinds)
		{
			CSharpDecompiler decompiler = GetDecompiler(assemblyFileName);

			foreach (ITypeDefinition type in decompiler.TypeSystem.MainModule.TypeDefinitions.Where(type =>
				         kinds.Contains(type.Kind)))
			{
				output.WriteLine($"{type.Kind} {type.FullName}");
			}

			return 0;
		}

		int ShowIL(string assemblyFileName, TextWriter output)
		{
			var module = new PEFile(assemblyFileName);
			output.WriteLine($"// IL code: {module.Name}");
			var disassembler = new ReflectionDisassembler(new PlainTextOutput(output), CancellationToken.None) {
				DebugInfo = TryLoadPDB(module),
				ShowSequencePoints = ShowILSequencePointsFlag,
			};
			disassembler.WriteModuleContents(module);
			return 0;
		}

		ProjectId DecompileAsProject(string assemblyFileName, string projectFileName)
		{
			var module = new PEFile(assemblyFileName);
			var resolver =
				new UniversalAssemblyResolver(assemblyFileName, false, module.Metadata.DetectTargetFrameworkId());
			foreach (var path in ReferencePaths)
			{
				resolver.AddSearchDirectory(path);
			}

			var decompiler = new WholeProjectDecompiler(GetSettings(module), resolver, resolver, TryLoadPDB(module));
			using StreamWriter projectFileWriter = new StreamWriter(File.OpenWrite(projectFileName));
			return decompiler.DecompileProject(module, Path.GetDirectoryName(projectFileName), projectFileWriter);
		}

		int Decompile(string assemblyFileName, TextWriter output, string? typeName = null)
		{
			CSharpDecompiler decompiler = GetDecompiler(assemblyFileName);

			if (typeName == null)
			{
				output.Write(decompiler.DecompileWholeModuleAsString());
			}
			else
			{
				var name = new FullTypeName(typeName);
				output.Write(decompiler.DecompileTypeAsString(name));
			}

			return 0;
		}

		int GeneratePdbForAssembly(string assemblyFileName, string pdbFileName, CommandLineApplication app)
		{
			var module = new PEFile(assemblyFileName,
				new FileStream(assemblyFileName, FileMode.Open, FileAccess.Read),
				PEStreamOptions.PrefetchEntireImage,
				metadataOptions: MetadataReaderOptions.None);

			if (!PortablePdbWriter.HasCodeViewDebugDirectoryEntry(module))
			{
				app.Error.WriteLine(
					$"Cannot create PDB file for {assemblyFileName}, because it does not contain a PE Debug Directory Entry of type 'CodeView'.");
				return ProgramExitCodes.EX_DATAERR;
			}

			using FileStream stream = new(pdbFileName, FileMode.OpenOrCreate, FileAccess.Write);
			CSharpDecompiler decompiler = GetDecompiler(assemblyFileName);
			PortablePdbWriter.WritePdb(module, decompiler, GetSettings(module), stream);

			return 0;
		}

		static int? DumpPackageAssemblies(string packageFileName, string outputDirectory, CommandLineApplication app)
		{
			using MemoryMappedFile memoryMappedPackage =
				MemoryMappedFile.CreateFromFile(packageFileName, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
			using MemoryMappedViewAccessor packageView =
				memoryMappedPackage.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
			if (!SingleFileBundle.IsBundle(packageView, out long bundleHeaderOffset))
			{
				app.Error.WriteLine(
					$"Cannot dump assembiles for {packageFileName}, because it is not a single file bundle.");
				return ProgramExitCodes.EX_DATAERR;
			}

			SingleFileBundle.Header manifest = SingleFileBundle.ReadManifest(packageView, bundleHeaderOffset);
			foreach (SingleFileBundle.Entry entry in manifest.Entries)
			{
				Stream contents;

				if (entry.CompressedSize == 0)
				{
					contents = new UnmanagedMemoryStream(packageView.SafeMemoryMappedViewHandle, entry.Offset,
						entry.Size);
				}
				else
				{
					Stream compressedStream = new UnmanagedMemoryStream(packageView.SafeMemoryMappedViewHandle,
						entry.Offset, entry.CompressedSize);
					Stream decompressedStream = new MemoryStream((int)entry.Size);
					using (DeflateStream deflateStream =
					       new DeflateStream(compressedStream, CompressionMode.Decompress))
					{
						deflateStream.CopyTo(decompressedStream);
					}

					if (decompressedStream.Length != entry.Size)
					{
						app.Error.WriteLine(
							$"Corrupted single-file entry '${entry.RelativePath}'. Declared decompressed size '${entry.Size}' is not the same as actual decompressed size '${decompressedStream.Length}'.");
						return ProgramExitCodes.EX_DATAERR;
					}

					decompressedStream.Seek(0, SeekOrigin.Begin);
					contents = decompressedStream;
				}

				using FileStream fileStream = File.Create(Path.Combine(outputDirectory, entry.RelativePath));
				contents.CopyTo(fileStream);
			}

			return 0;
		}

		IDebugInfoProvider? TryLoadPDB(PEFile module)
		{
			if (InputPDBFile.IsSet)
			{
				if (InputPDBFile.Value == null)
					return DebugInfoUtils.LoadSymbols(module);
				return DebugInfoUtils.FromFile(module, InputPDBFile.Value);
			}

			return null;
		}
	}
}