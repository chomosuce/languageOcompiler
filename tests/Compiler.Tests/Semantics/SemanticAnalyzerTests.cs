using System;
using System.Linq;
using Compiler.Ast;
using Compiler.Semantics;
using Xunit;

namespace Compiler.Tests.Semantics;

public class SemanticAnalyzerTests
{
    [Fact]
    public void ValidProgramPasses()
    {
        var source = @"
class Sample is
    var value : Integer(1)

    method GetValue : Integer => value

    method SetValue(newValue: Integer) is
        value := newValue
    end

    method Combine(other: Integer) : Integer is
        var sum : value.Plus(other)
        return sum
    end

    this(initial: Integer) is
        value := initial
    end
end
";

        SemanticTestHelper.Analyze(source);
    }

    [Fact]
    public void ReturnInsideConstructorThrows()
    {
        var source = @"
class Sample is
    this() is
        return
    end
end
";

        var exception = Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
        Assert.Contains("return", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReturnTypeMismatchThrows()
    {
        var source = @"
class Sample is
    method GetFlag : Integer is
        return Boolean(true)
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void MissingReturnValueThrows()
    {
        var source = @"
class Sample is
    method GetNumber : Integer is
        return
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void VoidMethodReturningValueThrows()
    {
        var source = @"
class Sample is
    method DoWork is
        return Integer(1)
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void UndeclaredVariableUsageThrows()
    {
        var source = @"
class Sample is
    method GetValue : Integer is
        return missing
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void AssignmentTypeMismatchThrows()
    {
        var source = @"
class Sample is
    method Update is
        var value : Integer(0)
        value := Boolean(true)
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void UnknownClassInConstructorCallThrows()
    {
        var source = @"
class Sample is
    method Create is
        var item : Mystery()
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void UnknownMethodCallThrows()
    {
        var source = @"
class Sample is
    method Use : Integer is
        return Missing()
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void MethodCallWrongArityThrows()
    {
        var source = @"
class Sample is
    method OneArg(value: Integer) : Integer => value

    method Use : Integer is
        return OneArg(Integer(1), Integer(2))
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void MethodCallArgumentTypeMismatchThrows()
    {
        var source = @"
class Sample is
    method OneArg(value: Integer) : Integer => value

    method Use : Integer is
        return OneArg(Boolean(true))
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void IfConditionMustBeBoolean()
    {
        var source = @"
class Sample is
    method Check is
        if Integer(1) then
        end
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void WhileConditionMustBeBoolean()
    {
        var source = @"
class Sample is
    method Loop is
        while Integer(1) loop
        end
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void DuplicateClassDeclarationThrows()
    {
        var source = @"
class Sample is
end

class Sample is
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void MissingBaseClassThrows()
    {
        var source = @"
class Derived extends Missing is
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void CyclicInheritanceThrows()
    {
        var source = @"
class First extends Second is
end

class Second extends First is
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void DuplicateFieldDeclarationThrows()
    {
        var source = @"
class Sample is
    var value : Integer(1)
    var value : Integer(2)
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void DuplicateLocalVariableThrows()
    {
        var source = @"
class Sample is
    method Use is
        var value : Integer(1)
        var value : Integer(2)
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void ExpressionBodyWithoutReturnTypeThrows()
    {
        var source = @"
class Sample is
    method Foo => Integer(1)
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void DuplicateMethodImplementationThrows()
    {
        var source = @"
class Sample is
    method Foo : Integer is
        return Integer(1)
    end

    method Foo : Integer is
        return Integer(2)
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void MethodReturnTypeMismatchBetweenDeclarationsThrows()
    {
        var source = @"
class Sample is
    method Foo : Integer
    method Foo : Real is
        return Real(1.0)
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void DuplicateMethodForwardDeclarationThrows()
    {
        var source = @"
class Sample is
    method Foo : Integer
    method Foo : Integer
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void DuplicateConstructorSignatureThrows()
    {
        var source = @"
class Sample is
    this(value: Integer) is
        var tmp : value
    end

    this(value: Integer) is
        var tmp : value
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void MemberAccessUnknownFieldThrows()
    {
        var source = @"
class Sample is
    method Use : Integer is
        return this.Value
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void MemberAccessOnOtherClassUnknownFieldThrows()
    {
        var source = @"
class Helper is
    var value : Integer(1)
end

class Sample is
    method Use : Integer is
        var helper : Helper()
        return helper.Missing
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void ConstructorArgumentMismatchThrows()
    {
        var source = @"
class Sample is
    this(initial: Integer) is
        var value : initial
    end

    method Make is
        var created : Sample()
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void RemovesUnusedLocalVariables()
    {
        var source = @"
class Sample is
    method Compute : Integer is
        var keep : Integer(1)
        var discard : Integer(2)
        return keep
    end
end
";

        var program = SemanticTestHelper.ParseProgram(source);
        SemanticTestHelper.Analyze(program);

        var method = Assert.IsType<MethodDeclarationNode>(program.Classes.Single().Members.Single(m => m is MethodDeclarationNode));
        var body = Assert.IsType<BlockBodyNode>(method.Body);

        Assert.Collection(
            body.Body.Items,
            item =>
            {
                var local = Assert.IsType<VariableDeclarationNode>(item);
                Assert.Equal("keep", local.Name);
            },
            item => Assert.IsType<ReturnStatementNode>(item));
    }

    [Fact]
    public void RemovesUnusedFields()
    {
        var source = @"
class Sample is
    var keep : Integer(1)
    var discard : Integer(2)

    method Get : Integer => keep
end
";

        var program = SemanticTestHelper.ParseProgram(source);
        SemanticTestHelper.Analyze(program);

        var members = program.Classes.Single().Members;

        Assert.Collection(
            members,
            member =>
            {
                var field = Assert.IsType<VariableDeclarationNode>(member);
                Assert.Equal("keep", field.Name);
            },
            member => Assert.IsType<MethodDeclarationNode>(member));
    }

    [Fact]
    public void RemovesUnreachableCodeAfterReturn()
    {
        var source = @"
class Sample is
    method Compute : Integer is
        return Integer(1)
        var unreachable : Integer(2)
        return Integer(2)
    end
end
";

        var program = SemanticTestHelper.ParseProgram(source);
        SemanticTestHelper.Analyze(program);

        var method = Assert.IsType<MethodDeclarationNode>(program.Classes.Single().Members.Single(m => m is MethodDeclarationNode));
        var body = Assert.IsType<BlockBodyNode>(method.Body);

        var singleItem = Assert.Single(body.Body.Items);
        Assert.IsType<ReturnStatementNode>(singleItem);
    }

    [Fact]
    public void ArrayConstructorsAndMethodsProduceSemanticTypes()
    {
        var source = @"
class Sample is
    method Build : Integer is
        var data : Array[Integer](10)
        var size : data.Length()
        var first : data.get(0)
        var updated : data.set(0, Integer(1))
        data := updated
        return size.Plus(first)
    end
end
";

        var program = SemanticTestHelper.ParseProgram(source);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);

        var method = Assert.IsType<MethodDeclarationNode>(program.Classes.Single().Members.Single());
        var body = Assert.IsType<BlockBodyNode>(method.Body);
        var locals = body.Body.Items.OfType<VariableDeclarationNode>().ToArray();
        Assert.Equal(4, locals.Length);

        var model = analyzer.Model;
        var dataCtor = Assert.IsType<ConstructorCallNode>(locals[0].InitialValue);
        Assert.Equal("Array[Integer]", model.VariableTypes[locals[0]].Name);
        Assert.True(model.VariableTypes[locals[0]].IsArray);
        Assert.Equal("Array[Integer]", model.ExpressionTypes[dataCtor].Name);

        var lengthCall = Assert.IsType<CallNode>(locals[1].InitialValue);
        Assert.Equal("Integer", model.VariableTypes[locals[1]].Name);
        Assert.Equal("Integer", model.ExpressionTypes[lengthCall].Name);

        var getCall = Assert.IsType<CallNode>(locals[2].InitialValue);
        Assert.Equal("Integer", model.VariableTypes[locals[2]].Name);
        Assert.Equal("Integer", model.ExpressionTypes[getCall].Name);

        var setCall = Assert.IsType<CallNode>(locals[3].InitialValue);
        Assert.Equal("Array[Integer]", model.VariableTypes[locals[3]].Name);
        Assert.Equal("Array[Integer]", model.ExpressionTypes[setCall].Name);
    }

    [Fact]
    public void ArrayConstructorRequiresIntegerLength()
    {
        var source = @"
class Sample is
    method Build is
        var invalid : Array[Integer](Boolean(true))
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void ListConstructorsAndMethodsProduceSemanticTypes()
    {
        var source = @"
class Item is
end

class Storage is
    method Use : Integer is
        var list : List[Item]()
        var single : List[Item](Item())
        var replicated : List[Item](Item(), 2)
        var appended : list.append(Item())
        var head : appended.head()
        var tail : appended.tail()
        var array : appended.toArray()
        list := replicated
        single := single.append(head)
        list := tail.append(head)
        return array.Length()
    end
end
";

        var program = SemanticTestHelper.ParseProgram(source);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);

        var storage = program.Classes.Single(c => c.Name == "Storage");
        var method = Assert.IsType<MethodDeclarationNode>(storage.Members.Single());
        var body = Assert.IsType<BlockBodyNode>(method.Body);
        var locals = body.Body.Items.OfType<VariableDeclarationNode>().ToArray();
        Assert.Equal(7, locals.Length);

        var model = analyzer.Model;
        Assert.All(locals.Take(4), local =>
        {
            Assert.Equal("List[Item]", model.VariableTypes[local].Name);
        });

        Assert.Equal("Item", model.VariableTypes[locals[4]].Name);
        Assert.Equal("List[Item]", model.VariableTypes[locals[5]].Name);
        Assert.Equal("Array[Item]", model.VariableTypes[locals[6]].Name);

        var appendCall = Assert.IsType<CallNode>(locals[3].InitialValue);
        Assert.Equal("List[Item]", model.ExpressionTypes[appendCall].Name);

        var headCall = Assert.IsType<CallNode>(locals[4].InitialValue);
        Assert.Equal("Item", model.ExpressionTypes[headCall].Name);

        var tailCall = Assert.IsType<CallNode>(locals[5].InitialValue);
        Assert.Equal("List[Item]", model.ExpressionTypes[tailCall].Name);

        var toArrayCall = Assert.IsType<CallNode>(locals[6].InitialValue);
        Assert.Equal("Array[Item]", model.ExpressionTypes[toArrayCall].Name);
    }

    [Fact]
    public void ListAppendRequiresMatchingElementType()
    {
        var source = @"
class Item is
end

class Storage is
    method Use is
        var list : List[Item]()
        var bad : list.append(Integer(1))
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }

    [Fact]
    public void ArrayAssignmentRequiresMatchingElementTypes()
    {
        var source = @"
class Sample is
    method Use is
        var ints : Array[Integer](10)
        var reals : Array[Real](10)
        ints := reals
    end
end
";

        Assert.Throws<SemanticException>(() => SemanticTestHelper.Analyze(source));
    }
}
