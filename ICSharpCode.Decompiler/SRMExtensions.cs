using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

using SRM = System.Reflection.Metadata;

namespace ICSharpCode.Decompiler
{
	internal static partial class SRMExtensions
	{
		public static bool HasFlag(this SRM.TypeDefinition typeDefinition, TypeAttributes attribute)
			=> (typeDefinition.Attributes & attribute) == attribute;

		public static bool HasFlag(this SRM.MethodDefinition methodDefinition, MethodAttributes attribute)
			=> (methodDefinition.Attributes & attribute) == attribute;

		public static bool HasFlag(this SRM.FieldDefinition fieldDefinition, FieldAttributes attribute)
			=> (fieldDefinition.Attributes & attribute) == attribute;

		public static bool HasFlag(this SRM.PropertyDefinition propertyDefinition, PropertyAttributes attribute)
			=> (propertyDefinition.Attributes & attribute) == attribute;

		public static bool HasFlag(this SRM.EventDefinition eventDefinition, EventAttributes attribute)
			=> (eventDefinition.Attributes & attribute) == attribute;

		public static bool IsTypeKind(this SRM.HandleKind kind) =>
			kind is SRM.HandleKind.TypeDefinition or SRM.HandleKind.TypeReference or SRM.HandleKind.TypeSpecification;

		public static bool IsMemberKind(this SRM.HandleKind kind) =>
			kind is SRM.HandleKind.MethodDefinition or SRM.HandleKind.PropertyDefinition
				or SRM.HandleKind.FieldDefinition
				or SRM.HandleKind.EventDefinition or SRM.HandleKind.MemberReference
				or SRM.HandleKind.MethodSpecification;

		public static bool IsEntityHandle(this SRM.Handle handle) =>
			handle.IsNil || (byte)handle.Kind < 112;

		public static bool IsValueType(this SRM.TypeDefinitionHandle handle, SRM.MetadataReader reader)
		{
			return reader.GetTypeDefinition(handle).IsValueType(reader);
		}

		public static bool IsValueType(this SRM.TypeDefinition typeDefinition, SRM.MetadataReader reader)
		{
			SRM.EntityHandle baseType = typeDefinition.GetBaseTypeOrNil();
			if (baseType.IsNil)
				return false;
			if (baseType.IsKnownType(reader, KnownTypeCode.Enum))
				return true;
			if (!baseType.IsKnownType(reader, KnownTypeCode.ValueType))
				return false;
			var thisType = typeDefinition.GetFullTypeName(reader);
			return !thisType.IsKnownType(KnownTypeCode.Enum);
		}

		public static bool IsEnum(this SRM.TypeDefinitionHandle handle, SRM.MetadataReader reader)
		{
			return reader.GetTypeDefinition(handle).IsEnum(reader);
		}

		private static bool IsEnum(this SRM.TypeDefinition typeDefinition, SRM.MetadataReader reader)
		{
			SRM.EntityHandle baseType = typeDefinition.GetBaseTypeOrNil();
			return !baseType.IsNil && baseType.IsKnownType(reader, KnownTypeCode.Enum);
		}

		public static bool IsEnum(this SRM.TypeDefinitionHandle handle, SRM.MetadataReader reader,
			out SRM.PrimitiveTypeCode underlyingType)
		{
			return reader.GetTypeDefinition(handle).IsEnum(reader, out underlyingType);
		}

		public static bool IsEnum(this SRM.TypeDefinition typeDefinition, SRM.MetadataReader reader,
			out SRM.PrimitiveTypeCode underlyingType)
		{
			underlyingType = 0;
			SRM.EntityHandle baseType = typeDefinition.GetBaseTypeOrNil();
			if (baseType.IsNil)
				return false;
			if (!baseType.IsKnownType(reader, KnownTypeCode.Enum))
				return false;
			foreach (var handle in typeDefinition.GetFields())
			{
				var field = reader.GetFieldDefinition(handle);
				if ((field.Attributes & FieldAttributes.Static) != 0)
					continue;
				var blob = reader.GetBlobReader(field.Signature);
				if (blob.ReadSignatureHeader().Kind != SRM.SignatureKind.Field)
					return false;
				underlyingType = (SRM.PrimitiveTypeCode)blob.ReadByte();
				return true;
			}

			return false;
		}

