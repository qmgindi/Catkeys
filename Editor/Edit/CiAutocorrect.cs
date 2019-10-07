using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Win32;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
//using System.Drawing;
using System.Linq;
//using System.Xml.Linq;

using Au;
using Au.Types;
using static Au.AStatic;
using Au.Controls;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;

class CiAutocorrect
{
    public class BeforeCharContext
    {
        public int oldPosUtf8, newPosUtf8;
        public bool dontSuppress;
    }

    /// <summary>
    /// Call when added text with { } etc and want it behave like when the user types { etc.
    /// </summary>
    public void BracesAdded(SciCode doc, int innerFrom, int innerTo, EBraces operation)
    {
        var r = doc.ZTempRanges_Add(this, innerFrom, innerTo);
        if(operation == EBraces.NewExpression) r.OwnerData = "new";
    }

    public enum EBraces
    {
        /// <summary>
        /// The same as when the user types '(' etc and is auto-added ')' etc. The user can overtype the ')' with the same character or delete '()' with Backspace.
        /// </summary>
        Regular,

        /// <summary>
        /// Like Regular, but also the user can overtype entire empty '()' with '[' or '{'. Like always, is auto-added ']' or '}' and the final result is '[]' or '{}'.
        /// </summary>
        NewExpression,
    }

    /// <summary>
    /// Called on Enter, Shift+Enter, Ctrl+Enter, Ctrl+; Tab and Backspace, before passing it to Scintilla. Won't pass if returns true.
    /// Enter: If before ')' or ']' and not after ',': leaves the argument list etc and adds newline, maybe semicolon, braces, indentation, and returns true.
    /// Shift+Enter or Ctrl+Enter: The same as above, but anywhere.
    /// Ctrl+;: Like SciBeforeCharAdded(';'), but anywhere; and inserts semicolon now.
    /// Tab: not impl. Returns false.
    /// Backspace: If inside an empty temp range, selects the '()' etc to erase and returns false.
    /// </summary>
    public bool SciBeforeKey(SciCode doc, Keys keyData)
    {
        switch(keyData) {
        case Keys.Enter:
            return _OnEnterOrSemicolon(anywhere: false, onSemicolon: false, out _);
        case Keys.Enter | Keys.Shift:
        case Keys.Enter | Keys.Control:
            _OnEnterOrSemicolon(anywhere: true, onSemicolon: false, out _);
            return true;
        case Keys.OemSemicolon | Keys.Control:
            _OnEnterOrSemicolon(anywhere: true, onSemicolon: true, out _);
            return true;
        case Keys.Tab:
            return false; //TODO: if in argument list, add or select next argument. Even if in string or comment.
        case Keys.Back:
            return SciBeforeCharAdded(doc, (char)8, out _);
        default:
            Debug.Assert(false);
            return false;
        }
    }

    /// <summary>
    /// Called on WM_CHAR, before passing it to Scintilla. Won't pass if returns true, unless ch is ';'.
    /// If ch is ')' etc, and at current position is ')' etc previously added on '(' etc, clears the temp range, sets the out vars and returns true.
    /// If ch is ';' inside '(...)' and the terminating ';' is missing, sets newPosUtf8 = where ';' should be and returns true.
    /// </summary>
    public bool SciBeforeCharAdded(SciCode doc, char ch, out BeforeCharContext c)
    {
        c = null;
        bool isBackspace = false, isOpenBrac = false;

        switch(ch) {
        case ';': return _OnEnterOrSemicolon(anywhere: false, onSemicolon: true, out c);
        case '\"': case '\'': case ')': case ']': case '}': case '>': break; //overtype auto-added char
        case (char)8: isBackspace = true; break; //delete auto-added char too
        case '[': case '{': isOpenBrac = true; break; //replace auto-added '()' when completing 'new Type' with '[]' or '{}'
        default: return false;
        }

        int pos = doc.Z.CurrentPos8;
        var r = doc.ZTempRanges_Enum(pos, this, endPosition: (ch == '\"' || ch == '\''), utf8: true).FirstOrDefault();
        if(r == null) return false;
        if(isOpenBrac && r.OwnerData != (object)"new") return false;
        r.GetCurrentFromTo(out int from, out int to, utf8: true);

        if(isBackspace || isOpenBrac) {
            if(pos != from) return false;
        } else {
            if(ch != (char)doc.Call(Sci.SCI_GETCHARAT, to)) return false; //info: '\0' if posUtf8 invalid
        }
        for(int i = pos; i < to; i++) switch((char)doc.Call(Sci.SCI_GETCHARAT, i)) { case ' ': case '\r': case '\n': case '\t': break; default: return false; } //eg space before '}'

        r.Remove();

        if(isBackspace || isOpenBrac) {
            doc.Call(Sci.SCI_SETSEL, pos - 1, to + 1); //select and pass to Scintilla, let it delete or overtype
            return false;
        }

        c = new BeforeCharContext { oldPosUtf8 = pos, newPosUtf8 = to + 1 };
        return true;
    }

