using System.IO;
using System.Linq;
using Compiler.Ast;
using Xunit;

namespace Compiler.Tests.Parsing;

public class ParserCommonScenarioTests
{
    [Fact]
    public void ParsesClassesWithInheritanceAndMembers()
    {
        var source = @"
class Base is
    var seed : Integer(1)
    method GetSeed : Integer => seed
end

class Derived extends Base is
    var factor : Real(2.5)
    method Multiply(value: Integer) : Real is
        var total : value.Mult(seed)
        return total
    end
    method Reset : Integer
    method Reset : Integer is
        var zero : Integer(0)
        return zero
    end
    this(seedValue: Integer, factorValue: Real) is
        var combined : seedValue.Plus(factorValue)
    end
end
";

        var program = ParserTestHelper.ParseProgram(source);

        Assert.Equal(2, program.Classes.Count);

        var baseClass = program.Classes[0];
        Assert.Equal("Base", baseClass.Name);
        Assert.Null(baseClass.BaseClass);
        Assert.Collection(
            baseClass.Members,
            member =>
            {
                var field = Assert.IsType<VariableDeclarationNode>(member);
                Assert.Equal("seed", field.Name);
                var ctor = Assert.IsType<ConstructorCallNode>(field.InitialValue);
                Assert.Equal("Integer", ctor.ClassName);
                var literal = Assert.IsType<IntegerLiteralNode>(Assert.Single(ctor.Arguments));
                Assert.Equal(1, literal.Value);
            },
            member =>
            {
                var method = Assert.IsType<MethodDeclarationNode>(member);
                Assert.Equal("GetSeed", method.Name);
                Assert.Empty(method.Parameters);
                var returnType = method.ReturnType;
                Assert.NotNull(returnType);
                Assert.Equal("Integer", returnType!.Name);
                var body = Assert.IsType<ExpressionBodyNode>(method.Body);
                var id = Assert.IsType<IdentifierNode>(body.Expression);
                Assert.Equal("seed", id.Name);
            });

        var derivedClass = program.Classes[1];
        Assert.Equal("Derived", derivedClass.Name);
        Assert.Equal("Base", derivedClass.BaseClass);

        Assert.Collection(
            derivedClass.Members,
            member =>
            {
                var field = Assert.IsType<VariableDeclarationNode>(member);
                Assert.Equal("factor", field.Name);
                var ctor = Assert.IsType<ConstructorCallNode>(field.InitialValue);
                Assert.Equal("Real", ctor.ClassName);
                var literal = Assert.IsType<RealLiteralNode>(Assert.Single(ctor.Arguments));
                Assert.Equal(2.5, literal.Value);
            },
            member =>
            {
                var method = Assert.IsType<MethodDeclarationNode>(member);
                Assert.Equal("Multiply", method.Name);
                var parameter = Assert.Single(method.Parameters);
                Assert.Equal("value", parameter.Name);
                Assert.Equal("Integer", parameter.Type.Name);
                var returnType = method.ReturnType;
                Assert.NotNull(returnType);
                Assert.Equal("Real", returnType!.Name);

                var body = Assert.IsType<BlockBodyNode>(method.Body);
                var items = body.Body.Items;
                Assert.Equal(2, items.Count);

                var local = Assert.IsType<VariableDeclarationNode>(items[0]);
                Assert.Equal("total", local.Name);
                var call = Assert.IsType<CallNode>(local.InitialValue);
                var callee = Assert.IsType<MemberAccessNode>(call.Callee);
                var callTarget = Assert.IsType<IdentifierNode>(callee.Target);
                Assert.Equal("value", callTarget.Name);
                Assert.Equal("Mult", callee.MemberName);
                var arg = Assert.IsType<IdentifierNode>(Assert.Single(call.Arguments));
                Assert.Equal("seed", arg.Name);

                var ret = Assert.IsType<ReturnStatementNode>(items[1]);
                var retValue = Assert.IsType<IdentifierNode>(ret.Value);
                Assert.Equal("total", retValue.Name);
            },
            member =>
            {
                var forward = Assert.IsType<MethodDeclarationNode>(member);
                Assert.Equal("Reset", forward.Name);
                var returnType = forward.ReturnType;
                Assert.NotNull(returnType);
                Assert.Equal("Integer", returnType!.Name);
                Assert.Null(forward.Body);
            },
            member =>
            {
                var impl = Assert.IsType<MethodDeclarationNode>(member);
                Assert.Equal("Reset", impl.Name);
                var returnType = impl.ReturnType;
                Assert.NotNull(returnType);
                Assert.Equal("Integer", returnType!.Name);
                var body = Assert.IsType<BlockBodyNode>(impl.Body);
                var items = body.Body.Items;
                Assert.Equal(2, items.Count);

                var zeroVar = Assert.IsType<VariableDeclarationNode>(items[0]);
                Assert.Equal("zero", zeroVar.Name);
                var zeroCtor = Assert.IsType<ConstructorCallNode>(zeroVar.InitialValue);
                Assert.Equal("Integer", zeroCtor.ClassName);
                var zeroLiteral = Assert.IsType<IntegerLiteralNode>(Assert.Single(zeroCtor.Arguments));
                Assert.Equal(0, zeroLiteral.Value);

                var ret = Assert.IsType<ReturnStatementNode>(items[1]);
                var retValue = Assert.IsType<IdentifierNode>(ret.Value);
                Assert.Equal("zero", retValue.Name);
            },
            member =>
            {
                var ctor = Assert.IsType<ConstructorDeclarationNode>(member);
                Assert.Equal(2, ctor.Parameters.Count);
        Assert.Equal(("seedValue", "Integer"), (ctor.Parameters[0].Name, ctor.Parameters[0].Type.Name));
        Assert.Equal(("factorValue", "Real"), (ctor.Parameters[1].Name, ctor.Parameters[1].Type.Name));

        var body = ctor.Body;
        var item = Assert.Single(body.Items);
        var local = Assert.IsType<VariableDeclarationNode>(item);
        Assert.Equal("combined", local.Name);
        var call = Assert.IsType<CallNode>(local.InitialValue);
        var callee = Assert.IsType<MemberAccessNode>(call.Callee);
        var left = Assert.IsType<IdentifierNode>(callee.Target);
        Assert.Equal("seedValue", left.Name);
        Assert.Equal("Plus", callee.MemberName);
        var argument = Assert.IsType<IdentifierNode>(Assert.Single(call.Arguments));
        Assert.Equal("factorValue", argument.Name);
    });

        var printer = new AstPrinter();
        var expectedAst = """
ProgramNode
Classes: [
  ClassNode
    Name: Base
    BaseClass: null
    Members: [
      VariableDeclarationNode
        Name: seed
          InitialValue:
          ConstructorCallNode
            ClassName: Integer
            Arguments: [
              IntegerLiteralNode(Value: 1)
            ]

,
      MethodDeclarationNode
        Name: GetSeed
        Parameters: []
          ReturnType:
          TypeNode(Name: Integer)
          Body:
          ExpressionBodyNode
              Expression:
              IdentifierNode(Name: seed)


    ]
,
  ClassNode
    Name: Derived
    BaseClass: Base
    Members: [
      VariableDeclarationNode
        Name: factor
          InitialValue:
          ConstructorCallNode
            ClassName: Real
            Arguments: [
              RealLiteralNode(Value: 2.5)
            ]

,
      MethodDeclarationNode
        Name: Multiply
        Parameters: [
          ParameterNode
            Name: value
              Type:
              TypeNode(Name: Integer)

        ]
          ReturnType:
          TypeNode(Name: Real)
          Body:
          BlockBodyNode
              Body:
              BodyNode
              Items: [
                VariableDeclarationNode
                  Name: total
                    InitialValue:
                    CallNode
                        Callee:
                        MemberAccessNode
                            Target:
                            IdentifierNode(Name: value)
                          MemberName: Mult

                      Arguments: [
                        IdentifierNode(Name: seed)
                      ]

,
                ReturnStatementNode
                    Value:
                    IdentifierNode(Name: total)

              ]

,
      MethodDeclarationNode
        Name: Reset
        Parameters: []
          ReturnType:
          TypeNode(Name: Integer)
        Body: null
,
      MethodDeclarationNode
        Name: Reset
        Parameters: []
          ReturnType:
          TypeNode(Name: Integer)
          Body:
          BlockBodyNode
              Body:
              BodyNode
              Items: [
                VariableDeclarationNode
                  Name: zero
                    InitialValue:
                    ConstructorCallNode
                      ClassName: Integer
                      Arguments: [
                        IntegerLiteralNode(Value: 0)
                      ]

,
                ReturnStatementNode
                    Value:
                    IdentifierNode(Name: zero)

              ]

,
      ConstructorDeclarationNode
        Parameters: [
          ParameterNode
            Name: seedValue
              Type:
              TypeNode(Name: Integer)
,
          ParameterNode
            Name: factorValue
              Type:
              TypeNode(Name: Real)

        ]
          Body:
          BodyNode
          Items: [
            VariableDeclarationNode
              Name: combined
                InitialValue:
                CallNode
                    Callee:
                    MemberAccessNode
                        Target:
                        IdentifierNode(Name: seedValue)
                      MemberName: Plus

                  Arguments: [
                    IdentifierNode(Name: factorValue)
                  ]


          ]

    ]

]

""";




        Assert.Equal(expectedAst, printer.Print(program));
    }

