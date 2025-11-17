using System;
using System.IO;
using Compiler.Ast;
using Compiler.LLvm;
using Compiler.Parser;
using Compiler.Semantics;

class Entry
{
    static void Main(string[] args)
    {
        var src = args.Length > 0
            ? File.ReadAllText(args[0])
            : "class Main is\n    var x : 42\nend\n";
        using var reader = new StringReader(src);

        var scanner = new Scanner(reader);
        var parser = new Parser(scanner);

        if (parser.Parse())
        {
            if (parser.Result is ProgramNode ast)
            {
                try
                {
                    // Run semantic analysis before printing the AST.
                    var analyzer = new SemanticAnalyzer();
                    analyzer.Analyze(ast);

                    var printer = new AstPrinter();
                    Console.WriteLine(printer.Print(ast));

                    var llvmGenerator = new LlvmGenerator();
                    var llvmIr = llvmGenerator.Generate(ast);

                    Console.WriteLine(";; ---- LLVM IR ----");
                    Console.WriteLine(llvmIr);
                }
                catch (SemanticException ex)
                {
                    Console.WriteLine($"Semantic error: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Parse succeeded but no AST produced");
            }
        }
        else
        {
            Console.WriteLine("Parse failed");
        }
    }
}