    /// <summary>
    /// Called on SCN_CHARADDED. If ch is '(' etc, adds ')' etc.
    /// </summary>
    public void SciCharAdded(CodeInfo.CharContext c)
    {
        char ch = c.ch;
        string replaceText = ch switch { '\"' => "\"", '\'' => "'", '(' => ")", '[' => "]", '{' => "}", '<' => ">", '*' => "*/", _ => null };
        if(replaceText == null) return;

        if(!CodeInfo.GetContextAndDocument(out var cd)) return;
        string code = cd.code;
        int pos = cd.position - 1; if(pos < 0) return;

        Debug.Assert(code[pos] == ch);
        if(code[pos] != ch) return;

        bool isBeforeWord = cd.position < code.Length && char.IsLetterOrDigit(code[cd.position]); //usually user wants to enclose the word manually, unless typed '{' in interpolated string
        if(isBeforeWord && ch != '{') return;

        var root = cd.document.GetSyntaxRootAsync().Result;
        //if(!root.ContainsDiagnostics) return; //no. Don't use errors. It can do more bad than good. Tested.

        int replaceLength = 0, tempRangeFrom = cd.position, tempRangeTo = cd.position, newPos = 0;

        if(ch == '*') { /**/
            var trivia = root.FindTrivia(pos);
            if(!trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) return;
            if(trivia.SpanStart != --pos) return;
            tempRangeFrom = 0;
        } else {
            var node = root.FindToken(pos).Parent;
            var kind = node.Kind();
            if(kind == SyntaxKind.InterpolatedStringText) {
                node = node.Parent;
                kind = node.Kind();
            }

            if(isBeforeWord && kind != SyntaxKind.Interpolation) return;

            var span = node.Span;
            if(span.Start > pos) return; // > if pos is in node's leading trivia, eg comments or #if-disabled block

            //CiUtil.PrintNode(node);
            //Print("ALL");
            //CiUtil.PrintNode(root, false);

            if(ch == '\"' || ch == '\'') {
                bool isVerbatim = false, isInterpolated = false;
                switch(kind) {
                case SyntaxKind.CharacterLiteralExpression when ch == '\'':
                    break;
                case SyntaxKind.StringLiteralExpression when ch == '\"':
                    isVerbatim = code[span.Start] == '@';
                    break;
                case SyntaxKind.InterpolatedStringExpression when ch == '\"':
                    isInterpolated = true;
                    isVerbatim = code[span.Start] == '@' || code[span.Start + 1] == '@';
                    break;
                default: return;
                }

                if(span.Start != pos - (isVerbatim ? 1 : 0) - (isInterpolated ? 1 : 0)) {
                    if(!isVerbatim) return;
                    //inside verbatim string replace " with ""
                    cd.position--; //insert " before ", and let caret be after ""
                    tempRangeFrom = 0;
                }
            } else {
                //Print(kind);
                if(ch == '<' && !(kind == SyntaxKind.TypeParameterList || kind == SyntaxKind.TypeArgumentList)) return; //can be operators
                switch(kind) {
                case SyntaxKind.CompilationUnit:
                case SyntaxKind.CharacterLiteralExpression:
                case SyntaxKind.StringLiteralExpression:
                    return;
                case SyntaxKind.InterpolatedStringExpression:
                    //after next typed { in interpolated string remove } added after first {
                    if(ch == '{' && code.Eq(pos - 1, "{{}") && c.doc.ZTempRanges_Enum(cd.position, this, endPosition: true).Any()) {
                        replaceLength = 1;
                        replaceText = null;
                        tempRangeFrom = 0;
                        break;
                    }
                    return;
                default:
                    if(_IsInNonblankTrivia(node, pos)) return;
                    if(ch == '{' && kind != SyntaxKind.Interpolation) {
                        replaceText = "  }";
                        if(pos > 0 && !char.IsWhiteSpace(code[pos - 1])) {
                            replaceText = " {  }";
                            tempRangeFrom++;
                            cd.position--; replaceLength = 1; //replace the '{' too
                        }
                        tempRangeTo = tempRangeFrom + 2;
                        newPos = tempRangeFrom + 1;
                    }
                    break;
                }
            }
        }

        c.doc.Z.ReplaceRange(true, cd.position, replaceLength, replaceText, true, ch == ';' ? SciFinalCurrentPos.AtEnd : default);
        if(newPos > 0) c.doc.Z.CurrentPos16 = newPos;

        if(tempRangeFrom > 0) c.doc.ZTempRanges_Add(this, tempRangeFrom, tempRangeTo);
        else c.ignoreChar = true;
    }