		public static bool IsDelegate(this SRM.TypeDefinitionHandle handle, SRM.MetadataReader reader)
		{
			return reader.GetTypeDefinition(handle).IsDelegate(reader);
		}

		public static bool IsDelegate(this SRM.TypeDefinition typeDefinition, SRM.MetadataReader reader)
		{
			var baseType = typeDefinition.GetBaseTypeOrNil();
			return !baseType.IsNil && baseType.IsKnownType(reader, KnownTypeCode.MulticastDelegate);
		}

		public static bool HasBody(this SRM.MethodDefinition methodDefinition)
		{
			const MethodAttributes noBodyAttrs = MethodAttributes.Abstract | MethodAttributes.PinvokeImpl;
			const MethodImplAttributes noBodyImplAttrs = MethodImplAttributes.InternalCall
			                                             | MethodImplAttributes.Native |
			                                             MethodImplAttributes.Unmanaged | MethodImplAttributes.Runtime;
			return (methodDefinition.Attributes & noBodyAttrs) == 0 &&
			       (methodDefinition.ImplAttributes & noBodyImplAttrs) == 0 &&
			       methodDefinition.RelativeVirtualAddress > 0;
		}

		public static int GetCodeSize(this SRM.MethodBodyBlock body)
		{
			ArgumentNullException.ThrowIfNull(body);

			return body.GetILReader().Length;
		}

		public static SRM.MethodDefinitionHandle GetAny(this SRM.PropertyAccessors accessors)
		{
			if (!accessors.Getter.IsNil)
				return accessors.Getter;
			return accessors.Setter;
		}

		public static SRM.MethodDefinitionHandle GetAny(this SRM.EventAccessors accessors)
		{
			if (!accessors.Adder.IsNil)
				return accessors.Adder;
			if (!accessors.Remover.IsNil)
				return accessors.Remover;
			return accessors.Raiser;
		}

		public static SRM.EntityHandle GetGenericType(this in SRM.TypeSpecification ts, SRM.MetadataReader metadata)
		{
			if (ts.Signature.IsNil)
				return default;
			// Do a quick scan using BlobReader
			var signature = metadata.GetBlobReader(ts.Signature);
			// When dealing with FSM implementations, we can safely assume that if it's a type spec,
			// it must be a generic type instance.
			if (signature.ReadByte() != (byte)SRM.SignatureTypeCode.GenericTypeInstance)
				return default;
			// Skip over the rawTypeKind: value type or class
			var rawTypeKind = signature.ReadCompressedInteger();
			if (rawTypeKind is < 17 or > 18)
			{
				return default;
			}

			// Only read the generic type, ignore the type arguments
			return signature.ReadTypeHandle();
		}

