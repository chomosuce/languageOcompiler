using System;
using System.Collections.Generic;
using System.Linq;
using Compiler.Ast;

namespace Compiler.Semantics;

public sealed partial class SemanticAnalyzer
{
    private sealed record TypeSymbol(string Name, TypeKind Kind)
    {
        public static readonly TypeSymbol Integer = new(BuiltInTypes.Integer.Name, TypeKind.Integer);
        public static readonly TypeSymbol Real = new(BuiltInTypes.Real.Name, TypeKind.Real);
        public static readonly TypeSymbol Boolean = new(BuiltInTypes.Boolean.Name, TypeKind.Boolean);
        public static readonly TypeSymbol Void = new("void", TypeKind.Void);
        public static readonly TypeSymbol Standard = new("?", TypeKind.Standard);

        public bool IsVoid => Kind == TypeKind.Void;
        public bool IsStandard => Kind == TypeKind.Standard;
        public bool IsArray => Kind == TypeKind.Array;

        public bool TryGetArrayElementType(out string elementType)
        {
            if (Kind != TypeKind.Array)
            {
                elementType = string.Empty;
                return false;
            }

            return TypeNameHelper.TryGetArrayElementType(Name, out elementType);
        }

        public override string ToString() => Name;
    }

    private sealed class ClassSymbol
    {
        private readonly Dictionary<string, VariableSymbol> _fields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<MethodSymbol>> _methods = new(StringComparer.Ordinal);
        private readonly List<ConstructorSymbol> _constructors = new();

        public ClassSymbol(ClassNode node)
        {
            Node = node;
        }

        public ClassNode Node { get; }

        public string Name => Node.Name;

        public string? BaseClassName => Node.BaseClass;

        public IReadOnlyCollection<VariableSymbol> Fields => _fields.Values;

        public IReadOnlyList<ConstructorSymbol> Constructors => _constructors;
        public IEnumerable<MethodSymbol> AllMethods => _methods.Values.SelectMany(methods => methods);

        public void RegisterMethod(MethodDeclarationNode node, TypeSymbol returnType, IReadOnlyList<ParameterSymbol> parameters)
        {
            if (!_methods.TryGetValue(node.Name, out var overloads))
            {
                overloads = new List<MethodSymbol>();
                _methods[node.Name] = overloads;
            }

            var methodSymbol = overloads.FirstOrDefault(method => method.HasSameSignature(parameters));

            if (methodSymbol is null)
            {
                methodSymbol = new MethodSymbol(node.Name, new List<ParameterSymbol>(parameters), returnType);
                overloads.Add(methodSymbol);
            }

            methodSymbol.RegisterDeclaration(node, returnType, parameters);
        }

        public void RegisterConstructor(ConstructorDeclarationNode node, IReadOnlyList<ParameterSymbol> parameters)
        {
            if (_constructors.Any(constructor => constructor.HasSameSignature(parameters)))
            {
                throw new SemanticException($"Constructor '{Name}' is already declared with the same signature.", node);
            }

            _constructors.Add(new ConstructorSymbol(node, parameters));
        }

        public bool HasField(string name) => _fields.ContainsKey(name);

        public void AddField(VariableSymbol symbol)
        {
            if (_fields.ContainsKey(symbol.Name))
            {
                throw new SemanticException($"Field '{symbol.Name}' is already declared in class '{Name}'.", symbol.Node);
            }

            _fields[symbol.Name] = symbol;
        }

        public bool TryGetField(string name, out VariableSymbol symbol) => _fields.TryGetValue(name, out symbol!);

        public IReadOnlyList<MethodSymbol> FindMethods(string name)
        {
            return _methods.TryGetValue(name, out var methods)
                ? methods
                : Array.Empty<MethodSymbol>();
        }

        public MethodSymbol? FindMethod(string name, IReadOnlyList<TypeSymbol> parameterTypes)
        {
            return FindMethods(name).FirstOrDefault(method => method.HasSameSignature(parameterTypes));
        }

        public void RemoveField(string name) => _fields.Remove(name);
    }

    private sealed class MethodSymbol
    {
        public MethodSymbol(string name, List<ParameterSymbol> parameters, TypeSymbol returnType)
        {
            Name = name;
            Parameters = parameters;
            ReturnType = returnType;
        }

        public string Name { get; }

        public List<ParameterSymbol> Parameters { get; }

        public TypeSymbol ReturnType { get; }

        public MethodDeclarationNode? Declaration { get; private set; }

        public MethodDeclarationNode? Implementation { get; private set; }