    bool _OnEnterOrSemicolon(bool anywhere, bool onSemicolon, out BeforeCharContext bcc)
    {
        bcc = null; //need to create it only if onSemicolon==true and anywhere==false and returns true

        if(!CodeInfo.GetContextWithoutDocument(out var cd)) return false;
        var code = cd.code;
        int pos = cd.position;
        if(pos < 1 || pos >= code.Length) return false; //TODO: if pos==code.Length, append newline and retry. Or always add }}.

        bool canCorrect = true, canAutoindent = !(onSemicolon | anywhere); //note: complResult is never Complex here
        if(!anywhere) {
            char ch = code[pos];
            canCorrect = (ch == ')' || ch == ']') && code[pos - 1] != ',';
            if(!(canCorrect | canAutoindent)) return false;
        }

        if(!cd.GetDocument()) return false;

        var root = cd.document.GetSyntaxRootAsync().Result;
        var tok1 = root.FindToken(pos);

        //CiUtil.PrintNode(tok1, printErrors: true);
        if(!anywhere && canCorrect) {
            var tokKind = tok1.Kind();
            canCorrect = (tokKind == SyntaxKind.CloseParenToken || tokKind == SyntaxKind.CloseBracketToken) && tok1.SpanStart == pos;
            if(!(canCorrect | canAutoindent)) return false;
        }

        if(!anywhere && _InNonblankTriviaOrStringOrChar(out bool suppress, cd, tok1, onSemicolon)) return suppress;

        SyntaxNode nodeFromPos = tok1.Parent;
        //CiUtil.PrintNode(nodeFromPos, printErrors: true);

        SyntaxNode node = null, indentNode = null;
        bool needSemicolon = false, needBlock = false, dontIndent = false;
        SyntaxToken token = default; C_Block block = null;
        foreach(var v in nodeFromPos.AncestorsAndSelf()) {
            if(v is StatementSyntax ss) {
                node = v;
                switch(ss) {
                case IfStatementSyntax k:
                    token = k.CloseParenToken;
                    block = k.Statement as BlockSyntax;
                    break;
                case ForStatementSyntax k:
                    if(onSemicolon && !anywhere) return false;
                    token = k.CloseParenToken;
                    block = k.Statement as BlockSyntax;
                    break;
                case CommonForEachStatementSyntax k:
                    token = k.CloseParenToken;
                    block = k.Statement as BlockSyntax;
                    break;
                case WhileStatementSyntax k:
                    token = k.CloseParenToken;
                    block = k.Statement as BlockSyntax;
                    break;
                case FixedStatementSyntax k:
                    token = k.CloseParenToken;
                    block = k.Statement as BlockSyntax;
                    break;
                case LockStatementSyntax k:
                    token = k.CloseParenToken;
                    block = k.Statement as BlockSyntax;
                    break;
                case UsingStatementSyntax k:
                    token = k.CloseParenToken;
                    block = k.Statement as BlockSyntax;
                    break;
                case SwitchStatementSyntax k:
                    token = k.CloseParenToken;
                    block = k;
                    dontIndent = true;
                    break;
                case LocalFunctionStatementSyntax k:
                    block = k.Body;
                    needSemicolon = block == null && k.ExpressionBody != null;
                    break;
                case DoStatementSyntax k:
                    needSemicolon = true;
                    break;
                case BlockSyntax k:
                    if(k.Parent is ExpressionSyntax && anywhere) continue; //eg lambda
                    canCorrect = false;
                    break;
                default: //method invocation, assignment expression, return, throw etc. Many cannot have parentheses or children, eg break, goto, empty (;).
                    needSemicolon = true;
                    break;
                }

                //if eg 'if(...) ThisNodeNotInBraces', next line must have indent of 'if'
                if(needSemicolon && node.Parent is StatementSyntax pa && !(pa is BlockSyntax)) indentNode = pa;

            } else if(v is MemberDeclarationSyntax mds) {
                node = v;
                switch(mds) {
                case NamespaceDeclarationSyntax k:
                    if(!k.OpenBraceToken.IsMissing) block = k;
                    else if(!(k.Name?.IsMissing ?? true)) token = k.Name.GetLastToken();
                    else return false;
                    dontIndent = true;
                    break;
                case BaseTypeDeclarationSyntax k: //class, struct, interface, enum
                    block = k;
                    break;
                case EnumMemberDeclarationSyntax _:
                    canCorrect = false;
                    break;
                case BaseMethodDeclarationSyntax k: //method, operator, constructor, destructor
                    block = k.Body;
                    if(block != null) break;
                    if(k.ExpressionBody != null || k.Parent is InterfaceDeclarationSyntax || k.ChildTokens().Any(o => _IsKind(o, SyntaxKind.ExternKeyword, SyntaxKind.AbstractKeyword))) {
                        needSemicolon = true;
                        //also may need semicolon if partial, but we don't know which part this is.
                    }
                    break;
                case PropertyDeclarationSyntax k:
                    needSemicolon = k.ExpressionBody != null || k.Initializer != null;
                    if(!needSemicolon) block = k.AccessorList;
                    break;
                case IndexerDeclarationSyntax k:
                    needSemicolon = k.ExpressionBody != null;
                    if(!needSemicolon) block = k.AccessorList;
                    break;
                case EventDeclarationSyntax k:
                    block = k.AccessorList;
                    break;
                default: //field, event field, delegate
                    needSemicolon = true;
                    break;
                }
            } else if(v is AttributeListSyntax als) {
                node = v;
                token = als.CloseBracketToken;
                if(token.IsMissing) canCorrect = false;
                break;
            } else {
                bool canCorrect2 = false;
                switch(v) {
                case ElseClauseSyntax k:
                    token = k.ElseKeyword;
                    block = k.Statement as BlockSyntax;
                    canCorrect2 = block == null && k.Parent is IfStatementSyntax pa && pa.Statement is BlockSyntax;
                    break;
                case FinallyClauseSyntax k:
                    block = k.Block;
                    token = k.FinallyKeyword;
                    canCorrect2 = true;
                    break;
                case CatchClauseSyntax k:
                    block = k.Block;
                    if(block == null) {
                        token = k.Filter?.CloseParenToken ?? k.Declaration?.CloseParenToken ?? k.CatchKeyword;
                    } else {
                        //workaround for: if '{ }' is missing but in that place is eg 'if(...) { }', says that the 'if()' is filter and the '{ }' is block of this 'catch'
                        var cfilter = k.Filter;
                        if(cfilter != null && cfilter.WhenKeyword.IsMissing) {
                            block = null;
                            token = k.Declaration?.CloseParenToken ?? k.CatchKeyword;
                        }
                    }
                    canCorrect2 = true;
                    break;
                case SwitchSectionSyntax _:
                    canCorrect = false;
                    break;
                case UsingDirectiveSyntax _:
                case ExternAliasDirectiveSyntax _:
                    needSemicolon = true;
                    break;
                default: continue;
                }
                node = v;
                if(canCorrect2 && !(canCorrect | onSemicolon) && block == null && pos > v.SpanStart) canCorrect = true;
            }

            if(canCorrect) {
                if(needSemicolon) {
                    token = node.GetLastToken();
                    needSemicolon = !token.IsKind(SyntaxKind.SemicolonToken);
                } else if(block != null) {
                    token = block.OpenBraceToken;
                } else {
                    needBlock = true;
                    if(token == default || token.IsMissing) token = node.GetLastToken();
                }
            }
            break;
        }

        //Print("----");
        //CiUtil.PrintNode(node);

        if(node == null) {
#if DEBUG
            switch(nodeFromPos) {
            case CompilationUnitSyntax _:
            case UsingDirectiveSyntax _:
            case ExternAliasDirectiveSyntax _:
                break;
            default:
                ADebug.Print($"{nodeFromPos.Kind()}, '{nodeFromPos}'");
                break;
            }
#endif
            return false;
        }
        if(!(canCorrect | canAutoindent)) return false;

        var doc = cd.sciDoc;
        if(canCorrect) {
            //Print($"Span={ node.Span}, Span.End={node.Span.End}, FullSpan={ node.FullSpan}, SpanStart={ node.SpanStart}, EndPosition={ node.EndPosition}, FullWidth={ node.FullWidth}, HasTrailingTrivia={ node.HasTrailingTrivia}, Position={ node.Position}, Width={ node.Width}");
            //return true;

            int endOfSpan = token.Span.End, endOfFullSpan = token.FullSpan.End;
            //Print(endOfSpan, endOfFullSpan);

            if(onSemicolon) {
                if(anywhere) {
                    doc.Z.GoToPos(true, (needSemicolon || endOfSpan > pos) ? endOfSpan : endOfFullSpan);
                    if(needSemicolon) doc.Z.ReplaceSel(";");
                } else {
                    bcc = new BeforeCharContext { oldPosUtf8 = pos, newPosUtf8 = endOfSpan, dontSuppress = needSemicolon };
                }
            } else {
                int indent = doc.Z.LineIndentationFromPos(true, (indentNode ?? node).SpanStart);
                if(needBlock || block != null) indent++;
                bool indentNext = indent > 0 && code[endOfFullSpan - 1] != '\n' && endOfFullSpan < code.Length; //indent next statement (or whatever) that was in the same line

                var b = new StringBuilder();
                if(needBlock) b.Append(" {"); else if(needSemicolon) b.Append(';');

                int replaceLen = endOfFullSpan - endOfSpan;
                int endOfFullTrimmed = endOfFullSpan; while(code[endOfFullTrimmed - 1] <= ' ') endOfFullTrimmed--; //remove newline and spaces
                if(endOfFullTrimmed > endOfSpan) b.Append(code, endOfSpan, endOfFullTrimmed - endOfSpan);
                b.AppendLine();

                if(indent > 0) b.Append('\t', dontIndent ? indent - 1 : indent);
                b.AppendLine();

                int finalPos = endOfSpan + b.Length - 2;
                if(needBlock) {
                    if(--indent > 0) b.Append('\t', indent);
                    b.AppendLine("}");
                }

                int endOfBlock = block?.CloseBraceToken.SpanStart ?? 0;
                if(indentNext) {
                    if(endOfFullSpan == endOfBlock) indent--;
                    if(indent > 0) b.Append('\t', indent);
                }

                if(endOfBlock > endOfFullSpan) { //if block contains statements, move the closing '}' down
                    bool hasNewline = false;
                    while(code[endOfBlock - 1] <= ' ') if(code[--endOfBlock] == '\n') hasNewline = true;
                    if(endOfBlock > endOfFullSpan) {
                        replaceLen += endOfBlock - endOfFullSpan;
                        b.Append(code, endOfFullSpan, endOfBlock - endOfFullSpan);
                        if(!hasNewline) b.AppendLine();
                        if(--indent > 0) b.Append('\t', indent);
                    }
                }

                var s = b.ToString();
                //Print($"'{s}'");
                doc.Z.ReplaceRange(true, endOfSpan, replaceLen, s, true);
                doc.Z.GoToPos(true, finalPos);
            }
        } else { //autoindent

            //Print(pos);

            //remove spaces and tabs around the line break
            static bool _IsSpace(char c) => c == ' ' || c == '\t';
            int from = pos, to = pos;
            while(from > 0 && _IsSpace(code[from - 1])) from--;
            while(to < code.Length && _IsSpace(code[to])) to++;
            int replaceFrom = from;

            //if we are not inside node span, find the first ancestor node where we are inside
            TextSpan spanN = node.Span;
            if(!(from >= spanN.End && tok1.IsKind(SyntaxKind.CloseParenToken))) { //if after ')', we are after eg if(...) or inside an expression
                for(; !(from > spanN.Start && from < spanN.End); spanN = node.Span) {
                    if(node is SwitchSectionSyntax && from >= spanN.End && !(nodeFromPos is BreakStatementSyntax)) break; //indent switch section statements and 'break'
                    node = node.Parent;
                    if(node == null) return false;
                }
            }

            //don't indent if we are after 'do ...' or 'try ...' or 'try ... catch ...'
            SyntaxNode doTryChild = null;
            switch(node) {
            case DoStatementSyntax k1: doTryChild = k1.Statement; break;
            case TryStatementSyntax k2: doTryChild = k2.Block; break;
            }
            if(doTryChild != null && spanN.End >= doTryChild.Span.End) node = node.Parent;

            //get indentation
            int indent = 0;
            bool prevBlock = false;
            foreach(var v in node.AncestorsAndSelf()) {
                if(v is BlockSyntax) {
                    prevBlock = true;
                } else {
                    if(prevBlock) { //don't indent block that is child of eg 'if' which adds indentation.
                        //Print("-");
                        prevBlock = false;
                        indent--;
                    }
                    switch(v) {
                    case SwitchStatementSyntax _: //don't indent 'case' in 'switch'. If node is a switch section, it will indent its child statements and 'break.
                    case AccessorListSyntax _:
                    case ElseClauseSyntax _:
                    case CatchClauseSyntax _:
                    case FinallyClauseSyntax _:
                    case NamespaceDeclarationSyntax _: //don't indent namespaces
                    case IfStatementSyntax k3 when k3.Parent is ElseClauseSyntax:
                        //Print("-" + v.GetType().Name, v.Span, pos);
                        continue;
                    case ExpressionSyntax _:
                    case BaseArgumentListSyntax _:
                    case ArgumentSyntax _:
                    case EqualsValueClauseSyntax _:
                    case VariableDeclaratorSyntax _:
                    case VariableDeclarationSyntax _:
                        //Print("--" + v.GetType().Name, v.Span, pos);
                        continue; //these can be if we are in a lambda block. And probably more, nevermind.
                    case CompilationUnitSyntax _:
                    case ClassDeclarationSyntax k1 when k1.Identifier.Text == "Script": //don't indent script class content
                    case ConstructorDeclarationSyntax k2 when k2.Identifier.Text == "Script": //don't indent script constructor content
                        goto gAfterLoop;
                    }
                }
                //Print(v.GetType().Name, v.Span, pos);
                indent++;
            }
            gAfterLoop:

            //indent if we are directly in switch statement below breakless section
            if(node == nodeFromPos && node is SwitchStatementSyntax ss) {
                var sectionAbove = ss.Sections.LastOrDefault(o => o.FullSpan.End <= pos);
                if(sectionAbove != null && !sectionAbove.Statements.Any(o => o is BreakStatementSyntax)) indent++;
            }

            //maybe need to add 1 line when breaking line inside '{  }', add tabs in current line, decrement indent in '}' line, etc
            int iOB = 0, iCB = 0;
            switch(node) {
            case BlockSyntax k: iOB = k.OpenBraceToken.Span.End; iCB = k.CloseBraceToken.SpanStart; break;
            case SwitchStatementSyntax k: iOB = k.OpenBraceToken.Span.End; iCB = k.CloseBraceToken.SpanStart; break;
            case BaseTypeDeclarationSyntax k: iOB = k.OpenBraceToken.Span.End; iCB = k.CloseBraceToken.SpanStart; break;
            case NamespaceDeclarationSyntax k: iOB = k.OpenBraceToken.Span.End; iCB = k.CloseBraceToken.SpanStart; break;
            case BasePropertyDeclarationSyntax k: //property, event, indexer
                var al = k.AccessorList;
                if(al != null) { iOB = al.OpenBraceToken.Span.End; iCB = al.CloseBraceToken.SpanStart; }
                break;
            }
            bool isBraceLine = to == iCB, expandBraces = isBraceLine && from == iOB;

            //Print($"from={from}, to={to}, nodeFromPos={nodeFromPos.GetType().Name}, node={node.GetType().Name}");
            //Print($"indent={indent}, isBraceLine={isBraceLine}, expandBraces={expandBraces}");

            if(indent < 1 && !expandBraces) return false;

            var b = new StringBuilder();

            //correct 'case' if indented too much. It happens when there are multiple 'case'.
            if(indent > 0 && node is SwitchSectionSyntax && nodeFromPos is SwitchLabelSyntax sl && from >= sl.Span.End) {
                int i = sl.SpanStart, j = i;
                if(cd.sciDoc.Z.LineIndentationFromPos(true, i) != indent - 1) {
                    while(_IsSpace(code[i - 1])) i--;
                    if(code[i - 1] == '\n') {
                        replaceFrom = i;
                        b.Append('\t', indent - 1);
                        b.Append(code, j, from - j);
                    }
                }
            }

            //append newlines and tabs
            if(expandBraces || from == 0 || code[from - 1] == '\n') {
                if(expandBraces) b.AppendLine();
                b.Append('\t', indent);
            }
            b.AppendLine();
            if(indent > 0 && isBraceLine && !(node is SwitchStatementSyntax)) indent--;
            if(indent > 0) b.Append('\t', indent);

            //replace text and set caret position
            var s = b.ToString();
            //Print($"'{s}'");
            doc.Z.ReplaceRange(true, replaceFrom, to - replaceFrom, s, true);
            pos = replaceFrom + s.Length;
            if(expandBraces) pos -= indent + 2; else if(isBraceLine && code.Eq(replaceFrom - 1, "\n")) pos -= indent;
            doc.Z.GoToPos(true, pos);
        }
        return true;
    }

