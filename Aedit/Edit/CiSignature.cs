using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Reflection;
using System.Linq;

using Au;
using Au.Types;
using Au.More;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.SignatureHelp;
using Microsoft.CodeAnalysis.CSharp.SignatureHelp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

//FUTURE: show for lambda parameters. Currently VS does not show too.

class CiSignature
{
	CiPopupText _textPopup;
	_Data _data; //not null while the popup window is visible
	CancellationTokenSource _cancelTS;

	class _Data
	{
		public SignatureHelpItems r;
		public _Span span;
		public int iSelected, iUserSelected;
		public SciCode sciDoc;

		public bool IsSameSpan(_Span span2) {
			return span2.start == span.start && span2.fromEnd == span.fromEnd;
			//never mind: we don't check whether text before and after is still the same. Not that important.
		}

		public int GetUserSelectedItemIfSameSpan(_Span span2, SignatureHelpItems r2) {
			if (iUserSelected < 0 || !IsSameSpan(span2) || r2.Items.Count != r.Items.Count) return -1;
			for (int i = 0; i < r.Items.Count; i++) {
				var hi1 = r.Items[i] as AbstractSignatureHelpProvider.SymbolKeySignatureHelpItem;
				var hi2 = r2.Items[i] as AbstractSignatureHelpProvider.SymbolKeySignatureHelpItem;
				Debug.Assert(!(hi1 == null || hi2 == null));
				if (hi1 == null || hi2 == null || hi2.Symbol != hi1.Symbol) return -1;
			}
			return iUserSelected;
		}
	}

	struct _Span
	{
		public int start, fromEnd;
		public _Span(int start, int fromEnd) { this.start = start; this.fromEnd = fromEnd; }
		public _Span(TextSpan span, string code) { this.start = span.Start; this.fromEnd = code.Length - span.End; }
	}

	public bool IsVisibleUI => _data != null;

	public void Cancel() {
		_cancelTS?.Cancel(); _cancelTS = null;
		_CancelUI();
	}

	void _CancelUI() {
		if (_data == null) return;
		foreach (var r in _data.sciDoc.ZTempRanges_Enum(this)) r.Remove();
		_data = null;
		_textPopup?.Hide();
	}

	public void SciPositionChanged(SciCode doc) {
		if (_afterCharAdded) { _afterCharAdded = false; return; }
		if (_data == null) return;
		_ShowSignature(doc, default);
	}
	bool _afterCharAdded;

	public void SciCharAdded(SciCode doc, char ch) {
		switch (ch) { case '(' or '[' or '<' or ')' or ']' or '>' or ',': break; default: return; }
		_ShowSignature(doc, ch);
		_afterCharAdded = true;
	}

