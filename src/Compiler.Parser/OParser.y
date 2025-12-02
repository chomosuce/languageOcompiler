%namespace Compiler.Parser
%parsertype Parser
%visibility public
%tokentype Tokens
%YYSTYPE Compiler.Parser.SemVal
%partial

%using System;
%using System.Collections.Generic;
%using Compiler.Ast;


%locations

/* from Lexer.TokenType */
%token KW_CLASS KW_EXTENDS KW_IS KW_END KW_VAR KW_METHOD
%token KW_THIS KW_RETURN KW_WHILE KW_LOOP KW_IF KW_THEN KW_ELSE

%token IDENT
%token INT_LITERAL REAL_LITERAL BOOL_LITERAL

%token LPAREN RPAREN COLON DOT COMMA
%token LBRACKET RBRACKET

%token ASSIGN      /* := */
%token ARROW       /* => */

%start program

%%

/* ======= Program ======= */

program
    : /* empty */
        {
            var program = AttachLocation(new ProgramNode(new List<ClassNode>()), @$);
            Result = program;
            $$ = program;
        }
    | class_list
        {
            var program = AttachLocation(new ProgramNode($1), @$);
            Result = program;
            $$ = program;
        }
    ;


class_list
    : class_decl                          { $$ = new List<ClassNode> { $1 }; }
    | class_list class_decl               { $1.Add($2); $$ = $1; }
    ;

/* ======= Class declaration =======

   class ClassName [ extends ClassName ]
         is { MemberDeclaration } end
*/
class_decl
    : KW_CLASS class_name KW_IS member_list KW_END
        {
            $$ = AttachLocation(new ClassNode($2, null, $4), @2);
        }
    | KW_CLASS class_name KW_EXTENDS class_name KW_IS member_list KW_END
        {
            $$ = AttachLocation(new ClassNode($2, $4, $6), @2);
        }
    ;

/* Class name as an identifier */
class_name
    : IDENT                              { $$ = $1.Id; }
    ;

/* ======= List of class memebers ======= */

member_list
    : /* empty */                        { $$ = new List<Member>(); }
    | member_list member                 { $1.Add($2); $$ = $1; }
    ;

member
    : var_decl                           { $$ = $1; }
    | method_decl                        { $$ = $1; }
    | ctor_decl                          { $$ = $1; }
    ;

/* ======= Variable =======

   var Identifier : Expression
   The type in O is derived from the initializer
   Put initialValue in the AST.
*/
var_decl
    : KW_VAR IDENT COLON expr
        {
            $$ = AttachLocation(new VariableDeclarationNode(
                    Name: $2.Id,
                    InitialValue: $4
                 ), @2);
        }
    ;


/* ======= TYPES ======= */

type_name
    : class_name                         { $$ = AttachLocation(new TypeNode($1), @1); }
    | array_type                         { $$ = $1; }
    ;

/* Array type: Array [ TypeName ] */
array_type
    : IDENT LBRACKET type_name RBRACKET
        {
            $$ = AttachLocation(new ArrayTypeNode($3), @1);
        }
    ;

/* ======= Method =======

   MethodDeclaration : MethodHeader [ MethodBody ]

   MethodHeader : method Identifier [ Parameters ] [ : Identifier ]
   MethodBody   : is Body end | => Expression | (отсутствует — forward)
*/
method_decl
    : KW_METHOD method_name opt_params opt_return_type method_body
        {
            $$ = AttachLocation(new MethodDeclarationNode(
                    Name: $2,
                    Parameters: $3,
                    ReturnType: $4,
                    Body: $5
                 ), @1);
        }
    | KW_METHOD method_name opt_params opt_return_type
        {
            /* forward declaration: Body == null */
            $$ = AttachLocation(new MethodDeclarationNode(
                    Name: $2,
                    Parameters: $3,
                    ReturnType: $4,
                    Body: null
                 ), @1);
        }
    ;

method_name
    : IDENT                              { $$ = $1.Id; }
    ;

opt_return_type
    : /* empty */                        { $$ = null; }
    | COLON type_name                    { $$ = $2; }
    ;
method_body
    : KW_IS body KW_END                  { $$ = AttachLocation(new BlockBodyNode($2), @1); }
    | ARROW expr                         { $$ = AttachLocation(new ExpressionBodyNode($2), @2); }
    ;

/* ======= Constructor =======

   this [ Parameters ] is Body end
*/
ctor_decl
    : KW_THIS opt_params KW_IS body KW_END
        {
            $$ = AttachLocation(new ConstructorDeclarationNode(
                    Parameters: $2,
                    Body: $4
                 ), @1);
        }
    ;

/* ======= Parameters =======

   Parameters : ( ParameterDeclaration { , ParameterDeclaration } )
   ParameterDeclaration : Identifier : ClassName
*/
opt_params
    : /* empty */                        { $$ = new List<ParameterNode>(); }
    | LPAREN RPAREN                      { $$ = new List<ParameterNode>(); }
    | LPAREN param_list RPAREN           { $$ = $2; }
    ;

param_list
    : param                              { $$ = new List<ParameterNode> { $1 }; }
    | param_list COMMA param             { $1.Add($3); $$ = $1; }
    ;

param
    : IDENT COLON class_name             { $$ = AttachLocation(new ParameterNode($1.Id, AttachLocation(new TypeNode($3), @3)), @1); }
    ;

/* ======= Body =======

   Body : { VariableDeclaration | Statement }
   Mixed list: local vars and operators. Both implement IBodyItem.
*/
body
    : body_items                         { $$ = AttachLocation(new BodyNode($1), @$); }
    ;