    bool _InNonblankTriviaOrStringOrChar(out bool suppress, in CodeInfo.Context cd, in SyntaxToken token, bool onSemicolon)
    {
        suppress = false;

        string prefix = null, suffix = null; bool newlineLast = false;
        int indent = 0;
        int pos = cd.position;
        var span = token.Span;
        if(pos < span.Start || pos > span.End) {
            var trivia = token.Parent.FindTrivia(pos);
            switch(trivia.Kind()) {
            case SyntaxKind.MultiLineCommentTrivia:
            case SyntaxKind.MultiLineDocumentationCommentTrivia:
                break;
            case SyntaxKind.SingleLineCommentTrivia:
                suffix = "//";
                break;
            case SyntaxKind.SingleLineDocumentationCommentTrivia:
                suffix = "/// ";
                newlineLast = cd.code.Regex(@"[ \t]*///", RXFlags.ANCHORED, new RXMore(pos));
                break;
            case SyntaxKind.None:
            case SyntaxKind.WhitespaceTrivia:
            case SyntaxKind.EndOfLineTrivia:
                return false;
            default: //eg directive or disabled code
                return true;
            }
            if(onSemicolon) return true;
        } else {
            if(pos == span.End) return false;
            bool atStart = pos == span.Start;

            var node = token.Parent;
            if(node.Parent is InterpolatedStringExpressionSyntax ise) {
                switch(token.Kind()) {
                case SyntaxKind.InterpolatedStringTextToken:
                case SyntaxKind.OpenBraceToken when atStart:
                    break;
                default:
                    return false;
                }
                node = ise;
            } else {
                switch(token.Kind()) {
                case SyntaxKind.StringLiteralToken when !atStart:
                case SyntaxKind.InterpolatedStringEndToken when atStart:
                    break;
                case SyntaxKind.CharacterLiteralToken when !atStart:
                    suppress = !onSemicolon;
                    return true;
                default:
                    return false;
                }
            }

            if(onSemicolon) return true;
            span = node.Span;
            if(0 != cd.code.Eq(span.Start, false, "@", "$@", "@$")) return true;
            prefix = "\\r\\n\" +";
            suffix = "\"";
            //indent more, unless line starts with "
            int i = cd.sciDoc.Z.LineStartFromPos(true, pos);
            if(!cd.code.Regex(@"[ \t]+\$?""", RXFlags.ANCHORED, new RXMore(i))) indent++;
        }

        var doc = cd.sciDoc;
        indent += doc.Z.LineIndentationFromPos(true, pos);
        if(indent < 1 && prefix == null && suffix == null) return true;

        var b = new StringBuilder();
        b.Append(prefix);
        if(!newlineLast) b.AppendLine();
        b.Append('\t', indent).Append(suffix);
        if(newlineLast) b.AppendLine();

        var s = b.ToString();
        doc.Z.ReplaceRange(true, pos, pos, s, finalCurrentPos: SciFinalCurrentPos.AtEnd);

        return suppress = true;
    }
    //TODO: when deleting or back-deleting line containg only tabs, delete tabs