	public void ShowSignature(SciCode doc) {
		_ShowSignature(doc, default);
	}

#if true
	async void _ShowSignature(SciCode doc, char ch) {
		//perf.first();
		if (!CodeInfo.GetContextAndDocument(out var cd, -2) || cd.pos16 < 2) return; //returns false if position is in meta comments

		_cancelTS?.Cancel();
		_cancelTS = new CancellationTokenSource();
		var cancelTS = _cancelTS;
		var cancelToken = cancelTS.Token;
#if DEBUG
		if (Debugger.IsAttached) { cancelToken = default; _cancelTS = null; }
#endif

		//ISignatureHelpProvider provider = null;
		SignatureHelpItems r = null;
		try {
			//could be sync, quite fast, but then sometimes reenters (GetItemsAsync waits/dispatches) and sometimes hangs
			r = await Task.Run(async () => {
				//perf.next();
				var providers = _SignatureHelpProviders;
				//print.it(providers);
				SignatureHelpItems r = null;
				var trigger = new SignatureHelpTriggerInfo(ch == default ? SignatureHelpTriggerReason.InvokeSignatureHelpCommand : SignatureHelpTriggerReason.TypeCharCommand, ch);
				foreach (var p in providers) {
					var r2 = await p.GetItemsAsync(cd.document, cd.pos16, trigger, cancelToken).ConfigureAwait(false);
					if (cancelToken.IsCancellationRequested) { /*print.it("IsCancellationRequested");*/ return null; } //often
					if (r2 == null) continue;
					if (r == null || r2.ApplicableSpan.Start > r.ApplicableSpan.Start) {
						r = r2;
						//provider = p;
					}
					//Example: 'print.it(new Something())'.
					//	The first provider probably is for Write (invocation).
					//	Then the second is for Something (object creation).
					//	We need the innermost, in this case Something.
				}
				return r;
			});
		}
		catch (OperationCanceledException) { /*Debug_.Print("canceled");*/ return; } //never noticed
		finally {
			cancelTS.Dispose();
			if (cancelTS == _cancelTS) _cancelTS = null;
		}
		//print.it(r, cancelToken.IsCancellationRequested);

		if (cancelToken.IsCancellationRequested) return;
		if (r == null) {
			_CancelUI();
			return;
		}
		Debug.Assert(doc == Panels.Editor.ZActiveDoc); //when active doc changed, cancellation must be requested
		if (cd.pos16 != doc.zCurrentPos16 || (object)cd.code != doc.zText) return; //changed while awaiting

		//perf.nw('s');

		//print.it($"<><c orange>pos={cd.pos16}, span={r.ApplicableSpan},    nItems={r.Items.Count},  argCount={r.ArgumentCount}, argIndex={r.ArgumentIndex}, argName={r.ArgumentName}, sel={r.SelectedItemIndex},    provider={provider}<>");

		//get span of the arglist. r.ApplicableSpan.Start is of the statement, not of the arglist. In chained methods it is the chain start.
		var root = cd.document.GetSyntaxRootAsync().Result;
		var fullSpan = r.ApplicableSpan;
		//CiUtil.HiliteRange(fullSpan); wait.doEvents(500);
		var start = fullSpan.Start;
		var tok = root.FindToken(cd.pos16);
		if (tok.Kind() is SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.LessThanToken) tok = tok.GetPreviousToken();
		var argNode = tok.Parent;
		while (argNode != null) {
			int i = argNode.SpanStart; if (i <= start) break;
			if (argNode is BaseArgumentListSyntax or AttributeArgumentListSyntax or TypeArgumentListSyntax) {
				start = i + 1;
				break;
			}
			//CiUtil.PrintNode(argNode);
			argNode = argNode.Parent;
		}
		var argSpan = new TextSpan(start, fullSpan.End - start);
		//CiUtil.PrintNode(argNode); CiUtil.HiliteRange(argSpan); //print.it(argSpan);

		var span = new _Span(argSpan, cd.code);
		int iSel = _data?.GetUserSelectedItemIfSameSpan(span, r) ?? -1; //preserve user selection in same session

		_data = new _Data {
			r = r,
			span = span,
			iUserSelected = iSel,
			sciDoc = doc,
		};

		if (iSel < 0) {
			iSel = r.SelectedItemIndex ?? (r.ArgumentCount == 0 ? 0 : -1);
			if (iSel < 0) {
				for (int i = 0; i < r.Items.Count; i++) if (r.Items[i].Parameters.Length >= r.ArgumentCount) { iSel = i; break; }
				if (iSel < 0) {
					for (int i = 0; i < r.Items.Count; i++) if (r.Items[i].IsVariadic) { iSel = i; break; }
					if (iSel < 0) iSel = 0;
				}
			}
		}

		doc.ZTempRanges_Add(this, argSpan.Start, argSpan.End, onLeave: () => {
			if (doc.ZTempRanges_Enum(doc.zCurrentPos8, this, utf8: true).Any()) return;
			_CancelUI();
		}, SciCode.ZTempRangeFlags.NoDuplicate);

		var rect = RECT.Union(CiUtil.GetCaretRectFromPos(doc, fullSpan.Start), CiUtil.GetCaretRectFromPos(doc, cd.pos16));
		doc.Hwnd.MapClientToScreen(ref rect);
		rect.Width += Dpi.Scale(200, doc.Hwnd);
		rect.left -= 6;

		_textPopup ??= new CiPopupText(CiPopupText.UsedBy.Signature, onHiddenOrDestroyed: (_, _) => _data = null) {
			OnLinkClick = (ph, e) => ph.Text = _FormatText(e.ToInt(1), userSelected: true)
		};
		_textPopup.Text = _FormatText(iSel, userSelected: false);

		if (!_textPopup.IsVisible) {
			CodeInfo.HideTextPopupAndTempWindows();
			if (CodeInfo._compl.IsVisibleUI) //without this does not show completions with selected enum when typed Function( when first parameter is enum
				CodeInfo._compl.Cancel();
		}

		_textPopup.Show(Panels.Editor.ZActiveDoc, rect, System.Windows.Controls.Dock.Bottom);
		//perf.nw();

		//also show Keys/Regex tool?
		//CiUtil.PrintNode(node);
		if (argNode is ArgumentListSyntax && cd.code.Eq(cd.pos16 - 1, "\"\"")) {
			//print.it("string");
			var semo = cd.document.GetSemanticModelAsync().Result;
			argNode = root.FindToken(cd.pos16).Parent;
			var stringFormat = CiUtil.GetParameterStringFormat(argNode, semo, false);
			//print.it(stringFormat);
			if (stringFormat != default) CodeInfo._tools.ShowForStringParameter(stringFormat, cd, argNode.Span, _textPopup.PopupWindow.Hwnd);
		}
	}
#else //old
	async void _ShowSignature(SciCode doc, char ch) {
		//perf.first();
		if (!CodeInfo.GetContextAndDocument(out var cd, -2)) return; //returns false if position is in meta comments

		_cancelTS?.Cancel();
		_cancelTS = new CancellationTokenSource();
		var cancelTS = _cancelTS;
		var cancelToken = cancelTS.Token;
#if DEBUG
		if (Debugger.IsAttached) { cancelToken = default; _cancelTS = null; }
#endif

		//ISignatureHelpProvider provider = null;
		SignatureHelpItems r = null;
		try {
			//could be sync, quite fast, but then sometimes reenters (GetItemsAsync waits/dispatches) and sometimes hangs
			r = await Task.Run(async () => {
				//perf.next();
				var providers = _SignatureHelpProviders;
				//print.it(providers);
				SignatureHelpItems r = null;
				var trigger = new SignatureHelpTriggerInfo(ch == default ? SignatureHelpTriggerReason.InvokeSignatureHelpCommand : SignatureHelpTriggerReason.TypeCharCommand, ch);
				foreach (var p in providers) {
					var r2 = await p.GetItemsAsync(cd.document, cd.pos16, trigger, cancelToken).ConfigureAwait(false);
					if (cancelToken.IsCancellationRequested) { /*print.it("IsCancellationRequested");*/ return null; } //often
					if (r2 == null) continue;
					if (r == null || r2.ApplicableSpan.Start > r.ApplicableSpan.Start) {
						r = r2;
						//provider = p;
					}
					//Example: 'print.it(new Something())'.
					//	The first provider probably is for Write (invocation).
					//	Then the second is for Something (object creation).
					//	We need the innermost, in this case Something.
				}
				return r;
			});
		}
		catch (OperationCanceledException) { /*Debug_.Print("canceled");*/ return; } //never noticed
		finally {
			cancelTS.Dispose();
			if (cancelTS == _cancelTS) _cancelTS = null;
		}
		//print.it(r, cancelToken.IsCancellationRequested);

		if (cancelToken.IsCancellationRequested) return;
		if (r == null) {
			_CancelUI();
			return;
		}
		Debug.Assert(doc == Panels.Editor.ZActiveDoc); //when active doc changed, cancellation must be requested
		if (cd.pos16 != doc.zCurrentPos16 || (object)cd.code != doc.zText) return; //changed while awaiting

		//perf.nw('s');

		//print.it($"<><c orange>pos={cd.pos16}, span={r.ApplicableSpan},    nItems={r.Items.Count},  argCount={r.ArgumentCount}, argIndex={r.ArgumentIndex}, argName={r.ArgumentName}, sel={r.SelectedItemIndex},    provider={provider}<>");

		//get span of the arglist. r.ApplicableSpan.Start is of the statement, not of the arglist. In chained methods it is the chain start.
		var root = cd.document.GetSyntaxRootAsync().Result;
		var fullSpan = r.ApplicableSpan;
		var start = fullSpan.Start;
		bool aspanStart = cd.code[fullSpan.Start] == '('; //normally End is at ')' and Start is < '(', but for tuple End is after ')' and Start is at '('
		var toke = root.FindToken(aspanStart ? fullSpan.Start : fullSpan.End);
		//CiUtil.HiliteRange(fullSpan);
		//return;

		SyntaxNode node;
		if (aspanStart) {
			node = toke.Parent;
		} else {
			switch (toke.Kind()) {
			case SyntaxKind.CloseParenToken:
			case SyntaxKind.CloseBracketToken:
				node = toke.Parent;
				break;
			default: //no closing )]>
				toke = toke.GetPreviousToken(); //toke = root.FindToken(cd.pos16); //both don't work for eg List< (no closing >), because there is BinaryExpressionSyntax instead of TypeArgumentListSyntax
				node = toke.Parent.FirstAncestorOrSelf<SyntaxNode>(o => _IsArglistNode(o), false);
				if (node == null || node.SpanStart < start) { /*Debug_.Print("todo");*/ return; } //eg List< (no closing >), difficult to detect. Never mind.
				break;
			}
		}
		bool _IsArglistNode(SyntaxNode sn) {
			//print.it(sn.GetType());
			return sn is BaseArgumentListSyntax or AttributeArgumentListSyntax or TypeArgumentListSyntax; //includes ArgumentListSyntax and BracketedArgumentListSyntax
		}

		start = Math.Max(fullSpan.Start, node.SpanStart);
		var argSpan = new TextSpan(start, fullSpan.End - start);
		//CiUtil.PrintNode(node); CiUtil.HiliteRange(argSpan); print.it(argSpan);

		var span = new _Span(argSpan, cd.code);
		int iSel = _data?.GetUserSelectedItemIfSameSpan(span, r) ?? -1; //preserve user selection in same session

		_data = new _Data {
			r = r,
			span = span,
			iUserSelected = iSel,
			sciDoc = doc,
		};

		if (iSel < 0) {
			iSel = r.SelectedItemIndex ?? (r.ArgumentCount == 0 ? 0 : -1);
			if (iSel < 0) {
				for (int i = 0; i < r.Items.Count; i++) if (r.Items[i].Parameters.Length >= r.ArgumentCount) { iSel = i; break; }
				if (iSel < 0) {
					for (int i = 0; i < r.Items.Count; i++) if (r.Items[i].IsVariadic) { iSel = i; break; }
					if (iSel < 0) iSel = 0;
				}
			}
		}

		doc.ZTempRanges_Add(this, argSpan.Start, argSpan.End, onLeave: () => {
			if (doc.ZTempRanges_Enum(doc.zCurrentPos8, this, utf8: true).Any()) return;
			_CancelUI();
		}, SciCode.ZTempRangeFlags.NoDuplicate);

		var rect = RECT.Union(CiUtil.GetCaretRectFromPos(doc, fullSpan.Start), CiUtil.GetCaretRectFromPos(doc, cd.pos16));
		doc.Hwnd.MapClientToScreen(ref rect);
		rect.Width += Dpi.Scale(200, doc.Hwnd);
		rect.left -= 6;

		_textPopup ??= new CiPopupText(CiPopupText.UsedBy.Signature, onHiddenOrDestroyed: (_, _) => _data = null) {
			OnLinkClick = (ph, e) => ph.Text = _FormatText(e.ToInt(1), userSelected: true)
		};
		_textPopup.Text = _FormatText(iSel, userSelected: false);

		if (!_textPopup.IsVisible) {
			CodeInfo.HideTextPopupAndTempWindows();
			CodeInfo._compl.Cancel();
		}

		_textPopup.Show(Panels.Editor.ZActiveDoc, rect, System.Windows.Controls.Dock.Bottom);
		//perf.nw();

		//also show Keys/Regex tool?
		//CiUtil.PrintNode(node);
		if (node is ArgumentListSyntax && cd.code.Eq(cd.pos16 - 1, "\"\"")) {
			//print.it("string");
			var semo = cd.document.GetSemanticModelAsync().Result;
			node = root.FindToken(cd.pos16).Parent;
			var stringFormat = CiUtil.GetParameterStringFormat(node, semo, false);
			//print.it(stringFormat);
			if (stringFormat != default) CodeInfo._tools.ShowForStringParameter(stringFormat, cd, node.Span, _textPopup.PopupWindow.Hwnd);
		}
	}
#endif