    [Fact]
    public void ParsesGenericArrayAndListConstructors()
    {
        var source = @"
class Collections is
    method Build is
        var numbers : Array[Integer](10)
        var reals : List[Real]()
        var fromValue : List[Real](1.0)
        var replicated : List[Real](1.0, 3)
    end
end
";

        var program = ParserTestHelper.ParseProgram(source);
        var cls = Assert.Single(program.Classes);
        var method = Assert.IsType<MethodDeclarationNode>(Assert.Single(cls.Members));
        var body = Assert.IsType<BlockBodyNode>(method.Body);
        var locals = body.Body.Items.OfType<VariableDeclarationNode>().ToArray();
        Assert.Equal(4, locals.Length);

        var arrayCtor = Assert.IsType<ConstructorCallNode>(locals[0].InitialValue);
        Assert.Equal("Array", arrayCtor.ClassName);
        Assert.NotNull(arrayCtor.GenericArgument);
        Assert.Equal("Integer", arrayCtor.GenericArgument!.Name);

        var listCtor = Assert.IsType<ConstructorCallNode>(locals[1].InitialValue);
        Assert.Equal("List", listCtor.ClassName);
        Assert.NotNull(listCtor.GenericArgument);
        Assert.Equal("Real", listCtor.GenericArgument!.Name);

        var fromValue = Assert.IsType<ConstructorCallNode>(locals[2].InitialValue);
        Assert.Equal("List", fromValue.ClassName);
        Assert.NotNull(fromValue.GenericArgument);
        Assert.Equal("Real", fromValue.GenericArgument!.Name);

        var replicated = Assert.IsType<ConstructorCallNode>(locals[3].InitialValue);
        Assert.Equal("List", replicated.ClassName);
        Assert.NotNull(replicated.GenericArgument);
        Assert.Equal("Real", replicated.GenericArgument!.Name);
    }