    #region util

    class C_Block
    {
        SyntaxNode _b;

        public static implicit operator C_Block(BlockSyntax n) => _New(n);
        public static implicit operator C_Block(SwitchStatementSyntax n) => _New(n);
        public static implicit operator C_Block(AccessorListSyntax n) => _New(n);
        public static implicit operator C_Block(BaseTypeDeclarationSyntax n) => _New(n);
        public static implicit operator C_Block(NamespaceDeclarationSyntax n) => _New(n);

        static C_Block _New(SyntaxNode n)
        {
            if(n == null || _BraceToken(n, false).IsMissing) return null;
            return new C_Block { _b = n };
        }

        static SyntaxToken _BraceToken(SyntaxNode n, bool right)
        {
            switch(n) {
            case BlockSyntax k: return right ? k.CloseBraceToken : k.OpenBraceToken;
            case SwitchStatementSyntax k: return right ? k.CloseBraceToken : k.OpenBraceToken;
            case AccessorListSyntax k: return right ? k.CloseBraceToken : k.OpenBraceToken;
            case BaseTypeDeclarationSyntax k: return right ? k.CloseBraceToken : k.OpenBraceToken;
            case NamespaceDeclarationSyntax k: return right ? k.CloseBraceToken : k.OpenBraceToken;
            }
            return default;
        }