        public void RegisterDeclaration(MethodDeclarationNode node, TypeSymbol returnType, IReadOnlyList<ParameterSymbol> parameters)
        {
            if (!HasSameSignature(parameters))
            {
                throw new SemanticException($"Method '{Name}' is already declared with a different signature.", node);
            }

            if (!string.Equals(ReturnType.Name, returnType.Name, StringComparison.Ordinal))
            {
                throw new SemanticException($"Method '{Name}' return type mismatch. Expected '{ReturnType.Name}' but found '{returnType.Name}'.", node);
            }

            if (node.Body is null)
            {
                if (Declaration is not null && Declaration.Body is null && !ReferenceEquals(Declaration, node))
                {
                    throw new SemanticException($"Method '{Name}' is already forward declared.", node);
                }

                Declaration ??= node;
                return;
            }

            if (Implementation is not null && !ReferenceEquals(Implementation, node))
            {
                throw new SemanticException($"Duplicate implementation for method '{Name}'.", node);
            }

            Implementation = node;
            Declaration ??= node;
            Parameters.Clear();
            foreach (var parameter in parameters)
            {
                Parameters.Add(parameter);
            }
        }

        public bool HasSameSignature(IReadOnlyList<ParameterSymbol> parameters)
        {
            if (Parameters.Count != parameters.Count)
            {
                return false;
            }

            for (var i = 0; i < Parameters.Count; i++)
            {
                if (!string.Equals(Parameters[i].Type.Name, parameters[i].Type.Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public bool HasSameSignature(IReadOnlyList<TypeSymbol> types)
        {
            if (Parameters.Count != types.Count)
            {
                return false;
            }

            for (var i = 0; i < Parameters.Count; i++)
            {
                if (!string.Equals(Parameters[i].Type.Name, types[i].Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public bool ArgumentsMatch(IReadOnlyList<TypeSymbol> argumentTypes)
        {
            if (Parameters.Count != argumentTypes.Count)
            {
                return false;
            }

            for (var i = 0; i < Parameters.Count; i++)
            {
                var parameterType = Parameters[i].Type;
                var argumentType = argumentTypes[i];

                if (parameterType.IsStandard || argumentType.IsStandard)
                {
                    continue;
                }

                if (!string.Equals(parameterType.Name, argumentType.Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class ConstructorSymbol
    {
        public ConstructorSymbol(ConstructorDeclarationNode node, IReadOnlyList<ParameterSymbol> parameters)
        {
            Node = node;
            Parameters = parameters;
        }

        public ConstructorDeclarationNode Node { get; }

        public IReadOnlyList<ParameterSymbol> Parameters { get; }

        public bool HasSameSignature(IReadOnlyList<ParameterSymbol> otherParameters)
        {
            if (Parameters.Count != otherParameters.Count)
            {
                return false;
            }

            for (var i = 0; i < Parameters.Count; i++)
            {
                if (!string.Equals(Parameters[i].Type.Name, otherParameters[i].Type.Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public bool ArgumentsMatch(IReadOnlyList<TypeSymbol> argumentTypes)
        {
            if (Parameters.Count != argumentTypes.Count)
            {
                return false;
            }

            for (var i = 0; i < Parameters.Count; i++)
            {
                var parameterType = Parameters[i].Type;
                var argumentType = argumentTypes[i];

                if (parameterType.IsStandard || argumentType.IsStandard)
                {
                    continue;
                }

                if (!string.Equals(parameterType.Name, argumentType.Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed record ParameterSymbol(string Name, TypeSymbol Type, ParameterNode Node)
    {
        public VariableSymbol ToVariableSymbol() => new(Name, Type, VariableKind.Parameter, Node);
    }

    private sealed class VariableSymbol
    {
        private bool _isUsed;

        public VariableSymbol(string name, TypeSymbol type, VariableKind kind, Node node)
        {
            Name = name;
            Type = type;
            Kind = kind;
            Node = node;
        }

        public string Name { get; }

        public TypeSymbol Type { get; }

        public VariableKind Kind { get; }

        public Node Node { get; }

        public bool IsUsed => _isUsed;

        public void MarkUsed() => _isUsed = true;
    }

    private enum VariableKind
    {
        Field,
        Local,
        Parameter,
    }

    private readonly record struct MethodContext(TypeSymbol ReturnType, bool AllowsReturn)
    {
        public static MethodContext None => new(TypeSymbol.Void, false);
    }

    private sealed class Scope
    {
        private readonly Dictionary<string, VariableSymbol> _variables = new(StringComparer.Ordinal);

        private Scope(Scope? parent)
        {
            Parent = parent;
        }

        public Scope? Parent { get; }

        public static Scope ForFields() => new(null);

        public static Scope ForMethod() => new(null);

        public Scope CreateChild() => new(this);

        public void Declare(VariableSymbol symbol)
        {
            if (_variables.ContainsKey(symbol.Name))
            {
                throw new SemanticException($"Identifier '{symbol.Name}' is already declared in this scope.", symbol.Node);
            }

            _variables[symbol.Name] = symbol;
        }

        public bool TryLookup(string name, out VariableSymbol symbol)
        {
            if (_variables.TryGetValue(name, out symbol!))
            {
                return true;
            }

            return Parent is not null && Parent.TryLookup(name, out symbol!);
        }

        public bool Contains(string name) => _variables.ContainsKey(name);
    }
}
