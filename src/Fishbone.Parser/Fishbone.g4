grammar Fishbone;

// --------------------------------------------------------------------------------
// parser rules
// --------------------------------------------------------------------------------

program : importStat* statement* EOF ;

importStat : IMPORT STRING SEMI ;

statement
    : declarationStat SEMI
    | assignmentStat SEMI
    | ID (COMMA ID)* ASSIGN expr SEMI
    | functionCallStat SEMI
    | functionDefinitionStat
    | ifStat
    | whileStat
    | foreachStat
    | blockStat
    | returnStat SEMI
    | breakStat SEMI
    | continueStat SEMI
    ;

blockStat : '{' statement* '}' ;

declarationStat : 'let' ID (COMMA ID)* ASSIGN expr ;
assignmentStat : ID (COMMA ID)* ASSIGN expr ;

ifStat : IF '(' expr ')' blockStat (ELSEIF '(' expr ')' blockStat)* (ELSE blockStat)? ;

whileStat : WHILE '(' expr ')' blockStat ;
foreachStat : FOREACH '(' ID IN expr ')' blockStat ;

functionCallStat : ID '(' (expr (COMMA expr)*)? ')' ;
functionDefinitionStat : FUNC ID '(' (ID (COMMA ID)*)? ')' blockStat ;
returnStat : RETURN (expr (COMMA expr)*)? ;
breakStat : 'break' ;
continueStat : 'continue' ;

expr
    : '(' expr ')'                    #ParenthesesExpr
    | functionCallStat                #FunctionCallExpr
    | (MINUS|NOT) expr                #UnaryExpr
    | expr (MUL|DIV) expr             #BinaryExpr
    | expr (PLUS|MINUS) expr          #BinaryExpr
    | expr (GE|LE|GT|LT) expr         #BinaryExpr
    | expr (EQ|NEQ) expr              #BinaryExpr
    | expr (AND|OR|XOR) expr          #BoolOperatorExpr
    | ID                              #IdExpr
    | INT                             #IntExpr
    | FLOAT                           #FloatExpr
    | STRING                          #StringExpr
    | (TRUE|FALSE)                    #BoolExpr
    ;

// --------------------------------------------------------------------------------
// lexer rules
// --------------------------------------------------------------------------------

COMMA  : ',' ;
SEMI   : ';' ;

INT    : [0-9]+ ('_'+ [0-9]+)* ;
FLOAT  : [0-9]* '.' [0-9]+ ;
STRING : '"' ~["]* '"' ; // anything that isnt a quote

PLUS  : '+' ;
MINUS : '-' ;
MUL   : '*' ;
DIV   : '/';

EQ  : '==' ;
NEQ : '!=' ;
GE  : '>=' ;
LE  : '<=' ;
GT  : '>' ;
LT  : '<' ;

AND : 'and' ;
OR  : 'or' ;
XOR : 'xor' ;
NOT : 'not' ;

ASSIGN : '=' ;

TRUE    : 'true' ;
FALSE   : 'false' ;
IF      : 'if' ;
ELSEIF  : 'else if' ;
ELSE    : 'else' ;

WHILE   : 'while' ;
FOREACH : 'foreach' ;
IN      : 'in';
FUNC    : 'func' ;
RETURN  : 'return' ;

IMPORT  : 'import' ;

LINE_COMMENT : '//' ~[\r\n]* -> skip ;
BLOCK_COMMENT : '/*' .*? '*/' -> skip ;

WS     : [ \t\r\n]+ -> skip;
ID     : [a-zA-Z_][a-zA-Z0-9_]*;