        public SyntaxToken OpenBraceToken => _BraceToken(_b, false);

        public SyntaxToken CloseBraceToken => _BraceToken(_b, true);
    }

    //currently unused
    //static bool _GetNodeIfNotInNonblankTriviaOrStringOrChar(out SyntaxNode node, CodeInfo.Context cd)
    //{
    //	int pos = cd.position;

    //	var root = cd.document.GetSyntaxRootAsync().Result;
    //	node = root.FindToken(pos).Parent;
    //	if(node.Parent is InterpolatedStringExpressionSyntax iss) node = iss;
    //	//CiUtil.PrintNode(node, true, true);

    //	var span = node.Span;
    //	if(pos > span.Start && pos < span.End) {
    //		switch(node.Kind()) {
    //		case SyntaxKind.CharacterLiteralExpression:
    //		case SyntaxKind.StringLiteralExpression:
    //		case SyntaxKind.InterpolatedStringExpression:
    //		case SyntaxKind.CompilationUnit:
    //		case SyntaxKind.Block:
    //		case SyntaxKind.ClassDeclaration:
    //		case SyntaxKind.StructDeclaration:
    //		case SyntaxKind.InterfaceDeclaration:
    //			return false;
    //		}
    //	} else {
    //		if(pos == span.End) {
    //			switch(node.Kind()) {
    //			case SyntaxKind.CharacterLiteralExpression:
    //			case SyntaxKind.StringLiteralExpression:
    //			case SyntaxKind.InterpolatedStringExpression:
    //				if(node.ContainsDiagnostics && node.GetDiagnostics().Any(o => o.Id == "CS1010")) return false; //newline in constant
    //				break;
    //			}
    //		}
    //		if(_IsInNonblankTrivia(node, pos)) return false;
    //	}

