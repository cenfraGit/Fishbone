grammar Fishbone;

// --------------------------------------------------------------------------------
// parser rules
// --------------------------------------------------------------------------------

program : statement* EOF ;

statement
    : declarationStat SEMI
    | assignmentStat SEMI
    | indexedAssignmentStat SEMI
    | compoundAssignmentStat SEMI
    | ID (COMMA ID)* ASSIGN expr SEMI
    | expr SEMI
    | functionDefinitionStat
    | ifStat
    | whileStat
    | foreachStat
    | forStat
    | blockStat
    | tryStat
    | returnStat SEMI
    | breakStat SEMI
    | continueStat SEMI
    | throwStat SEMI
    ;

blockStat : '{' statement* '}' ;

declarationStat       : LET ID (COMMA ID)* ASSIGN expr ;
assignmentStat        : ID (COMMA ID)* ASSIGN expr ;
indexedAssignmentStat : expr ASSIGN expr ;
compoundAssignmentStat : expr (PLUS_ASSIGN|MINUS_ASSIGN|MUL_ASSIGN|DIV_ASSIGN|MOD_ASSIGN) expr ;

ifStat : IF '(' expr ')' statement (ELSE statement)? ;

whileStat   : WHILE '(' expr ')' statement ;
foreachStat : FOREACH '(' ID IN expr ')' statement ;
forStat     : FOR '(' ID IN expr (COMMA expr (COMMA expr)?)? ')' statement ;

functionDefinitionStat : FUNC ID '(' (ID (COMMA ID)*)? ')' blockStat ;

// at least one of catchClause/finallyClause is required (enforced when building the AST)
tryStat       : TRY blockStat catchClause? finallyClause? ;
catchClause   : CATCH ('(' ID ')')? blockStat ;
finallyClause : FINALLY blockStat ;

throwStat : THROW expr? ; // bare 'throw;' rethrows, only valid inside catch

argument     : (OUT|REF)? expr ;
returnStat   : RETURN (expr (COMMA expr)*)? ;
breakStat    : BREAK ;
continueStat : CONTINUE ;
dictPair     : expr COLON expr ;

expr
    : '(' expr ')'                            #ParenthesesExpr
    | '[' (expr (COMMA expr)*)? ']'           #ListExpr
    | '{' (dictPair (COMMA dictPair)*)? '}'   #DictionaryExpr
    | expr '(' (argument (COMMA argument)*)? ')'  #CallExpr
    | expr '.' ID                             #MemberAccessExpr
    | expr '[' expr ']'                       #IndexingExpr
    | (MINUS|NOT) expr                        #UnaryExpr
    | expr (MUL|DIV|MOD) expr                 #BinaryExpr
    | expr (PLUS|MINUS) expr                  #BinaryExpr
    | expr AS ID                              #CastExpr
    | expr (GE|LE|GT|LT) expr                 #BinaryExpr
    | expr (EQ|NEQ) expr                      #BinaryExpr
    | expr (AND|OR|XOR) expr                  #BoolOperatorExpr
    | NULL                                    #NullExpr
    | ID                                      #IdExpr
    | INT                                     #IntExpr
    | DOUBLE                                  #DoubleExpr
    | STRING                                  #StringExpr
    | RAW_STRING                              #RawStringExpr
    | INTERP_STRING                           #InterpStringExpr
    | (TRUE|FALSE)                            #BoolExpr
    ;

// entry point for parsing a single expression (used for interpolation holes)
exprStandalone : expr EOF ;

// --------------------------------------------------------------------------------
// lexer rules
// --------------------------------------------------------------------------------

COMMA  : ',' ;
SEMI   : ';' ;
COLON  : ':' ;

INT    : [0-9]+ ('_'+ [0-9]+)* ;
DOUBLE : [0-9]* '.' [0-9]+ ;
STRING : '"' (ESC | ~["\\\r\n])* '"' ;

RAW_STRING : '@"' ('""' | ~["])* '"' ;

INTERP_STRING : '$"' (ESC | '{{' | '}}' | HOLE | ~["\\{}\r\n])* '"' ;
fragment HOLE : '{' (RAW_STRING | STRING | HOLE | ~["{}])* '}' ;

fragment ESC : '\\' . ;

PLUS  : '+' ;
MINUS : '-' ;
MUL   : '*' ;
DIV   : '/';
MOD   : '%' ;

PLUS_ASSIGN  : '+=' ;
MINUS_ASSIGN : '-=' ;
MUL_ASSIGN   : '*=' ;
DIV_ASSIGN   : '/=' ;
MOD_ASSIGN   : '%=' ;

EQ  : '==' ;
NEQ : '!=' ;
GE  : '>=' ;
LE  : '<=' ;
GT  : '>' ;
LT  : '<' ;

NULL : 'null' ;

AND : 'and' ;
OR  : 'or' ;
XOR : 'xor' ;
NOT : 'not' ;

ASSIGN : '=' ;

TRUE    : 'true' ;
FALSE   : 'false' ;
IF      : 'if' ;
ELSE    : 'else' ;

WHILE   : 'while' ;
FOREACH : 'foreach' ;
FOR     : 'for' ;
IN      : 'in';
AS      : 'as';
FUNC    : 'func' ;
BREAK   : 'break' ;
CONTINUE: 'continue' ;
RETURN  : 'return' ;
TRY     : 'try' ;
CATCH   : 'catch' ;
FINALLY : 'finally' ;
THROW   : 'throw' ;
LET     : 'let' ;
OUT     : 'out' ;
REF     : 'ref' ;

LINE_COMMENT : '//' ~[\r\n]* -> skip ;
BLOCK_COMMENT : '/*' .*? '*/' -> skip ;

WS     : [ \t\r\n]+ -> skip;
ID     : [a-zA-Z_][a-zA-Z0-9_]*;