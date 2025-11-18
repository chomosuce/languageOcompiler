using System;
using System.Collections.Generic;
using Compiler.Ast;

namespace Compiler.Semantics;

public sealed record SemanticType(string Name, TypeKind Kind)
{
    public bool IsVoid => Kind == TypeKind.Void;
    public bool IsStandard => Kind == TypeKind.Standard;

    public bool IsBoolean => Kind == TypeKind.Boolean;
    public bool IsInteger => Kind == TypeKind.Integer;
    public bool IsReal => Kind == TypeKind.Real;

    public bool IsArray => Kind == TypeKind.Array;
    public bool IsList => Kind == TypeKind.List;

    public bool IsPrimitive => Kind is TypeKind.Boolean or TypeKind.Integer or TypeKind.Real;

    public bool IsReference => Kind is TypeKind.Class or TypeKind.Array or TypeKind.List;

    public bool TryGetArrayElementType(out string elementType)
    {
        if (Kind != TypeKind.Array)
        {
            elementType = string.Empty;
            return false;
        }

        return TypeNameHelper.TryGetArrayElementType(Name, out elementType);
    }

    public bool TryGetListElementType(out string elementType)
    {
        if (Kind != TypeKind.List)
        {
            elementType = string.Empty;
            return false;
        }

        return TypeNameHelper.TryGetListElementType(Name, out elementType);
    }

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