    //	return true;
    //}

    static bool _IsInNonblankTrivia(SyntaxNode node, int pos)
    {
        var trivia = node.FindTrivia(pos);
        if(trivia.RawKind != 0) {
            //Print($"{trivia.Kind()}, {pos}, {trivia.FullSpan}, '{trivia}'");
            var ts = trivia.Span;
            if(!(pos > ts.Start && pos < ts.End)) { //pos is not inside trivia; possibly at start or end.
                bool lookBefore = pos == ts.Start && trivia.IsKind(SyntaxKind.EndOfLineTrivia) && node.FullSpan.Start < pos;
                if(!lookBefore) return false;
                trivia = node.FindTrivia(pos - 1); //can be eg single-line comment
                switch(trivia.Kind()) {
                case SyntaxKind.MultiLineCommentTrivia:
                case SyntaxKind.MultiLineDocumentationCommentTrivia:
                    return false;
                }
                //CiUtil.PrintNode(trivia);
            }
            switch(trivia.Kind()) {
            case SyntaxKind.None:
            case SyntaxKind.WhitespaceTrivia:
            case SyntaxKind.EndOfLineTrivia:
                break;
            default:
                return true; //mostly comments, directives and #if-disabled text
            }
        }
        return false;
    }

    static bool _IsKind(in SyntaxToken t, SyntaxKind k1, SyntaxKind k2) { var k = t.RawKind; return k == (int)k1 || k == (int)k2; }

    #endregion
}
