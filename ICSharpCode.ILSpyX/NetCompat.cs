#if !NET
namespace System.Diagnostics.CodeAnalysis
{
	/// <summary>Specifies that the output will be non-null if the named parameter is non-null.</summary>
	[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, AllowMultiple = true)]
	public sealed class NotNullIfNotNullAttribute : Attribute
	{
		/// <summary>Initializes the attribute with the associated parameter name.</summary>
		/// <param name="parameterName">
		/// The associated parameter name.  The output will be non-null if the argument to the parameter specified is non-null.
		/// </param>
		public NotNullIfNotNullAttribute(string parameterName) => ParameterName = parameterName;

		/// <summary>Gets the associated parameter name.</summary>
		public string ParameterName { get; }
	}
	
	/// <summary>Specifies that when a method returns <see cref="ReturnValue"/>, the parameter will not be null even if the corresponding type allows it.</summary>
	[AttributeUsage(AttributeTargets.Parameter)]
	internal sealed class NotNullWhenAttribute : Attribute
	{
		/// <summary>Initializes the attribute with the specified return value condition.</summary>
		/// <param name="returnValue">
		/// The return value condition. If the method returns this value, the associated parameter will not be null.
		/// </param>
		public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

		/// <summary>Gets the return value condition.</summary>
		public bool ReturnValue { get; }
	}

	internal sealed class DoesNotReturnIfAttribute : Attribute
	{
		/// <summary>Initializes the attribute with the specified parameter value.</summary>
		/// <param name="parameterValue">
		/// The condition parameter value. Code after the method will be considered unreachable by diagnostics if the argument to
		/// the associated parameter matches this value.
		/// </param>
		public DoesNotReturnIfAttribute(bool parameterValue) => ParameterValue = parameterValue;

		/// <summary>Gets the condition parameter value.</summary>
		public bool ParameterValue { get; }
	}
}
#endif