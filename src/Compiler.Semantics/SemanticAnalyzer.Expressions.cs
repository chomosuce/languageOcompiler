using System;
using System.Collections.Generic;
using System.Linq;
using Compiler.Ast;

namespace Compiler.Semantics;

public sealed partial class SemanticAnalyzer
{
    // Определяем тип выражения, выбирая обработчик в зависимости от вида узла
    private TypeSymbol EvaluateExpression(Expression expression, Scope scope, ClassSymbol classSymbol, MethodContext context, int loopDepth)
    {
        switch (expression)
        {
            // Литералы возвращают предопределённые типы.
            case IntegerLiteralNode integerLiteral:
                return Annotate(integerLiteral, TypeSymbol.Integer);

            case RealLiteralNode realLiteral:
                return Annotate(realLiteral, TypeSymbol.Real);

            case BooleanLiteralNode booleanLiteral:
                return Annotate(booleanLiteral, TypeSymbol.Boolean);

            // Идентификатор или this берётся из текущей области видимости/класса
            case IdentifierNode identifier:
            {
                var type = ResolveIdentifierType(identifier, scope, classSymbol);
                return Annotate(identifier, type);
            }

            case ThisNode thisNode:
                return Annotate(thisNode, new TypeSymbol(classSymbol.Name, TypeKind.Class));

            // Вызовы конструктора и функций анализируются через отдельные методы.
            case ConstructorCallNode constructorCall:
            {
                var constructedType = AnalyzeConstructorCall(constructorCall, scope, classSymbol, context, loopDepth);
                return Annotate(constructorCall, constructedType);
            }

            case CallNode call:
            {
                var returnType = AnalyzeCallExpression(call, scope, classSymbol, context, loopDepth);
                return Annotate(call, returnType);
            }

            case MemberAccessNode memberAccess:
            {
                var memberType = ResolveMemberAccessType(memberAccess, scope, classSymbol, context);
                return Annotate(memberAccess, memberType);
            }

            default:
                throw new SemanticException($"Unsupported expression of type '{expression.GetType().Name}'.", expression);
        }
    }

    // Анализирует вызов конструктора: проверяет аргументы и возвращаемый тип
    private TypeSymbol AnalyzeConstructorCall(ConstructorCallNode constructorCall, Scope scope, ClassSymbol classSymbol, MethodContext context, int loopDepth)
    {
        if (TryAnalyzeArrayConstructor(constructorCall, scope, classSymbol, context, loopDepth, out var arrayType))
        {
            return arrayType;
        }

        if (TryAnalyzeListConstructor(constructorCall, scope, classSymbol, context, loopDepth, out var listType))
        {
            return listType;
        }

        var argumentTypes = constructorCall.Arguments
            .Select(argument => EvaluateExpression(argument, scope, classSymbol, context, loopDepth))
            .ToList();

        var constructedType = ResolveNamedType(constructorCall.ClassName, constructorCall);

        if (_classes.TryGetValue(constructorCall.ClassName, out var targetClass))
        {
            EnsureConstructorExists(targetClass, argumentTypes, constructorCall);
        }

        return constructedType;
    }

    private bool TryAnalyzeArrayConstructor(ConstructorCallNode constructorCall, Scope scope, ClassSymbol classSymbol, MethodContext context, int loopDepth, out TypeSymbol result)
    {
        result = TypeSymbol.Standard;

        if (!string.Equals(constructorCall.ClassName, "Array", StringComparison.Ordinal))
        {
            return false;
        }

        if (constructorCall.GenericArgument is null)
        {
            throw new SemanticException("Array constructor requires element type specification.", constructorCall);
        }

        var elementType = ResolveTypeNode(constructorCall.GenericArgument, constructorCall);

        if (constructorCall.Arguments.Count != 1)
        {
            throw new SemanticException("Array constructor expects a single length argument.", constructorCall);
        }

        var lengthType = EvaluateExpression(constructorCall.Arguments[0], scope, classSymbol, context, loopDepth);
        if (!string.Equals(lengthType.Name, BuiltInTypes.Integer.Name, StringComparison.Ordinal))
        {
            throw new SemanticException("Array length must be of type Integer.", constructorCall.Arguments[0]);
        }

        result = new TypeSymbol($"Array[{elementType.Name}]", TypeKind.Array);
        return true;
    }

