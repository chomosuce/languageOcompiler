using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Compiler.Ast;

namespace Compiler.LLvm;

/// <summary>
/// Traverses the parsed AST and emits a very small LLVM IR subset.
/// The generator currently understands:
///  * Struct layouts based on class fields (limited type inference).
///  * Methods with expression bodies or a single return statement returning a literal.
/// Unsupported constructs are replaced with reasonable fallbacks so the resulting IR
/// still validates, making it easy to extend coverage incrementally.
/// </summary>
public sealed class LlvmGenerator
{
    public string Generate(ProgramNode program)
    {
        if (program is null)
        {
            throw new ArgumentNullException(nameof(program));
        }

        var builder = new StringBuilder();
        builder.AppendLine("; ModuleID = 'languageOcompiler'");
        builder.AppendLine("source_filename = \"languageO\""); // helps llc/lli error messages
        builder.AppendLine();

        foreach (var classNode in program.Classes)
        {
            EmitClassLayout(builder, classNode);
        }

        if (program.Classes.Count > 0)
        {
            builder.AppendLine();
        }

        foreach (var classNode in program.Classes)
        {
            foreach (var method in classNode.Members.OfType<MethodDeclarationNode>())
            {
                EmitMethod(builder, classNode, method);
            }
        }

        return builder.ToString();
    }

    private void EmitClassLayout(StringBuilder builder, ClassNode classNode)
    {
        var fieldTypes = classNode.Members
            .OfType<VariableDeclarationNode>()
            .Select(InferFieldType)
            .ToList();

        var layout = fieldTypes.Count == 0
            ? "{}"
            : string.Join(", ", fieldTypes);

        builder.Append('%');
        builder.Append(classNode.Name);
        builder.Append(" = type { ");
        builder.Append(layout);
        builder.AppendLine(" }");
    }

    private void EmitMethod(StringBuilder builder, ClassNode classNode, MethodDeclarationNode method)
    {
        // Forward declaration: skip code generation, a full definition must exist elsewhere
        if (method.Body is null)
        {
            return;
        }

        var returnType = ResolveType(method.ReturnType);
        var signature = BuildSignature(classNode, method, returnType);

        builder.Append("define ");
        builder.Append(returnType);
        builder.Append(' ');
        builder.Append(signature);
        builder.AppendLine(" {");
        builder.AppendLine("entry:");

        if (returnType == "void")
        {
            builder.AppendLine("  ret void");
            builder.AppendLine("}");
            builder.AppendLine();
            return;
        }

        var returnExpression = ExtractReturnExpression(method);
        if (returnExpression is not null && TryEmitLiteral(returnExpression, out var literalType, out var literalValue))
        {
            if (literalType == returnType)
            {
                builder.Append("  ret ");
                builder.Append(returnType);
                builder.Append(' ');
                builder.AppendLine(literalValue);
            }
            else
            {
                builder.Append("  ; literal type mismatch (");
                builder.Append(literalType);
                builder.Append(" -> ");
                builder.Append(returnType);
                builder.AppendLine("), returning default");
                builder.Append("  ret ");
                builder.Append(returnType);
                builder.Append(' ');
                builder.AppendLine(GetDefaultValue(returnType));
            }
        }
        else
        {
            builder.AppendLine("  ; unsupported body, emitting default literal");
            builder.Append("  ret ");
            builder.Append(returnType);
            builder.Append(' ');
            builder.AppendLine(GetDefaultValue(returnType));
        }

        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static string MangleMethodName(ClassNode classNode, MethodDeclarationNode method)
    {
        var sb = new StringBuilder();
        sb.Append(classNode.Name);
        sb.Append('_');
        sb.Append(method.Name);

        if (method.Parameters.Count > 0)
        {
            sb.Append("__");
            for (var i = 0; i < method.Parameters.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('_');
                }

                var pType = method.Parameters[i].Type;
                sb.Append(Sanitize(GetMangleTypeName(pType)));
            }
        }

        return "@" + sb.ToString();
    }

    private static string GetMangleTypeName(TypeNode? typeNode)
    {
        return typeNode switch
        {
            null => "void",
            ArrayTypeNode => "Array",
            ListTypeNode => "List",
            _ => typeNode.Name,
        };
    }

