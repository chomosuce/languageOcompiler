using System;
using System.Collections.Generic;
using System.Linq;
using Compiler.Ast;
using Compiler.Semantics;

namespace Compiler.LLvm;

public sealed partial class LlvmGenerator
{
    private readonly Dictionary<string, ClassLayout> _layouts = new(StringComparer.Ordinal);
    private int _nextClassId;

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

    private MethodSignature CreateMethodSignature(SemanticMethod method)
    {
        var parameterTypesKey = string.Join(",", method.Parameters
            .Select(parameter => GetCanonicalTypeName(parameter.Type)));
        return new MethodSignature(method.Name, parameterTypesKey);
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

    private sealed record MethodSignature(string Name, string ParameterTypesKey);

    private sealed record MethodImplementation(ClassLayout DeclaringClass, SemanticMethod Method);

    private sealed record MethodCase(ClassLayout Layout, MethodImplementation Implementation);
}