    private bool TryAnalyzeListConstructor(ConstructorCallNode constructorCall, Scope scope, ClassSymbol classSymbol, MethodContext context, int loopDepth, out TypeSymbol result)
    {
        result = TypeSymbol.Standard;

        if (!string.Equals(constructorCall.ClassName, "List", StringComparison.Ordinal))
        {
            return false;
        }

        if (constructorCall.GenericArgument is null)
        {
            throw new SemanticException("List constructor requires element type specification.", constructorCall);
        }

        var elementType = ResolveTypeNode(constructorCall.GenericArgument, constructorCall);

        switch (constructorCall.Arguments.Count)
        {
            case 0:
                break;
            case 1:
            {
                var argumentType = EvaluateExpression(constructorCall.Arguments[0], scope, classSymbol, context, loopDepth);
                EnsureTypesCompatible(elementType, argumentType, constructorCall.Arguments[0]);
                break;
            }
            case 2:
            {
                var valueType = EvaluateExpression(constructorCall.Arguments[0], scope, classSymbol, context, loopDepth);
                EnsureTypesCompatible(elementType, valueType, constructorCall.Arguments[0]);
                var countType = EvaluateExpression(constructorCall.Arguments[1], scope, classSymbol, context, loopDepth);
                if (!string.Equals(countType.Name, BuiltInTypes.Integer.Name, StringComparison.Ordinal))
                {
                    throw new SemanticException("List count argument must be of type Integer.", constructorCall.Arguments[1]);
                }
                break;
            }
            default:
                throw new SemanticException("Unsupported List constructor arity.", constructorCall);
        }

        result = new TypeSymbol($"List[{elementType.Name}]", TypeKind.List);
        return true;
    }

    // Проверяет вызов функции: выводит тип аргументов и выбирает целевой метод
    private TypeSymbol AnalyzeCallExpression(CallNode call, Scope scope, ClassSymbol classSymbol, MethodContext context, int loopDepth)
    {
        var argumentTypes = call.Arguments
            .Select(argument => EvaluateExpression(argument, scope, classSymbol, context, loopDepth))
            .ToList();

        return call.Callee switch
        {
            IdentifierNode identifier => ResolveIdentifierCall(identifier, argumentTypes, scope, classSymbol, call),
            MemberAccessNode memberAccess => ResolveMemberCall(memberAccess, argumentTypes, scope, classSymbol, call, loopDepth),
            _ => throw new SemanticException("Unsupported call target expression.", call.Callee),
        };
    }

    // Вызов по идентификатору ищет метод в текущем классе и сверяет сигнатуру
    private TypeSymbol ResolveIdentifierCall(IdentifierNode identifier, IReadOnlyList<TypeSymbol> argumentTypes, Scope scope, ClassSymbol classSymbol, CallNode call)
    {
        var method = ResolveMethodOrThrow(classSymbol, identifier.Name, argumentTypes, call);
        EnsureArgumentsCompatible(method, argumentTypes, call);
        return method.ReturnType;
    }

    // Вызов через доступ к члену повторно вычисляет тип целевого объекта и ищет метод в его классе
    private TypeSymbol ResolveMemberCall(MemberAccessNode memberAccess, IReadOnlyList<TypeSymbol> argumentTypes, Scope scope, ClassSymbol currentClass, CallNode call, int loopDepth)
    {
        var targetType = EvaluateExpression(memberAccess.Target, scope, currentClass, MethodContext.None, loopDepth);

        if (TryResolveBuiltInMethod(targetType, memberAccess.MemberName, argumentTypes, call, out var builtInResult))
        {
            return builtInResult;
        }

        if (_classes.TryGetValue(targetType.Name, out var targetClass))
        {
            var method = ResolveMethodOrThrow(targetClass, memberAccess.MemberName, argumentTypes, call);
            EnsureArgumentsCompatible(method, argumentTypes, call);
            return method.ReturnType;
        }

        if (_builtInTypes.Contains(targetType.Name))
        {
            return TypeSymbol.Standard;
        }

        if (targetType.IsStandard)
        {
            return TypeSymbol.Standard;
        }

        throw new SemanticException($"Method '{memberAccess.MemberName}' is not declared on type '{targetType.Name}'.", memberAccess);
    }