	System.Windows.Documents.Section _FormatText(int iSel, bool userSelected) {
		_data.iSelected = iSel;
		if (userSelected) _data.iUserSelected = iSel;

		var r = _data.r;
		ISymbol currentItem = null;
		SignatureHelpParameter currentParameter = null;
		var x = new CiText();

		//print.clear();
		for (int i = 0; i < r.Items.Count; i++) {
			var sh = r.Items[i];
			if (sh is AbstractSignatureHelpProvider.SymbolKeySignatureHelpItem kk) {
				var sym = kk.Symbol;
				if (i == iSel) currentItem = sym;
				x.StartOverload(i == iSel, i);
#if false
				x.AppendTaggedParts(sh.PrefixDisplayParts); //works, but formats not as I like (too much garbage). Has bugs with tuples.
#else
				//if(nt != null) {
				//	print.it(1, nt.IsGenericType, nt.IsTupleType, nt.IsUnboundGenericType, nt.Arity, nt.CanBeReferencedByName);
				//	print.it(2, nt.IsAnonymousType, nt.IsDefinition, nt.IsImplicitlyDeclared, nt.Kind, nt.TypeKind);
				//	print.it(3, nt.MemberNames);
				//	print.it(4, nt.Name, nt.MetadataName, nt.OriginalDefinition, nt.TupleUnderlyingType);
				//	print.it("TypeParameters:");
				//	print.it(nt.TypeParameters);
				//	print.it("TypeArguments:");
				//	print.it(nt.TypeArguments);
				//	print.it("TupleElements:");
				//	try { var te = nt.TupleElements; if(!te.IsDefault) print.it(te); } catch(Exception e1) { print.it(e1.ToStringWithoutStack()); }
				//	print.it("---");
				//}

				int isTuple = 0; //1 ValueTuple<...>, 2 (...)
				var nt = sym as INamedTypeSymbol;
				if (nt != null && nt.IsTupleType) isTuple = nt.IsDefinition ? 1 : 2;

				if (isTuple == 1) x.Append("ValueTuple"); //AppendSymbolWithoutParameters formats incorrectly
				else if (isTuple == 0) x.AppendSymbolWithoutParameters(sym);
				string b1 = "(", b2 = ")";
				if (nt != null) {
					if (nt.IsGenericType && isTuple != 2) { b1 = "<"; b2 = ">"; }
				} else if (sym is IPropertySymbol) {
					b1 = "["; b2 = "]";
				}
				x.Append(b1);
#endif
				int iArg = r.ArgumentIndex, lastParam = sh.Parameters.Length - 1;
				int selParam = iArg <= lastParam ? iArg : (sh.IsVariadic ? lastParam : -1);
				if (!r.ArgumentName.NE()) {
					var pa = sh.Parameters;
					for (int pi = 0; pi < pa.Length; pi++) if (pa[pi].Name == r.ArgumentName) { selParam = pi; break; }
				}
				x.AppendParameters(sym, selParam, sh);
				//x.AppendParameters(sh, selParam); //works, but formats not as I like (too much garbage)
#if false
				x.AppendTaggedParts(sh.SuffixDisplayParts);
#else
				x.Append(b2);
#endif
				if (i == iSel && selParam >= 0) currentParameter = sh.Parameters[selParam];
				x.EndOverload(i == iSel);
			} else {
				Debug_.Print(sh);
			}
		}

		if (currentItem != null) {
			var tt = r.Items[iSel].DocumentationFactory?.Invoke(default);
			bool haveDoc = tt?.Any() ?? false;
			string helpUrl = CiUtil.GetSymbolHelpUrl(currentItem);
			string sourceUrl = CiGoTo.GetLinkData(currentItem);
			bool haveLinks = helpUrl != null || sourceUrl != null;
			if (haveDoc || haveLinks) {
				x.StartParagraph();
				if (haveDoc) x.AppendTaggedParts(tt);
				if (haveLinks) {
					if (haveDoc) x.Append(" ");
					x.AppendSymbolLinks(helpUrl, sourceUrl);
					x.Append(".");
				}
				x.EndParagraph();
			}
		}

		if (currentParameter != null && !currentParameter.Name.NE()) { //if tuple, Name is "" and then would be exception
			x.StartParagraph("parameter");
			x.Bold(currentParameter.Name); x.Append(":  ");
			x.AppendTaggedParts(currentParameter.DocumentationFactory?.Invoke(default));
			x.EndParagraph();
		}

		return x.Result;
	}

