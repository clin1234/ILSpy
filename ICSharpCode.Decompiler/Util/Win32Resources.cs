﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace ICSharpCode.Decompiler.Util
{
	/// <summary>
	/// Represents win32 resources
	/// </summary>
	internal static class Win32Resources
	{
		/// <summary>
		/// Reads win32 resource root directory
		/// </summary>
		/// <param name="pe"></param>
		/// <returns></returns>
		public static unsafe Win32ResourceDirectory? ReadWin32Resources(this PEReader pe)
		{
			if (pe is null) throw new ArgumentNullException(nameof(pe));

			int rva = pe.PEHeaders.PEHeader?.ResourceTableDirectory.RelativeVirtualAddress ?? 0;
			if (rva == 0)
				return null;
			byte* pRoot = pe.GetSectionData(rva).Pointer;
			return new Win32ResourceDirectory(pe, pRoot, 0, new Win32ResourceName("Root"));
		}

		public static Win32ResourceDirectory? Find(this Win32ResourceDirectory root, Win32ResourceName? type)
		{
			if (root is null) throw new ArgumentNullException(nameof(root));
			if (type is null) throw new ArgumentNullException(nameof(type));
			if (!root.Name.HasName || root.Name.Name != "Root")
				throw new ArgumentOutOfRangeException(nameof(root));

			return root.FindDirectory(type);
		}

		public static Win32ResourceDirectory? Find(this Win32ResourceDirectory root, Win32ResourceName type,
			Win32ResourceName name)
		{
			if (root is null) throw new ArgumentNullException(nameof(root));
			if (type is null) throw new ArgumentNullException(nameof(type));
			if (name is null) throw new ArgumentNullException(nameof(name));
			if (!root.Name.HasName || root.Name.Name != "Root")
				throw new ArgumentOutOfRangeException(nameof(root));

			return root.FindDirectory(type)?.FindDirectory(name);
		}

		public static Win32ResourceData? Find(this Win32ResourceDirectory root, Win32ResourceName type,
			Win32ResourceName name, Win32ResourceName langId)
		{
			if (root is null) throw new ArgumentNullException(nameof(root));
			if (type is null) throw new ArgumentNullException(nameof(type));
			if (name is null) throw new ArgumentNullException(nameof(name));
			if (langId is null) throw new ArgumentNullException(nameof(langId));
			if (!root.Name.HasName || root.Name.Name != "Root")
				throw new ArgumentOutOfRangeException(nameof(root));

			return root.FindDirectory(type)?.FindDirectory(name)?.FindData(langId);
		}
	}

	[DebuggerDisplay("Directory: {" + nameof(Name) + "}")]
	public sealed class Win32ResourceDirectory
	{
		internal unsafe Win32ResourceDirectory(PEReader pe, byte* pRoot, int offset, Win32ResourceName name)
		{
			var p = (IMAGE_RESOURCE_DIRECTORY*)(pRoot + offset);
			NumberOfNamedEntries = p->NumberOfNamedEntries;
			NumberOfIdEntries = p->NumberOfIdEntries;

			Name = name;
			Directories = new List<Win32ResourceDirectory>();
			Datas = new List<Win32ResourceData>();
			var pEntries = (IMAGE_RESOURCE_DIRECTORY_ENTRY*)(p + 1);
			int total = NumberOfNamedEntries + NumberOfIdEntries;
			for (int i = 0; i < total; i++)
			{
				var pEntry = pEntries + i;
				name = new Win32ResourceName(pRoot, pEntry);
				if ((pEntry->OffsetToData & 0x80000000) == 0)
					Datas.Add(new Win32ResourceData(pe, pRoot, (int)pEntry->OffsetToData, name));
				else
					Directories.Add(new Win32ResourceDirectory(pe, pRoot, (int)(pEntry->OffsetToData & 0x7FFFFFFF),
						name));
			}
		}

		public Win32ResourceName Name { get; }

		public IList<Win32ResourceDirectory> Directories { get; }

		private IList<Win32ResourceData> Datas { get; }

		public Win32ResourceDirectory? FindDirectory(Win32ResourceName name)
		{
			foreach (var directory in Directories)
			{
				if (directory.Name == name)
					return directory;
			}

			return null;
		}

		public Win32ResourceData? FindData(Win32ResourceName name)
		{
			foreach (var data in Datas)
			{
				if (data.Name == name)
					return data;
			}

			return null;
		}

		public Win32ResourceDirectory? FirstDirectory()
		{
			return Directories.Count != 0 ? Directories[0] : null;
		}

		public Win32ResourceData? FirstData()
		{
			return Datas.Count != 0 ? Datas[0] : null;
		}

		#region Structure

		private ushort NumberOfNamedEntries { get; }
		private ushort NumberOfIdEntries { get; }

		#endregion
	}

	[DebuggerDisplay("Data: {" + nameof(Name) + "}")]
	public sealed unsafe class Win32ResourceData
	{
		private readonly void* _pointer;

		internal Win32ResourceData(PEReader pe, byte* pRoot, int offset, Win32ResourceName name)
		{
			var p = (IMAGE_RESOURCE_DATA_ENTRY*)(pRoot + offset);
			OffsetToData = p->OffsetToData;
			Size = p->Size;

			_pointer = pe.GetSectionData((int)OffsetToData).Pointer;
			Name = name;
		}

		public Win32ResourceName Name { get; }

		public byte[] Data {
			get {
				byte[] data = new byte[Size];
				fixed (void* pData = data)
					Buffer.MemoryCopy(_pointer, pData, Size, Size);
				return data;
			}
		}

		#region Structure

		private uint OffsetToData { get; }
		private uint Size { get; }

		#endregion
	}

	public sealed class Win32ResourceName
	{
		private readonly object _name;

		internal Win32ResourceName(string name)
		{
			_name = name ?? throw new ArgumentNullException(nameof(name));
		}

		public Win32ResourceName(int id) : this(checked((ushort)id))
		{
		}

		public Win32ResourceName(ushort id)
		{
			_name = id;
		}

		internal unsafe Win32ResourceName(byte* pRoot, IMAGE_RESOURCE_DIRECTORY_ENTRY* pEntry)
		{
			_name = (pEntry->Name & 0x80000000) == 0
				? (ushort)pEntry->Name
				: ReadString(pRoot, (int)(pEntry->Name & 0x7FFFFFFF));

			static string ReadString(byte* pRoot, int offset)
			{
				var pString = (IMAGE_RESOURCE_DIRECTORY_STRING*)(pRoot + offset);
				return new string(pString->NameString, 0, pString->Length);
			}
		}

		internal bool HasName => _name is string;

		private bool HasId => _name is ushort;

		internal string Name => (string)_name;

		private ushort Id => (ushort)_name;

		public static bool operator ==(Win32ResourceName x, Win32ResourceName y)
		{
			if (x.HasName)
			{
				return y.HasName && string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) == 0;
			}

			return y.HasId && x.Id == y.Id;
		}

		public static bool operator !=(Win32ResourceName x, Win32ResourceName y)
		{
			return !(x == y);
		}

		public override int GetHashCode()
		{
			return _name.GetHashCode();
		}

		public override bool Equals(object? obj)
		{
			if (obj is not Win32ResourceName name)
				return false;
			return this == name;
		}

		public override string ToString()
		{
			return HasName ? $"Name: {Name}" : $"Id: {Id}";
		}
	}

	internal struct IMAGE_RESOURCE_DIRECTORY
	{
		public uint Characteristics;
		public uint TimeDateStamp;
		public ushort MajorVersion;
		public ushort MinorVersion;
		public ushort NumberOfNamedEntries;
		public ushort NumberOfIdEntries;
	}

	internal struct IMAGE_RESOURCE_DIRECTORY_ENTRY
	{
		public uint Name;
		public uint OffsetToData;
	}

	internal unsafe struct IMAGE_RESOURCE_DIRECTORY_STRING
	{
		public ushort Length;
		public fixed char NameString[1];
	}

	internal struct IMAGE_RESOURCE_DATA_ENTRY
	{
		public uint OffsetToData;
		public uint Size;
		public uint CodePage;
		public uint Reserved;
	}
}