    private bool TryResolveBuiltInMethod(TypeSymbol targetType, string methodName, IReadOnlyList<TypeSymbol> argumentTypes, CallNode call, out TypeSymbol result)
    {
        if ((string.Equals(targetType.Name, BuiltInTypes.Integer.Name, StringComparison.Ordinal) ||
             string.Equals(targetType.Name, BuiltInTypes.Real.Name, StringComparison.Ordinal) ||
             string.Equals(targetType.Name, BuiltInTypes.Boolean.Name, StringComparison.Ordinal)) &&
            string.Equals(methodName, "Print", StringComparison.Ordinal))
        {
            if (argumentTypes.Count != 0)
            {
                throw new SemanticException("Print() does not accept arguments.", call);
            }

            result = targetType;
            return true;
        }

        if (targetType.IsArray && TryResolveArrayMethod(targetType, methodName, argumentTypes, call, out result))
        {
            return true;
        }

        if (targetType.IsList && TryResolveListMethod(targetType, methodName, argumentTypes, call, out result))
        {
            return true;
        }

        result = TypeSymbol.Standard;
        return false;
    }

    private bool TryResolveArrayMethod(TypeSymbol arrayType, string methodName, IReadOnlyList<TypeSymbol> argumentTypes, CallNode call, out TypeSymbol result)
    {
        result = TypeSymbol.Standard;

        if (!arrayType.TryGetArrayElementType(out var elementTypeName))
        {
            return false;
        }

        var elementType = ResolveNamedType(elementTypeName, call);

        switch (methodName)
        {
            case "Length":
            {
                if (argumentTypes.Count != 0)
                {
                    throw new SemanticException("Array.Length() does not accept arguments.", call);
                }

                result = TypeSymbol.Integer;
                return true;
            }

            case "get":
            {
                if (argumentTypes.Count != 1)
                {
                    throw new SemanticException("Array.get(index) expects a single argument.", call);
                }

                EnsureTypesCompatible(TypeSymbol.Integer, argumentTypes[0], call);
                result = elementType;
                return true;
            }

            case "set":
            {
                if (argumentTypes.Count != 2)
                {
                    throw new SemanticException("Array.set(index, value) expects two arguments.", call);
                }

                EnsureTypesCompatible(TypeSymbol.Integer, argumentTypes[0], call);
                EnsureTypesCompatible(elementType, argumentTypes[1], call);
                result = new TypeSymbol(arrayType.Name, TypeKind.Array);
                return true;
            }
        }

        return false;
    }

    private bool TryResolveListMethod(TypeSymbol listType, string methodName, IReadOnlyList<TypeSymbol> argumentTypes, CallNode call, out TypeSymbol result)
    {
        result = TypeSymbol.Standard;

        if (!listType.TryGetListElementType(out var elementTypeName))
        {
            return false;
        }

        var elementType = ResolveNamedType(elementTypeName, call);

        switch (methodName)
        {
            case "append":
            {
                if (argumentTypes.Count != 1)
                {
                    throw new SemanticException("List.append(value) expects a single argument.", call);
                }

                EnsureTypesCompatible(elementType, argumentTypes[0], call);
                result = new TypeSymbol(listType.Name, TypeKind.List);
                return true;
            }

            case "head":
            {
                if (argumentTypes.Count != 0)
                {
                    throw new SemanticException("List.head() does not accept arguments.", call);
                }

                result = elementType;
                return true;
            }

            case "tail":
            {
                if (argumentTypes.Count != 0)
                {
                    throw new SemanticException("List.tail() does not accept arguments.", call);
                }

                result = new TypeSymbol(listType.Name, TypeKind.List);
                return true;
            }

            case "toArray":
            {
                if (argumentTypes.Count != 0)
                {
                    throw new SemanticException("List.toArray() does not accept arguments.", call);
                }

                result = new TypeSymbol($"Array[{elementType.Name}]", TypeKind.Array);
                return true;
            }
        }

        return false;
    }
}