	static List<ISignatureHelpProvider> _GetSignatureHelpProviders() {
		var a = new List<ISignatureHelpProvider>();
		var types = Assembly.GetAssembly(typeof(InvocationExpressionSignatureHelpProvider)).DefinedTypes;
		foreach (var t in types.Where(t =>
			t.Namespace == "Microsoft.CodeAnalysis.CSharp.SignatureHelp"
			&& t.IsDefined(typeof(ExportSignatureHelpProviderAttribute))
			//&& t.ImplementedInterfaces.Contains(typeof(ISignatureHelpProvider)) && !t.IsAbstract
			)) {
			//print.it(t);
			var c = t.GetConstructor(Type.EmptyTypes);
			Debug_.PrintIf(c == null, t.ToString());
			if (c == null) continue;
			var o = c.Invoke(null) as ISignatureHelpProvider; Debug.Assert(o != null); if (o == null) continue;
			a.Add(o);
		}
		return a;
	}

	List<ISignatureHelpProvider> _SignatureHelpProviders => _shp ??= _GetSignatureHelpProviders();
	List<ISignatureHelpProvider> _shp;

	public bool OnCmdKey(KKey key) {
		if (_data != null) {
			switch (key) {
			case KKey.Escape:
				Cancel();
				return true;
			case KKey.Down:
			case KKey.Up:
				int i = _data.iSelected, n = _data.r.Items.Count;
				if (key == KKey.Down) {
					if (++i >= n) i = 0;
				} else {
					if (--i < 0) i = n - 1;
				}
				if (i != _data.iSelected) _textPopup.Text = _FormatText(i, userSelected: true);
				return true;
			}
		}
		return false;
	}
}
