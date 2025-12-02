using System.Collections.Generic;


namespace Compiler.Ast
{
    // BASE TYPES
    public abstract record Node
    {
        public int Line { get; init; }
        public int Column { get; init; }
    
        // for visitor
        public abstract T Accept<T>(IAstVisitor<T> visitor);
    }

    // Element of method/constructor body:
    // - statement (Statement)
    // - local variable (VariableDeclarationNode)
    public interface IBodyItem { } // mark that type can be nested in BodyNode
    public abstract record Expression : Node;
    public abstract record Statement : Node, IBodyItem;
    public abstract record Member : Node;

    // PROGRAM AND CLASS

    // AST-root contains list of classes: [ClassNode("A", ...)]

    public record ProgramNode(List<ClassNode> Classes) : Node
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Class declaration
    // AST:
    //   new ClassNode(Name:"B", BaseClass:"A", Members:[...])
    public record ClassNode(
        string Name,
        string? BaseClass,          // null if no inheritance
        List<Member> Members
    ) : Node
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // CLASS MEMBERS

    // Declaration of a class or local variable (in the body):
    //   VariableDeclaration : var Identifier : Expression
    //
    // O example:
    //   var x : Integer(5)      // literal Integer(5)
    //   var b : Boolean(true)
    //
    // AST:
    //   new VariableDeclarationNode("x", new ConstructorCallNode("Integer",[IntLiteral(5)]))
    //   new VariableDeclarationNode("b", new ConstructorCallNode("Boolean",[BoolLiteral(true)]))
    public record VariableDeclarationNode(
        string Name,
        Expression InitialValue,
        TypeNode? ResolvedType = null // for semantics (final inferred type)
    ) : Member, IBodyItem
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Method declaration
    //
    // Supporting forward declarations (Body == null), as in specification:
    //   MethodDeclaration : MethodHeader [ MethodBody ]
    //
    // Examples of O headers:
    //   method getX : Integer
    //   method inc(a: Integer)
    //   method max(a: Integer, b: Integer) : Integer
    //
    // Short body: (ExpresionBodyNode)
    //   method getX : Integer => x
    //
    // Full body: (BlockBodyNode)
    //   method inc(a: Integer) is
    //       x := x.Plus(a)
    //   end
    public record MethodDeclarationNode(
        string Name,
        List<ParameterNode> Parameters,
        TypeNode? ReturnType,       // null if method does not return a value
        MethodBodyNode? Body        // null => no body
    ) : Member
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Constructor declaration
    //
    // O example:
    //   this(p: Integer, q: Integer) is
    //       var s : p.Plus(q)
    //       ...
    //   end
    public record ConstructorDeclarationNode(
        List<ParameterNode> Parameters,
        BodyNode Body
    ) : Member
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Method body:
    // - BlockBodyNode: "is Body end"
    // - ExpressionBodyNode: "=> Expression" (only for methods)
    public abstract record MethodBodyNode : Node
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // "=> Expression"
    //
    // O example:
    //   method getX : Integer => x
    //
    // AST:
    //   new MethodDeclarationNode("getX", [], Integer, new ExpressionBodyNode(Identifier("x")))
    //   [] - passed parameters
    public record ExpressionBodyNode(Expression Expression) : MethodBodyNode
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // "is Body end"
    //
    // O example:
    //   method inc(a: Integer) is
    //       x := x.Plus(a)
    //   end
    public record BlockBodyNode(BodyNode Body) : MethodBodyNode
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // TYPES

    public record TypeNode(string Name) : Node
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    public static class BuiltInTypes
    {
        public static readonly TypeNode Integer = new TypeNode("Integer");
        public static readonly TypeNode Real = new TypeNode("Real");
        public static readonly TypeNode Boolean = new TypeNode("Boolean");
    }

    // Method/constructor parameter:
    //
    // O example:
    //   method inc(a: Integer)
    //                  ^------ ParameterNode("a", TypeNode("Integer"))
    public record ParameterNode(
        string Name,
        TypeNode Type
    ) : Node
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // METHOD/CONSTRUCTOR BODY

    // Body — is a list of "body elements": local vars AND/OR statements.
    //
    // O example:
    //   method foo is
    //       var i : Integer(1)          // VariableDeclarationNode
    //       while i.Less(10) loop       // WhileLoopNode
    //           i := i.Plus(1)          // AssignmentNode
    //       end
    //   end
    public record BodyNode(
        List<IBodyItem> Items
    ) : Node
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // STATEMENTS

