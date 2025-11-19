using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Compiler.Ast;
using Compiler.Semantics;

namespace Compiler.LLvm;

public sealed class LlvmGenerator
{
    private readonly SemanticModel _semanticModel;
    private readonly Dictionary<string, ClassLayout> _layouts = new(StringComparer.Ordinal);
    private int _nextClassId;

    private const string IntegerFormatGlobal = "@.fmt_int";
    private const string RealFormatGlobal = "@.fmt_real";
    private const int IntegerFormatLength = 4;
    private const int RealFormatLength = 4;

    private static readonly SemanticType IntegerType = new(BuiltInTypes.Integer.Name, TypeKind.Integer);
    private static readonly SemanticType RealType = new(BuiltInTypes.Real.Name, TypeKind.Real);
    private static readonly SemanticType BooleanType = new(BuiltInTypes.Boolean.Name, TypeKind.Boolean);

    public LlvmGenerator(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public string Generate(ProgramNode program)
    {
        if (program is null)
        {
            throw new ArgumentNullException(nameof(program));
        }

        var layouts = BuildLayouts(program);
        var builder = new StringBuilder();
        builder.AppendLine("; ModuleID = 'languageOcompiler'");
        builder.AppendLine("source_filename = \"languageO\"");
        EmitRuntimeTypeDefinitions(builder);
        builder.AppendLine();
        EmitRuntimeDeclarations(builder);
        builder.AppendLine();
        EmitRuntimeConstants(builder);
        builder.AppendLine();

        EmitTypeDefinitions(builder, layouts);
        builder.AppendLine();
        EmitConstructors(builder, layouts);
        EmitMethods(builder, layouts);
        EmitProgramEntry(builder, layouts, program);

        return builder.ToString();
    }

    private IReadOnlyDictionary<string, ClassLayout> BuildLayouts(ProgramNode program)
    {
        _layouts.Clear();
        _nextClassId = 0;

        foreach (var classNode in program.Classes)
        {
            if (_semanticModel.Classes.TryGetValue(classNode.Name, out var semanticClass))
            {
                EnsureLayout(semanticClass);
            }
        }

        return _layouts;
    }

    private ClassLayout EnsureLayout(SemanticClass semanticClass)
    {
        if (_layouts.TryGetValue(semanticClass.Name, out var cached))
        {
            return cached;
        }

        var inheritedFields = new List<FieldLayout>();
        ClassLayout? baseLayout = null;

        if (semanticClass.BaseClass is not null &&
            _semanticModel.Classes.TryGetValue(semanticClass.BaseClass, out var baseClass))
        {
            baseLayout = EnsureLayout(baseClass);
            foreach (var baseField in baseLayout.Fields)
            {
                inheritedFields.Add(baseField with { Index = inheritedFields.Count });
            }
        }
        else
        {
            inheritedFields.Add(new FieldLayout("__classId", "i32", IntegerType, 0));
        }

        var fields = new List<FieldLayout>(inheritedFields);

        foreach (var field in semanticClass.Fields)
        {
            fields.Add(new FieldLayout(field.Name, ResolveTypeName(field.Type), field.Type, fields.Count));
        }

        var layout = new ClassLayout(semanticClass, ++_nextClassId, fields, baseLayout);
        _layouts[semanticClass.Name] = layout;
        baseLayout?.DerivedClasses.Add(layout);

        if (baseLayout is not null)
        {
            foreach (var (key, value) in baseLayout.Methods)
            {
                layout.Methods[key] = value;
            }
        }

        foreach (var method in semanticClass.Methods)
        {
            var signature = CreateMethodSignature(method);
            layout.Methods[signature] = new MethodImplementation(layout, method);
        }

        return layout;
    }

    private static void EmitRuntimeTypeDefinitions(StringBuilder builder)
    {
        builder.AppendLine("%Array = type { i32, i8* }");
        builder.AppendLine("%List = type { i8* }");
    }

    private static void EmitRuntimeDeclarations(StringBuilder builder)
    {
        builder.AppendLine("declare i8* @malloc(i64)");
        builder.AppendLine("declare %Array* @o_array_new(i32)");
        builder.AppendLine("declare i32 @o_array_length(%Array*)");
        builder.AppendLine("declare i8* @o_array_get(%Array*, i32)");
        builder.AppendLine("declare void @o_array_set(%Array*, i32, i8*)");
        builder.AppendLine("declare %List* @o_list_empty()");
        builder.AppendLine("declare %List* @o_list_singleton(i8*)");
        builder.AppendLine("declare %List* @o_list_replicate(i8*, i32)");
        builder.AppendLine("declare %List* @o_list_append(%List*, i8*)");
        builder.AppendLine("declare i8* @o_list_head(%List*)");
        builder.AppendLine("declare %List* @o_list_tail(%List*)");
        builder.AppendLine("declare %Array* @o_list_to_array(%List*)");
        builder.AppendLine("declare i32 @printf(i8*, ...)");
    }

    private static void EmitRuntimeConstants(StringBuilder builder)
    {
        builder.AppendLine(@"@.fmt_int = private unnamed_addr constant [4 x i8] c""%d\0A\00""");
        builder.AppendLine(@"@.fmt_real = private unnamed_addr constant [4 x i8] c""%f\0A\00""");
    }

    private void EmitTypeDefinitions(StringBuilder builder, IReadOnlyDictionary<string, ClassLayout> layouts)
    {
        foreach (var layout in layouts.Values.OrderBy(l => l.ClassId))
        {
            var fieldList = layout.Fields.Count == 0
                ? string.Empty
                : string.Join(", ", layout.Fields.Select(field => field.LlvmType));

            builder.Append('%');
            builder.Append(layout.Name);
            builder.Append(" = type { ");
            builder.Append(fieldList);
            builder.AppendLine(" }");
        }
    }

    private void EmitConstructors(StringBuilder builder, IReadOnlyDictionary<string, ClassLayout> layouts)
    {
        foreach (var layout in layouts.Values.OrderBy(l => l.ClassId))
        {
            foreach (var ctor in layout.SemanticClass.Constructors)
            {
                EmitConstructor(builder, layout, ctor);
            }
        }
    }

    private void EmitMethods(StringBuilder builder, IReadOnlyDictionary<string, ClassLayout> layouts)
    {
        foreach (var layout in layouts.Values.OrderBy(l => l.ClassId))
        {
            foreach (var method in layout.SemanticClass.Methods)
            {
                EmitMethod(builder, layout, method);
            }
        }
    }

    private void EmitProgramEntry(StringBuilder builder, IReadOnlyDictionary<string, ClassLayout> layouts, ProgramNode program)
    {
        builder.AppendLine("define i32 @main()");
        builder.AppendLine("{");
        builder.AppendLine("entry:");

        var startClass = DetermineStartClass(layouts, program);

        if (startClass is null)
        {
            builder.AppendLine("  ret i32 0");
            builder.AppendLine("}");
            builder.AppendLine();
            return;
        }

        builder.Append("  %size_ptr = getelementptr %");
        builder.Append(startClass.Name);
        builder.Append(", %");
        builder.Append(startClass.Name);
        builder.AppendLine("* null, i32 1");

        builder.Append("  %size = ptrtoint %");
        builder.Append(startClass.Name);
        builder.AppendLine("* %size_ptr to i64");

        builder.AppendLine("  %raw = call i8* @malloc(i64 %size)");

        builder.Append("  %obj = bitcast i8* %raw to %");
        builder.Append(startClass.Name);
        builder.AppendLine("*");

        builder.Append("  %class_id_ptr = getelementptr %");
        builder.Append(startClass.Name);
        builder.Append(", %");
        builder.Append(startClass.Name);
        builder.AppendLine("* %obj, i32 0, i32 0");

        builder.Append("  store i32 ");
        builder.Append(startClass.ClassId);
        builder.AppendLine(", i32* %class_id_ptr");

        var ctor = startClass.SemanticClass.Constructors.FirstOrDefault(c => c.Parameters.Count == 0);
        if (ctor is not null)
        {
            var ctorName = MangleConstructorName(startClass, ctor);
            builder.Append("  call void ");
            builder.Append(ctorName);
            builder.Append("(%");
            builder.Append(startClass.Name);
            builder.AppendLine("* %obj)");
        }
        else
        {
            builder.AppendLine("  ; No parameterless constructor found, skipping invocation");
        }

        builder.AppendLine("  ret i32 0");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private ClassLayout? DetermineStartClass(IReadOnlyDictionary<string, ClassLayout> layouts, ProgramNode program)
    {
        if (layouts.TryGetValue("Main", out var main))
        {
            return main;
        }

        foreach (var classNode in program.Classes)
        {
            if (layouts.TryGetValue(classNode.Name, out var layout))
            {
                return layout;
            }
        }

        return null;
    }

    private void EmitConstructor(StringBuilder builder, ClassLayout layout, SemanticConstructor ctor)
    {
        var parameters = new List<string>
        {
            $"%{layout.Name}* %this",
        };

        foreach (var parameter in ctor.Parameters)
        {
            parameters.Add($"{ResolveTypeName(parameter.Type)} %{Sanitize(parameter.Name)}");
        }

        var mangledName = MangleConstructorName(layout, ctor);
        builder.Append("define void ");
        builder.Append(mangledName);
        builder.Append('(');
        builder.Append(string.Join(", ", parameters));
        builder.AppendLine(") {");
        builder.AppendLine("entry:");

        var emitter = new FunctionEmitter(builder);
        var context = FunctionContext.ForConstructor(layout, ctor, emitter);
        InitializeParameters(context, ctor.Parameters);
        EmitBody(context, ctor.Declaration.Body);
        EnsureFunctionTermination(context, "void");

        builder.AppendLine("}");
        builder.AppendLine();
    }

    private void EmitMethod(StringBuilder builder, ClassLayout layout, SemanticMethod method)
    {
        if (method.Declaration.Body is null)
        {
            return;
        }

        var returnType = ResolveTypeName(method.ReturnType);
        var signature = BuildSignature(layout, method);

        builder.Append("define ");
        builder.Append(returnType);
        builder.Append(' ');
        builder.Append(signature);
        builder.AppendLine(" {");
        builder.AppendLine("entry:");

        var emitter = new FunctionEmitter(builder);
        var context = FunctionContext.ForMethod(layout, method, emitter);
        InitializeParameters(context, method.Parameters);
        EmitMethodBody(context, method.Declaration.Body);
        EnsureFunctionTermination(context, returnType);

        builder.AppendLine("}");
        builder.AppendLine();
    }

    private void EmitMethodBody(FunctionContext context, MethodBodyNode body)
    {
        switch (body)
        {
            case ExpressionBodyNode expressionBody:
            {
                var value = EmitExpression(context, expressionBody.Expression);
                EmitReturn(context, value);
                break;
            }

            case BlockBodyNode blockBody:
            {
                EmitBody(context, blockBody.Body);
                break;
            }
        }
    }

    private void EmitBody(FunctionContext context, BodyNode body)
    {
        foreach (var item in body.Items)
        {
            if (context.Emitter.IsCurrentBlockTerminated)
            {
                break;
            }

            switch (item)
            {
                case VariableDeclarationNode local:
                    EmitLocalDeclaration(context, local);
                    break;

                case Statement statement:
                    EmitStatement(context, statement);
                    break;
            }
        }
    }

    private void EmitLocalDeclaration(FunctionContext context, VariableDeclarationNode local)
    {
        if (!_semanticModel.VariableTypes.TryGetValue(local, out var semanticType))
        {
            return;
        }

        var value = EmitExpression(context, local.InitialValue);
        var slot = context.DeclareVariable(local.Name, semanticType);
        StoreValue(context, value, slot.Pointer, slot.LlvmType);
    }

    private void EmitStatement(FunctionContext context, Statement statement)
    {
        switch (statement)
        {
            case AssignmentNode assignment:
                EmitAssignment(context, assignment);
                break;

            case IfStatementNode ifStatement:
                EmitIf(context, ifStatement);
                break;

            case WhileLoopNode whileLoop:
                EmitWhile(context, whileLoop);
                break;

            case ReturnStatementNode returnStatement:
                EmitReturn(context, returnStatement);
                break;

            default:
                context.Emitter.EmitRaw($"; Unsupported statement '{statement.GetType().Name}'");
                break;
        }
    }

    private void EmitAssignment(FunctionContext context, AssignmentNode assignment)
    {
        var value = EmitExpression(context, assignment.Value);
        var (pointer, llvmType) = GetAssignmentTarget(context, assignment.Target);

        if (string.IsNullOrEmpty(pointer))
        {
            context.Emitter.EmitRaw("; Failed to resolve assignment target");
            return;
        }

        StoreValue(context, value, pointer, llvmType);
    }

    private (string Pointer, string LlvmType) GetAssignmentTarget(FunctionContext context, Expression target)
    {
        switch (target)
        {
            case IdentifierNode identifier:
            {
                if (context.TryGetVariable(identifier.Name, out var slot))
                {
                    return (slot.Pointer, slot.LlvmType);
                }

                var fieldPointer = GetFieldPointer(context, context.ThisValue, identifier.Name, out var fieldLayout);
                return (fieldPointer, fieldLayout?.LlvmType ?? "");
            }

            case MemberAccessNode memberAccess:
            {
                var instance = EmitExpression(context, memberAccess.Target);
                var fieldPointer = GetFieldPointer(context, instance, memberAccess.MemberName, out var fieldLayout);
                return (fieldPointer, fieldLayout?.LlvmType ?? "");
            }
        }

        return (string.Empty, string.Empty);
    }

    private void EmitIf(FunctionContext context, IfStatementNode ifStatement)
    {
        var condition = EmitExpression(context, ifStatement.Condition);
        var conditionValue = EnsureBoolean(context, condition);

        var thenLabel = context.Emitter.NewLabel("then");
        var elseLabel = ifStatement.ElseBranch is null
            ? null
            : context.Emitter.NewLabel("else");
        var mergeLabel = context.Emitter.NewLabel("endif");

        var falseTarget = elseLabel ?? mergeLabel;
        context.Emitter.EmitRaw($"br i1 {conditionValue}, label %{thenLabel}, label %{falseTarget}");

        context.Emitter.EmitLabel(thenLabel);
        EmitBody(context, ifStatement.ThenBranch);
        if (!context.Emitter.IsCurrentBlockTerminated)
        {
            context.Emitter.EmitRaw($"br label %{mergeLabel}");
            context.Emitter.MarkTerminated();
        }

        if (elseLabel is not null)
        {
            context.Emitter.EmitLabel(elseLabel);
            EmitBody(context, ifStatement.ElseBranch!);
            if (!context.Emitter.IsCurrentBlockTerminated)
            {
                context.Emitter.EmitRaw($"br label %{mergeLabel}");
                context.Emitter.MarkTerminated();
            }
        }

        context.Emitter.EmitLabel(mergeLabel);
    }

    private void EmitWhile(FunctionContext context, WhileLoopNode whileLoop)
    {
        var conditionLabel = context.Emitter.NewLabel("while_cond");
        var bodyLabel = context.Emitter.NewLabel("while_body");
        var exitLabel = context.Emitter.NewLabel("while_exit");

        context.Emitter.EmitRaw($"br label %{conditionLabel}");
        context.Emitter.EmitLabel(conditionLabel);

        var condition = EmitExpression(context, whileLoop.Condition);
        var conditionValue = EnsureBoolean(context, condition);
        context.Emitter.EmitRaw($"br i1 {conditionValue}, label %{bodyLabel}, label %{exitLabel}");

        context.Emitter.EmitLabel(bodyLabel);
        EmitBody(context, whileLoop.Body);
        if (!context.Emitter.IsCurrentBlockTerminated)
        {
            context.Emitter.EmitRaw($"br label %{conditionLabel}");
            context.Emitter.MarkTerminated();
        }

        context.Emitter.EmitLabel(exitLabel);
    }

    private void EmitReturn(FunctionContext context, ReturnStatementNode statement)
    {
        if (statement.Value is null)
        {
            context.Emitter.EmitRaw("ret void");
            context.Emitter.MarkTerminated();
            return;
        }

        var value = EmitExpression(context, statement.Value);
        EmitReturn(context, value);
    }

    private void EmitReturn(FunctionContext context, LlvmValue value)
    {
        if (context.ReturnType.IsVoid)
        {
            context.Emitter.EmitRaw("ret void");
        }
        else
        {
            var expectedType = ResolveTypeName(context.ReturnType);
            if (!string.Equals(expectedType, value.LlvmType, StringComparison.Ordinal))
            {
                value = ConvertValue(context, value, context.ReturnType);
            }

            context.Emitter.EmitRaw($"ret {expectedType} {value.Register}");
        }

        context.Emitter.MarkTerminated();
    }

    private void EnsureFunctionTermination(FunctionContext context, string returnType)
    {
        if (context.Emitter.IsCurrentBlockTerminated)
        {
            return;
        }

        if (returnType == "void")
        {
            context.Emitter.EmitRaw("ret void");
        }
        else
        {
            context.Emitter.EmitRaw($"ret {returnType} {GetDefaultValue(returnType)}");
        }

        context.Emitter.MarkTerminated();
    }

    private void InitializeParameters(FunctionContext context, IReadOnlyList<SemanticParameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            var slot = context.DeclareVariable(parameter.Name, parameter.Type);
            var llvmParamName = $"%{Sanitize(parameter.Name)}";
            context.Emitter.EmitRaw($"store {slot.LlvmType} {llvmParamName}, {slot.LlvmType}* {slot.Pointer}");
        }
    }

    private LlvmValue EmitExpression(FunctionContext context, Expression expression)
    {
        var semanticType = _semanticModel.GetExpressionType(expression);

        switch (expression)
        {
            case IntegerLiteralNode integerLiteral:
                return new LlvmValue(integerLiteral.Value.ToString(CultureInfo.InvariantCulture), "i32", semanticType);

            case RealLiteralNode realLiteral:
                return new LlvmValue(realLiteral.Value.ToString(CultureInfo.InvariantCulture), "double", semanticType);

            case BooleanLiteralNode booleanLiteral:
                return new LlvmValue(booleanLiteral.Value ? "1" : "0", "i1", semanticType);

            case ThisNode:
                return context.ThisValue;

            case IdentifierNode identifier:
                return LoadIdentifier(context, identifier, semanticType);

            case MemberAccessNode memberAccess:
                return LoadMemberAccess(context, memberAccess, semanticType);

            case ConstructorCallNode constructorCall:
                return EmitConstructorCall(context, constructorCall, semanticType);

            case CallNode callNode:
                return EmitCall(context, callNode, semanticType);
        }

        context.Emitter.EmitRaw($"; Unsupported expression '{expression.GetType().Name}'");
        return new LlvmValue(GetDefaultValue(ResolveTypeName(semanticType)), ResolveTypeName(semanticType), semanticType);
    }

    private LlvmValue LoadIdentifier(FunctionContext context, IdentifierNode identifier, SemanticType semanticType)
    {
        if (context.TryGetVariable(identifier.Name, out var slot))
        {
            var register = context.Emitter.EmitAssignment($"load {slot.LlvmType}, {slot.LlvmType}* {slot.Pointer}");
            return new LlvmValue(register, slot.LlvmType, slot.SemanticType);
        }

        var fieldPointer = GetFieldPointer(context, context.ThisValue, identifier.Name, out var layout);
        if (layout is null)
        {
            return new LlvmValue(GetDefaultValue(ResolveTypeName(semanticType)), ResolveTypeName(semanticType), semanticType);
        }

        var registerName = context.Emitter.EmitAssignment($"load {layout.LlvmType}, {layout.LlvmType}* {fieldPointer}");
        return new LlvmValue(registerName, layout.LlvmType, layout.SemanticType);
    }

    private LlvmValue LoadMemberAccess(FunctionContext context, MemberAccessNode memberAccess, SemanticType semanticType)
    {
        var instance = EmitExpression(context, memberAccess.Target);
        var fieldPointer = GetFieldPointer(context, instance, memberAccess.MemberName, out var layout);
        if (layout is null)
        {
            return new LlvmValue(GetDefaultValue(ResolveTypeName(semanticType)), ResolveTypeName(semanticType), semanticType);
        }

        var register = context.Emitter.EmitAssignment($"load {layout.LlvmType}, {layout.LlvmType}* {fieldPointer}");
        return new LlvmValue(register, layout.LlvmType, layout.SemanticType);
    }

    private LlvmValue EmitConstructorCall(FunctionContext context, ConstructorCallNode constructorCall, SemanticType semanticType)
    {
        if (TryEmitConstructorLiteral(constructorCall, out var llvmType, out var literal))
        {
            return new LlvmValue(literal, llvmType, semanticType);
        }

        var arguments = constructorCall.Arguments
            .Select(argument => EmitExpression(context, argument))
            .ToList();

        if (semanticType.IsArray)
        {
            return EmitArrayConstructor(context, arguments, semanticType);
        }

        if (semanticType.IsList)
        {
            return EmitListConstructor(context, arguments, semanticType);
        }

        if (!_layouts.TryGetValue(constructorCall.ClassName, out var layout))
        {
            context.Emitter.EmitRaw($"; Unknown constructor target '{constructorCall.ClassName}'");
            return new LlvmValue("null", ResolveTypeName(semanticType), semanticType);
        }

        var sizePtr = context.Emitter.EmitAssignment($"getelementptr %{layout.Name}, %{layout.Name}* null, i32 1");
        var size = context.Emitter.EmitAssignment($"ptrtoint %{layout.Name}* {sizePtr} to i64");
        var raw = context.Emitter.EmitAssignment($"call i8* @malloc(i64 {size})");
        var instance = context.Emitter.EmitAssignment($"bitcast i8* {raw} to %{layout.Name}*");
        var classIdPtr = context.Emitter.EmitAssignment($"getelementptr %{layout.Name}, %{layout.Name}* {instance}, i32 0, i32 0");
        context.Emitter.EmitRaw($"store i32 {layout.ClassId}, i32* {classIdPtr}");

        var ctor = ResolveConstructor(layout, arguments.Select(arg => arg.SemanticType).ToList());
        if (ctor is not null)
        {
            var ctorName = MangleConstructorName(layout, ctor);
            var argumentList = new List<string> { $"%{layout.Name}* {instance}" };
            for (var i = 0; i < arguments.Count; i++)
            {
                var argumentValue = EnsureType(context, arguments[i], ctor.Parameters[i].Type);
                argumentList.Add($"{argumentValue.LlvmType} {argumentValue.Register}");
            }

            context.Emitter.EmitRaw($"call void {ctorName}({string.Join(", ", argumentList)})");
        }
        else
        {
            context.Emitter.EmitRaw("; Unable to resolve constructor overload");
        }

        return new LlvmValue(instance, $"%{layout.Name}*", semanticType);
    }

    private LlvmValue EmitArrayConstructor(FunctionContext context, IReadOnlyList<LlvmValue> arguments, SemanticType semanticType)
    {
        if (arguments.Count != 1)
        {
            context.Emitter.EmitRaw("; Array constructor expects a single length argument");
            return new LlvmValue("null", ResolveTypeName(semanticType), semanticType);
        }

        var lengthValue = EnsureType(context, arguments[0], IntegerType);
        var arrayInstance = context.Emitter.EmitAssignment($"call %Array* @o_array_new(i32 {lengthValue.Register})");
        return new LlvmValue(arrayInstance, ResolveTypeName(semanticType), semanticType);
    }

    private LlvmValue EmitListConstructor(FunctionContext context, IReadOnlyList<LlvmValue> arguments, SemanticType semanticType)
    {
        if (!semanticType.TryGetListElementType(out var elementTypeName))
        {
            return new LlvmValue("null", ResolveTypeName(semanticType), semanticType);
        }

        var elementType = CreateSemanticType(elementTypeName);
        var listTypeName = ResolveTypeName(semanticType);

        switch (arguments.Count)
        {
            case 0:
            {
                var empty = context.Emitter.EmitAssignment("call %List* @o_list_empty()");
                return new LlvmValue(empty, listTypeName, semanticType);
            }
            case 1:
            {
                var value = EnsureType(context, arguments[0], elementType);
                var pointer = ConvertValueToRuntimePointer(context, value);
                var single = context.Emitter.EmitAssignment($"call %List* @o_list_singleton(i8* {pointer})");
                return new LlvmValue(single, listTypeName, semanticType);
            }
            case 2:
            {
                var value = EnsureType(context, arguments[0], elementType);
                var pointer = ConvertValueToRuntimePointer(context, value);
                var count = EnsureType(context, arguments[1], IntegerType);
                var replicated = context.Emitter.EmitAssignment($"call %List* @o_list_replicate(i8* {pointer}, i32 {count.Register})");
                return new LlvmValue(replicated, listTypeName, semanticType);
            }
            default:
                context.Emitter.EmitRaw("; Unsupported List constructor arity");
                return new LlvmValue("null", listTypeName, semanticType);
        }
    }

    private SemanticConstructor? ResolveConstructor(ClassLayout layout, IReadOnlyList<SemanticType> argumentTypes)
    {
        foreach (var constructor in layout.SemanticClass.Constructors)
        {
            if (constructor.Parameters.Count != argumentTypes.Count)
            {
                continue;
            }

            var match = true;
            for (var i = 0; i < constructor.Parameters.Count; i++)
            {
                var expected = GetCanonicalTypeName(constructor.Parameters[i].Type);
                var actual = GetCanonicalTypeName(argumentTypes[i]);
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return constructor;
            }
        }

        return null;
    }

    private LlvmValue EmitCall(FunctionContext context, CallNode call, SemanticType returnType)
    {
        var argumentValues = call.Arguments
            .Select(argument => EmitExpression(context, argument))
            .ToList();

        if (call.Callee is IdentifierNode identifier)
        {
            return EmitInstanceMethodCall(context, context.ThisValue, identifier.Name, argumentValues, returnType);
        }

        if (call.Callee is MemberAccessNode memberAccess)
        {
            var receiver = EmitExpression(context, memberAccess.Target);
            return EmitInstanceMethodCall(context, receiver, memberAccess.MemberName, argumentValues, returnType);
        }

        context.Emitter.EmitRaw("; Unsupported call target");
        return new LlvmValue(GetDefaultValue(ResolveTypeName(returnType)), ResolveTypeName(returnType), returnType);
    }

    private LlvmValue EmitInstanceMethodCall(FunctionContext context, LlvmValue receiver, string methodName, IReadOnlyList<LlvmValue> arguments, SemanticType returnType)
    {
        if (TryEmitBuiltinMethod(context, receiver, methodName, arguments, returnType, out var builtinResult))
        {
            return builtinResult;
        }

        if (!receiver.SemanticType.IsReference)
        {
            context.Emitter.EmitRaw("; Attempted to call method on non-reference type");
            return new LlvmValue(GetDefaultValue(ResolveTypeName(returnType)), ResolveTypeName(returnType), returnType);
        }

        if (!_layouts.TryGetValue(receiver.SemanticType.Name, out var staticLayout))
        {
            context.Emitter.EmitRaw($"; Unknown receiver type '{receiver.SemanticType.Name}'");
            return new LlvmValue(GetDefaultValue(ResolveTypeName(returnType)), ResolveTypeName(returnType), returnType);
        }

        var signature = new MethodSignature(methodName, arguments.Select(arg => GetCanonicalTypeName(arg.SemanticType)).ToArray());
        var cases = CollectDispatchCases(staticLayout, signature).ToList();

        if (cases.Count == 0)
        {
            context.Emitter.EmitRaw("; No overload matches dispatch signature");
            return new LlvmValue(GetDefaultValue(ResolveTypeName(returnType)), ResolveTypeName(returnType), returnType);
        }

        var classIdPtr = context.Emitter.EmitAssignment($"getelementptr %{staticLayout.Name}, %{staticLayout.Name}* {receiver.Register}, i32 0, i32 0");
        var classId = context.Emitter.EmitAssignment($"load i32, i32* {classIdPtr}");

        var resultPointer = returnType.IsVoid
            ? string.Empty
            : context.Emitter.EmitAssignment($"alloca {ResolveTypeName(returnType)}");

        var defaultLabel = context.Emitter.NewLabel("dispatch_default");
        var mergeLabel = context.Emitter.NewLabel("dispatch_merge");
        var caseLabels = cases.Select(caseInfo => (Label: context.Emitter.NewLabel($"dispatch_{caseInfo.Layout.Name}"), Case: caseInfo)).ToList();

        context.Emitter.EmitRaw($"switch i32 {classId}, label %{defaultLabel} [");
        foreach (var (label, caseInfo) in caseLabels)
        {
            context.Emitter.EmitRaw($"    i32 {caseInfo.Layout.ClassId}, label %{label}");
        }

        context.Emitter.EmitRaw("]");

        foreach (var (label, caseInfo) in caseLabels)
        {
            context.Emitter.EmitLabel(label);
            EmitDispatchCase(context, receiver, caseInfo, arguments, resultPointer, returnType, mergeLabel);
        }

        context.Emitter.EmitLabel(defaultLabel);
        if (!returnType.IsVoid)
        {
            var defaultValue = GetDefaultValue(ResolveTypeName(returnType));
            context.Emitter.EmitRaw($"store {ResolveTypeName(returnType)} {defaultValue}, {ResolveTypeName(returnType)}* {resultPointer}");
        }

        context.Emitter.EmitRaw($"br label %{mergeLabel}");
        context.Emitter.EmitLabel(mergeLabel);

        if (returnType.IsVoid)
        {
            return new LlvmValue(string.Empty, "void", returnType);
        }

        var loadedValue = context.Emitter.EmitAssignment($"load {ResolveTypeName(returnType)}, {ResolveTypeName(returnType)}* {resultPointer}");
        return new LlvmValue(loadedValue, ResolveTypeName(returnType), returnType);
    }

    private void EmitDispatchCase(FunctionContext context, LlvmValue receiver, MethodCase caseInfo, IReadOnlyList<LlvmValue> arguments, string resultPointer, SemanticType returnType, string mergeLabel)
    {
        var implementation = caseInfo.Implementation;
        var declaringLayout = implementation.DeclaringClass;
        var receiverValue = receiver;

        if (!string.Equals(receiverValue.LlvmType, $"%{declaringLayout.Name}*", StringComparison.Ordinal))
        {
            var bitcast = context.Emitter.EmitAssignment($"bitcast {receiverValue.LlvmType} {receiverValue.Register} to %{declaringLayout.Name}*");
            receiverValue = new LlvmValue(bitcast, $"%{declaringLayout.Name}*", receiver.SemanticType);
        }

        var args = new List<string> { $"%{declaringLayout.Name}* {receiverValue.Register}" };
        for (var i = 0; i < arguments.Count; i++)
        {
            var parameterType = implementation.Method.Parameters[i].Type;
            var argument = EnsureType(context, arguments[i], parameterType);
            args.Add($"{argument.LlvmType} {argument.Register}");
        }

        var methodName = MangleMethodName(declaringLayout, implementation.Method);
        if (returnType.IsVoid)
        {
            context.Emitter.EmitRaw($"call void {methodName}({string.Join(", ", args)})");
        }
        else
        {
            var returnTypeName = ResolveTypeName(returnType);
            var callResult = context.Emitter.EmitAssignment($"call {returnTypeName} {methodName}({string.Join(", ", args)})");
            context.Emitter.EmitRaw($"store {returnTypeName} {callResult}, {returnTypeName}* {resultPointer}");
        }

        context.Emitter.EmitRaw($"br label %{mergeLabel}");
    }

    private IEnumerable<MethodCase> CollectDispatchCases(ClassLayout layout, MethodSignature signature)
    {
        foreach (var descendant in EnumerateHierarchy(layout))
        {
            if (descendant.Methods.TryGetValue(signature, out var implementation))
            {
                yield return new MethodCase(descendant, implementation);
            }
        }
    }

    private IEnumerable<ClassLayout> EnumerateHierarchy(ClassLayout root)
    {
        yield return root;
        foreach (var child in root.DerivedClasses)
        {
            foreach (var descendant in EnumerateHierarchy(child))
            {
                yield return descendant;
            }
        }
    }

    private bool TryEmitBuiltinMethod(FunctionContext context, LlvmValue receiver, string methodName, IReadOnlyList<LlvmValue> arguments, SemanticType returnType, out LlvmValue result)
    {
        if (receiver.SemanticType.IsArray)
        {
            return TryEmitArrayBuiltin(context, receiver, methodName, arguments, returnType, out result);
        }

        if (receiver.SemanticType.IsList)
        {
            return TryEmitListBuiltin(context, receiver, methodName, arguments, returnType, out result);
        }

        if (receiver.SemanticType.IsInteger)
        {
            return TryEmitIntegerBuiltin(context, receiver, methodName, arguments, returnType, out result);
        }

        if (receiver.SemanticType.IsReal)
        {
            return TryEmitRealBuiltin(context, receiver, methodName, arguments, returnType, out result);
        }

        if (receiver.SemanticType.IsBoolean)
        {
            return TryEmitBooleanBuiltin(context, receiver, methodName, arguments, returnType, out result);
        }

        result = default;
        return false;
    }

    private bool TryEmitArrayBuiltin(FunctionContext context, LlvmValue receiver, string methodName, IReadOnlyList<LlvmValue> arguments, SemanticType returnType, out LlvmValue result)
    {
        result = default;

        if (!receiver.SemanticType.TryGetArrayElementType(out var elementTypeName))
        {
            return false;
        }

        var elementType = CreateSemanticType(elementTypeName);

        switch (methodName)
        {
            case "Length" when arguments.Count == 0:
            {
                var value = context.Emitter.EmitAssignment($"call i32 @o_array_length(%Array* {receiver.Register})");
                result = new LlvmValue(value, "i32", IntegerType);
                return true;
            }

            case "get" when arguments.Count == 1:
            {
                var index = EnsureType(context, arguments[0], IntegerType);
                var raw = context.Emitter.EmitAssignment($"call i8* @o_array_get(%Array* {receiver.Register}, i32 {index.Register})");
                result = ConvertRuntimePointerToValue(context, raw, returnType);
                return true;
            }

            case "set" when arguments.Count == 2:
            {
                var index = EnsureType(context, arguments[0], IntegerType);
                var value = EnsureType(context, arguments[1], elementType);
                var pointer = ConvertValueToRuntimePointer(context, value);
                context.Emitter.EmitRaw($"call void @o_array_set(%Array* {receiver.Register}, i32 {index.Register}, i8* {pointer})");
                result = new LlvmValue(receiver.Register, receiver.LlvmType, receiver.SemanticType);
                return true;
            }
        }

        return false;
    }

    private bool TryEmitListBuiltin(FunctionContext context, LlvmValue receiver, string methodName, IReadOnlyList<LlvmValue> arguments, SemanticType returnType, out LlvmValue result)
    {
        result = default;

        if (!receiver.SemanticType.TryGetListElementType(out var elementTypeName))
        {
            return false;
        }

        var elementType = CreateSemanticType(elementTypeName);

        switch (methodName)
        {
            case "append" when arguments.Count == 1:
            {
                var value = EnsureType(context, arguments[0], elementType);
                var pointer = ConvertValueToRuntimePointer(context, value);
                var appended = context.Emitter.EmitAssignment($"call %List* @o_list_append(%List* {receiver.Register}, i8* {pointer})");
                result = new LlvmValue(appended, ResolveTypeName(returnType), returnType);
                return true;
            }

            case "head" when arguments.Count == 0:
            {
                var raw = context.Emitter.EmitAssignment($"call i8* @o_list_head(%List* {receiver.Register})");
                result = ConvertRuntimePointerToValue(context, raw, returnType);
                return true;
            }

            case "tail" when arguments.Count == 0:
            {
                var tail = context.Emitter.EmitAssignment($"call %List* @o_list_tail(%List* {receiver.Register})");
                result = new LlvmValue(tail, ResolveTypeName(returnType), returnType);
                return true;
            }

            case "toArray" when arguments.Count == 0:
            {
                var arrayValue = context.Emitter.EmitAssignment($"call %Array* @o_list_to_array(%List* {receiver.Register})");
                result = new LlvmValue(arrayValue, ResolveTypeName(returnType), returnType);
                return true;
            }
        }

        return false;
    }

    private bool TryEmitIntegerBuiltin(FunctionContext context, LlvmValue receiver, string methodName, IReadOnlyList<LlvmValue> arguments, SemanticType returnType, out LlvmValue result)
    {
        switch (methodName)
        {
            case "Plus" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"add i32 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i32", IntegerType);
                return true;
            }

            case "Minus" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"sub i32 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i32", IntegerType);
                return true;
            }

            case "Mult" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"mul i32 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i32", IntegerType);
                return true;
            }

            case "Div" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"sdiv i32 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i32", IntegerType);
                return true;
            }

            case "Rem" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"srem i32 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i32", IntegerType);
                return true;
            }

            case "Less" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"icmp slt i32 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "Greater" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"icmp sgt i32 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "Equal" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"icmp eq i32 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "toReal" when arguments.Count == 0:
            {
                var value = context.Emitter.EmitAssignment($"sitofp i32 {receiver.Register} to double");
                result = new LlvmValue(value, "double", RealType);
                return true;
            }

            case "toBoolean" when arguments.Count == 0:
            {
                var value = context.Emitter.EmitAssignment($"icmp ne i32 {receiver.Register}, 0");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "Print" when arguments.Count == 0:
            {
                EmitIntegerPrint(context, receiver);
                result = receiver;
                return true;
            }
        }

        result = default;
        return false;
    }

    private bool TryEmitRealBuiltin(FunctionContext context, LlvmValue receiver, string methodName, IReadOnlyList<LlvmValue> arguments, SemanticType returnType, out LlvmValue result)
    {
        switch (methodName)
        {
            case "Plus" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"fadd double {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "double", RealType);
                return true;
            }

            case "Minus" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"fsub double {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "double", RealType);
                return true;
            }

            case "Mult" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"fmul double {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "double", RealType);
                return true;
            }

            case "Div" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"fdiv double {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "double", RealType);
                return true;
            }

            case "Less" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"fcmp olt double {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "Greater" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"fcmp ogt double {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "Equal" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"fcmp oeq double {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "toInteger" when arguments.Count == 0:
            {
                var value = context.Emitter.EmitAssignment($"fptosi double {receiver.Register} to i32");
                result = new LlvmValue(value, "i32", IntegerType);
                return true;
            }

            case "Print" when arguments.Count == 0:
            {
                EmitRealPrint(context, receiver);
                result = receiver;
                return true;
            }
        }

        result = default;
        return false;
    }

    private bool TryEmitBooleanBuiltin(FunctionContext context, LlvmValue receiver, string methodName, IReadOnlyList<LlvmValue> arguments, SemanticType returnType, out LlvmValue result)
    {
        switch (methodName)
        {
            case "And" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"and i1 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "Or" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"or i1 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "Xor" when arguments.Count == 1:
            {
                var arg = EnsureType(context, arguments[0], receiver.SemanticType);
                var value = context.Emitter.EmitAssignment($"xor i1 {receiver.Register}, {arg.Register}");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "Not" when arguments.Count == 0:
            {
                var value = context.Emitter.EmitAssignment($"xor i1 {receiver.Register}, 1");
                result = new LlvmValue(value, "i1", BooleanType);
                return true;
            }

            case "toInteger" when arguments.Count == 0:
            {
                var value = context.Emitter.EmitAssignment($"zext i1 {receiver.Register} to i32");
                result = new LlvmValue(value, "i32", IntegerType);
                return true;
            }

            case "Print" when arguments.Count == 0:
            {
                EmitBooleanPrint(context, receiver);
                result = receiver;
                return true;
            }
        }

        result = default;
        return false;
    }

    private void EmitIntegerPrint(FunctionContext context, LlvmValue value)
    {
        var ensured = EnsureType(context, value, IntegerType);
        var formatPointer = GetFormatPointer(context, IntegerFormatGlobal, IntegerFormatLength);
        context.Emitter.EmitRaw($"call i32 (i8*, ...) @printf(i8* {formatPointer}, i32 {ensured.Register})");
    }

    private void EmitRealPrint(FunctionContext context, LlvmValue value)
    {
        var ensured = EnsureType(context, value, RealType);
        var formatPointer = GetFormatPointer(context, RealFormatGlobal, RealFormatLength);
        context.Emitter.EmitRaw($"call i32 (i8*, ...) @printf(i8* {formatPointer}, double {ensured.Register})");
    }

    private void EmitBooleanPrint(FunctionContext context, LlvmValue value)
    {
        var ensured = EnsureType(context, value, BooleanType);
        var promoted = context.Emitter.EmitAssignment($"zext i1 {ensured.Register} to i32");
        var formatPointer = GetFormatPointer(context, IntegerFormatGlobal, IntegerFormatLength);
        context.Emitter.EmitRaw($"call i32 (i8*, ...) @printf(i8* {formatPointer}, i32 {promoted})");
    }

    private string GetFormatPointer(FunctionContext context, string globalName, int length)
    {
        return context.Emitter.EmitAssignment($"getelementptr [{length} x i8], [{length} x i8]* {globalName}, i32 0, i32 0");
    }

    private LlvmValue EnsureType(FunctionContext context, LlvmValue value, SemanticType targetType)
    {
        var targetTypeName = ResolveTypeName(targetType);
        if (string.Equals(value.LlvmType, targetTypeName, StringComparison.Ordinal))
        {
            return value;
        }

        return ConvertValue(context, value, targetType);
    }

    private string ConvertValueToRuntimePointer(FunctionContext context, LlvmValue value)
    {
        if (value.SemanticType.IsReference || value.SemanticType.IsArray || value.SemanticType.IsList)
        {
            if (value.LlvmType == "i8*")
            {
                return value.Register;
            }

            return context.Emitter.EmitAssignment($"bitcast {value.LlvmType} {value.Register} to i8*");
        }

        var size = GetPrimitiveSizeInBytes(value.SemanticType);
        var raw = context.Emitter.EmitAssignment($"call i8* @malloc(i64 {size})");
        var typedPtr = context.Emitter.EmitAssignment($"bitcast i8* {raw} to {value.LlvmType}*");
        context.Emitter.EmitRaw($"store {value.LlvmType} {value.Register}, {value.LlvmType}* {typedPtr}");
        return raw;
    }

    private LlvmValue ConvertRuntimePointerToValue(FunctionContext context, string pointerRegister, SemanticType targetType)
    {
        if (targetType.IsReference || targetType.IsArray || targetType.IsList)
        {
            var llvmType = ResolveTypeName(targetType);
            var bitcast = context.Emitter.EmitAssignment($"bitcast i8* {pointerRegister} to {llvmType}");
            return new LlvmValue(bitcast, llvmType, targetType);
        }

        var targetLlvmType = ResolveTypeName(targetType);
        var typedPtr = context.Emitter.EmitAssignment($"bitcast i8* {pointerRegister} to {targetLlvmType}*");
        var loaded = context.Emitter.EmitAssignment($"load {targetLlvmType}, {targetLlvmType}* {typedPtr}");
        return new LlvmValue(loaded, targetLlvmType, targetType);
    }

    private LlvmValue ConvertValue(FunctionContext context, LlvmValue value, SemanticType targetType)
    {
        if (value.SemanticType.IsInteger && targetType.IsReal)
        {
            var converted = context.Emitter.EmitAssignment($"sitofp i32 {value.Register} to double");
            return new LlvmValue(converted, "double", targetType);
        }

        if (value.SemanticType.IsReal && targetType.IsInteger)
        {
            var converted = context.Emitter.EmitAssignment($"fptosi double {value.Register} to i32");
            return new LlvmValue(converted, "i32", targetType);
        }

        if (value.SemanticType.IsInteger && targetType.IsBoolean)
        {
            var converted = context.Emitter.EmitAssignment($"icmp ne i32 {value.Register}, 0");
            return new LlvmValue(converted, "i1", targetType);
        }

        if (value.SemanticType.IsBoolean && targetType.IsInteger)
        {
            var converted = context.Emitter.EmitAssignment($"zext i1 {value.Register} to i32");
            return new LlvmValue(converted, "i32", targetType);
        }

        return new LlvmValue(value.Register, ResolveTypeName(targetType), targetType);
    }

    private static SemanticType CreateSemanticType(string name)
    {
        if (string.Equals(name, BuiltInTypes.Integer.Name, StringComparison.Ordinal))
        {
            return new SemanticType(name, TypeKind.Integer);
        }

        if (string.Equals(name, BuiltInTypes.Real.Name, StringComparison.Ordinal))
        {
            return new SemanticType(name, TypeKind.Real);
        }

        if (string.Equals(name, BuiltInTypes.Boolean.Name, StringComparison.Ordinal))
        {
            return new SemanticType(name, TypeKind.Boolean);
        }

        if (TypeNameHelper.IsArrayType(name))
        {
            return new SemanticType(name, TypeKind.Array);
        }

        if (TypeNameHelper.IsListType(name))
        {
            return new SemanticType(name, TypeKind.List);
        }

        return new SemanticType(name, TypeKind.Class);
    }

    private static int GetPrimitiveSizeInBytes(SemanticType type)
    {
        if (type.IsInteger)
        {
            return 4;
        }

        if (type.IsReal)
        {
            return 8;
        }

        if (type.IsBoolean)
        {
            return 1;
        }

        throw new InvalidOperationException($"Unable to determine size for primitive type '{type.Name}'.");
    }

    private string GetFieldPointer(FunctionContext context, LlvmValue instance, string fieldName, out FieldLayout? layout)
    {
        layout = null;

        if (!_layouts.TryGetValue(instance.SemanticType.Name, out var classLayout))
        {
            return string.Empty;
        }

        layout = classLayout.Fields.FirstOrDefault(field => string.Equals(field.Name, fieldName, StringComparison.Ordinal));
        if (layout is null)
        {
            return string.Empty;
        }

        return context.Emitter.EmitAssignment($"getelementptr %{classLayout.Name}, %{classLayout.Name}* {instance.Register}, i32 0, i32 {layout.Index}");
    }

    private void StoreValue(FunctionContext context, LlvmValue value, string pointer, string llvmType)
    {
        if (string.IsNullOrEmpty(pointer))
        {
            return;
        }

        if (!string.Equals(value.LlvmType, llvmType, StringComparison.Ordinal))
        {
            value = new LlvmValue(value.Register, llvmType, value.SemanticType);
        }

        context.Emitter.EmitRaw($"store {llvmType} {value.Register}, {llvmType}* {pointer}");
    }

    private string EnsureBoolean(FunctionContext context, LlvmValue value)
    {
        if (value.LlvmType == "i1")
        {
            return value.Register;
        }

        var converted = ConvertValue(context, value, BooleanType);
        return converted.Register;
    }

    private string BuildSignature(ClassLayout layout, SemanticMethod method)
    {
        var parameters = new List<string>
        {
            $"%{layout.Name}* %this",
        };

        foreach (var parameter in method.Parameters)
        {
            parameters.Add($"{ResolveTypeName(parameter.Type)} %{Sanitize(parameter.Name)}");
        }

        var mangledName = MangleMethodName(layout, method);
        return $"{mangledName}({string.Join(", ", parameters)})";
    }

    private string MangleMethodName(ClassLayout layout, SemanticMethod method)
    {
        var builder = new StringBuilder();
        builder.Append('@');
        builder.Append(layout.Name);
        builder.Append('_');
        builder.Append(method.Name);

        foreach (var parameter in method.Parameters)
        {
            builder.Append("__");
            builder.Append(GetCanonicalTypeName(parameter.Type));
        }

        return builder.ToString();
    }

    private string MangleConstructorName(ClassLayout layout, SemanticConstructor constructor)
    {
        var builder = new StringBuilder();
        builder.Append('@');
        builder.Append(layout.Name);
        builder.Append("_ctor");

        foreach (var parameter in constructor.Parameters)
        {
            builder.Append("__");
            builder.Append(GetCanonicalTypeName(parameter.Type));
        }

        return builder.ToString();
    }

    private MethodSignature CreateMethodSignature(SemanticMethod method)
    {
        var parameterTypes = method.Parameters
            .Select(parameter => GetCanonicalTypeName(parameter.Type))
            .ToArray();
        return new MethodSignature(method.Name, parameterTypes);
    }

    private static string GetCanonicalTypeName(SemanticType type)
    {
        if (type.IsInteger)
        {
            return "Integer";
        }

        if (type.IsReal)
        {
            return "Real";
        }

        if (type.IsBoolean)
        {
            return "Boolean";
        }

        if (type.IsVoid)
        {
            return "Void";
        }

        return SanitizeTypeName(type.Name);
    }

    private static string Sanitize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.ToString();
    }

    private static string SanitizeTypeName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.ToString();
    }

    private static string ResolveTypeName(SemanticType type)
    {
        if (type.IsInteger)
        {
            return "i32";
        }

        if (type.IsReal)
        {
            return "double";
        }

        if (type.IsBoolean)
        {
            return "i1";
        }

        if (type.IsArray)
        {
            return "%Array*";
        }

        if (type.IsList)
        {
            return "%List*";
        }

        if (type.IsVoid)
        {
            return "void";
        }

        return $"%{SanitizeTypeName(type.Name)}*";
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

    private static bool TryEmitConstructorLiteral(ConstructorCallNode ctor, out string llvmType, out string literalValue)
    {
        llvmType = string.Empty;
        literalValue = string.Empty;

        if (ctor.Arguments.Count != 1)
        {
            return false;
        }

        if (ctor.ClassName == BuiltInTypes.Integer.Name && ctor.Arguments[0] is IntegerLiteralNode intLiteral)
        {
            llvmType = "i32";
            literalValue = intLiteral.Value.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (ctor.ClassName == BuiltInTypes.Real.Name && ctor.Arguments[0] is RealLiteralNode realLiteral)
        {
            llvmType = "double";
            literalValue = realLiteral.Value.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (ctor.ClassName == BuiltInTypes.Boolean.Name && ctor.Arguments[0] is BooleanLiteralNode boolLiteral)
        {
            llvmType = "i1";
            literalValue = boolLiteral.Value ? "1" : "0";
            return true;
        }

        return false;
    }

    private sealed record FieldLayout(string Name, string LlvmType, SemanticType SemanticType, int Index);

    private sealed class ClassLayout
    {
        public ClassLayout(SemanticClass semanticClass, int classId, IReadOnlyList<FieldLayout> fields, ClassLayout? baseClass)
        {
            SemanticClass = semanticClass;
            ClassId = classId;
            Fields = fields;
            BaseClass = baseClass;
            Methods = new Dictionary<MethodSignature, MethodImplementation>();
            DerivedClasses = new List<ClassLayout>();
        }

        public SemanticClass SemanticClass { get; }

        public string Name => SemanticClass.Name;

        public int ClassId { get; }

        public IReadOnlyList<FieldLayout> Fields { get; }

        public ClassLayout? BaseClass { get; }

        public Dictionary<MethodSignature, MethodImplementation> Methods { get; }

        public List<ClassLayout> DerivedClasses { get; }
    }

    private sealed record MethodSignature(string Name, IReadOnlyList<string> ParameterTypes);

    private sealed record MethodImplementation(ClassLayout DeclaringClass, SemanticMethod Method);

    private sealed record MethodCase(ClassLayout Layout, MethodImplementation Implementation);

    private readonly record struct LlvmValue(string Register, string LlvmType, SemanticType SemanticType);

    private readonly record struct VariableSlot(string Pointer, string LlvmType, SemanticType SemanticType);

    private sealed class FunctionEmitter
    {
        private readonly StringBuilder _builder;
        private int _tempIndex;
        private int _labelIndex;
        private bool _currentBlockTerminated;

        public FunctionEmitter(StringBuilder builder)
        {
            _builder = builder;
        }

        public bool IsCurrentBlockTerminated => _currentBlockTerminated;

        public string EmitAssignment(string instruction)
        {
            var temp = NewTemporary();
            _builder.Append("  ");
            _builder.Append(temp);
            _builder.Append(" = ");
            _builder.AppendLine(instruction);
            _currentBlockTerminated = instruction.StartsWith("br") || instruction.StartsWith("ret");
            return temp;
        }

        public void EmitRaw(string text)
        {
            _builder.Append("  ");
            _builder.AppendLine(text);
            if (text.StartsWith("ret", StringComparison.Ordinal) || text.StartsWith("br", StringComparison.Ordinal))
            {
                _currentBlockTerminated = true;
            }
        }

        public void EmitLabel(string label)
        {
            _builder.Append(label);
            _builder.AppendLine(":");
            _currentBlockTerminated = false;
        }

        public string NewLabel(string prefix)
        {
            return $"{prefix}_{_labelIndex++}";
        }

        public void MarkTerminated()
        {
            _currentBlockTerminated = true;
        }

        private string NewTemporary() => $"%t{_tempIndex++}";
    }

    private sealed class FunctionContext
    {
        private readonly Dictionary<string, VariableSlot> _variables = new(StringComparer.Ordinal);

        private FunctionContext(ClassLayout layout, SemanticType returnType, FunctionEmitter emitter)
        {
            ClassLayout = layout;
            ReturnType = returnType;
            Emitter = emitter;
            ThisValue = new LlvmValue("%this", $"%{layout.Name}*", new SemanticType(layout.Name, TypeKind.Class));
        }

        public static FunctionContext ForMethod(ClassLayout layout, SemanticMethod method, FunctionEmitter emitter)
        {
            return new FunctionContext(layout, method.ReturnType, emitter);
        }

        public static FunctionContext ForConstructor(ClassLayout layout, SemanticConstructor constructor, FunctionEmitter emitter)
        {
            return new FunctionContext(layout, SemanticTypeVoid, emitter);
        }

        private static SemanticType SemanticTypeVoid => new("Void", TypeKind.Void);

        public ClassLayout ClassLayout { get; }

        public FunctionEmitter Emitter { get; }

        public LlvmValue ThisValue { get; }

        public SemanticType ReturnType { get; }

        public VariableSlot DeclareVariable(string name, SemanticType type)
        {
            var llvmType = LlvmGenerator.ResolveTypeName(type);
            var pointer = Emitter.EmitAssignment($"alloca {llvmType}");
            var slot = new VariableSlot(pointer, llvmType, type);
            _variables[name] = slot;
            return slot;
        }

        public bool TryGetVariable(string name, out VariableSlot slot) => _variables.TryGetValue(name, out slot);
    }
}