    [Fact]
    public void ParsesControlFlowAndCallChains()
    {
        var source = @"
class Algorithms is
    method Max(a: Array) : Integer is
        var max : Integer(0)
        var i : Integer(1)
        while i.Less(a.Length()) loop
            if a.get(i).Greater(max) then
                max := a.get(i)
            else
                max := max
            end
            i := i.Plus(1)
        end
        return max
    end
end
";

        var program = ParserTestHelper.ParseProgram(source);
        var cls = Assert.Single(program.Classes);
        var method = Assert.IsType<MethodDeclarationNode>(Assert.Single(cls.Members));
        var body = Assert.IsType<BlockBodyNode>(method.Body);

        Assert.Equal(4, body.Body.Items.Count);

        var maxDecl = Assert.IsType<VariableDeclarationNode>(body.Body.Items[0]);
        Assert.Equal("max", maxDecl.Name);
        var maxCtor = Assert.IsType<ConstructorCallNode>(maxDecl.InitialValue);
        Assert.Equal("Integer", maxCtor.ClassName);
        Assert.Equal(0, Assert.IsType<IntegerLiteralNode>(Assert.Single(maxCtor.Arguments)).Value);

        var indexDecl = Assert.IsType<VariableDeclarationNode>(body.Body.Items[1]);
        Assert.Equal("i", indexDecl.Name);

        var whileLoop = Assert.IsType<WhileLoopNode>(body.Body.Items[2]);
        var condition = Assert.IsType<CallNode>(whileLoop.Condition);
        var lessAccess = Assert.IsType<MemberAccessNode>(condition.Callee);
        Assert.Equal("Less", lessAccess.MemberName);
        Assert.IsType<IdentifierNode>(lessAccess.Target);
        var lessArg = Assert.IsType<CallNode>(Assert.Single(condition.Arguments));
        var lengthAccess = Assert.IsType<MemberAccessNode>(lessArg.Callee);
        Assert.Equal("Length", lengthAccess.MemberName);
        Assert.Equal("a", Assert.IsType<IdentifierNode>(lengthAccess.Target).Name);
        Assert.Empty(lessArg.Arguments);

        var whileItems = whileLoop.Body.Items;
        Assert.Equal(2, whileItems.Count);

        var ifStmt = Assert.IsType<IfStatementNode>(whileItems[0]);
        var greaterCall = Assert.IsType<CallNode>(ifStmt.Condition);
        var greaterAccess = Assert.IsType<MemberAccessNode>(greaterCall.Callee);
        Assert.Equal("Greater", greaterAccess.MemberName);
        var getCall = Assert.IsType<CallNode>(greaterAccess.Target);
        // a.get(i) is represented as Call(MemberAccess(a, "get"), [Identifier("i")])
        var getAccess = Assert.IsType<MemberAccessNode>(getCall.Callee);
        Assert.Equal("get", getAccess.MemberName);
        Assert.Equal("a", Assert.IsType<IdentifierNode>(getAccess.Target).Name);
        var getArg = Assert.IsType<IdentifierNode>(Assert.Single(getCall.Arguments));
        Assert.Equal("i", getArg.Name);
        Assert.Equal("max", Assert.IsType<IdentifierNode>(Assert.Single(greaterCall.Arguments)).Name);

        var thenBody = ifStmt.ThenBranch.Items;
        Assert.Single(thenBody);
        var assignThen = Assert.IsType<AssignmentNode>(thenBody[0]);
        Assert.Equal("max", Assert.IsType<IdentifierNode>(assignThen.Target).Name);
        var thenValue = Assert.IsType<CallNode>(assignThen.Value);
        var thenCallee = Assert.IsType<MemberAccessNode>(thenValue.Callee);
        Assert.Equal("get", thenCallee.MemberName);
        Assert.Equal("a", Assert.IsType<IdentifierNode>(thenCallee.Target).Name);

        var elseBranch = ifStmt.ElseBranch;
        Assert.NotNull(elseBranch);
        var elseItems = elseBranch!.Items;
        Assert.Single(elseItems);
        var assignElse = Assert.IsType<AssignmentNode>(elseItems[0]);
        Assert.Equal("max", Assert.IsType<IdentifierNode>(assignElse.Target).Name);
        Assert.Equal("max", Assert.IsType<IdentifierNode>(assignElse.Value).Name);

        var increment = Assert.IsType<AssignmentNode>(whileItems[1]);
        Assert.Equal("i", Assert.IsType<IdentifierNode>(increment.Target).Name);
        var plusCall = Assert.IsType<CallNode>(increment.Value);
        Assert.Equal("Plus", Assert.IsType<MemberAccessNode>(plusCall.Callee).MemberName);

        var returnStmt = Assert.IsType<ReturnStatementNode>(body.Body.Items[3]);
        Assert.Equal("max", Assert.IsType<IdentifierNode>(returnStmt.Value).Name);

        var printer = new AstPrinter();
        var expectedAst = """
ProgramNode
Classes: [
  ClassNode
    Name: Algorithms
    BaseClass: null
    Members: [
      MethodDeclarationNode
        Name: Max
        Parameters: [
          ParameterNode
            Name: a
              Type:
              TypeNode(Name: Array)

        ]
          ReturnType:
          TypeNode(Name: Integer)
          Body:
          BlockBodyNode
              Body:
              BodyNode
              Items: [
                VariableDeclarationNode
                  Name: max
                    InitialValue:
                    ConstructorCallNode
                      ClassName: Integer
                      Arguments: [
                        IntegerLiteralNode(Value: 0)
                      ]

,
                VariableDeclarationNode
                  Name: i
                    InitialValue:
                    ConstructorCallNode
                      ClassName: Integer
                      Arguments: [
                        IntegerLiteralNode(Value: 1)
                      ]

,
                WhileLoopNode
                    Condition:
                    CallNode
                        Callee:
                        MemberAccessNode
                            Target:
                            IdentifierNode(Name: i)
                          MemberName: Less

                      Arguments: [
                        CallNode
                            Callee:
                            MemberAccessNode
                                Target:
                                IdentifierNode(Name: a)
                              MemberName: Length

                          Arguments: []

                      ]

                    Body:
                    BodyNode
                    Items: [
                      IfStatementNode
                          Condition:
                          CallNode
                              Callee:
                              MemberAccessNode
                                  Target:
                                  CallNode
                                      Callee:
                                      MemberAccessNode
                                          Target:
                                          IdentifierNode(Name: a)
                                        MemberName: get

                                    Arguments: [
                                      IdentifierNode(Name: i)
                                    ]

                                MemberName: Greater

                            Arguments: [
                              IdentifierNode(Name: max)
                            ]

                          ThenBranch:
                          BodyNode
                          Items: [
                            AssignmentNode
                                Target:
                                IdentifierNode(Name: max)
                                Value:
                                CallNode
                                    Callee:
                                    MemberAccessNode
                                        Target:
                                        IdentifierNode(Name: a)
                                      MemberName: get

                                  Arguments: [
                                    IdentifierNode(Name: i)
                                  ]


                          ]
                          ElseBranch:
                          BodyNode
                          Items: [
                            AssignmentNode
                                Target:
                                IdentifierNode(Name: max)
                                Value:
                                IdentifierNode(Name: max)

                          ]
,
                      AssignmentNode
                          Target:
                          IdentifierNode(Name: i)
                          Value:
                          CallNode
                              Callee:
                              MemberAccessNode
                                  Target:
                                  IdentifierNode(Name: i)
                                MemberName: Plus

                            Arguments: [
                              IntegerLiteralNode(Value: 1)
                            ]


                    ]
,
                ReturnStatementNode
                    Value:
                    IdentifierNode(Name: max)

              ]


    ]

]

""";




        Assert.Equal(expectedAst, printer.Print(program));
    }

