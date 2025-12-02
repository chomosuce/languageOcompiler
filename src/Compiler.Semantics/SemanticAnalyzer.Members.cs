using System;
using System.Linq;
using Compiler.Ast;

namespace Compiler.Semantics;

public sealed partial class SemanticAnalyzer
{
    private void AnalyzeFieldDeclaration(ClassSymbol classSymbol, VariableDeclarationNode field)
    {
        // Поле с тем же именем в классе
        if (classSymbol.HasField(field.Name))
        {
            throw new SemanticException($"Field '{field.Name}' is already declared in class '{classSymbol.Name}'.", field);
        }

        var scope = Scope.ForFields();

        foreach (var existingField in classSymbol.Fields)
        {
            scope.Declare(existingField);
        }

        var fieldType = EvaluateExpression(field.InitialValue, scope, classSymbol, MethodContext.None, loopDepth: 0);
        if (fieldType.IsVoid)
        {
            throw new SemanticException("Field initializer cannot have type 'void'.", field.InitialValue);
        }
        TrackVariableType(field, fieldType);
        var symbol = new VariableSymbol(field.Name, fieldType, VariableKind.Field, field);
        classSymbol.AddField(symbol);
        _variableSymbols[field] = symbol;
    }

    private void AnalyzeMethodDeclaration(ClassSymbol classSymbol, MethodDeclarationNode methodNode)
    {
        var parameterTypes = methodNode.Parameters
            .Select(parameter => ResolveTypeNode(parameter.Type, parameter))
            .ToList();

        var methodSymbol = classSymbol.FindMethod(methodNode.Name, parameterTypes)
            ?? throw new SemanticException($"Method '{methodNode.Name}' has not been declared with matching signature.", methodNode);

        if (!ReferenceEquals(methodSymbol.Implementation, methodNode))
        {
            throw new SemanticException($"Method '{methodNode.Name}' implementation does not match the declared signature.", methodNode);
        }

        var scope = Scope.ForMethod();

        foreach (var parameter in methodSymbol.Parameters)
        {
            scope.Declare(parameter.ToVariableSymbol());
        }

        var context = new MethodContext(methodSymbol.ReturnType, true);

        switch (methodNode.Body)
        {
            case ExpressionBodyNode expressionBody:
            {
                var valueType = EvaluateExpression(expressionBody.Expression, scope, classSymbol, context, loopDepth: 0);
                EnsureReturnCompatibility(methodSymbol.ReturnType, valueType, expressionBody);
                break;
            }

            case BlockBodyNode blockBody:
            {
                AnalyzeBody(blockBody.Body, scope, classSymbol, context, loopDepth: 0);
                break;
            }
        }
    }

    private void AnalyzeConstructorDeclaration(ClassSymbol classSymbol, ConstructorDeclarationNode ctorNode)
    {
        var scope = Scope.ForMethod();

        foreach (var parameter in ctorNode.Parameters)
        {
            scope.Declare(new VariableSymbol(parameter.Name, ResolveTypeNode(parameter.Type, parameter), VariableKind.Parameter, parameter));
        }

        var context = MethodContext.None;

        AnalyzeBody(ctorNode.Body, scope, classSymbol, context, loopDepth: 0);
    }
}
