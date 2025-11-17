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
                return Annotate(thisNode, new TypeSymbol(classSymbol.Name));

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
                return Annotate(expression, TypeSymbol.Unknown);
        }
    }

    // Анализирует вызов конструктора: проверяет аргументы и возвращаемый тип
    private TypeSymbol AnalyzeConstructorCall(ConstructorCallNode constructorCall, Scope scope, ClassSymbol classSymbol, MethodContext context, int loopDepth)
    {
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

        if (_classes.TryGetValue(targetType.Name, out var targetClass))
        {
            var method = ResolveMethodOrThrow(targetClass, memberAccess.MemberName, argumentTypes, call);
            EnsureArgumentsCompatible(method, argumentTypes, call);
            return method.ReturnType;
        }

        if (_builtInTypes.Contains(targetType.Name))
        {
            return TypeSymbol.Unknown;
        }

        if (targetType.IsUnknown)
        {
            return TypeSymbol.Unknown;
        }

        throw new SemanticException($"Method '{memberAccess.MemberName}' is not declared on type '{targetType.Name}'.", memberAccess);
    }
}