body_items
    : /* empty */                        { $$ = new List<IBodyItem>(); }
    | body_items body_item               { $1.Add($2); $$ = $1; }
    ;

body_item
    : var_decl                           { $$ = $1; }
    | stmt                               { $$ = $1; }
    ;

/* ======= Operators =======

   Statement :
       Assignment
     | WhileLoop
     | IfStatement
     | ReturnStatement
*/

stmt
    : assignment                         { $$ = $1; }
    | while_stmt                         { $$ = $1; }
    | if_stmt                            { $$ = $1; }
    | return_stmt                        { $$ = $1; }
    | expr                               { $$ = AttachLocation(new ExpressionStatementNode($1), @1); }
    ;

/* Assignment : Identifier := Expression */
assignment
    : IDENT ASSIGN expr
        {
            var lhs = AttachLocation(new IdentifierNode($1.Id), @1);
            $$ = AttachLocation(new AssignmentNode(lhs, $3), @1);
        }
    ;

/* WhileLoop : while Expression loop Body end */
while_stmt
    : KW_WHILE expr KW_LOOP body KW_END
        { $$ = AttachLocation(new WhileLoopNode($2, $4), @1); }
    ;

/* IfStatement : if Expression then Body [ else Body ] end */
if_stmt
    : KW_IF expr KW_THEN body KW_END
        { $$ = AttachLocation(new IfStatementNode($2, $4, null), @1); }
    | KW_IF expr KW_THEN body KW_ELSE body KW_END
        { $$ = AttachLocation(new IfStatementNode($2, $4, $6), @1); }
    ;

/* ReturnStatement : return [ Expression ] */
return_stmt
    : KW_RETURN                          { $$ = AttachLocation(new ReturnStatementNode(null), @1); }
    | KW_RETURN expr                     { $$ = AttachLocation(new ReturnStatementNode($2), @1); }
    ;

/* ======= Expression =======

   Expression :
       Primary
     | ConstructorInvokation
     | FunctionCall
     | Expression { . Expression }  (chains via dot)

   In AST:
     - ConstructorCallNode("Integer", [args...])
     - CallNode(calleeExpr, [args...])
     - MemberAccessNode(targetExpr, "name")

   disassemble the "cores" (primary/constructor/call_or_access) and allow right-hand recursion for chains via dot.
*/

expr
    : call_or_access                     { $$ = $1; }
    ;

/* call_or_access covers both a single primary/constructor and chains
   kind of: primary (. IDENT (args?) )*
*/
call_or_access
    : primary                            { $$ = $1; }
    | constructor_invocation             { $$ = $1; }
    | call_or_access DOT IDENT
        {
            /* member access: a.b  */
            $$ = AttachLocation(new MemberAccessNode($1, $3.Id), @3);
        }
    | call_or_access LPAREN RPAREN
        {
            /* call without arguments: f() or a.b() */
            $$ = AttachLocation(new CallNode($1, new List<Expression>()), @2);
        }
    | call_or_access LPAREN arg_list RPAREN
        {
            /* call with arguments: f(x, y) или a.b(x) */
            $$ = AttachLocation(new CallNode($1, $3), @2);
        }
    ;

/* ConstructorInvokation : ClassName [ Arguments ] */
constructor_invocation
    : class_name LPAREN RPAREN
        { $$ = AttachLocation(new ConstructorCallNode($1, new List<Expression>()), @1); }
    | class_name LPAREN arg_list RPAREN
        { $$ = AttachLocation(new ConstructorCallNode($1, $3), @1); }
    | IDENT LBRACKET type_name RBRACKET LPAREN RPAREN
        {
            if ($1 == "Array")
                $$ = AttachLocation(new ConstructorCallNode("Array", new List<Expression>(), $3), @1);
            else
                $$ = AttachLocation(new ConstructorCallNode($1, new List<Expression>()), @1);
        }
    | IDENT LBRACKET type_name RBRACKET LPAREN arg_list RPAREN
        {
            if ($1 == "Array")
                $$ = AttachLocation(new ConstructorCallNode("Array", $6, $3), @1);
            else
                $$ = AttachLocation(new ConstructorCallNode($1, $6), @1);
        }
    ;

/* Arguments: () | ( expr {, expr} ) */
opt_args
    : /* empty */                        { $$ = new List<Expression>(); }
    | LPAREN RPAREN                      { $$ = new List<Expression>(); }
    | LPAREN arg_list RPAREN             { $$ = $2; }
    ;

arg_list
    : expr                               { $$ = new List<Expression> { $1 }; }
    | arg_list COMMA expr                { $1.Add($3); $$ = $1; }
    ;

/* Primary :
     IntegerLiteral | RealLiteral | BooleanLiteral | this
*/
primary
    : INT_LITERAL                        { $$ = AttachLocation(new IntegerLiteralNode($1.Int), @1); }
    | REAL_LITERAL                       { $$ = AttachLocation(new RealLiteralNode($1.Real), @1); }
    | BOOL_LITERAL                       { $$ = AttachLocation(new BooleanLiteralNode($1.Bool), @1); }
    | KW_THIS                            { $$ = AttachLocation(new ThisNode(), @1); }
    | IDENT                              { $$ = AttachLocation(new IdentifierNode($1.Id), @1); }
    ;

%%

/* ============= C# trailer ============= */

public Parser(Scanner scanner) : base(scanner)
{
}

public ProgramNode? Result { get; private set; }
