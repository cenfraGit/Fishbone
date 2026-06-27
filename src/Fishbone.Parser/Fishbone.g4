grammar Fishbone;

// --------------------------------------------------------------------------------
// parser rules
// --------------------------------------------------------------------------------

program : statement* EOF ;

statement
    : declarationStat SEMI
    | assignmentStat SEMI
    | indexedAssignmentStat SEMI
    | ID (COMMA ID)* ASSIGN expr SEMI
    | expr SEMI
    | functionDefinitionStat
    | ifStat
    | whileStat
    | foreachStat
    | forStat
    | blockStat
    | returnStat SEMI
    | breakStat SEMI
    | continueStat SEMI
    ;

blockStat : '{' statement* '}' ;

declarationStat       : LET ID (COMMA ID)* ASSIGN expr ;
assignmentStat        : ID (COMMA ID)* ASSIGN expr ;
indexedAssignmentStat : expr ASSIGN expr ;

ifStat : IF '(' expr ')' blockStat (ELSEIF '(' expr ')' blockStat)* (ELSE blockStat)? ;

whileStat   : WHILE '(' expr ')' blockStat ;
foreachStat : FOREACH '(' ID IN expr ')' blockStat ;
forStat     : FOR '(' ID IN expr (COMMA expr (COMMA expr)?)? ')' blockStat ;

functionDefinitionStat : FUNC ID '(' (ID (COMMA ID)*)? ')' blockStat ;

returnStat   : RETURN (expr (COMMA expr)*)? ;
breakStat    : BREAK ;
continueStat : CONTINUE ;
dictPair     : expr COLON expr ;

expr
    : '(' expr ')'                            #ParenthesesExpr
    | '[' (expr (COMMA expr)*)? ']'           #ListExpr
    | '{' (dictPair (COMMA dictPair)*)? '}'   #DictionaryExpr
    | expr '(' (expr (COMMA expr)*)? ')'      #CallExpr
    | expr '.' ID                             #MemberAccessExpr
    | expr '[' expr ']'                       #IndexingExpr
    | (MINUS|NOT) expr                        #UnaryExpr
    | expr (MUL|DIV) expr                     #BinaryExpr
    | expr (PLUS|MINUS) expr                  #BinaryExpr
    | expr (GE|LE|GT|LT) expr                 #BinaryExpr
    | expr (EQ|NEQ) expr                      #BinaryExpr
    | expr (AND|OR|XOR) expr                  #BoolOperatorExpr
    | NULL                                    #NullExpr
    | ID                                      #IdExpr
    | INT                                     #IntExpr
    | DOUBLE                                  #DoubleExpr
    | STRING                                  #StringExpr
    | (TRUE|FALSE)                            #BoolExpr
    ;

// --------------------------------------------------------------------------------
// lexer rules
// --------------------------------------------------------------------------------

COMMA  : ',' ;
SEMI   : ';' ;
COLON  : ':' ;

INT    : [0-9]+ ('_'+ [0-9]+)* ;
DOUBLE : [0-9]* '.' [0-9]+ ;
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

NULL : 'null' ;

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
FOR     : 'for' ;
IN      : 'in';
FUNC    : 'func' ;
BREAK   : 'break' ;
CONTINUE: 'continue' ;
RETURN  : 'return' ;
LET     : 'let' ;

LINE_COMMENT : '//' ~[\r\n]* -> skip ;
BLOCK_COMMENT : '/*' .*? '*/' -> skip ;

WS     : [ \t\r\n]+ -> skip;
ID     : [a-zA-Z_][a-zA-Z0-9_]*;