    [Fact]
    public void ParsesThisAccessAndGenericConstructorInvocation()
    {
        var source = @"
class Storage is
    var data : Array[Integer](10)
    method HeadValue : Integer => this.data.head()
    method Reset() is
        data := Array[Integer](5)
    end
end
";

        var program = ParserTestHelper.ParseProgram(source);
        var cls = Assert.Single(program.Classes);

        Assert.Collection(
            cls.Members,
            member =>
            {
                var field = Assert.IsType<VariableDeclarationNode>(member);
                Assert.Equal("data", field.Name);
                var ctor = Assert.IsType<ConstructorCallNode>(field.InitialValue);
                Assert.Equal("Array", ctor.ClassName);
                var sizeLiteral = Assert.IsType<IntegerLiteralNode>(Assert.Single(ctor.Arguments));
                Assert.Equal(10, sizeLiteral.Value);
            },
            member =>
            {
                var method = Assert.IsType<MethodDeclarationNode>(member);
                Assert.Equal("HeadValue", method.Name);
                var returnType = method.ReturnType;
                Assert.NotNull(returnType);
                Assert.Equal("Integer", returnType!.Name);
                var body = Assert.IsType<ExpressionBodyNode>(method.Body);
                var outerCall = Assert.IsType<CallNode>(body.Expression);
                var headAccess = Assert.IsType<MemberAccessNode>(outerCall.Callee);
                Assert.Equal("head", headAccess.MemberName);
                var dataAccess = Assert.IsType<MemberAccessNode>(headAccess.Target);
                Assert.Equal("data", dataAccess.MemberName);
                Assert.IsType<ThisNode>(dataAccess.Target);
                Assert.Empty(outerCall.Arguments);
            },
            member =>
            {
                var method = Assert.IsType<MethodDeclarationNode>(member);
                Assert.Equal("Reset", method.Name);
                Assert.Null(method.ReturnType);
                var body = Assert.IsType<BlockBodyNode>(method.Body);
                var assignment = Assert.IsType<AssignmentNode>(Assert.Single(body.Body.Items));
                Assert.Equal("data", Assert.IsType<IdentifierNode>(assignment.Target).Name);
                var ctor = Assert.IsType<ConstructorCallNode>(assignment.Value);
                Assert.Equal("Array", ctor.ClassName);
                var arg = Assert.IsType<IntegerLiteralNode>(Assert.Single(ctor.Arguments));
                Assert.Equal(5, arg.Value);
            });

        var printer = new AstPrinter();
        var expectedAst = """
ProgramNode
Classes: [
  ClassNode
    Name: Storage
    BaseClass: null
    Members: [
      VariableDeclarationNode
        Name: data
          InitialValue:
          ConstructorCallNode
            ClassName: Array
              GenericArgument:
              TypeNode(Name: Integer)
            Arguments: [
              IntegerLiteralNode(Value: 10)
            ]

,
      MethodDeclarationNode
        Name: HeadValue
        Parameters: []
          ReturnType:
          TypeNode(Name: Integer)
          Body:
          ExpressionBodyNode
              Expression:
              CallNode
                  Callee:
                  MemberAccessNode
                      Target:
                      MemberAccessNode
                          Target:
                          ThisNode
                        MemberName: data

                    MemberName: head

                Arguments: []


,
      MethodDeclarationNode
        Name: Reset
        Parameters: []
        ReturnType: null
          Body:
          BlockBodyNode
              Body:
              BodyNode
              Items: [
                AssignmentNode
                    Target:
                    IdentifierNode(Name: data)
                    Value:
                    ConstructorCallNode
                      ClassName: Array
                        GenericArgument:
                        TypeNode(Name: Integer)
                      Arguments: [
                        IntegerLiteralNode(Value: 5)
                      ]


              ]


    ]

]

""";

        Assert.Equal(expectedAst, printer.Print(program));
    }

    [Fact]
    public void MissingEndKeywordCausesParseFailure()
    {
        var source = @"
class Incomplete is
    var value : Integer(0)
";

        using var reader = new StringReader(source);
        var parser = new Compiler.Parser.Parser(new Compiler.Parser.Scanner(reader));

        Assert.False(parser.Parse());
        Assert.Null(parser.Result);
    }

    [Fact]
    public void VariableDeclarationWithoutInitializerFails()
    {
        var source = @"
class Broken is
    var value :
end
";

        using var reader = new StringReader(source);
        var parser = new Compiler.Parser.Parser(new Compiler.Parser.Scanner(reader));

        Assert.False(parser.Parse());
        Assert.Null(parser.Result);
    }
}
