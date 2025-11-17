using System;
using System.Collections.Generic;
using System.Linq;
using Compiler.Ast;

namespace Compiler.Semantics;

public sealed partial class SemanticAnalyzer
{
    private MethodSymbol ResolveMethodOrThrow(ClassSymbol classSymbol, string name, IReadOnlyList<TypeSymbol> argumentTypes, Node call)
    {
        var methods = classSymbol.FindMethods(name);

        if (methods.Count == 0)
        {
            throw new SemanticException($"Method '{name}' is not declared on type '{classSymbol.Name}'.", call);
        }

        if (methods.Count == 1)
        {
            var single = methods[0];

            if (single.ArgumentsMatch(argumentTypes))
            {
                return single;
            }

            throw new SemanticException($"Arguments do not match the signature of method '{name}'.", call);
        }

        foreach (var method in methods)
        {
            if (method.ArgumentsMatch(argumentTypes))
            {
                return method;
            }
        }

        throw new SemanticException($"No overload for method '{name}' matches the provided arguments.", call);
    }

    private void EnsureConstructorExists(ClassSymbol classSymbol, IReadOnlyList<TypeSymbol> argumentTypes, ConstructorCallNode call)
    {
        var constructors = classSymbol.Constructors;

        if (constructors.Count == 0)
        {
            if (argumentTypes.Count == 0)
            {
                return;
            }

            throw new SemanticException($"Constructor '{classSymbol.Name}' does not accept arguments.", call);
        }

        if (constructors.Any(constructor => constructor.ArgumentsMatch(argumentTypes)))
        {
            return;
        }

        var argDescription = string.Join(", ", argumentTypes.Select(type => type.Name));
        throw new SemanticException($"No constructor on '{classSymbol.Name}' matches argument types ({argDescription}).", call);
    }

    private TypeSymbol ResolveIdentifierType(IdentifierNode identifier, Scope scope, ClassSymbol classSymbol)
    {
        if (scope.TryLookup(identifier.Name, out var variable))
        {
            variable.MarkUsed();
            return variable.Type;
        }

        if (TryFindField(classSymbol, identifier.Name, out var field))
        {
            field.MarkUsed();
            return field.Type;
        }

        throw new SemanticException($"Identifier '{identifier.Name}' is not declared.", identifier);
    }

    private TypeSymbol ResolveMemberAccessType(MemberAccessNode memberAccess, Scope scope, ClassSymbol classSymbol, MethodContext context)
    {
        var targetType = EvaluateExpression(memberAccess.Target, scope, classSymbol, context, loopDepth: 0);

        if (_classes.TryGetValue(targetType.Name, out var targetClass))
        {
            if (TryFindField(targetClass, memberAccess.MemberName, out var field))
            {
                field.MarkUsed();
                return field.Type;
            }

            throw new SemanticException($"Field '{memberAccess.MemberName}' is not declared on type '{targetType.Name}'.", memberAccess);
        }

        if (_builtInTypes.Contains(targetType.Name))
        {
            return TypeSymbol.Unknown;
        }

        if (targetType.IsUnknown)
        {
            return TypeSymbol.Unknown;
        }

        throw new SemanticException($"Type '{targetType.Name}' is not declared.", memberAccess.Target);
    }

    private bool TryFindField(ClassSymbol classSymbol, string fieldName, out VariableSymbol field)
    {
        var current = classSymbol;

        while (true)
        {
            if (current.TryGetField(fieldName, out field))
            {
                return true;
            }

            if (current.BaseClassName is null || !_classes.TryGetValue(current.BaseClassName, out current))
            {
                break;
            }
        }

        field = null!;
        return false;
    }

    private void EnsureBooleanExpression(TypeSymbol type, Expression expression)
    {
        if (type.IsUnknown || type.Name == TypeSymbol.Boolean.Name)
        {
            return;
        }

        throw new SemanticException("Expected expression of type 'Boolean'.", expression);
    }

    private void EnsureTypesCompatible(TypeSymbol expected, TypeSymbol actual, Node node)
    {
        if (expected.IsUnknown || actual.IsUnknown)
        {
            return;
        }

        if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal))
        {
            throw new SemanticException($"Type mismatch. Expected '{expected.Name}' but found '{actual.Name}'.", node);
        }
    }

    private void EnsureReturnCompatibility(TypeSymbol expected, TypeSymbol actual, Node node)
    {
        if (expected.IsVoid)
        {
            throw new SemanticException("Expression-bodied method must declare a return type.", node);
        }

        EnsureTypesCompatible(expected, actual, node);
    }

    private TypeSymbol ResolveNamedType(string typeName, Node node)
    {
        if (string.Equals(typeName, "Void", StringComparison.OrdinalIgnoreCase))
        {
            return TypeSymbol.Void;
        }

        if (_builtInTypes.Contains(typeName) || _classes.ContainsKey(typeName))
        {
            return new TypeSymbol(typeName);
        }

        return typeName switch
        {
            "Array" => new TypeSymbol(typeName),
            "List" => new TypeSymbol(typeName),
            _ => throw new SemanticException($"Type '{typeName}' is not declared.", node),
        };
    }

    private TypeSymbol ResolveTypeNode(TypeNode typeNode, Node context)
    {
        return typeNode switch
        {
            ArrayTypeNode arrayType => new TypeSymbol($"Array[{ResolveTypeNode(arrayType.ElementType, context).Name}]"),
            ListTypeNode listType => new TypeSymbol($"List[{ResolveTypeNode(listType.ElementType, context).Name}]"),
            _ => ResolveNamedType(typeNode.Name, typeNode),
        };
    }

    private void EnsureArgumentsCompatible(MethodSymbol method, IReadOnlyList<TypeSymbol> arguments, Node node)
    {
        if (method.Parameters.Count != arguments.Count)
        {
            throw new SemanticException($"Method '{method.Name}' expects {method.Parameters.Count} argument(s) but received {arguments.Count}.", node);
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            EnsureTypesCompatible(method.Parameters[i].Type, arguments[i], node);
        }
    }
}