    // Specification gives form "Identifier := Expression".
    // In practice, it is convenient to allow MemberAccess (this.x) on the left as well.
    //
    // O examples:
    //   x := Integer(5)
    //   this.x := x.Plus(1)
    //
    // AST:
    //   new AssignmentNode(Identifier("x"), ConstructorCall("Integer",[Int(5)]))
    //   new AssignmentNode(MemberAccess(This(),"x"), Call(MemberAccess(Identifier("x"),"Plus"), [Int(1)]))
    public record AssignmentNode(
        Expression Target,
        Expression Value
    ) : Statement
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // While loop:
    //
    // O example:
    //   while i.LessEqual(n) loop
    //       ...
    //   end
    //
    // AST:
    //   new WhileLoopNode(cond: Call(MemberAccess(Id("i"),"LessEqual"), [Id("n")]), body: ...)
    public record WhileLoopNode(
        Expression Condition,
        BodyNode Body
    ) : Statement
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // If statement:
    //
    // Example:
    //   if b then
    //       ...
    //   end
    //
    //   if a.Greater(b) then
    //       ...
    //   else
    //       ...
    //   end
    public record IfStatementNode(
        Expression Condition,
        BodyNode ThenBranch,
        BodyNode? ElseBranch        // null if no else
    ) : Statement
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Return from method:
    //
    // Example:
    //   return
    //   return x
    public record ReturnStatementNode(
        Expression? Value           // null if return without value
    ) : Statement
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Expression used as a standalone statement (side-effect only)
    public record ExpressionStatementNode(Expression Expression) : Statement
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // EXPRESSIONS
    
    public record IntegerLiteralNode(int Value) : Expression
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    public record RealLiteralNode(double Value) : Expression
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    public record BooleanLiteralNode(bool Value) : Expression
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Identifier of variable/method
    public record IdentifierNode(string Name) : Expression
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Keyword 'this' — current object
    public record ThisNode() : Expression
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Constructor call: ClassName [Arguments]
    //
    // O examples:
    //   Integer(5)
    //   Boolean(true)
    //
    // AST:
    //   new ConstructorCallNode("Integer", [Int(5)])
    public record ConstructorCallNode(
        string ClassName,
        List<Expression> Arguments,
        TypeNode? GenericArgument = null
    ) : Expression
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Unified call: Expression [ Arguments ]
    //
    // In O language calls are "expression followed by arguments"
    // This covers method calls (via dot) and "free" calls
    // (actually methods of the current object).
    //
    // O examples:
    //   x.Plus(1)                 => Call(MemberAccess(Id("x"),"Plus"), [Int(1)])
    //   head()                    => Call(Identifier("head"), [])
    //   a.b().c(d).e              => chains via MemberAccess + Call
    public record CallNode(
        Expression Callee,
        List<Expression> Arguments
    ) : Expression
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Member access via dot
    //
    // O examples:
    //   this.x                    => MemberAccess(This(),"x")
    //   a.Length                  => MemberAccess(Id("a"),"Length")
    //
    // Combined with CallNode we get calls: a.get(i) => Call(MemberAccess(Id("a"),"get"), [Id("i")])
    public record MemberAccessNode(
        Expression Target,
        string MemberName
    ) : Expression
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    // Array type: Array[TypeNode]
    public record ArrayTypeNode(TypeNode ElementType) : TypeNode("Array")
    {
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }

    public interface IAstVisitor<T>
    {
        T Visit(ProgramNode node);
        T Visit(ClassNode node);
        T Visit(VariableDeclarationNode node);
        T Visit(MethodDeclarationNode node);
        T Visit(ConstructorDeclarationNode node);
        T Visit(TypeNode node);
        T Visit(ParameterNode node);
        T Visit(BodyNode node);
        T Visit(AssignmentNode node);
        T Visit(WhileLoopNode node);
        T Visit(IfStatementNode node);
        T Visit(ReturnStatementNode node);
        T Visit(ExpressionStatementNode node);
        T Visit(IntegerLiteralNode node);
        T Visit(RealLiteralNode node);
        T Visit(BooleanLiteralNode node);
        T Visit(IdentifierNode node);
        T Visit(ThisNode node);
        T Visit(ConstructorCallNode node);
        T Visit(CallNode node);
        T Visit(MemberAccessNode node);
        T Visit(MethodBodyNode node);     // type for ExpressionBody/BlockBody
        T Visit(ExpressionBodyNode node);
        T Visit(BlockBodyNode node);
        T Visit(ArrayTypeNode node);
    }
}
