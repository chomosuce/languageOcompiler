using System;
using System.Collections.Generic;
using Compiler.Ast;

namespace Compiler.Semantics;

public sealed record SemanticType(string Name, bool IsVoid, bool IsUnknown)
{
    public bool IsBoolean => string.Equals(Name, BuiltInTypes.Boolean.Name, StringComparison.Ordinal);
    public bool IsInteger => string.Equals(Name, BuiltInTypes.Integer.Name, StringComparison.Ordinal);
    public bool IsReal => string.Equals(Name, BuiltInTypes.Real.Name, StringComparison.Ordinal);
    public bool IsPrimitive => IsBoolean || IsInteger || IsReal;
    public bool IsReference => !IsPrimitive && !IsVoid;
}

public sealed record SemanticParameter(string Name, SemanticType Type, ParameterNode Node);

public sealed record SemanticField(string Name, SemanticType Type, VariableDeclarationNode Node);

public sealed record SemanticMethod(string Name, SemanticType ReturnType, IReadOnlyList<SemanticParameter> Parameters, MethodDeclarationNode Declaration);

public sealed record SemanticConstructor(IReadOnlyList<SemanticParameter> Parameters, ConstructorDeclarationNode Declaration);

public sealed record SemanticClass(
    string Name,
    string? BaseClass,
    IReadOnlyList<SemanticField> Fields,
    IReadOnlyList<SemanticMethod> Methods,
    IReadOnlyList<SemanticConstructor> Constructors);

public sealed class SemanticModel
{
    public SemanticModel(
        IReadOnlyDictionary<Expression, SemanticType> expressionTypes,
        IReadOnlyDictionary<VariableDeclarationNode, SemanticType> variableTypes,
        IReadOnlyDictionary<string, SemanticClass> classes)
    {
        ExpressionTypes = expressionTypes;
        VariableTypes = variableTypes;
        Classes = classes;
    }

    public IReadOnlyDictionary<Expression, SemanticType> ExpressionTypes { get; }

    public IReadOnlyDictionary<VariableDeclarationNode, SemanticType> VariableTypes { get; }

    public IReadOnlyDictionary<string, SemanticClass> Classes { get; }

    public SemanticType GetExpressionType(Expression expression) => ExpressionTypes[expression];
}
