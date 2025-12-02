#nullable disable

using System;
using System.Collections.Generic;
using System.Text;

namespace Compiler.Ast
{
    public class AstPrinter : IAstVisitor<string>
    {
        private int _indentLevel = 0;
        private const int IndentSize = 2;

        public string Print(Node node)
        {
            return node.Accept(this);
        }

        private string Indent()
        {
            return new string(' ', _indentLevel * IndentSize);
        }

        private string WithIndent(Func<string> action)
        {
            _indentLevel++;
            var result = action();
            _indentLevel--;
            return result;
        }

        private string FormatList<T>(string label, IList<T> items) where T : Node
        {
            if (items.Count == 0)
                return $"{Indent()}{label}: []";

            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}{label}: [");
            
            _indentLevel++;
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(items[i].Accept(this));
                if (i < items.Count - 1)
                    sb.Append(",");
                sb.AppendLine();
            }
            _indentLevel--;
            
            sb.Append($"{Indent()}]");
            return sb.ToString();
        }

        private string FormatProperty(string name, string value)
        {
            return $"{Indent()}{name}: {value}";
        }

        private string FormatProperty(string name, Node node)
        {
            if (node == null)
                return $"{Indent()}{name}: null";
                
            return WithIndent(() => 
            {
                var result = new StringBuilder();
                result.AppendLine($"{Indent()}{name}:");
                result.Append(node.Accept(this));
                return result.ToString();
            });
        }

        public string Visit(ProgramNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}ProgramNode");
            sb.AppendLine(FormatList("Classes", node.Classes));
            return sb.ToString();
        }

        public string Visit(ClassNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}ClassNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Name", node.Name));
            sb.AppendLine(FormatProperty("BaseClass", node.BaseClass ?? "null"));
            sb.AppendLine(FormatList("Members", node.Members));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(VariableDeclarationNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}VariableDeclarationNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Name", node.Name));
            sb.AppendLine(FormatProperty("InitialValue", node.InitialValue));
            if (node.ResolvedType != null)
                sb.AppendLine(FormatProperty("ResolvedType", node.ResolvedType));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(MethodDeclarationNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}MethodDeclarationNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Name", node.Name));
            sb.AppendLine(FormatList("Parameters", node.Parameters));
            sb.AppendLine(FormatProperty("ReturnType", node.ReturnType));
            sb.AppendLine(FormatProperty("Body", node.Body));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(ConstructorDeclarationNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}ConstructorDeclarationNode");
            _indentLevel++;
            sb.AppendLine(FormatList("Parameters", node.Parameters));
            sb.AppendLine(FormatProperty("Body", node.Body));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(TypeNode node)
        {
            return $"{Indent()}TypeNode(Name: {node.Name})";
        }

        public string Visit(ParameterNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}ParameterNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Name", node.Name));
            sb.AppendLine(FormatProperty("Type", node.Type));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(BodyNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}BodyNode");
            
            if (node.Items.Count == 0)
            {
                sb.AppendLine($"{Indent()}Items: []");
                return sb.ToString();
            }

            sb.AppendLine($"{Indent()}Items: [");
            _indentLevel++;
            
            for (int i = 0; i < node.Items.Count; i++)
            {
                var item = node.Items[i];
                if (item is Node astNode)
                {
                    sb.Append(astNode.Accept(this));
                }
                else
                {
                    sb.Append($"{Indent()}{item.GetType().Name}");
                }
                
                if (i < node.Items.Count - 1)
                    sb.Append(",");
                sb.AppendLine();
            }
            
            _indentLevel--;
            sb.Append($"{Indent()}]");
            
            return sb.ToString();
        }

        public string Visit(AssignmentNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}AssignmentNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Target", node.Target));
            sb.AppendLine(FormatProperty("Value", node.Value));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(WhileLoopNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}WhileLoopNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Condition", node.Condition));
            sb.AppendLine(FormatProperty("Body", node.Body));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(IfStatementNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}IfStatementNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Condition", node.Condition));
            sb.AppendLine(FormatProperty("ThenBranch", node.ThenBranch));
            sb.AppendLine(FormatProperty("ElseBranch", node.ElseBranch));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(ReturnStatementNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}ReturnStatementNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Value", node.Value));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(ExpressionStatementNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}ExpressionStatementNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Expression", node.Expression));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(IntegerLiteralNode node)
        {
            return $"{Indent()}IntegerLiteralNode(Value: {node.Value})";
        }

        public string Visit(RealLiteralNode node)
        {
            return $"{Indent()}RealLiteralNode(Value: {node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
        }

        public string Visit(BooleanLiteralNode node)
        {
            return $"{Indent()}BooleanLiteralNode(Value: {node.Value})";
        }

        public string Visit(IdentifierNode node)
        {
            return $"{Indent()}IdentifierNode(Name: {node.Name})";
        }

        public string Visit(ThisNode node)
        {
            return $"{Indent()}ThisNode";
        }

        public string Visit(ConstructorCallNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}ConstructorCallNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("ClassName", node.ClassName));
            if (node.GenericArgument is not null)
            {
                sb.AppendLine(FormatProperty("GenericArgument", node.GenericArgument));
            }
            sb.AppendLine(FormatList("Arguments", node.Arguments));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(CallNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}CallNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Callee", node.Callee));
            sb.AppendLine(FormatList("Arguments", node.Arguments));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(MemberAccessNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}MemberAccessNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Target", node.Target));
            sb.AppendLine(FormatProperty("MemberName", node.MemberName));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(MethodBodyNode node)
        {
            return node.Accept(this);
        }

        public string Visit(ExpressionBodyNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}ExpressionBodyNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Expression", node.Expression));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(BlockBodyNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Indent()}BlockBodyNode");
            _indentLevel++;
            sb.AppendLine(FormatProperty("Body", node.Body));
            _indentLevel--;
            return sb.ToString();
        }

        public string Visit(ArrayTypeNode node)
        {
            return $"{Indent()}ArrayTypeNode(ElementType: {node.ElementType.Accept(this)})";
        }

    }
}
