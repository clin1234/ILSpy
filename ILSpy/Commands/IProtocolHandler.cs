using System.Reflection.Metadata;

using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.ILSpy.TreeNodes;

namespace ICSharpCode.ILSpy
{
	public interface IProtocolHandler
	{
		ILSpyTreeNode Resolve(string protocol, PEFile module, Handle handle, out bool newTabPage);
	}
}