		public static SRM.EntityHandle GetDeclaringType(this SRM.EntityHandle entity, SRM.MetadataReader metadata)
		{
			switch (entity.Kind)
			{
				case SRM.HandleKind.TypeDefinition:
					var td = metadata.GetTypeDefinition((SRM.TypeDefinitionHandle)entity);
					return td.GetDeclaringType();
				case SRM.HandleKind.TypeReference:
					var tr = metadata.GetTypeReference((SRM.TypeReferenceHandle)entity);
					return tr.GetDeclaringType();
				case SRM.HandleKind.TypeSpecification:
					var ts = metadata.GetTypeSpecification((SRM.TypeSpecificationHandle)entity);
					return ts.GetGenericType(metadata).GetDeclaringType(metadata);
				case SRM.HandleKind.FieldDefinition:
					var fd = metadata.GetFieldDefinition((SRM.FieldDefinitionHandle)entity);
					return fd.GetDeclaringType();
				case SRM.HandleKind.MethodDefinition:
					var md = metadata.GetMethodDefinition((SRM.MethodDefinitionHandle)entity);
					return md.GetDeclaringType();
				case SRM.HandleKind.MemberReference:
					var mr = metadata.GetMemberReference((SRM.MemberReferenceHandle)entity);
					return mr.Parent;
				case SRM.HandleKind.EventDefinition:
					var ed = metadata.GetEventDefinition((SRM.EventDefinitionHandle)entity);
					return metadata.GetMethodDefinition(ed.GetAccessors().GetAny()).GetDeclaringType();
				case SRM.HandleKind.PropertyDefinition:
					var pd = metadata.GetPropertyDefinition((SRM.PropertyDefinitionHandle)entity);
					return metadata.GetMethodDefinition(pd.GetAccessors().GetAny()).GetDeclaringType();
				case SRM.HandleKind.MethodSpecification:
					var ms = metadata.GetMethodSpecification((SRM.MethodSpecificationHandle)entity);
					return ms.Method.GetDeclaringType(metadata);
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private static SRM.TypeReferenceHandle GetDeclaringType(this in SRM.TypeReference tr)
		{
			return tr.ResolutionScope.Kind switch {
				SRM.HandleKind.TypeReference => (SRM.TypeReferenceHandle)tr.ResolutionScope,
				_ => default
			};
		}

		public static FullTypeName GetFullTypeName(this SRM.EntityHandle handle, SRM.MetadataReader reader)
		{
			if (handle.IsNil)
				throw new ArgumentNullException(nameof(handle));
			return handle.Kind switch {
				SRM.HandleKind.TypeDefinition => ((SRM.TypeDefinitionHandle)handle).GetFullTypeName(reader),
				SRM.HandleKind.TypeReference => ((SRM.TypeReferenceHandle)handle).GetFullTypeName(reader),
				SRM.HandleKind.TypeSpecification => ((SRM.TypeSpecificationHandle)handle).GetFullTypeName(reader),
				_ => throw new ArgumentOutOfRangeException()
			};
		}

		public static bool IsKnownType(this SRM.EntityHandle handle, SRM.MetadataReader reader,
			KnownTypeCode knownType)
		{
			return IsKnownType(handle, reader, KnownTypeReference.Get(knownType).TypeName);
		}

		internal static bool IsKnownType(this SRM.EntityHandle handle, SRM.MetadataReader reader,
			KnownAttribute knownType)
		{
			return IsKnownType(handle, reader, knownType.GetTypeName());
		}

		private static bool IsKnownType(SRM.EntityHandle handle, SRM.MetadataReader reader, TopLevelTypeName knownType)
		{
			if (handle.IsNil)
				return false;
			SRM.StringHandle nameHandle, namespaceHandle;
			switch (handle.Kind)
			{
				case SRM.HandleKind.TypeReference:
					var tr = reader.GetTypeReference((SRM.TypeReferenceHandle)handle);
					// ignore exported and nested types
					if (tr.ResolutionScope.IsNil || tr.ResolutionScope.Kind == SRM.HandleKind.TypeReference)
						return false;
					nameHandle = tr.Name;
					namespaceHandle = tr.Namespace;
					break;
				case SRM.HandleKind.TypeDefinition:
					var td = reader.GetTypeDefinition((SRM.TypeDefinitionHandle)handle);
					if (td.IsNested)
						return false;
					nameHandle = td.Name;
					namespaceHandle = td.Namespace;
					break;
				case SRM.HandleKind.TypeSpecification:
					var ts = reader.GetTypeSpecification((SRM.TypeSpecificationHandle)handle);
					var blob = reader.GetBlobReader(ts.Signature);
					return SignatureIsKnownType(reader, knownType, ref blob);
				default:
					return false;
			}

			if (knownType.TypeParameterCount == 0)
			{
				if (!reader.StringComparer.Equals(nameHandle, knownType.Name))
					return false;
			}
			else
			{
				string name = reader.GetString(nameHandle);
				name = ReflectionHelper.SplitTypeParameterCountFromReflectionName(name, out int typeParameterCount);
				if (typeParameterCount != knownType.TypeParameterCount || name != knownType.Name)
					return false;
			}

			if (namespaceHandle.IsNil)
			{
				return knownType.Namespace.Length == 0;
			}
			else
			{
				return reader.StringComparer.Equals(namespaceHandle, knownType.Namespace);
			}
		}

		private static bool SignatureIsKnownType(SRM.MetadataReader reader, TopLevelTypeName knownType,
			ref SRM.BlobReader blob)
		{
			if (!blob.TryReadCompressedInteger(out int typeCode))
				return false;
			switch (typeCode)
			{
				case 0x1: // ELEMENT_TYPE_VOID
					return knownType.IsKnownType(KnownTypeCode.Void);
				case 0x2: // ELEMENT_TYPE_BOOLEAN 
					return knownType.IsKnownType(KnownTypeCode.Boolean);
				case 0x3: // ELEMENT_TYPE_CHAR 
					return knownType.IsKnownType(KnownTypeCode.Char);
				case 0x4: // ELEMENT_TYPE_I1 
					return knownType.IsKnownType(KnownTypeCode.SByte);
				case 0x5: // ELEMENT_TYPE_U1
					return knownType.IsKnownType(KnownTypeCode.Byte);
				case 0x6: // ELEMENT_TYPE_I2
					return knownType.IsKnownType(KnownTypeCode.Int16);
				case 0x7: // ELEMENT_TYPE_U2
					return knownType.IsKnownType(KnownTypeCode.UInt16);
				case 0x8: // ELEMENT_TYPE_I4
					return knownType.IsKnownType(KnownTypeCode.Int32);
				case 0x9: // ELEMENT_TYPE_U4
					return knownType.IsKnownType(KnownTypeCode.UInt32);
				case 0xA: // ELEMENT_TYPE_I8
					return knownType.IsKnownType(KnownTypeCode.Int64);
				case 0xB: // ELEMENT_TYPE_U8
					return knownType.IsKnownType(KnownTypeCode.UInt64);
				case 0xC: // ELEMENT_TYPE_R4
					return knownType.IsKnownType(KnownTypeCode.Single);
				case 0xD: // ELEMENT_TYPE_R8
					return knownType.IsKnownType(KnownTypeCode.Double);
				case 0xE: // ELEMENT_TYPE_STRING
					return knownType.IsKnownType(KnownTypeCode.String);
				case 0x16: // ELEMENT_TYPE_TYPEDBYREF
					return knownType.IsKnownType(KnownTypeCode.TypedReference);
				case 0x18: // ELEMENT_TYPE_I
					return knownType.IsKnownType(KnownTypeCode.IntPtr);
				case 0x19: // ELEMENT_TYPE_U
					return knownType.IsKnownType(KnownTypeCode.UIntPtr);
				case 0x1C: // ELEMENT_TYPE_OBJECT
					return knownType.IsKnownType(KnownTypeCode.Object);
				case 0xF: // ELEMENT_TYPE_PTR 
				case 0x10: // ELEMENT_TYPE_BYREF 
				case 0x45: // ELEMENT_TYPE_PINNED
				case 0x1D: // ELEMENT_TYPE_SZARRAY
				case 0x1B: // ELEMENT_TYPE_FNPTR 
				case 0x14: // ELEMENT_TYPE_ARRAY 
					return false;
				case 0x1F: // ELEMENT_TYPE_CMOD_REQD 
				case 0x20: // ELEMENT_TYPE_CMOD_OPT 
					// modifier
					blob.ReadTypeHandle(); // skip modifier
					return SignatureIsKnownType(reader, knownType, ref blob);
				case 0x15: // ELEMENT_TYPE_GENERICINST 
					// generic type
					return SignatureIsKnownType(reader, knownType, ref blob);
				case 0x13: // ELEMENT_TYPE_VAR
				case 0x1E: // ELEMENT_TYPE_MVAR 
					// index
					return false;
				case 0x11: // ELEMENT_TYPE_VALUETYPE
				case 0x12: // ELEMENT_TYPE_CLASS
					return IsKnownType(blob.ReadTypeHandle(), reader, knownType);
				default:
					return false;
			}
		}

		private static FullTypeName GetFullTypeName(this SRM.TypeSpecificationHandle handle, SRM.MetadataReader reader)
		{
			if (handle.IsNil)
				throw new ArgumentNullException(nameof(handle));
			var ts = reader.GetTypeSpecification(handle);
			return ts.DecodeSignature(new FullTypeNameSignatureDecoder(reader), default(Unit));
		}

		public static FullTypeName GetFullTypeName(this SRM.TypeReferenceHandle handle, SRM.MetadataReader reader)
		{
			if (handle.IsNil)
				throw new ArgumentNullException(nameof(handle));
			var tr = reader.GetTypeReference(handle);
			string name;
			try
			{
				name = reader.GetString(tr.Name);
			}
			catch (BadImageFormatException)
			{
				name = $"TR{reader.GetToken(handle):x8}";
			}

			name = ReflectionHelper.SplitTypeParameterCountFromReflectionName(
				name, out var typeParameterCount);
			SRM.TypeReferenceHandle declaringTypeHandle;
			try
			{
				declaringTypeHandle = tr.GetDeclaringType();
			}
			catch (BadImageFormatException)
			{
				declaringTypeHandle = default;
			}

			if (declaringTypeHandle.IsNil)
			{
				string ns;
				try
				{
					ns = tr.Namespace.IsNil ? "" : reader.GetString(tr.Namespace);
				}
				catch (BadImageFormatException)
				{
					ns = "";
				}

				return new FullTypeName(new TopLevelTypeName(ns, name, typeParameterCount));
			}
			else
			{
				return declaringTypeHandle.GetFullTypeName(reader).NestedType(name, typeParameterCount);
			}
		}

		public static FullTypeName GetFullTypeName(this SRM.TypeDefinitionHandle handle, SRM.MetadataReader reader)
		{
			if (handle.IsNil)
				throw new ArgumentNullException(nameof(handle));
			return reader.GetTypeDefinition(handle).GetFullTypeName(reader);
		}

		public static FullTypeName GetFullTypeName(this SRM.TypeDefinition td, SRM.MetadataReader reader)
		{
			SRM.TypeDefinitionHandle declaringTypeHandle;
			string name = ReflectionHelper.SplitTypeParameterCountFromReflectionName(
				reader.GetString(td.Name), out var typeParameterCount);
			if ((declaringTypeHandle = td.GetDeclaringType()).IsNil)
			{
				string @namespace = td.Namespace.IsNil ? "" : reader.GetString(td.Namespace);
				return new FullTypeName(new TopLevelTypeName(@namespace, name, typeParameterCount));
			}
			else
			{
				return declaringTypeHandle.GetFullTypeName(reader).NestedType(name, typeParameterCount);
			}
		}

		public static FullTypeName GetFullTypeName(this SRM.ExportedType type, SRM.MetadataReader metadata)
		{
			string name = ReflectionHelper.SplitTypeParameterCountFromReflectionName(
				metadata.GetString(type.Name), out int typeParameterCount);
			if (type.Implementation.Kind == SRM.HandleKind.ExportedType)
			{
				var outerType = metadata.GetExportedType((SRM.ExportedTypeHandle)type.Implementation);
				return outerType.GetFullTypeName(metadata).NestedType(name, typeParameterCount);
			}
			else
			{
				string ns = type.Namespace.IsNil ? "" : metadata.GetString(type.Namespace);
				return new TopLevelTypeName(ns, name, typeParameterCount);
			}
		}

		public static bool IsAnonymousType(this SRM.TypeDefinition type, SRM.MetadataReader metadata)
		{
			string name = metadata.GetString(type.Name);
			if (type.Namespace.IsNil && type.HasGeneratedName(metadata)
			                         && (name.Contains("AnonType") || name.Contains("AnonymousType")))
			{
				return type.IsCompilerGenerated(metadata);
			}

			return false;
		}

		public static SRM.BlobReader GetInitialValue(this SRM.FieldDefinition field, PEReader pefile,
			ICompilation typeSystem)
		{
			if (!field.HasFlag(FieldAttributes.HasFieldRVA))
				return default;
			int rva = field.GetRelativeVirtualAddress();
			if (rva == 0)
				return default;
			int size = field.DecodeSignature(new FieldValueSizeDecoder(typeSystem), default);
			var sectionData = pefile.GetSectionData(rva);
			if (sectionData.Length == 0 && size != 0)
				throw new BadImageFormatException($"Field data (rva=0x{rva:x}) could not be found"
				                                  + "in any section!");
			if (size < 0 || size > sectionData.Length)
				throw new BadImageFormatException($"Invalid size {size} for field data!");
			return sectionData.GetReader(0, size);
		}

		public static SRM.EntityHandle GetBaseTypeOrNil(this SRM.TypeDefinition definition)
		{
			try
			{
				return definition.BaseType;
			}
			catch (BadImageFormatException)
			{
				return default;
			}
		}

		public static string ToILSyntax(this SRM.SignatureCallingConvention callConv)
		{
			return callConv switch {
				SRM.SignatureCallingConvention.Default => "default",
				SRM.SignatureCallingConvention.CDecl => "unmanaged cdecl",
				SRM.SignatureCallingConvention.StdCall => "unmanaged stdcall",
				SRM.SignatureCallingConvention.ThisCall => "unmanaged thiscall",
				SRM.SignatureCallingConvention.FastCall => "unmanaged fastcall",
				SRM.SignatureCallingConvention.VarArgs => "vararg",
				SRM.SignatureCallingConvention.Unmanaged => "unmanaged",
				_ => callConv.ToString().ToLowerInvariant()
			};
		}

		sealed class FieldValueSizeDecoder : SRM.ISignatureTypeProvider<int, GenericContext>
		{
			readonly MetadataModule module;
			readonly int pointerSize;

			public FieldValueSizeDecoder(ICompilation typeSystem = null)
			{
				this.module = (MetadataModule)typeSystem?.MainModule;
				if (module == null)
					this.pointerSize = IntPtr.Size;
				else
					this.pointerSize = module.PEFile.Reader.PEHeaders.PEHeader.Magic == PEMagic.PE32 ? 4 : 8;
			}

			public int GetArrayType(int elementType, SRM.ArrayShape shape) =>
				GetPrimitiveType(SRM.PrimitiveTypeCode.Object);

			public int GetSZArrayType(int elementType) => GetPrimitiveType(SRM.PrimitiveTypeCode.Object);
			public int GetByReferenceType(int elementType) => pointerSize;
			public int GetFunctionPointerType(SRM.MethodSignature<int> signature) => pointerSize;

			public int GetGenericInstantiation(int genericType, ImmutableArray<int> typeArguments)
				=> genericType;

			public int GetGenericMethodParameter(GenericContext genericContext, int index) => 0;
			public int GetGenericTypeParameter(GenericContext genericContext, int index) => 0;
			public int GetModifiedType(int modifier, int unmodifiedType, bool isRequired) => unmodifiedType;
			public int GetPinnedType(int elementType) => elementType;
			public int GetPointerType(int elementType) => pointerSize;

			public int GetPrimitiveType(SRM.PrimitiveTypeCode typeCode)
			{
				switch (typeCode)
				{
					case SRM.PrimitiveTypeCode.Boolean:
					case SRM.PrimitiveTypeCode.Byte:
					case SRM.PrimitiveTypeCode.SByte:
						return 1;
					case SRM.PrimitiveTypeCode.Char:
					case SRM.PrimitiveTypeCode.Int16:
					case SRM.PrimitiveTypeCode.UInt16:
						return 2;
					case SRM.PrimitiveTypeCode.Int32:
					case SRM.PrimitiveTypeCode.UInt32:
					case SRM.PrimitiveTypeCode.Single:
						return 4;
					case SRM.PrimitiveTypeCode.Int64:
					case SRM.PrimitiveTypeCode.UInt64:
					case SRM.PrimitiveTypeCode.Double:
						return 8;
					case SRM.PrimitiveTypeCode.IntPtr:
					case SRM.PrimitiveTypeCode.UIntPtr:
						return pointerSize;
					default:
						return 0;
				}
			}

			public int GetTypeFromDefinition(SRM.MetadataReader reader, SRM.TypeDefinitionHandle handle,
				byte rawTypeKind)
			{
				var td = reader.GetTypeDefinition(handle);
				return td.GetLayout().Size;
			}

			public int GetTypeFromReference(SRM.MetadataReader reader, SRM.TypeReferenceHandle handle,
				byte rawTypeKind)
			{
				var typeDef = module?.ResolveType(handle, new GenericContext()).GetDefinition();
				if (typeDef == null || typeDef.MetadataToken.IsNil)
					return 0;
				reader = typeDef.ParentModule.PEFile.Metadata;
				var td = reader.GetTypeDefinition((SRM.TypeDefinitionHandle)typeDef.MetadataToken);
				return td.GetLayout().Size;
			}

			public int GetTypeFromSpecification(SRM.MetadataReader reader, GenericContext genericContext,
				SRM.TypeSpecificationHandle handle, byte rawTypeKind)
			{
				return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
			}
		}

		#region HasGeneratedName

		public static bool IsGeneratedName(this SRM.StringHandle handle, SRM.MetadataReader metadata)
		{
			return !handle.IsNil
			       && (metadata.GetString(handle).StartsWith("<", StringComparison.Ordinal)
			           || metadata.GetString(handle).Contains("$"));
		}

		public static bool HasGeneratedName(this SRM.MethodDefinitionHandle handle, SRM.MetadataReader metadata)
		{
			return metadata.GetMethodDefinition(handle).Name.IsGeneratedName(metadata);
		}

		public static bool HasGeneratedName(this SRM.TypeDefinitionHandle handle, SRM.MetadataReader metadata)
		{
			return metadata.GetTypeDefinition(handle).Name.IsGeneratedName(metadata);
		}

		public static bool HasGeneratedName(this SRM.TypeDefinition type, SRM.MetadataReader metadata)
		{
			return type.Name.IsGeneratedName(metadata);
		}

		public static bool HasGeneratedName(this SRM.FieldDefinitionHandle handle, SRM.MetadataReader metadata)
		{
			return metadata.GetFieldDefinition(handle).Name.IsGeneratedName(metadata);
		}

		#endregion

		#region IsCompilerGenerated

		public static bool IsCompilerGenerated(this SRM.MethodDefinitionHandle handle, SRM.MetadataReader metadata)
		{
			return metadata.GetMethodDefinition(handle).IsCompilerGenerated(metadata);
		}

		public static bool IsCompilerGeneratedOrIsInCompilerGeneratedClass(this SRM.MethodDefinitionHandle handle,
			SRM.MetadataReader metadata)
		{
			SRM.MethodDefinition method = metadata.GetMethodDefinition(handle);
			if (method.IsCompilerGenerated(metadata))
				return true;
			SRM.TypeDefinitionHandle declaringTypeHandle = method.GetDeclaringType();
			if (!declaringTypeHandle.IsNil && declaringTypeHandle.IsCompilerGenerated(metadata))
				return true;
			return false;
		}

		public static bool IsCompilerGeneratedOrIsInCompilerGeneratedClass(this SRM.TypeDefinitionHandle handle,
			SRM.MetadataReader metadata)
		{
			SRM.TypeDefinition type = metadata.GetTypeDefinition(handle);
			if (type.IsCompilerGenerated(metadata))
				return true;
			SRM.TypeDefinitionHandle declaringTypeHandle = type.GetDeclaringType();
			if (!declaringTypeHandle.IsNil && declaringTypeHandle.IsCompilerGenerated(metadata))
				return true;
			return false;
		}

		public static bool IsCompilerGenerated(this SRM.MethodDefinition method, SRM.MetadataReader metadata)
		{
			return method.GetCustomAttributes().HasKnownAttribute(metadata, KnownAttribute.CompilerGenerated);
		}

		public static bool IsCompilerGenerated(this SRM.FieldDefinitionHandle handle, SRM.MetadataReader metadata)
		{
			return metadata.GetFieldDefinition(handle).IsCompilerGenerated(metadata);
		}

		public static bool IsCompilerGenerated(this SRM.FieldDefinition field, SRM.MetadataReader metadata)
		{
			return field.GetCustomAttributes().HasKnownAttribute(metadata, KnownAttribute.CompilerGenerated);
		}

		public static bool IsCompilerGenerated(this SRM.TypeDefinitionHandle handle, SRM.MetadataReader metadata)
		{
			return metadata.GetTypeDefinition(handle).IsCompilerGenerated(metadata);
		}

		public static bool IsCompilerGenerated(this SRM.TypeDefinition type, SRM.MetadataReader metadata)
		{
			return type.GetCustomAttributes().HasKnownAttribute(metadata, KnownAttribute.CompilerGenerated);
		}

		#endregion

		#region Attribute extensions

		/// <summary>
		/// Gets the type of the attribute.
		/// </summary>
		public static SRM.EntityHandle GetAttributeType(this SRM.CustomAttribute attribute, SRM.MetadataReader reader)
		{
			switch (attribute.Constructor.Kind)
			{
				case SRM.HandleKind.MethodDefinition:
					var md = reader.GetMethodDefinition((SRM.MethodDefinitionHandle)attribute.Constructor);
					return md.GetDeclaringType();
				case SRM.HandleKind.MemberReference:
					var mr = reader.GetMemberReference((SRM.MemberReferenceHandle)attribute.Constructor);
					return mr.Parent;
				default:
					throw new BadImageFormatException("Unexpected token kind for attribute constructor: "
					                                  + attribute.Constructor.Kind);
			}
		}

		public static bool HasKnownAttribute(this SRM.CustomAttributeHandleCollection customAttributes,
			SRM.MetadataReader metadata, KnownAttribute type)
		{
			foreach (var handle in customAttributes)
			{
				var customAttribute = metadata.GetCustomAttribute(handle);
				if (customAttribute.IsKnownAttribute(metadata, type))
					return true;
			}

			return false;
		}

		internal static bool IsKnownAttribute(this SRM.CustomAttribute attr, SRM.MetadataReader metadata,
			KnownAttribute attrType)
		{
			return attr.GetAttributeType(metadata).IsKnownType(metadata, attrType);
		}

		public static Nullability? GetNullableContext(this SRM.CustomAttributeHandleCollection customAttributes,
			SRM.MetadataReader metadata)
		{
			foreach (var handle in customAttributes)
			{
				var customAttribute = metadata.GetCustomAttribute(handle);
				if (customAttribute.IsKnownAttribute(metadata, KnownAttribute.NullableContext))
				{
					// Decode 
					SRM.CustomAttributeValue<IType> value;
					try
					{
						value = customAttribute.DecodeValue(
							MetadataExtensions.MinimalAttributeTypeProvider);
					}
					catch (BadImageFormatException)
					{
						continue;
					}
					catch (EnumUnderlyingTypeResolveException)
					{
						continue;
					}

					if (value.FixedArguments.Length == 1 && value.FixedArguments[0].Value is byte b and <= 2)
					{
						return (Nullability)b;
					}
				}
			}

			return null;
		}

		#endregion
	}
}