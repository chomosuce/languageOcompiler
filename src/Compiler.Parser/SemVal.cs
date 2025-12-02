using System;
using System.Collections.Generic;
using Compiler.Ast;

namespace Compiler.Parser
{
    public sealed class SemVal
    {
        private readonly object? _value;
        private readonly string? _id;
        private readonly int? _int;
        private readonly double? _real;
        private readonly bool? _bool;

        public SemVal()
        {
            _value = null;
        }

        private SemVal(object? value, string? id = null, int? intValue = null, double? realValue = null, bool? boolValue = null)
        {
            _value = value;
            _id = id;
            _int = intValue;
            _real = realValue;
            _bool = boolValue;
        }

        public static SemVal FromId(string id) => new SemVal(id, id: id);

        public static SemVal FromInt(long value)
        {
            var intValue = checked((int)value);
            return new SemVal(intValue, intValue: intValue);
        }

        public static SemVal FromReal(double value) => new SemVal(value, realValue: value);

        public static SemVal FromBool(bool value) => new SemVal(value, boolValue: value);

        // Getters that throw an error if the stored semantic value isnt of the expected primitive type
        public string Id => _id ?? _value as string ?? throw new InvalidOperationException("Semantic value does not contain an identifier");

        public int Int => _int ?? _value switch
        {
            int i => i,
            long l => checked((int)l),
            _ => throw new InvalidOperationException("Semantic value does not contain an integer literal"),
        };

        public double Real => _real ?? _value switch
        {
            double d => d,
            float f => f,
            _ => throw new InvalidOperationException("Semantic value does not contain a real literal"),
        };

        public bool Bool => _bool ?? _value switch
        {
            bool b => b,
            _ => throw new InvalidOperationException("Semantic value does not contain a boolean literal"),
        };

        // Internal guard utilities: either retrieve a castable object or throw error
        private static InvalidOperationException Missing(string message) => new InvalidOperationException(message);

        // Checks that _value inside SemVal have type T
        private T Require<T>() where T : class
        {
            if (_value is T typed)
            {
                return typed;
            }

            throw Missing($"Semantic value does not contain an instance of {typeof(T).Name}.");
        }

        private static T Require<T>(SemVal? value) where T : class => value?.Require<T>() ?? throw Missing($"Semantic value does not contain an instance of {typeof(T).Name}.");

        // If value == null return null else Require<T>(value)
        private static T? Allow<T>(SemVal? value) where T : class => value is null ? null : Require<T>(value);

        // Dedicated guard for union members that need to behave as IBodyItem (statements or local vars)
        private static IBodyItem RequireBodyItem(SemVal? value)
        {
            if (value?._value is IBodyItem bodyItem)
            {
                return bodyItem;
            }

            throw Missing("Semantic value does not contain an IBodyItem.");
        }

        // Allow grammar rules to reuse existing List<T> instances and push new elements into them
        public void Add(SemVal? item)
        {
            if (item is null)
            {
                throw Missing("Cannot add a null semantic value.");
            }

            switch (_value)
            {
                case List<ClassNode> classes:
                    classes.Add(Require<ClassNode>(item));
                    break;
                case List<Member> members:
                    members.Add(Require<Member>(item));
                    break;
                case List<ParameterNode> parameters:
                    parameters.Add(Require<ParameterNode>(item));
                    break;
                case List<IBodyItem> bodyItems:
                    bodyItems.Add(RequireBodyItem(item));
                    break;
                case List<Expression> expressions:
                    expressions.Add(Require<Expression>(item));
                    break;
                default:
                    throw Missing($"Semantic value of type {_value?.GetType().Name ?? "null"} does not support Add.");
            }
        }

        // Implicit conversions from AST nodes, lists, and primitives so grammar actions can simply assign
        public static implicit operator SemVal?(ProgramNode? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(List<ClassNode>? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(ClassNode? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(List<Member>? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(Member? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(TypeNode? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(MethodBodyNode? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(BodyNode? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(List<IBodyItem>? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(Statement? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(Expression? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(List<Expression>? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(ParameterNode? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(List<ParameterNode>? value) => value is null ? null : new SemVal(value);
        public static implicit operator SemVal?(string? value) => value is null ? null : new SemVal(value, id: value);
        public static implicit operator SemVal(int value) => new SemVal(value, intValue: value);
        public static implicit operator SemVal(double value) => new SemVal(value, realValue: value);
        public static implicit operator SemVal(bool value) => new SemVal(value, boolValue: value);
        public static implicit operator SemVal?(ArrayTypeNode? value) => value is null ? null : new SemVal(value);

        // Symmetric conversions: allow grammar code to pull the boxed AST node or primitive back out
        public static implicit operator ProgramNode?(SemVal? value) => Allow<ProgramNode>(value);
        public static implicit operator List<ClassNode>?(SemVal? value) => Allow<List<ClassNode>>(value);
        public static implicit operator ClassNode?(SemVal? value) => Allow<ClassNode>(value);
        public static implicit operator List<Member>?(SemVal? value) => Allow<List<Member>>(value);
        public static implicit operator Member?(SemVal? value) => Allow<Member>(value);
        public static implicit operator TypeNode?(SemVal? value) => Allow<TypeNode>(value);
        public static implicit operator MethodBodyNode?(SemVal? value) => Allow<MethodBodyNode>(value);
        public static implicit operator BodyNode?(SemVal? value) => Allow<BodyNode>(value);
        public static implicit operator List<IBodyItem>?(SemVal? value) => Allow<List<IBodyItem>>(value);
        public static implicit operator Statement?(SemVal? value) => Allow<Statement>(value);
        public static implicit operator Expression?(SemVal? value) => Allow<Expression>(value);
        public static implicit operator List<Expression>?(SemVal? value) => Allow<List<Expression>>(value);
        public static implicit operator ParameterNode?(SemVal? value) => Allow<ParameterNode>(value);
        public static implicit operator List<ParameterNode>?(SemVal? value) => Allow<List<ParameterNode>>(value);
        public static implicit operator string?(SemVal? value) => value?.Id;
        public static implicit operator int(SemVal? value) => value?.Int ?? throw Missing("Semantic value does not contain an integer literal.");
        public static implicit operator double(SemVal? value) => value?.Real ?? throw Missing("Semantic value does not contain a real literal.");
        public static implicit operator bool(SemVal? value) => value?.Bool ?? throw Missing("Semantic value does not contain a boolean literal.");
        public static implicit operator ArrayTypeNode?(SemVal? value) => Allow<ArrayTypeNode>(value);
    }
}
