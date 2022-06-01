using ICSharpCode.Decompiler.CSharp.Syntax.PatternMatching;

namespace ICSharpCode.Decompiler.CSharp.Syntax
{
	public sealed class InterpolatedStringExpression : Expression
	{
		public static readonly TokenRole OpenQuote = new("$\"");
		public static readonly TokenRole CloseQuote = new("\"");

		public AstNodeCollection<InterpolatedStringContent> Content {
			get { return GetChildrenByRole(InterpolatedStringContent.Role); }
		}

		public override void AcceptVisitor(IAstVisitor visitor)
		{
			visitor.VisitInterpolatedStringExpression(this);
		}

		public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
		{
			return visitor.VisitInterpolatedStringExpression(this);
		}

		public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitInterpolatedStringExpression(this, data);
		}

		protected internal override bool DoMatch(AstNode? other, Match match)
		{
			return other is InterpolatedStringExpression { IsNull: false } o && this.Content.DoMatch(o.Content, match);
		}
	}

	public abstract class InterpolatedStringContent : AstNode
	{
		public new static readonly InterpolatedStringContent Null = new NullInterpolatedStringContent();
		public new static readonly Role<InterpolatedStringContent> Role = new("InterpolatedStringContent", Null);

		public override NodeType NodeType => NodeType.Unknown;

		#region Null

		sealed class NullInterpolatedStringContent : InterpolatedStringContent
		{
			public override bool IsNull {
				get {
					return true;
				}
			}

			public override void AcceptVisitor(IAstVisitor visitor)
			{
				visitor.VisitNullNode(this);
			}

			public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
			{
				return visitor.VisitNullNode(this);
			}

			public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
			{
				return visitor.VisitNullNode(this, data);
			}

			protected internal override bool DoMatch(AstNode other, Match match)
			{
				return other == null || other.IsNull;
			}
		}

		#endregion
	}

	/// <summary>
	/// { Expression }
	/// </summary>
	public sealed class Interpolation : InterpolatedStringContent
	{
		public static readonly TokenRole LBrace = new("{");
		public static readonly TokenRole RBrace = new("}");

		public Interpolation()
		{
		}

		public Interpolation(Expression expression, string suffix = null)
		{
			Expression = expression;
			Suffix = suffix;
		}

		public CSharpTokenNode LBraceToken {
			get { return GetChildByRole(LBrace); }
		}

		public Expression Expression {
			get { return GetChildByRole(Roles.Expression); }
			init { SetChildByRole(Roles.Expression, value); }
		}

		public string? Suffix { get; }

		public CSharpTokenNode RBraceToken {
			get { return GetChildByRole(RBrace); }
		}

		public override void AcceptVisitor(IAstVisitor visitor)
		{
			visitor.VisitInterpolation(this);
		}

		public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
		{
			return visitor.VisitInterpolation(this);
		}

		public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitInterpolation(this, data);
		}

		protected internal override bool DoMatch(AstNode? other, Match match)
		{
			return other is Interpolation o && this.Expression.DoMatch(o.Expression, match);
		}
	}

	public sealed class InterpolatedStringText : InterpolatedStringContent
	{
		public InterpolatedStringText()
		{
		}

		public InterpolatedStringText(string? text)
		{
			Text = text;
		}

		public string Text { get; set; }

		public override void AcceptVisitor(IAstVisitor visitor)
		{
			visitor.VisitInterpolatedStringText(this);
		}

		public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
		{
			return visitor.VisitInterpolatedStringText(this);
		}

		public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitInterpolatedStringText(this, data);
		}

		protected internal override bool DoMatch(AstNode? other, Match match)
		{
			return other is InterpolatedStringText o && o.Text == this.Text;
		}
	}
}