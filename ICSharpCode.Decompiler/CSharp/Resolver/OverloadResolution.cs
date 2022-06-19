﻿// Copyright (c) 2010-2013 AlphaSierraPapa for the SharpDevelop Team
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using ICSharpCode.Decompiler.Semantics;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem.Implementation;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.CSharp.Resolver
{
	/// <summary>
	/// C# overload resolution (C# 4.0 spec: §7.5).
	/// </summary>
	internal sealed class OverloadResolution
	{
		readonly string?[] argumentNames;
		readonly ResolveResult[] arguments;

		readonly ICompilation compilation;
		readonly CSharpConversions? conversions;

		readonly IType[]? explicitlyGivenTypeArguments;

		//List<Candidate> candidates = new List<Candidate>();
		Candidate? bestCandidate;
		Candidate? bestCandidateAmbiguousWith;
		OverloadResolutionErrors bestCandidateValidationResult;
		bool bestCandidateWasValidated;

		#region Constructor

		public OverloadResolution(ICompilation compilation, ResolveResult[]? arguments, string[]? argumentNames = null,
			IType[]? typeArguments = null, CSharpConversions? conversions = null)
		{
			if (compilation == null) throw new ArgumentNullException(nameof(compilation));
			if (arguments == null) throw new ArgumentNullException(nameof(arguments));
			if (argumentNames == null)
				argumentNames = new string[arguments.Length];
			else if (argumentNames.Length != arguments.Length)
				throw new ArgumentException("argumentsNames.Length must be equal to arguments.Length");
			this.compilation = compilation;
			this.arguments = arguments;
			this.argumentNames = argumentNames;

			// keep explicitlyGivenTypeArguments==null when no type arguments were specified
			if (typeArguments is { Length: > 0 })
				this.explicitlyGivenTypeArguments = typeArguments;

			this.conversions = conversions ?? CSharpConversions.Get(compilation);
			this.AllowExpandingParams = true;
			this.AllowOptionalParameters = true;
		}

		#endregion

		#region MapCorrespondingParameters

		void MapCorrespondingParameters(Candidate candidate)
		{
			// C# 4.0 spec: §7.5.1.1 Corresponding parameters
			// Updated for C# 7.2 non-trailing named arguments
			candidate.ArgumentToParameterMap = new int[arguments.Length];
			bool hasPositionalArgument = false;
			// go backwards, so that hasPositionalArgument tells us whether there
			// are non-trailing named arguments
			for (int i = arguments.Length - 1; i >= 0; i--)
			{
				candidate.ArgumentToParameterMap[i] = -1;
				if (argumentNames[i] == null || hasPositionalArgument)
				{
					hasPositionalArgument = true;
					// positional argument or non-trailing named argument
					if (i < candidate.ParameterTypes.Length)
					{
						candidate.ArgumentToParameterMap[i] = i;
						if (argumentNames[i] != null && argumentNames[i] != candidate.Parameters[i].Name)
						{
							// non-trailing named argument must match name
							candidate.AddError(OverloadResolutionErrors.NoParameterFoundForNamedArgument);
						}
					}
					else if (candidate.IsExpandedForm)
					{
						candidate.ArgumentToParameterMap[i] = candidate.ParameterTypes.Length - 1;
						if (argumentNames[i] != null)
						{
							// can't use non-trailing named argument here
							candidate.AddError(OverloadResolutionErrors.NoParameterFoundForNamedArgument);
						}
					}
					else
					{
						candidate.AddError(OverloadResolutionErrors.TooManyPositionalArguments);
					}
				}
				else
				{
					// (trailing) named argument
					for (int j = 0; j < candidate.Parameters.Count; j++)
					{
						if (argumentNames[i] == candidate.Parameters[j].Name)
						{
							candidate.ArgumentToParameterMap[i] = j;
						}
					}

					if (candidate.ArgumentToParameterMap[i] < 0)
						candidate.AddError(OverloadResolutionErrors.NoParameterFoundForNamedArgument);
				}
			}
		}

		#endregion

		#region ConsiderIfNewCandidateIsBest

		void ConsiderIfNewCandidateIsBest(Candidate candidate)
		{
			if (bestCandidate == null)
			{
				bestCandidate = candidate;
				bestCandidateWasValidated = false;
			}
			else
			{
				switch (BetterFunctionMember(candidate, bestCandidate))
				{
					case 0:
						// Overwrite 'bestCandidateAmbiguousWith' so that API users can
						// detect the set of all ambiguous methods if they look at
						// bestCandidateAmbiguousWith after each step.
						bestCandidateAmbiguousWith = candidate;
						break;
					case 1:
						bestCandidate = candidate;
						bestCandidateWasValidated = false;
						bestCandidateAmbiguousWith = null;
						break;
					// case 2: best candidate stays best
				}
			}
		}

		#endregion

		sealed class Candidate
		{
			/// <summary>
			/// Returns the normal form candidate, if this is an expanded candidate.
			/// </summary>
			public readonly bool IsExpandedForm;

			public readonly IParameterizedMember Member;

			/// <summary>
			/// Gets the original member parameters (before any substitution!)
			/// </summary>
			public readonly IReadOnlyList<IParameter> Parameters;

			/// <summary>
			/// Gets the parameter types. In the first step, these are the types without any substition.
			/// After type inference, substitutions will be performed.
			/// </summary>
			public readonly IType[] ParameterTypes;

			/// <summary>
			/// Gets the original method type parameters (before any substitution!)
			/// </summary>
			public readonly IReadOnlyList<ITypeParameter>? TypeParameters;

			/// <summary>
			/// Conversions applied to the arguments.
			/// This field is set by the CheckApplicability step.
			/// </summary>
			public Conversion?[] ArgumentConversions;

			/// <summary>
			/// argument index -> parameter index; -1 for arguments that could not be mapped
			/// </summary>
			public int[] ArgumentToParameterMap;

			public int ErrorCount;

			public OverloadResolutionErrors Errors;

			public bool HasUnmappedOptionalParameters;

			public IType[]? InferredTypes;

			public Candidate(IParameterizedMember member, bool isExpanded)
			{
				this.Member = member;
				this.IsExpandedForm = isExpanded;
				IParameterizedMember memberDefinition = (IParameterizedMember)member.MemberDefinition;
				// For specificialized methods, go back to the original parameters:
				// (without any type parameter substitution, not even class type parameters)
				// We'll re-substitute them as part of RunTypeInference().
				this.Parameters = memberDefinition.Parameters;
				if (memberDefinition is IMethod methodDefinition && methodDefinition.TypeParameters.Count > 0)
				{
					this.TypeParameters = methodDefinition.TypeParameters;
				}

				this.ParameterTypes = new IType[this.Parameters.Count];
			}

			public bool IsGenericMethod {
				get {
					return Member is IMethod method && method.TypeParameters.Count > 0;
				}
			}

			public int ArgumentsPassedToParamsArray {
				get {
					int count = 0;
					if (IsExpandedForm)
					{
						int paramsParameterIndex = this.Parameters.Count - 1;
						foreach (int parameterIndex in ArgumentToParameterMap)
						{
							if (parameterIndex == paramsParameterIndex)
								count++;
						}
					}

					return count;
				}
			}

			public void AddError(OverloadResolutionErrors newError)
			{
				this.Errors |= newError;
				if (!IsApplicable(newError))
					this.ErrorCount++;
			}
		}

		#region Input Properties

		/// <summary>
		/// Gets/Sets whether the methods are extension methods that are being called using extension method syntax.
		/// </summary>
		/// <remarks>
		/// Setting this property to true restricts the possible conversions on the first argument to
		/// implicit identity, reference, or boxing conversions.
		/// </remarks>
		public bool IsExtensionMethodInvocation { get; init; }

		/// <summary>
		/// Gets/Sets whether expanding 'params' into individual elements is allowed.
		/// The default value is true.
		/// </summary>
		public bool AllowExpandingParams { get; init; }

		/// <summary>
		/// Gets/Sets whether optional parameters may be left at their default value.
		/// The default value is true.
		/// If this property is set to false, optional parameters will be treated like regular parameters.
		/// </summary>
		public bool AllowOptionalParameters { get; init; }

		/// <summary>
		/// Gets/Sets whether a value argument can be passed to an `in` reference parameter.
		/// </summary>
		public bool AllowImplicitIn { get; init; } = true;

		/// <summary>
		/// Gets/Sets whether ConversionResolveResults created by this OverloadResolution
		/// instance apply overflow checking.
		/// The default value is false.
		/// </summary>
		public bool CheckForOverflow { get; init; }

		/// <summary>
		/// Gets the arguments for which this OverloadResolution instance was created.
		/// </summary>
		public IList<ResolveResult>? Arguments {
			get { return arguments; }
		}

		#endregion

		#region AddCandidate

		/// <summary>
		/// Adds a candidate to overload resolution.
		/// </summary>
		/// <param name="member">The candidate member to add.</param>
		/// <param name="additionalErrors">Additional errors that apply to the candidate.
		/// This is used to represent errors during member lookup (e.g. OverloadResolutionErrors.Inaccessible)
		/// in overload resolution.</param>
		/// <returns>The errors that prevent the member from being applicable, if any.
		/// Note: this method does not return errors that do not affect applicability.</returns>
		internal OverloadResolutionErrors AddCandidate(IParameterizedMember member,
			OverloadResolutionErrors additionalErrors = OverloadResolutionErrors.None)
		{
			if (member == null) throw new ArgumentNullException(nameof(member));

			Candidate c = new(member, false);
			c.AddError(additionalErrors);
			if (CalculateCandidate(c))
			{
				//candidates.Add(c);
			}

			if (this.AllowExpandingParams && member.Parameters.Count > 0
			                              && member.Parameters[^1].IsParams)
			{
				Candidate expandedCandidate = new(member, true);
				expandedCandidate.AddError(additionalErrors);
				// consider expanded form only if it isn't obviously wrong
				if (CalculateCandidate(expandedCandidate))
				{
					//candidates.Add(expandedCandidate);

					if (expandedCandidate.ErrorCount < c.ErrorCount)
						return expandedCandidate.Errors;
				}
			}

			return c.Errors;
		}

		/// <summary>
		/// Calculates applicability etc. for the candidate.
		/// </summary>
		/// <returns>True if the calculation was successful, false if the candidate should be removed without reporting an error</returns>
		bool CalculateCandidate(Candidate candidate)
		{
			if (!ResolveParameterTypes(candidate, false))
				return false;
			MapCorrespondingParameters(candidate);
			RunTypeInference(candidate);
			CheckApplicability(candidate);
			ConsiderIfNewCandidateIsBest(candidate);
			return true;
		}

		bool ResolveParameterTypes(Candidate candidate, bool useSpecializedParameters)
		{
			for (int i = 0; i < candidate.Parameters.Count; i++)
			{
				IType type;
				if (useSpecializedParameters)
				{
					// Use the parameter type of the specialized non-generic method or indexer
					Debug.Assert(!candidate.IsGenericMethod);
					type = candidate.Member.Parameters[i].Type;
				}
				else
				{
					// Use the type of the original formal parameter
					type = candidate.Parameters[i].Type;
				}

				if (candidate.IsExpandedForm && i == candidate.Parameters.Count - 1)
				{
					if (type is ArrayType { Dimensions: 1 } arrayType)
						type = arrayType.ElementType;
					else
						return
							false; // error: cannot unpack params-array. abort considering the expanded form for this candidate
				}

				candidate.ParameterTypes[i] = type;
			}

			return true;
		}

		#endregion

		#region AddMethodLists

		/// <summary>
		/// Adds all candidates from the method lists.
		/// 
		/// This method implements the logic that causes applicable methods in derived types to hide
		/// all methods in base types.
		/// </summary>
		/// <param name="methodLists">The methods, grouped by declaring type. Base types must come first in the list.</param>
		public void AddMethodLists(IReadOnlyList<MethodListWithDeclaringType> methodLists)
		{
			if (methodLists == null) throw new ArgumentNullException(nameof(methodLists));
			// Base types come first, so go through the list backwards (derived types first)
			bool[] isHiddenByDerivedType = methodLists.Count > 1 ? new bool[methodLists.Count] : null;
			for (int i = methodLists.Count - 1; i >= 0; i--)
			{
				if (isHiddenByDerivedType != null && isHiddenByDerivedType[i])
				{
					Log.WriteLine(
						"  Skipping methods in {0} because they are hidden by an applicable method in a derived type",
						methodLists[i].DeclaringType);
					continue;
				}

				MethodListWithDeclaringType methodList = methodLists[i];
				bool foundApplicableCandidateInCurrentList = false;

				foreach (var method in methodList)
				{
					Log.Indent();
					OverloadResolutionErrors errors = AddCandidate(method);
					Log.Unindent();
					LogCandidateAddingResult("  Candidate", method, errors);

					foundApplicableCandidateInCurrentList |= IsApplicable(errors);
				}

				if (foundApplicableCandidateInCurrentList && i > 0)
				{
					foreach (IType baseType in methodList.DeclaringType.GetAllBaseTypes())
					{
						for (int j = 0; j < i; j++)
						{
							if (!isHiddenByDerivedType[j] && baseType.Equals(methodLists[j].DeclaringType))
								isHiddenByDerivedType[j] = true;
						}
					}
				}
			}
		}

		[Conditional("DEBUG")]
		internal void LogCandidateAddingResult(string text, IParameterizedMember method,
			OverloadResolutionErrors errors)
		{
#if DEBUG
			Log.WriteLine(
				$"{text} {method} = {(errors == OverloadResolutionErrors.None ? "Success" : errors.ToString())}{(this.BestCandidate == method ? " (best candidate so far)" : this.BestCandidateAmbiguousWith == method ? " (ambiguous)" : "")}");
#endif
		}

		#endregion

		#region RunTypeInference

		void RunTypeInference(Candidate candidate)
		{
			if (candidate.TypeParameters == null)
			{
				if (explicitlyGivenTypeArguments != null)
				{
					// method does not expect type arguments, but was given some
					candidate.AddError(OverloadResolutionErrors.WrongNumberOfTypeArguments);
				}

				// Grab new parameter types:
				ResolveParameterTypes(candidate, true);
				return;
			}

			IReadOnlyList<IType>? classTypeArguments;
			if (candidate.Member.DeclaringType is ParameterizedType parameterizedDeclaringType)
			{
				classTypeArguments = parameterizedDeclaringType.TypeArguments;
			}
			else
			{
				classTypeArguments = null;
			}

			// The method is generic:
			if (explicitlyGivenTypeArguments != null)
			{
				if (explicitlyGivenTypeArguments.Length == candidate.TypeParameters.Count)
				{
					candidate.InferredTypes = explicitlyGivenTypeArguments;
				}
				else
				{
					candidate.AddError(OverloadResolutionErrors.WrongNumberOfTypeArguments);
					// wrong number of type arguments given, so truncate the list or pad with UnknownType
					candidate.InferredTypes = new IType?[candidate.TypeParameters.Count];
					for (int i = 0; i < candidate.InferredTypes.Length; i++)
					{
						if (i < explicitlyGivenTypeArguments.Length)
							candidate.InferredTypes[i] = explicitlyGivenTypeArguments[i];
						else
							candidate.InferredTypes[i] = SpecialType.UnknownType;
					}
				}
			}
			else
			{
				TypeInference ti = new(compilation, conversions);
				IType[] parameterTypes = candidate.ArgumentToParameterMap
					.SelectReadOnlyArray(parameterIndex =>
						parameterIndex >= 0 ? candidate.ParameterTypes[parameterIndex] : SpecialType.UnknownType);
				candidate.InferredTypes = ti.InferTypeArguments(candidate.TypeParameters, arguments, parameterTypes,
					out bool success, classTypeArguments);
				if (!success)
					candidate.AddError(OverloadResolutionErrors.TypeInferenceFailed);
			}

			// Now substitute in the formal parameters:
			var substitution = new ConstraintValidatingSubstitution(classTypeArguments, candidate.InferredTypes, this);
			for (int i = 0; i < candidate.ParameterTypes.Length; i++)
			{
				candidate.ParameterTypes[i] = candidate.ParameterTypes[i].AcceptVisitor(substitution);
			}

			if (!substitution.ConstraintsValid)
				candidate.AddError(OverloadResolutionErrors.ConstructedTypeDoesNotSatisfyConstraint);
		}

		sealed class ConstraintValidatingSubstitution : TypeParameterSubstitution
		{
			readonly CSharpConversions? conversions;
			public bool ConstraintsValid = true;

			public ConstraintValidatingSubstitution(IReadOnlyList<IType>? classTypeArguments,
				IReadOnlyList<IType>? methodTypeArguments, OverloadResolution overloadResolution)
				: base(classTypeArguments, methodTypeArguments)
			{
				this.conversions = overloadResolution.conversions;
			}

			public override IType VisitParameterizedType(ParameterizedType type)
			{
				IType newType = base.VisitParameterizedType(type);
				if (newType != type && ConstraintsValid)
				{
					// something was changed, so we need to validate the constraints
					if (newType is ParameterizedType newParameterizedType)
					{
						// C# 4.0 spec: §4.4.4 Satisfying constraints
						var typeParameters = newParameterizedType.TypeParameters;
						var substitution = newParameterizedType.GetSubstitution();
						for (int i = 0; i < typeParameters.Count; i++)
						{
							if (!ValidateConstraints(typeParameters[i], newParameterizedType.GetTypeArgument(i),
								    substitution, conversions))
							{
								ConstraintsValid = false;
								break;
							}
						}
					}
				}

				return newType;
			}
		}

		#endregion

		#region Validate Constraints

		OverloadResolutionErrors ValidateMethodConstraints(Candidate candidate)
		{
			// If type inference already failed, we won't check the constraints:
			if ((candidate.Errors & OverloadResolutionErrors.TypeInferenceFailed) != 0)
				return OverloadResolutionErrors.None;

			if (candidate.TypeParameters == null || candidate.TypeParameters.Count == 0)
				return OverloadResolutionErrors.None; // the method isn't generic
			var substitution = GetSubstitution(candidate);
			for (int i = 0; i < candidate.TypeParameters.Count; i++)
			{
				if (!ValidateConstraints(candidate.TypeParameters[i], substitution.MethodTypeArguments[i],
					    substitution))
					return OverloadResolutionErrors.MethodConstraintsNotSatisfied;
			}

			return OverloadResolutionErrors.None;
		}

		/// <summary>
		/// Validates whether the given type argument satisfies the constraints for the given type parameter.
		/// </summary>
		/// <param name="typeParameter">The type parameter.</param>
		/// <param name="typeArgument">The type argument.</param>
		/// <param name="substitution">The substitution that defines how type parameters are replaced with type arguments.
		/// The substitution is used to check constraints that depend on other type parameters (or recursively on the same type parameter).
		/// May be null if no substitution should be used.</param>
		/// <returns>True if the constraints are satisfied; false otherwise.</returns>
		public static bool ValidateConstraints(ITypeParameter typeParameter, IType typeArgument,
			TypeVisitor? substitution = null)
		{
			if (typeParameter == null) throw new ArgumentNullException(nameof(typeParameter));
			if (typeArgument == null) throw new ArgumentNullException(nameof(typeArgument));
			return ValidateConstraints(typeParameter, typeArgument, substitution,
				CSharpConversions.Get(typeParameter.Owner.Compilation));
		}

		internal static bool ValidateConstraints(ITypeParameter typeParameter, IType typeArgument,
			TypeVisitor? substitution, CSharpConversions? conversions)
		{
			switch (typeArgument.Kind)
			{
				// void, null, and pointers cannot be used as type arguments
				case TypeKind.Void:
				case TypeKind.Null:
				case TypeKind.Pointer:
					return false;
			}

			if (typeParameter.HasReferenceTypeConstraint)
			{
				if (typeArgument.IsReferenceType != true)
					return false;
			}

			if (typeParameter.HasValueTypeConstraint)
			{
				if (!NullableType.IsNonNullableValueType(typeArgument))
					return false;
			}

			if (typeParameter.HasDefaultConstructorConstraint)
			{
				ITypeDefinition def = typeArgument.GetDefinition();
				if (def is { IsAbstract: true })
					return false;
				var ctors = typeArgument.GetConstructors(
					m => m.Parameters.Count == 0 && m.Accessibility == Accessibility.Public,
					GetMemberOptions.IgnoreInheritedMembers | GetMemberOptions.ReturnMemberDefinitions
				);
				if (!ctors.Any())
					return false;
			}

			foreach (IType constraintType in typeParameter.DirectBaseTypes)
			{
				IType c = constraintType;
				if (substitution != null)
					c = c.AcceptVisitor(substitution);
				if (!conversions.IsConstraintConvertible(typeArgument, c))
					return false;
			}

			return true;
		}

		#endregion

		#region CheckApplicability

		/// <summary>
		/// Returns whether a candidate with the given errors is still considered to be applicable.
		/// </summary>
		public static bool IsApplicable(OverloadResolutionErrors errors)
		{
			const OverloadResolutionErrors errorsThatDoNotMatterForApplicability =
				OverloadResolutionErrors.AmbiguousMatch | OverloadResolutionErrors.MethodConstraintsNotSatisfied;
			return (errors & ~errorsThatDoNotMatterForApplicability) == OverloadResolutionErrors.None;
		}

		void CheckApplicability(Candidate candidate)
		{
			// C# 4.0 spec: §7.5.3.1 Applicable function member

			// Test whether parameters were mapped the correct number of arguments:
			int[] argumentCountPerParameter = new int[candidate.ParameterTypes.Length];
			foreach (var parameterIndex in candidate.ArgumentToParameterMap.Where(static parameterIndex =>
				         parameterIndex >= 0))
			{
				argumentCountPerParameter[parameterIndex]++;
			}

			for (int i = 0; i < argumentCountPerParameter.Length; i++)
			{
				if (candidate.IsExpandedForm && i == argumentCountPerParameter.Length - 1)
					continue; // any number of arguments is fine for the params-array
				switch (argumentCountPerParameter[i])
				{
					case 0 when this.AllowOptionalParameters && candidate.Parameters[i].IsOptional:
						candidate.HasUnmappedOptionalParameters = true;
						break;
					case 0:
						candidate.AddError(OverloadResolutionErrors.MissingArgumentForRequiredParameter);
						break;
					case > 1:
						candidate.AddError(OverloadResolutionErrors.MultipleArgumentsForSingleParameter);
						break;
				}
			}

			candidate.ArgumentConversions = new Conversion?[arguments.Length];
			// Test whether argument passing mode matches the parameter passing mode
			for (int i = 0; i < arguments.Length; i++)
			{
				int parameterIndex = candidate.ArgumentToParameterMap[i];
				if (parameterIndex < 0)
				{
					candidate.ArgumentConversions[i] = Conversion.None;
					continue;
				}

				ReferenceKind paramRefKind = candidate.Parameters[parameterIndex].ReferenceKind;
				switch (arguments[i])
				{
					case ByReferenceResolveResult brrr:
					{
						if (brrr.ReferenceKind != paramRefKind)
							candidate.AddError(OverloadResolutionErrors.ParameterPassingModeMismatch);
						break;
					}
					case OutVarResolveResult:
					{
						if (paramRefKind != ReferenceKind.Out)
							candidate.AddError(OverloadResolutionErrors.ParameterPassingModeMismatch);
						// 'out var decl' arguments are compatible with any out parameter
						continue;
					}
					default:
					{
						// AllowImplicitIn: `in` parameters can be filled implicitly without `in` DirectionExpression
						// IsExtensionMethodInvocation: `this ref` and `this in` parameters can be filled implicitly
						if (((paramRefKind == ReferenceKind.In && AllowImplicitIn)
						     || (IsExtensionMethodInvocation && parameterIndex == 0 &&
						         paramRefKind is ReferenceKind.In or ReferenceKind.Ref)
						    ) && candidate.ParameterTypes[parameterIndex].SkipModifiers() is ByReferenceType brt)
						{
							// Treat the parameter as if it was not declared "in" for the following steps
							// (applicability + better function member)
							candidate.ParameterTypes[parameterIndex] = brt.ElementType;
						}
						else if (paramRefKind != ReferenceKind.None)
						{
							candidate.AddError(OverloadResolutionErrors.ParameterPassingModeMismatch);
						}

						break;
					}
				}

				IType parameterType = candidate.ParameterTypes[parameterIndex];
				Conversion? c = conversions.ImplicitConversion(arguments[i], parameterType);
				candidate.ArgumentConversions[i] = c;
				if (IsExtensionMethodInvocation && parameterIndex == 0)
				{
					// First parameter to extension method must be an identity, reference or boxing conversion
					if (!(c == Conversion.IdentityConversion || c == Conversion.ImplicitReferenceConversion ||
					      c == Conversion.BoxingConversion))
						candidate.AddError(OverloadResolutionErrors.ArgumentTypeMismatch);
				}
				else
				{
					if ((!c.IsValid && !c.IsUserDefined && !c.IsMethodGroupConversion) &&
					    parameterType.Kind != TypeKind.Unknown)
						candidate.AddError(OverloadResolutionErrors.ArgumentTypeMismatch);
				}
			}
		}

		#endregion

		#region BetterFunctionMember

		/// <summary>
		/// Returns 1 if c1 is better than c2; 2 if c2 is better than c1; or 0 if neither is better.
		/// </summary>
		int BetterFunctionMember(Candidate c1, Candidate c2)
		{
			switch (c1.ErrorCount)
			{
				// prefer applicable members (part of heuristic that produces a best candidate even if none is applicable)
				case 0 when c2.ErrorCount > 0:
					return 1;
				case > 0 when c2.ErrorCount == 0:
					return 2;
			}

			// C# 4.0 spec: §7.5.3.2 Better function member
			bool c1IsBetter = false;
			bool c2IsBetter = false;
			bool parameterTypesEqual = true;
			for (int i = 0; i < arguments.Length; i++)
			{
				int p1 = c1.ArgumentToParameterMap[i];
				int p2 = c2.ArgumentToParameterMap[i];
				switch (p1)
				{
					case >= 0 when p2 < 0:
						c1IsBetter = true;
						break;
					case < 0 when p2 >= 0:
						c2IsBetter = true;
						break;
					case >= 0 when p2 >= 0:
					{
						if (!conversions.IdentityConversion(c1.ParameterTypes[p1], c2.ParameterTypes[p2]))
							parameterTypesEqual = false;
						switch (conversions.BetterConversion(arguments[i], c1.ParameterTypes[p1],
							        c2.ParameterTypes[p2]))
						{
							case 1:
								c1IsBetter = true;
								break;
							case 2:
								c2IsBetter = true;
								break;
						}

						break;
					}
				}
			}

			switch (c1IsBetter)
			{
				case true when !c2IsBetter:
					return 1;
				case false when c2IsBetter:
					return 2;
			}

			// prefer members with less errors (part of heuristic that produces a best candidate even if none is applicable)
			if (c1.ErrorCount < c2.ErrorCount)
				return 1;
			if (c1.ErrorCount > c2.ErrorCount)
				return 2;

			if (!c1IsBetter && !c2IsBetter && parameterTypesEqual)
			{
				// we need the tie-breaking rules

				switch (c1.IsGenericMethod)
				{
					// non-generic methods are better
					case false when c2.IsGenericMethod:
						return 1;
					case true when !c2.IsGenericMethod:
						return 2;
				}

				switch (c1.IsExpandedForm)
				{
					// non-expanded members are better
					case false when c2.IsExpandedForm:
						return 1;
					case true when !c2.IsExpandedForm:
						return 2;
				}

				// prefer the member with less arguments mapped to the params-array
				int r = c1.ArgumentsPassedToParamsArray.CompareTo(c2.ArgumentsPassedToParamsArray);
				switch (r)
				{
					case < 0:
						return 1;
					case > 0:
						return 2;
				}

				switch (c1.HasUnmappedOptionalParameters)
				{
					// prefer the member where no default values need to be substituted
					case false when c2.HasUnmappedOptionalParameters:
						return 1;
					case true when !c2.HasUnmappedOptionalParameters:
						return 2;
				}

				// compare the formal parameters
				r = MoreSpecificFormalParameters(c1, c2);
				if (r != 0)
					return r;

				// prefer non-lifted operators
				ILiftedOperator? lift1 = c1.Member as ILiftedOperator;
				ILiftedOperator? lift2 = c2.Member as ILiftedOperator;
				if (lift1 == null && lift2 != null)
					return 1;
				if (lift1 != null && lift2 == null)
					return 2;
			}

			return 0;
		}

		int MoreSpecificFormalParameters(Candidate c1, Candidate c2)
		{
			// prefer the member with more formal parmeters (in case both have different number of optional parameters)
			int r = c1.Parameters.Count.CompareTo(c2.Parameters.Count);
			return r switch {
				> 0 => 1,
				< 0 => 2,
				_ => MoreSpecificFormalParameters(c1.Parameters.Select(p => p.Type), c2.Parameters.Select(p => p.Type))
			};
		}

		static int MoreSpecificFormalParameters(IEnumerable<IType>? t1, IEnumerable<IType?> t2)
		{
			bool c1IsBetter = false;
			bool c2IsBetter = false;
			foreach (var pair in t1.Zip(t2, (a, b) => new { Item1 = a, Item2 = b }))
			{
				switch (MoreSpecificFormalParameter(pair.Item1, pair.Item2))
				{
					case 1:
						c1IsBetter = true;
						break;
					case 2:
						c2IsBetter = true;
						break;
				}
			}

			return c1IsBetter switch {
				true when !c2IsBetter => 1,
				false when c2IsBetter => 2,
				_ => 0
			};
		}

		static int MoreSpecificFormalParameter(IType t1, IType t2)
		{
			if ((t1 is ITypeParameter) && t2 is not ITypeParameter)
				return 2;
			if ((t2 is ITypeParameter) && t1 is not ITypeParameter)
				return 1;

			switch (t1)
			{
				case ParameterizedType p1
					when t2 is ParameterizedType p2 && p1.TypeParameterCount == p2.TypeParameterCount:
				{
					int r = MoreSpecificFormalParameters(p1.TypeArguments, p2.TypeArguments);
					if (r > 0)
						return r;
					break;
				}
				case TypeWithElementType tew1 when t2 is TypeWithElementType tew2:
					return MoreSpecificFormalParameter(tew1.ElementType, tew2.ElementType);
			}

			return 0;
		}

		#endregion

		#region Output Properties

		public IParameterizedMember? BestCandidate {
			get { return bestCandidate?.Member; }
		}

		/// <summary>
		/// Returns the errors that apply to the best candidate.
		/// This includes additional errors that do not affect applicability (e.g. AmbiguousMatch, MethodConstraintsNotSatisfied)
		/// </summary>
		public OverloadResolutionErrors BestCandidateErrors {
			get {
				if (bestCandidate == null)
					return OverloadResolutionErrors.None;
				if (!bestCandidateWasValidated)
				{
					bestCandidateValidationResult = ValidateMethodConstraints(bestCandidate);
					bestCandidateWasValidated = true;
				}

				OverloadResolutionErrors err = bestCandidate.Errors | bestCandidateValidationResult;
				if (bestCandidateAmbiguousWith != null)
					err |= OverloadResolutionErrors.AmbiguousMatch;
				return err;
			}
		}

		public bool FoundApplicableCandidate {
			get { return bestCandidate != null && IsApplicable(bestCandidate.Errors); }
		}

		public IParameterizedMember? BestCandidateAmbiguousWith {
			get { return bestCandidateAmbiguousWith?.Member; }
		}

		public bool BestCandidateIsExpandedForm {
			get { return bestCandidate?.IsExpandedForm ?? false; }
		}

		public bool IsAmbiguous {
			get { return bestCandidateAmbiguousWith != null; }
		}

		public IReadOnlyList<IType>? InferredTypeArguments {
			get {
				if (bestCandidate is { InferredTypes: { } })
					return bestCandidate.InferredTypes;
				return EmptyList<IType>.Instance;
			}
		}

		/// <summary>
		/// Gets the implicit conversions that are being applied to the arguments.
		/// </summary>
		public IList<Conversion?> ArgumentConversions {
			get {
				if (bestCandidate is { ArgumentConversions: { } })
					return bestCandidate.ArgumentConversions;
				return Enumerable.Repeat(Conversion.None, arguments.Length).ToList();
			}
		}

		/// <summary>
		/// Gets an array that maps argument indices to parameter indices.
		/// For arguments that could not be mapped to any parameter, the value will be -1.
		/// 
		/// parameterIndex = GetArgumentToParameterMap()[argumentIndex]
		/// </summary>
		public IReadOnlyList<int>? GetArgumentToParameterMap()
		{
			return bestCandidate?.ArgumentToParameterMap;
		}

		/// <summary>
		/// Returns the arguments for the method call in the order they were provided (not in the order of the parameters).
		/// Arguments are wrapped in a <see cref="ConversionResolveResult"/> if an implicit conversion is being applied
		/// to them when calling the method.
		/// </summary>
		public IList<ResolveResult>? GetArgumentsWithConversions()
		{
			if (bestCandidate == null)
				return arguments;
			return GetArgumentsWithConversions(null, null);
		}

		/// <summary>
		/// Returns the arguments for the method call in the order they were provided (not in the order of the parameters).
		/// Arguments are wrapped in a <see cref="ConversionResolveResult"/> if an implicit conversion is being applied
		/// to them when calling the method.
		/// For arguments where an explicit argument name was provided, the argument will
		/// be wrapped in a <see cref="NamedArgumentResolveResult"/>.
		/// </summary>
		public IList<ResolveResult>? GetArgumentsWithConversionsAndNames()
		{
			if (bestCandidate == null)
				return arguments;
			return GetArgumentsWithConversions(null, GetBestCandidateWithSubstitutedTypeArguments());
		}

		IList<ResolveResult>? GetArgumentsWithConversions(ResolveResult? targetResolveResult,
			IParameterizedMember bestCandidateForNamedArguments)
		{
			var conversions = this.ArgumentConversions;
			ResolveResult[] args = new ResolveResult[arguments.Length];
			for (int i = 0; i < args.Length; i++)
			{
				var argument = arguments[i];
				if (this.IsExtensionMethodInvocation && i == 0 && targetResolveResult != null)
					argument = targetResolveResult;
				int parameterIndex = bestCandidate.ArgumentToParameterMap[i];
				if (parameterIndex >= 0 && conversions[i] != Conversion.IdentityConversion)
				{
					// Wrap argument in ConversionResolveResult
					IType parameterType = bestCandidate.ParameterTypes[parameterIndex];
					if (parameterType.Kind != TypeKind.Unknown)
					{
						if (arguments[i].IsCompileTimeConstant && conversions[i].IsValid &&
						    !conversions[i].IsUserDefined)
						{
							argument = new CSharpResolver(compilation).WithCheckForOverflow(CheckForOverflow)
								.ResolveCast(parameterType, argument);
						}
						else
						{
							argument = new ConversionResolveResult(parameterType, argument, conversions[i],
								CheckForOverflow);
						}
					}
				}

				if (bestCandidateForNamedArguments != null && argumentNames[i] != null)
				{
					// Wrap argument in NamedArgumentResolveResult
					if (parameterIndex >= 0)
					{
						argument = new NamedArgumentResolveResult(
							bestCandidateForNamedArguments.Parameters[parameterIndex], argument,
							bestCandidateForNamedArguments);
					}
					else
					{
						argument = new NamedArgumentResolveResult(argumentNames[i], argument);
					}
				}

				args[i] = argument;
			}

			return args;
		}

		public IParameterizedMember? GetBestCandidateWithSubstitutedTypeArguments()
		{
			if (bestCandidate == null)
				return null;
			if (bestCandidate.Member is IMethod method && method.TypeParameters.Count > 0)
			{
				return ((IMethod)method.MemberDefinition).Specialize(GetSubstitution(bestCandidate));
			}

			return bestCandidate.Member;
		}

		TypeParameterSubstitution? GetSubstitution(Candidate candidate)
		{
			// Do not compose the substitutions, but merge them.
			// This is required for InvocationTests.SubstituteClassAndMethodTypeParametersAtOnce
			return new TypeParameterSubstitution(candidate.Member.Substitution?.ClassTypeArguments,
				candidate.InferredTypes);
		}

		/// <summary>
		/// Creates a ResolveResult representing the result of overload resolution.
		/// </summary>
		/// <param name="targetResolveResult">
		/// The target expression of the call. May be <c>null</c> for static methods/constructors.
		/// </param>
		/// <param name="initializerStatements">
		/// Statements for Objects/Collections initializer.
		/// <see cref="InvocationResolveResult.InitializerStatements"/>
		/// </param>
		/// <param name="returnTypeOverride">
		/// If not null, use this instead of the ReturnType of the member as the type of the created resolve result.
		/// </param>
		public CSharpInvocationResolveResult CreateResolveResult(ResolveResult targetResolveResult,
			IList<ResolveResult>? initializerStatements = null, IType? returnTypeOverride = null)
		{
			IParameterizedMember? member = GetBestCandidateWithSubstitutedTypeArguments();
			if (member == null)
				throw new InvalidOperationException();

			return new CSharpInvocationResolveResult(
				this.IsExtensionMethodInvocation
					? new TypeResolveResult(member.DeclaringType ?? SpecialType.UnknownType)
					: targetResolveResult,
				member,
				GetArgumentsWithConversions(targetResolveResult, member),
				this.BestCandidateErrors,
				this.IsExtensionMethodInvocation,
				this.BestCandidateIsExpandedForm,
				isDelegateInvocation: false,
				argumentToParameterMap: this.GetArgumentToParameterMap(),
				initializerStatements: initializerStatements,
				returnTypeOverride: returnTypeOverride);
		}

		#endregion
	}
}