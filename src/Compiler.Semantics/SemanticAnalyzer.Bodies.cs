using System;
using System.Collections.Generic;
using Compiler.Ast;

namespace Compiler.Semantics;

public sealed partial class SemanticAnalyzer
{
    private void AnalyzeBody(BodyNode body, Scope scope, ClassSymbol classSymbol, MethodContext context, int loopDepth)
    {
        var isReachable = true;

        foreach (var item in body.Items)
        {
            if (!isReachable)
            {
                continue;
            }

            switch (item)
            {
                case VariableDeclarationNode local:
                {
                    if (scope.Contains(local.Name))
                    {
                        throw new SemanticException($"Variable '{local.Name}' is already declared in this scope.", local);
                    }

                    var valueType = EvaluateExpression(local.InitialValue, scope, classSymbol, context, loopDepth);
                    TrackVariableType(local, valueType);
                    var symbol = new VariableSymbol(local.Name, valueType, VariableKind.Local, local);
                    scope.Declare(symbol);
                    _variableSymbols[local] = symbol;
                    break;
                }

                case Statement statement:
                    AnalyzeStatement(statement, scope, classSymbol, context, loopDepth);
                    if (statement is ReturnStatementNode)
                    {
                        isReachable = false;
                    }

                    break;
            }
        }

        OptimizeBodyItems(body);
    }

    private void AnalyzeStatement(Statement statement, Scope scope, ClassSymbol classSymbol, MethodContext context, int loopDepth)
    {
        switch (statement)
        {
            case AssignmentNode assignment:
                AnalyzeAssignment(assignment, scope, classSymbol, context);
                break;

            case WhileLoopNode whileLoop:
                AnalyzeWhileLoop(whileLoop, scope, classSymbol, context, loopDepth);
                break;

            case IfStatementNode ifStatement:
                AnalyzeIfStatement(ifStatement, scope, classSymbol, context, loopDepth);
                break;

            case ReturnStatementNode returnStatement:
                AnalyzeReturnStatement(returnStatement, scope, classSymbol, context);
                break;

            default:
                throw new SemanticException($"Unsupported statement of type '{statement.GetType().Name}'.", statement);
        }
    }

    private void AnalyzeAssignment(AssignmentNode assignment, Scope scope, ClassSymbol classSymbol, MethodContext context)
    {
        var targetType = assignment.Target switch
        {
            IdentifierNode identifier => ResolveIdentifierType(identifier, scope, classSymbol),
            MemberAccessNode memberAccess => ResolveMemberAccessType(memberAccess, scope, classSymbol, context),
            _ => throw new SemanticException("Unsupported assignment target.", assignment.Target),
        };

        var valueType = EvaluateExpression(assignment.Value, scope, classSymbol, context, loopDepth: 0);

        if (targetType.IsVoid)
        {
            throw new SemanticException("Cannot assign to a void-typed target.", assignment.Target);
        }

        EnsureTypesCompatible(targetType, valueType, assignment.Value);
    }

    private void AnalyzeWhileLoop(WhileLoopNode loop, Scope scope, ClassSymbol classSymbol, MethodContext context, int loopDepth)
    {
        var conditionType = EvaluateExpression(loop.Condition, scope, classSymbol, context, loopDepth);
        EnsureBooleanExpression(conditionType, loop.Condition);

        var childScope = scope.CreateChild();
        AnalyzeBody(loop.Body, childScope, classSymbol, context, loopDepth + 1);
    }

    private void AnalyzeIfStatement(IfStatementNode statement, Scope scope, ClassSymbol classSymbol, MethodContext context, int loopDepth)
    {
        var conditionType = EvaluateExpression(statement.Condition, scope, classSymbol, context, loopDepth);
        EnsureBooleanExpression(conditionType, statement.Condition);

        var thenScope = scope.CreateChild();
        AnalyzeBody(statement.ThenBranch, thenScope, classSymbol, context, loopDepth);

        if (statement.ElseBranch is not null)
        {
            var elseScope = scope.CreateChild();
            AnalyzeBody(statement.ElseBranch, elseScope, classSymbol, context, loopDepth);
        }
    }

    private void AnalyzeReturnStatement(ReturnStatementNode statement, Scope scope, ClassSymbol classSymbol, MethodContext context)
    {
        if (!context.AllowsReturn)
        {
            throw new SemanticException("The 'return' keyword can only be used inside methods.", statement);
        }

        if (context.ReturnType.IsVoid)
        {
            if (statement.Value is not null)
            {
                throw new SemanticException("Methods without a return type cannot return a value.", statement);
            }

            return;
        }

        if (statement.Value is null)
        {
            throw new SemanticException($"Method must return a value of type '{context.ReturnType.Name}'.", statement);
        }

        var valueType = EvaluateExpression(statement.Value, scope, classSymbol, context, loopDepth: 0);
        EnsureTypesCompatible(context.ReturnType, valueType, statement.Value);
    }
}