    private string BuildSignature(ClassNode classNode, MethodDeclarationNode method, string returnType)
    {
        var parameterList = new List<string>
        {
            $"%{classNode.Name}* %this"
        };

        foreach (var parameter in method.Parameters)
        {
            var llvmType = ResolveType(parameter.Type);
            parameterList.Add($"{llvmType} %{Sanitize(parameter.Name)}");
        }

        var mangledName = MangleMethodName(classNode, method);
        return $"{mangledName}({string.Join(", ", parameterList)})";
    }

    private string InferFieldType(VariableDeclarationNode field)
    {
        var inferred = InferTypeFromExpression(field.InitialValue)
            ?? field.ResolvedType?.Name
            ?? "opaque";

        return ResolveTypeName(inferred, treatAsPointerForCustomType: true);
    }

    private static string? InferTypeFromExpression(Expression expression)
    {
        return expression switch
        {
            IntegerLiteralNode => "Integer",
            RealLiteralNode => "Real",
            BooleanLiteralNode => "Boolean",
            ConstructorCallNode ctor => ctor.ClassName,
            _ => null,
        };
    }

    private Expression? ExtractReturnExpression(MethodDeclarationNode method)
    {
        if (method.Body is ExpressionBodyNode expressionBody)
        {
            return expressionBody.Expression;
        }

        if (method.Body is BlockBodyNode blockBody)
        {
            foreach (var item in blockBody.Body.Items)
            {
                if (item is ReturnStatementNode { Value: { } value })
                {
                    return value;
                }
            }
        }

        return null;
    }

    private bool TryEmitLiteral(Expression expression, out string llvmType, out string literalValue)
    {
        switch (expression)
        {
            case IntegerLiteralNode integerLiteral:
                llvmType = "i32";
                literalValue = integerLiteral.Value.ToString(CultureInfo.InvariantCulture);
                return true;

            case RealLiteralNode realLiteral:
                llvmType = "double";
                literalValue = realLiteral.Value.ToString(CultureInfo.InvariantCulture);
                return true;

            case BooleanLiteralNode booleanLiteral:
                llvmType = "i1";
                literalValue = booleanLiteral.Value ? "1" : "0";
                return true;

            case ConstructorCallNode ctor when TryEmitConstructorLiteral(ctor, out llvmType!, out literalValue!):
                return true;
        }

        llvmType = string.Empty;
        literalValue = string.Empty;
        return false;
    }

    private bool TryEmitConstructorLiteral(ConstructorCallNode ctor, out string llvmType, out string literalValue)
    {
        llvmType = string.Empty;
        literalValue = string.Empty;

        if (ctor.Arguments.Count != 1)
        {
            return false;
        }

        return ctor.ClassName switch
        {
            "Integer" when TryEmitLiteral(ctor.Arguments[0], out llvmType, out literalValue) && llvmType == "i32" => true,
            "Real" when TryEmitLiteral(ctor.Arguments[0], out llvmType, out literalValue) && llvmType == "double" => true,
            "Boolean" when TryEmitLiteral(ctor.Arguments[0], out llvmType, out literalValue) && llvmType == "i1" => true,
            _ => false,
        };
    }

    private string ResolveType(TypeNode? typeNode)
    {
        if (typeNode is null)
        {
            return "void";
        }

        return typeNode switch
        {
            ArrayTypeNode => "%Array*",
            ListTypeNode => "%List*",
            _ => ResolveTypeName(typeNode.Name, treatAsPointerForCustomType: true),
        };
    }

    private string ResolveTypeName(string typeName, bool treatAsPointerForCustomType)
    {
        return typeName switch
        {
            "Integer" => "i32",
            "Real" => "double",
            "Boolean" => "i1",
            "Void" => "void",
            _ => treatAsPointerForCustomType ? $"%{typeName}*" : $"%{typeName}",
        };
    }

    private static string GetDefaultValue(string llvmType)
    {
        return llvmType switch
        {
            "i32" => "0",
            "double" => "0.0",
            "i1" => "0",
            _ => "null",
        };
    }

    private static string Sanitize(string parameterName)
    {
        var sanitized = new StringBuilder(parameterName.Length);
        foreach (var c in parameterName)
        {
            sanitized.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return sanitized.ToString();
    }
}
