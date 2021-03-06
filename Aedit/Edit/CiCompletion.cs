//note: the Roslyn project has been modified. Eg added Symbols property to the CompletionItem class.

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
using Au.Controls;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Completion;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.CSharp.Extensions.ContextQuery;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.Options;

partial class CiCompletion
{
	CiPopupList _popupList;
	_Data _data; //not null while the popup list window is visible
	CancellationTokenSource _cancelTS;

	class _Data
	{
		public CompletionService completionService;
		public Document document;
		public SemanticModel model;
		public List<CiComplItem> items;
		public int codeLength;
		public string filterText;
		public SciCode.ITempRange tempRange;
		public bool noAutoSelect;
		Dictionary<CompletionItem, CiComplItem> _map;
		public CiWinapi winapi;

		public Dictionary<CompletionItem, CiComplItem> Map {
			get {
				if (_map == null) {
					_map = new Dictionary<CompletionItem, CiComplItem>(items.Count);
					foreach (var v in items) _map.Add(v.ci, v);
				}
				return _map;
			}
		}
	}

	public bool IsVisibleUI => _data != null;

	public void Cancel() {
		_cancelTS?.Cancel(); _cancelTS = null;
		_CancelUI();
	}

	void _CancelUI(bool popupListHidden = false, bool tempRangeRemoved = false) {
		//print.it("_CancelUI", _data != null);
		if (_data == null) return;
		if (!tempRangeRemoved) _data.tempRange.Remove();
		_data = null;
		if (!popupListHidden) _popupList.Hide();
	}

	public void SciUpdateUI(SciCode doc) { //modified, position changed, clicked
										   //int pos = doc.CurrentPosChars;
										   //var node = CiTools.NodeAt(pos);
										   //print.it(CiTools.IsInString(ref node, pos));
	}

	/// <summary>
	/// Called before <see cref="CiAutocorrect.SciCharAdded"/> and before passing the character to Scintilla.
	/// If showing popup list, synchronously commits the selected item if need (inserts text).
	/// Else inserts text at caret position and now caret is after the text.
	/// </summary>
	public CiComplResult SciCharAdding_Commit(SciCode doc, char ch) {
		CiComplResult R = CiComplResult.None;
		if (_data != null) {
			if (!SyntaxFacts.IsIdentifierPartCharacter(ch)) {
				var ci = _popupList.SelectedItem;
				if (ci != null && _data.filterText.Length == 0 && ch != '.') ci = null;
				if (ci != null) R = _Commit(doc, ci, ch, default);
				_CancelUI();
				//return;
			}
		}
		return R;
	}

	/// <summary>
	/// Asynchronously shows popup list if need. If already showing, synchronously filters/selects items.
	/// Called after <see cref="SciCharAdding_Commit"/> and <see cref="CiAutocorrect.SciCharAdded"/>.
	/// </summary>
	public void SciCharAdded_ShowList(CodeInfo.CharContext c) {
		if (_data == null) {
			_ShowList(c.ch);
		}
	}

	public void SciModified(SciCode doc, in Sci.SCNotification n) {
		if (_data != null) {
			bool trValid = _data.tempRange.GetCurrentFromTo(out int from, out int to, utf8: true);
			Debug.Assert(trValid); if (!trValid) { Cancel(); return; }
			string s = doc.zRangeText(false, from, to);
			foreach (var v in s) if (!SyntaxFacts.IsIdentifierPartCharacter(v)) return; //mostly because now is before SciCharAddedCommit, which commits (or cancels) if added invalid char
			_data.filterText = s;
			_FilterItems(_data);
			_popupList.UpdateVisibleItems(); //and calls SelectBestMatch
		}
	}

	public void ShowList() {
		_ShowList(default);
	}

	//static bool s_workaround1;

	//SHOULDDO: delay
	async void _ShowList(char ch) {
		//print.clear();

		//using
		var p1 = perf.local(); //FUTURE: remove all p1 lines

		//print.it(_cancelTS);
		bool isCommand = ch == default, wasBusy = _cancelTS != null;
		Cancel();

		if (!CodeInfo.GetContextWithoutDocument(out var cd)) return; //returns false if position is in meta comments
		SciCode doc = cd.sciDoc;
		int position = cd.pos16;
		string code = cd.code;

		if (ch != default && position > 1 && SyntaxFacts.IsIdentifierPartCharacter(ch) && SyntaxFacts.IsIdentifierPartCharacter(code[position - 2])) { //in word
			if (!wasBusy) return;
			ch = default;
		}

		//CodeInfo.HideTextPopupAndTempWindows(); //no
		CodeInfo.HideTextPopup();

		//using var nogcr = keys.isScrollLock ? new Debug_.NoGcRegion(50_000_000) : default;

		if (!cd.GetDocument()) return; //returns false if fails (unlikely)
		Document document = cd.document;
		Debug.Assert(code == document.GetTextAsync().Result.ToString());
		p1.Next('d');

		if (ch == '/') {
			InsertCode.DocComment(cd);
			return;
		}

		bool isDot = false, canGroup = false;
		PSFormat stringFormat = PSFormat.None; TextSpan stringSpan = default;
		CompletionService completionService = null;
		SemanticModel model = null;
		CompilationUnitSyntax root = null;
		//ISymbol symL = null; //symbol at left of . etc
		CSharpSyntaxContext syncon = null;
		ITypeSymbol typeL = null; //not null if X. where X is not type/namespace/unknown
		ISymbol symL = null;
		int typenameStart = -1;

		_cancelTS = new CancellationTokenSource();
		var cancelTS = _cancelTS;
		var cancelToken = cancelTS.Token;
#if DEBUG
		if (Debugger.IsAttached) { cancelToken = default; _cancelTS = null; }
#endif

		try {
			CompletionList r = await Task.Run(async () => { //info: actually GetCompletionsAsync etc are not async
				completionService = CompletionService.GetService(document);
				if (cancelToken.IsCancellationRequested) return null;

				model = await document.GetSemanticModelAsync(cancelToken).ConfigureAwait(false); //speed: does not make slower, because GetCompletionsAsync calls it too
				root = model.Root as CompilationUnitSyntax;
				var node = root.FindToken(position).Parent;
				var workspace = document.Project.Solution.Workspace;
				syncon = CSharpSyntaxContext.CreateContext(workspace, model, position, cancelToken);
				p1.Next('s');

				//never mind: To make faster in some cases, could now return if in comments or non-regex etc string.
				//	It is not so easy to check correctly. GetCompletionsAsync then not very fast and not too slow.

				//in some cases show list when typing a character where GetCompletionsAsync works only on command
				if (ch == '[' && syncon.IsAttributeNameContext) ch = default;
				if (ch == ' ' && syncon.IsObjectCreationTypeContext) ch = default;

				//This was a temporary workaround for exception in one Roslyn version, in AbstractEmbeddedLanguageCompletionProvider.GetLanguageProviders().
				//if (!s_workaround1 && ch != default) {
				//	_ = completionService.GetCompletionsAsync(document, position).Result;
				//	s_workaround1 = true;
				//}

				//print.it(syncon.IsGlobalStatementContext);

				var trigger = ch == default ? default : CompletionTrigger.CreateInsertionTrigger(ch);
				var r1 = await completionService.GetCompletionsAsync(document, position, trigger, null, _Options(workspace), cancelToken).ConfigureAwait(false);
				p1.Next('C');
				if (r1 != null) {
					canGroup = true;
					//is it member access?
					if (node is InitializerExpressionSyntax) {
						//if only properties, group by inheritance. Else group by namespace; it's a collection initializer list and contains everything.
						isDot = !r1.Items.Any(o => o.Symbols?[0] is not IPropertySymbol);
						if (!isDot && ch == '{') return null; //eg 'new int[] {'
					} else {
#if true
						isDot = syncon.IsRightOfNameSeparator;
						if (isDot) { //set canGroup = false if Namespace.X or alias::X
							if (syncon.IsInImportsDirective) {
								canGroup = false;
							} else {
								var token = syncon.TargetToken; //not LeftToken, it seems they are swapped
								node = token.Parent;
								//CiUtil.PrintNode(token);
								//CiUtil.PrintNode(node);

								switch (node) {
								case MemberAccessExpressionSyntax s1: // . or ->
									node = s1.Expression;
									break;
								case QualifiedNameSyntax s1: // eg . outside functions
									node = s1.Left;
									break;
								case AliasQualifiedNameSyntax: // ::
								case ExplicitInterfaceSpecifierSyntax: //Interface.X
								case QualifiedCrefSyntax: //does not include base members
									canGroup = false;
									break;
								case RangeExpressionSyntax: //noticed once, don't know how, could not reproduce
									isDot = canGroup = false;
									break;
								default:
									Debug_.Print(node.GetType());
									isDot = canGroup = false;
									break;
								}

								if (canGroup) {
#if true //need typeL
									var ti = model.GetTypeInfo(node).Type;
									if (ti == null) {
										Debug_.PrintIf(model.GetSymbolInfo(node).Symbol is not INamespaceSymbol, node);
										canGroup = false;
									} else {
										symL = model.GetSymbolInfo(node).Symbol;
										Debug_.PrintIf(symL is INamespaceSymbol, node);
										//print.it(symL, symL is INamedTypeSymbol);
										if (symL is INamedTypeSymbol) typenameStart = node.SpanStart;
										else typeL = ti;

									}
#else //need just canGroup
									if (model.GetSymbolInfo(node).Symbol is INamespaceSymbol) canGroup = false;
#endif
								}
								//print.it(canGroup);
							}
						}
#else
						//old code, before discovering CSharpSyntaxContext
						int i = r1.Span.Start - 1;
						if (i > 0) {
							var token = root.FindToken(i); //fast
							if (position >= token.Span.End) {
								var tk = token.Kind();
								//print.it(tk);
								//CiUtil.PrintNode(node);
								isDot = tk == SyntaxKind.DotToken || tk == SyntaxKind.MinusGreaterThanToken || tk == SyntaxKind.ColonColonToken;
								if (isDot) {
									node = token.Parent;
									ExpressionSyntax nodeL = null; //node at left of . etc
									bool canBeNamespace = false;
									switch (node) {
									case MemberAccessExpressionSyntax s1: // . or ->
										nodeL = s1.Expression;
										canBeNamespace = true;
										break;
									case MemberBindingExpressionSyntax s1 when s1.OperatorToken.GetPreviousToken().Parent is ConditionalAccessExpressionSyntax cae:
										// ?. //OperatorToken is '.', GetPreviousToken is '?'
										//nodeL = cae.Expression;
										break;
									case QualifiedNameSyntax s1: // eg . outside functions
										nodeL = s1.Left;
										canBeNamespace = true;
										break;
									case AliasQualifiedNameSyntax s1: // ::
										canGroup = false;
										//nodeL = s1.Alias;
										break;
									case ExplicitInterfaceSpecifierSyntax s1:
										//nodeL = s1.Name;
										break;
									default:
										isDot = false;
										Debug_.Print(node.GetType());
										break;
									}

									if (canBeNamespace && tk == SyntaxKind.DotToken) {
										if (model.GetSymbolInfo(nodeL).Symbol is INamespaceSymbol) canGroup = false;
									}
								}
							}
						}
#endif
					}
					//p1.Next('M');
				} else if (isCommand) {
					if (CiUtil.IsInString(ref node, position)) {
						stringSpan = node.Span;
						stringFormat = CiUtil.GetParameterStringFormat(node, model, true);
						if (stringFormat == PSFormat.wildex) { //is regex in wildex?
							if (code.RegexMatch(@"[\$@]*""(?:\*\*\*\w+ )?\*\*c?rc? ", 0, out RXGroup rg, RXFlags.ANCHORED, stringSpan.Start..stringSpan.End)
								&& position >= stringSpan.Start + rg.Length) stringFormat = PSFormat.regexp;
						} else if (stringFormat == PSFormat.None) stringFormat = (PSFormat)100;
					}
				}
				return r1;
			});

			if (cancelToken.IsCancellationRequested) return;

			if (r == null) {
				if (stringFormat == (PSFormat)100) {
					int i = popupMenu.showSimple("Regex|Keys", MSFlags.ByCaret);
					stringFormat = i switch { 1 => PSFormat.regexp, 2 => PSFormat.keys, _ => default };
				}
				if (stringFormat != default) CodeInfo._tools.ShowForStringParameter(stringFormat, cd, stringSpan);
				return;
			}

			Debug.Assert(doc == Panels.Editor.ZActiveDoc); //when active doc changed, cancellation must be requested
			if (position != doc.zCurrentPos16 || (object)code != doc.zText) return; //changed while awaiting
			p1.Next('T');

			var provider = _GetProvider(r.Items[0]);
			if (!isDot) isDot = provider == CiComplProvider.Override;
			//print.it(provider, isDot, canGroup, r.Items[0].DisplayText);

			var span = r.Span;
			if (span.Length > 0 && provider == CiComplProvider.Regex) span = new TextSpan(position, 0);

			var d = new _Data {
				completionService = completionService,
				document = document,
				model = model,
				codeLength = code.Length,
				filterText = code.Substring(span.Start, span.Length),
				items = new List<CiComplItem>(r.Items.Length),
				noAutoSelect = r.SuggestionModeItem != null,
			};

			//Debug_.PrintIf(r.SuggestionModeItem != null && r.SuggestionModeItem.ToString() != "<lambda expression>" && !r.SuggestionModeItem.ToString().NE(), r.SuggestionModeItem); //in '#if X' non-nul but empty text

			//ISymbol enclosing = null;
			//bool _IsAccessible(ISymbol symbol) {
			//	enclosing ??= model.GetEnclosingNamedTypeOrAssembly(position, default);
			//	return enclosing != null && symbol.IsAccessibleWithin(enclosing);
			//}

			//info: some members of enum UnmanagedType are missing. Hidden with EditorBrowsableState.Never, don't know why.

			//var testInternal = CodeInfo.Meta.TestInternal;
			Dictionary<INamespaceOrTypeSymbol, List<int>> groups = canGroup ? new(new CiNamespaceOrTypeSymbolEqualityComparer()) : null;
			List<int> keywordsGroup = null, etcGroup = null, snippetsGroup = null;
			bool hasNamespaces = false;
			foreach (var ci_ in r.Items) {
				var ci = ci_;
				Debug.Assert(ci.Symbols == null || ci.Symbols.Count > 0); //we may use code ci?.Symbols[0]. Roslyn uses this code in CompletionItem ctor: var firstSymbol = symbols[0];


				var v = new CiComplItem(provider, ci);
				var sym = v.FirstSymbol;
				//print.it(ci.DisplayText, sym);
				//if(ci.SortText != ci.DisplayText) print.it($"<>{ci.DisplayText}, sort={ci.SortText}");
				//if(ci.FilterText != ci.DisplayText) print.it($"<>{ci.DisplayText}, filter={ci.FilterText}");
				//if(!ci.DisplayTextSuffix.NE()) print.it($"<>{ci.DisplayText}, suf={ci.DisplayTextSuffix}");
				//if(!ci.DisplayTextPrefix.NE()) print.it($"<>{ci.DisplayText}, pre={ci.DisplayTextPrefix}");

				//print.it(ci.Flags); //a new internal property. Always None.

				if (sym != null) {
					//why cref provider adds internals from other assemblies?
					if (provider == CiComplProvider.Cref) {
						switch (sym.Kind) {
						case SymbolKind.NamedType when v.access == CiItemAccess.Internal && !sym.IsInSource() && !model.IsAccessible(0, sym):
							//print.it(sym);
							continue;
						case SymbolKind.Namespace:
							//print.it(sym, sym.ContainingAssembly?.Name);
							switch (sym.Name) {
							case "Internal" when sym.ContainingAssembly?.Name == "System.Core":
							case "Windows" when sym.ContainingAssembly?.Name == "mscorlib":
								continue;
							}
							break;
						}
					}
				}

				switch (v.kind) {
				case CiItemKind.Method:
					if (sym != null) {
						if (sym.IsStatic) {
							switch (ci.DisplayText) {
							case "Equals":
							case "ReferenceEquals":
								//hide static members inherited from Object
								if (sym.ContainingType.BaseType == null) { //Object
									if (isDot && !(symL is INamedTypeSymbol ints1 && ints1.BaseType == null)) continue; //if not object
									v.moveDown = CiComplItemMoveDownBy.Name;
								}
								break;
							case "Main" when _IsOurScriptClass(sym.ContainingType):
								v.moveDown = CiComplItemMoveDownBy.Name;
								break;
							}
						} else {
							switch (ci.DisplayText) {
							case "Equals":
							case "GetHashCode":
							case "GetType":
							case "ToString":
							case "MemberwiseClone":
							case "GetEnumerator": //IEnumerable
							case "CompareTo": //IComparable
							case "GetTypeCode": //IConvertible
							case "Clone" when sym.ContainingType.Name == "String": //this useless method would be the first in the list
								v.moveDown = CiComplItemMoveDownBy.Name;
								break;
							}
							//var ct = sym.ContainingType;
							//print.it(ct.ToString(), ct.Name, ct.ContainingNamespace.ToString(), ct.BaseType);
						}
					}
					break;
				case CiItemKind.Namespace when ci.Symbols != null: //null if extern alias
					hasNamespaces = true;
					switch (ci.DisplayText) {
					case "Accessibility":
					case "UIAutomationClientsideProviders":
						v.moveDown = CiComplItemMoveDownBy.Name;
						break;
					case "XamlGeneratedNamespace": continue;
					}
					break;
				case CiItemKind.TypeParameter:
					if (sym == null && ci.DisplayText == "T") continue;
					break;
				case CiItemKind.Class:
					if (!isDot && sym is INamedTypeSymbol ins && _IsOurScriptClass(ins)) v.moveDown = CiComplItemMoveDownBy.Name;
					if (typenameStart >= 0 && ci.DisplayText == "l" && CiWinapi.IsWinapiClassSymbol(symL as INamedTypeSymbol)) v.hidden = CiComplItemHiddenBy.Always; //not continue;, then no groups
					break;
				case CiItemKind.EnumMember:
				case CiItemKind.Label:
					canGroup = false;
					break;
				case CiItemKind.LocalVariable:
					if (isDot) continue; //see the bug comments below
					break;
				case CiItemKind.Keyword when syncon.IsGlobalStatementContext:
					if (ci.DisplayText == "from") v.ci = ci.WithDisplayText("return").WithFilterText("return").WithSortText("return"); //Roslyn bug: no 'return', but is 'from' (why?)
																																	   //print.it(ci.DisplayText);
					break;
				}

				static bool _IsOurScriptClass(INamedTypeSymbol t) => t.Name is "Program" or "Script";

				if (sym != null && v.kind != CiItemKind.LocalVariable && v.kind != CiItemKind.Namespace && v.kind != CiItemKind.TypeParameter) {
					bool isObsolete = ci.Symbols.All(sy => sy.GetAttributes().Any(o => o.AttributeClass.Name == "ObsoleteAttribute")); //can be several overloads, some obsolete but others not
					if (isObsolete) v.moveDown = CiComplItemMoveDownBy.Obsolete;
				}

				d.items.Add(v);
			}

			if (canGroup) {
				for (int i = 0; i < d.items.Count; i++) {
					var v = d.items[i];
					var sym = v.FirstSymbol;
					if (sym == null) {
						if (v.kind == CiItemKind.Keyword) (keywordsGroup ??= new List<int>()).Add(i);
						else (etcGroup ??= new List<int>()).Add(i);
					} else {
						INamespaceOrTypeSymbol nts;
						if (!isDot) nts = sym.ContainingNamespace;
						//else if(sym is ReducedExtensionMethodSymbol em) nts = em.ReceiverType; //rejected. Didn't work well, eg with linq.
						else nts = sym.ContainingType;

						//Roslyn bug: sometimes adds some garbage items.
						//To reproduce: invoke global list. Then invoke list for a string variable. Adds String, Object, all local string variables, etc. Next time works well. After Enum dot adds the enum type, even in VS; in VS sometimes adds enum methods and extmethods.
						//Debug_.PrintIf(nts == null, sym.Name);
						if (nts == null) continue;

						if (groups.TryGetValue(nts, out var list)) list.Add(i); else groups.Add(nts, new List<int> { i });
					}
				}

				//snippets, favorite namespaces, winapi
				if (isDot) {
					if (typeL != null) { //eg variable.x
						CodeInfo._favorite.AddCompletions(d.items, groups, span, syncon, typeL); //add extension methods from favorite namespaces
					} else if (symL is INamedTypeSymbol nts && CiWinapi.IsWinapiClassSymbol(nts)) { //type.x
						int i = d.items.Count;
						bool newExpr = syncon.TargetToken.Parent.Parent is ObjectCreationExpressionSyntax; //info: syncon.IsObjectCreationTypeContext false
						d.winapi = CiWinapi.AddWinapi(nts, d.items, span, typenameStart, newExpr);
						int n = d.items.Count - i;
						if (n > 0) {
							snippetsGroup = new List<int>(n);
							for (; i < d.items.Count; i++) snippetsGroup.Add(i);
						}
					}
				} else if (hasNamespaces && !d.noAutoSelect) {
					//add types from favorite namespaces
					if (syncon.IsTypeContext) {
						CodeInfo._favorite.AddCompletions(d.items, groups, span, syncon, null);
					}

					//add snippets
					if (provider != CiComplProvider.Cref) {
						int i = d.items.Count;
						CiSnippets.AddSnippets(d.items, span, root, code, syncon);
						for (; i < d.items.Count; i++) (snippetsGroup ??= new List<int>()).Add(i);
					}
				}
			}

			if (d.items.Count == 0) return;

			List<string> groupsList = null;
			if (canGroup && groups.Count + (keywordsGroup == null ? 0 : 1) + (etcGroup == null ? 0 : 1) + (snippetsGroup == null ? 0 : 1) > 1) {
				List<(string, List<int>)> g = null;
				if (isDot) {
					var gs = groups.ToList();
					gs.Sort((k1, k2) => {
						//let extension methods be at bottom, sorted by type name
						int em1 = d.items[k1.Value[0]].kind == CiItemKind.ExtensionMethod ? 1 : 0;
						int em2 = d.items[k2.Value[0]].kind == CiItemKind.ExtensionMethod ? 1 : 0;
						int diff = em1 - em2;
						if (diff != 0) return diff;
						if (em1 == 1) return string.Compare(k1.Key.Name, k2.Key.Name, StringComparison.OrdinalIgnoreCase);
#if true
						//sort non-extension members by inheritance base interface
						var t1 = k1.Key as INamedTypeSymbol; var t2 = k2.Key as INamedTypeSymbol;
						if (t1.InheritsFromOrImplementsOrEqualsIgnoringConstruction(t2)) return -1;
						if (t2.InheritsFromOrImplementsOrEqualsIgnoringConstruction(t1)) return 1;
						//interface and object? For both, BaseType returns null and InheritsFromOrImplementsOrEqualsIgnoringConstruction returns false.
						var tk1 = t1.TypeKind; var tk2 = t2.TypeKind;
						if (tk1 == TypeKind.Class && t1.BaseType == null) return 1; //t1 is object
						if (tk2 == TypeKind.Class && t2.BaseType == null) return -1; //t2 is object
						Debug_.Print($"{t1}, {t2},    {t1.BaseType}, {t2.BaseType},    {tk1}, {tk2}");
#else
						//sort non-extension members by inheritance
						var t1 = k1.Key as INamedTypeSymbol; var t2 = k2.Key as INamedTypeSymbol;
						if (_IsBase(t1, t2)) return -1;
						if (_IsBase(t2, t1)) return 1;
						static bool _IsBase(INamedTypeSymbol t, INamedTypeSymbol tBase) {
							for (t = t.BaseType; t != null; t = t.BaseType) if (t == tBase) return true;
							return false;
						}
						//can be both interfaces, or interface and object. For object and interfaces BaseType returns null.
						var tk1 = t1.TypeKind; var tk2 = t2.TypeKind;
						if (tk1 == TypeKind.Class && t1.BaseType == null) return 1; //t1 is object
						if (tk2 == TypeKind.Class && t2.BaseType == null) return -1; //t2 is object
						if (tk1 == TypeKind.Interface && tk2 == TypeKind.Interface) {
							if (t2.AllInterfaces.Contains(t1)) return 1;
							if (t1.AllInterfaces.Contains(t2)) return -1;
						}
						//fails for eg ObservableCollection<>. Uses 2 variables for t2 and t1.BaseType although it is the same type.
						Debug_.Print($"{t1}, {t2}, {k1.Value.Count}, {k2.Value.Count}, {tk1}, {tk2}, {t1.BaseType}, {t2.BaseType}"); //usually because of Roslyn bugs
#endif

						//SHOULDDO: workaround for Roslyn bug: in argument-lambda, on dot after lambda parameter, also adds members of types of parameter at that position of other overloads.
						return 0;
					});
					//print.it(gs);

#if true
					if (gs[0].Key.Name == "String") { //don't group Au extension methods
						for (int i = 1; i < gs.Count; i++) {
							if (d.items[gs[i].Value[0]].kind != CiItemKind.ExtensionMethod) continue;
							var ns = gs[i].Key.ContainingNamespace;
							if (ns.Name == "Au") {
								gs[0].Value.AddRange(gs[i].Value);
								gs.RemoveAt(i);
								break;
							}
						}
					}
#else
					if(!App.Settings.ci_complGroupEM) { //don't group non-Linq extension methods
						for(int i = 1; i < gs.Count; i++) {
							if(d.items[gs[i].Value[0]].kind != CiItemKind.ExtensionMethod) continue;
							var ns = gs[i].Key.ContainingNamespace;
							if(ns.Name != "Linq") {
								gs[0].Value.AddRange(gs[i].Value);
								gs.RemoveAt(i--);
							}
						}
					}
#endif

					g = gs.Select(o => (o.Key.Name, o.Value)).ToList(); //list<(itype, list)> -> list<typeName, list>
				} else {
					g = groups.Select(o => (o.Key.QualifiedName(), o.Value)).ToList(); //dictionary<inamespace, list> -> list<namespaceName, list>
																					   //print.it("----");
																					   //foreach(var v in g) print.it(v.Item1, v.Item2.Count);
					g.Sort((e1, e2) => string.Compare(e1.Item1, e2.Item1, StringComparison.OrdinalIgnoreCase));
				}
				if (keywordsGroup != null) g.Add(("keywords", keywordsGroup));
				if (snippetsGroup != null) g.Add((isDot ? "" : "snippets", snippetsGroup));
				if (etcGroup != null) g.Add(("etc", etcGroup));
				for (int i = 0; i < g.Count; i++) {
					foreach (var v in g[i].Item2) d.items[v].group = i;
				}
				groupsList = g.Select(o => o.Item1).ToList();
			}

			if (!span.IsEmpty) _FilterItems(d);
			p1.Next('F');

			d.tempRange = doc.ZTempRanges_Add(this, span.Start, span.End, () => {
				//print.it("leave", _data==d);
				if (_data == d) _CancelUI(tempRangeRemoved: true);
			}, position == span.End ? SciCode.ZTempRangeFlags.LeaveIfPosNotAtEndOfRange : 0);

			_data = d;
			if (_popupList == null) {
				_popupList = new CiPopupList(this);
				_popupList.PopupWindow.Hidden += (_, _) => _CancelUI(popupListHidden: true);
			}
			_popupList.Show(doc, span.Start, _data.items, groupsList); //and calls SelectBestMatch
		}
		catch (OperationCanceledException) { /*Debug_.Print("canceled");*/ return; }
		finally {
			if (_data == null) {
				p1.Next('z');
				//print.it($"{p1.ToString()}  |  ch='{(ch == default ? "" : ch.ToString())}', canceled={cancelTS.IsCancellationRequested}");
			}
			cancelTS.Dispose();
			if (cancelTS == _cancelTS) _cancelTS = null;
		}
	}

	static void _FilterItems(_Data d) {
		var filterText = d.filterText;
		foreach (var v in d.items) {
			v.hidden &= ~CiComplItemHiddenBy.FilterText;
			v.hilite = 0;
			v.moveDown &= ~CiComplItemMoveDownBy.FilterText;
		}
		if (!filterText.NE()) {
			string textLower = filterText.ToLower(), textUpper = filterText.Upper();
			char c0Lower = textLower[0], c0Upper = textUpper[0];
			foreach (var v in d.items) {
				if (v.kind == CiItemKind.None) continue; //eg regex completion
				var s = v.ci.FilterText;
				//Debug_.PrintIf(v.ci.FilterText != v.Text, $"{v.ci.FilterText}, {v.Text}");
				//print.it(v.Text, v.ci.FilterText, v.ci.SortText, v.ci.ToString());
				bool found = false;
				int iFirst = _FilterFindChar(s, 0, c0Lower, c0Upper), iFirstFirst = iFirst;
				if (iFirst >= 0) {
					if (filterText.Length == 1) {
						_HiliteChar(iFirst);
					} else {
						while (!s.Eq(iFirst, filterText, true)) {
							iFirst = _FilterFindChar(s, iFirst + 1, c0Lower, c0Upper);
							if (iFirst < 0) break;
						}
						if (iFirst >= 0) {
							_HiliteSubstring(iFirst);
						} else { //has all uppercase chars? Eg add OneTwoThree if text is "ott" or "ot" or "tt".
							_HiliteChar(iFirstFirst);
							for (int i = 1, j = iFirstFirst + 1; i < filterText.Length; i++, j++) {
								j = _FilterFindChar(s, j, textLower[i], textUpper[i], camel: true);
								if (j < 0) { found = false; break; }
								_HiliteChar(j);
							}
						}
					}
				}

				void _HiliteChar(int i) {
					found = true;
					if (i < 64) v.hilite |= 1UL << i;
				}

				void _HiliteSubstring(int i) {
					found = true;
					int to = Math.Min(i + filterText.Length, 64);
					while (i < to) v.hilite |= 1UL << i++;
				}

				if (!found) {
					if (filterText.Length > 1 && (iFirst = s.Find(filterText, true)) >= 0) {
						v.moveDown |= CiComplItemMoveDownBy.FilterText; //sort bottom
						_HiliteSubstring(iFirst);
					} else v.hidden |= CiComplItemHiddenBy.FilterText;
				}

				//if DisplayText != FilterText, correct or clear hilites. Eg cref.
				if (found && s != v.Text) {
					iFirst = v.Text.Find(s);
					if (iFirst < 0) v.hilite = 0; else v.hilite <<= iFirst;
				}
			}
		}
	}

	/// <summary>
	/// Finds character in s where it is one of: uppercase; lowercase after '_'/'@'; not uppercase/lowercase; any at i=0.
	/// </summary>
	/// <param name="s"></param>
	/// <param name="i">Start index.</param>
	/// <param name="cLower">Lowercase version of character to find.</param>
	/// <param name="cUpper">Uppercase version of character to find.</param>
	/// <param name="camel">Uppercase must not be preceded and followed by uppercase.</param>
	static int _FilterFindChar(string s, int i, char cLower, char cUpper, bool camel = false) {
		for (; i < s.Length; i++) {
			char c = s[i];
			if (c == cUpper) { //any not lowercase
				if (!camel) return i;
				if (i == 0 || !char.IsUpper(c)) return i;
				if (!char.IsUpper(s[i - 1])) return i;
				if (i + 1 < s.Length && char.IsLower(s[i + 1])) return i;
			}
			if (c == cLower) { //lowercase
				if (i == 0) return i;
				switch (s[i - 1]) { case '_': case '@': return i; }
			}
		}
		return -1;
	}

	public void SelectBestMatch(IEnumerable<CompletionItem> listItems) {
		CiComplItem ci = null;
		var filterText = _data.filterText;
		if (!(_data.noAutoSelect || filterText == "_")) { //noAutoSelect when lambda argument

			//rejected. Need FilterItems anyway, eg to select enum type or 'new' type.
			//if(filterText.NE()) {
			//	_popupList.SelectFirstVisible();
			//	return;
			//}

			//perf.first();
			var visible = listItems.ToImmutableArray();
			if (!visible.IsEmpty) {
				//perf.next();
				var fi = _data.completionService.FilterItems(_data.document, visible, filterText);
				//perf.next();
				//print.it(fi);
				//print.it(visible.Length, fi.Length);
				if (!fi.IsDefaultOrEmpty) if (fi.Length < visible.Length || filterText.Length > 0 || visible.Length == 1) ci = _data.Map[fi[0]];
			}
			//perf.nw('b');
		}
		_popupList.SelectedItem = ci;
	}
	//FUTURE: when typed 1-2 lowercase chars, select keyword instead of type.
	//	Now these types are selected first (but none when typed 3 chars):
	/*
	elm else
	folders for
	inputBlocker int
	regexp return/ref/readonly/record    //ref/record are rare and before return, but readonly...
	trayIcon true/try
	uiimage uint

	RARE
	clipboard class
	filesystem finally/fixed
	pathname params
	print/process private/protected

	*/

	public System.Windows.Documents.Section GetDescriptionDoc(CiComplItem ci, int iSelect) {
		if (_data == null) return null;
		switch (ci.Provider) {
		case CiComplProvider.Snippet: return CiSnippets.GetDescription(ci);
		case CiComplProvider.Winapi: return CiWinapi.GetDescription(ci);
		}
		switch (ci.kind) {
		case CiItemKind.Keyword: return CiText.FromKeyword(ci.Text);
		case CiItemKind.Label: return CiText.FromLabel(ci.Text);
		}
		var symbols = ci.Symbols;
		if (symbols != null) return CiText.FromSymbols(symbols, iSelect, _data.model, _data.tempRange.CurrentFrom);
		if (ci.kind == CiItemKind.Namespace) return null; //extern alias
		Debug_.PrintIf(ci.kind != CiItemKind.None, ci.kind); //None if Regex
		var r = _data.completionService.GetDescriptionAsync(_data.document, ci.ci).Result; //fast if Regex, else not tested
		return r == null ? null : CiText.FromTaggedParts(r.TaggedParts);
	}

	/// <summary>
	/// Inserts the replacement text of the completion item.
	/// ch == default if clicked or pressed Enter or Tab or a hotkey eg Ctrl+Enter.
	/// key == default if clicked or typed a character (except Tab and Enter). Does not include hotkey modifiers.
	/// </summary>
	CiComplResult _Commit(SciCode doc, CiComplItem item, char ch, KKey key) {
		if (item.Provider == CiComplProvider.Regex) { //can complete only on click or Tab
			if (ch != default || !(key == default || key == KKey.Tab)) return CiComplResult.None;
		}

		bool isSpace; if (isSpace = ch == ' ') ch = default;
		int codeLenDiff = doc.zLen16 - _data.codeLength;

		if (item.Provider == CiComplProvider.Snippet) {
			if (ch != default && ch != '(') return CiComplResult.None;
			CiSnippets.Commit(doc, item, codeLenDiff);
			return CiComplResult.Complex;
		}

		var ci = item.ci;
		string s; int i, len;
		bool isComplex = false;
		bool ourProvider = item.Provider is CiComplProvider.Favorite or CiComplProvider.Winapi;
		if (ourProvider) {
			s = item.Text;
			i = _data.tempRange.CurrentFrom;
			len = _data.tempRange.CurrentTo - i;
		} else {
			var change = _data.completionService.GetChangeAsync(_data.document, ci).Result;
			//note: don't use the commitCharacter parameter. Some providers, eg XML doc, always set IncludesCommitCharacter=true, even when commitCharacter==null, but may include or not, and may include inside text or at the end.

			s = change.TextChange.NewText;
			var span = change.TextChange.Span;
			i = span.Start;
			len = span.Length + codeLenDiff;
			isComplex = change.NewPosition.HasValue;
			//print.it($"{change.NewPosition.HasValue}, cp={doc.CurrentPosChars}, i={i}, len={len}, span={span}, repl='{s}'    filter='{_data.filterText}'");
			//print.it($"'{s}'");
			if (isComplex) { //xml doc, override, regex
				if (ch != default) return CiComplResult.None;
				//ci.DebugPrint();
				int newPos = change.NewPosition.Value;
				switch (item.Provider) {
				case CiComplProvider.Override:
					newPos = -1; //difficult to calculate and not useful

					//Replace 4 spaces with tab. Make { in same line.
					s = s.Replace("    ", "\t").RegexReplace(@"\R\t*\{", " {", 1);
					//Correct indentation. 
					int indent = 0, indent2 = doc.zLineIndentationFromPos(true, _data.tempRange.CurrentFrom);
					for (int j = s.IndexOf('\t'); (uint)j < s.Length && s[j] == '\t'; j++) indent++;
					if (indent > indent2) s = s.RegexReplace("(?m)^" + new string('\t', indent - indent2), "");
					break;
				case CiComplProvider.XmlDoc when !s.Ends('>') && s.RegexMatch(@"^<?(\w+)", 1, out string tag):
					if (s == tag || (ci.Properties.TryGetValue("AfterCaretText", out var s1) && s1.NE())) newPos++;
					s += "></" + tag + ">";
					break;
				}
				doc.zReplaceRange(true, i, i + len, s);
				if (newPos >= 0) doc.zSelect(true, newPos, newPos, makeVisible: true);
				return CiComplResult.Complex;
			}
		}
		Debug_.PrintIf(i != _data.tempRange.CurrentFrom && item.Provider != CiComplProvider.Regex, $"{_data.tempRange.CurrentFrom}, {i}");
		//ci.DebugPrint();

		//if typed space after method or keyword 'if' etc, replace the space with '(' etc. Also add if pressed Tab or Enter.
		CiAutocorrect.EBraces bracesOperation = default;
		int positionBack = 0, bracesFrom = 0, bracesLen = 0;
		bool isEnter = key == KKey.Enter;
		//ci.DebugPrint();
		if (s.FindAny("({[<") < 0) {
			if (ch == default) { //completed with Enter, Tab, Space or click
				string s2 = null;
				switch (item.kind) {
				case CiItemKind.Method or CiItemKind.ExtensionMethod:
					ch = '(';
					break;
				case CiItemKind.Keyword:
					string name = item.Text;
					switch (name) {
					case "nameof":
					case "sizeof":
					case "typeof":
						ch = '(';
						break;
					case "for":
					case "foreach":
					case "while":
					case "lock":
					case "catch":
					case "if" when !_IsDirective():
					case "fixed" when _IsInFunction(): //else probably a fixed array field
					case "using" when isEnter && _IsInFunction(): //else directive or without ()
					case "when" when _IsInAncestorNode(i, n => (n is CatchClauseSyntax, n is SwitchSectionSyntax)): //catch(...) when
						ch = '(';
						s2 = " ()"; //users may prefer space, like 'if (i<1)'. If not, let they type '(' instead.
						break;
					case "try":
					case "finally":
					case "get" when isEnter:
					case "set" when isEnter:
					case "add" when isEnter:
					case "remove" when isEnter:
					case "do" when isEnter:
					case "unsafe" when isEnter:
					case "else" when isEnter && !_IsDirective():
						ch = '{';
						break;
					case "checked":
					case "unchecked":
						ch = isEnter ? '{' : '(';
						break;
					case "switch":
						//is it switch statement or switch expression? Difficult to detect. Detect some common cases.
						if (i > 0 && CodeInfo.GetContextWithoutDocument(out var cd, i)) {
							if (cd.code[i - 1] == ' ' && cd.GetDocument()) {
								var node = cd.document.GetSyntaxRootAsync().Result.FindToken(i - 1).Parent;
								//print.it(node.Kind(), i, node.Span, node);
								if (node.SpanStart < i) switch (node) { case ExpressionSyntax: case BaseArgumentListSyntax: ch = '{'; break; } //expression
							}
							if (ch == default) goto case "for";
						}
						break;
					default:
						if (0 != name.Eq(false, s_kwType)) _NewExpression();
						break;
					}
					break;
				case CiItemKind.Class or CiItemKind.Structure:
					if (ci.DisplayTextSuffix == "<>") ch = '<';
					else _NewExpression();
					break;
				}

				bool _IsInFunction() => _IsInAncestorNodeOfType<BaseMethodDeclarationSyntax>(i);

				bool _IsDirective() => doc.zText.Eq(i - 1, "#"); //info: CompletionItem of 'if' and '#if' are identical. Nevermind: this code does not detect '# if'.

				//If 'new Type', adds '()'.
				//If then the user types '[' for 'new Type[]' or '{' for 'new Type { initializers }', autocorrection will replace the '()' with '[]' or '{  }'.
				void _NewExpression() {
					if (!CodeInfo.GetDocumentAndFindNode(out _, out var node, i)) return;
					var p1 = node.Parent; if (p1 is QualifiedNameSyntax) p1 = p1.Parent;
					if (p1 is ObjectCreationExpressionSyntax) {
						ch = '(';
						bracesOperation = CiAutocorrect.EBraces.NewExpression;
					}
				}

				if (isComplex = ch != default) {
					if (ch == '{') {
						if (isEnter) {
							int indent = doc.zLineIndentationFromPos(true, i);
							var b = new StringBuilder(" {\r\n");
							b.Append('\t', indent + 1);
							b.Append("\r\n").Append('\t', indent).Append('}');
							s2 = b.ToString();
							positionBack = indent + 3;
						} else {
							s2 = " {  }";
							positionBack = 2;
						}
						bracesFrom = i + s.Length + 2;
						bracesLen = s2.Length - 3;
					} else if (App.Settings.ci_complParen switch { 0 => isSpace, 1 => true, _ => false } && !_data.noAutoSelect && !doc.zText.Eq(i + len, ch)) { //info: noAutoSelect when lambda argument
						s2 ??= ch == '(' ? "()" : "<>";
						positionBack = 1;
						bracesFrom = i + s.Length + s2.Length - 1;
					} else {
						ch = default;
						s2 = null;
						isComplex = false;
					}
					s += s2;
				}
			} else if (!(ch == '(' || ch == '<' || _data.noAutoSelect)) { //completed with ';' or ',' or '.' or '?' or '-' or any other non-identifier char space, Tab, Enter
				switch (item.kind) {
				case CiItemKind.Method or CiItemKind.ExtensionMethod:
					s += "()";
					break;
				}
			}
		}

		try {
			if (!isComplex && s == _data.filterText) return CiComplResult.None;

			doc.zSetAndReplaceSel(true, i, i + len, s);
			if (isComplex) {
				if (positionBack > 0) doc.zCurrentPos16 = i + s.Length - positionBack;
				if (bracesFrom > 0) CodeInfo._correct.BracesAdded(doc, bracesFrom, bracesFrom + bracesLen, bracesOperation);
			}

			return isComplex ? CiComplResult.Complex : CiComplResult.Simple;
		}
		finally {
			if (ourProvider) {
				switch (item.Provider) {
				case CiComplProvider.Favorite: CiFavorite.OnCommitInsertUsingDirective(item); break;
				case CiComplProvider.Winapi: _data.winapi.OnCommitInsertDeclaration(item); break;
				}
			}
			if (isComplex) if (ch == '(' || ch == '<') CodeInfo._signature.SciCharAdded(doc, ch);
		}
	}

	static bool _IsInAncestorNodeOfType<T>(int pos) where T : SyntaxNode
		=> CodeInfo.GetDocumentAndFindNode(out _, out var node, pos) && null != node.GetAncestor<T>();

	static bool _IsInAncestorNode(int pos, Func<SyntaxNode, (bool yes, bool no)> f) {
		if (!CodeInfo.GetDocumentAndFindNode(out _, out var node, pos)) return false;
		while ((node = node.Parent) != null) {
			//CiUtil.PrintNode(node);
			var (yes, no) = f(node);
			if (yes) return true;
			if (no) return false;
		}
		return false;
	}

	static string[] s_kwType = { "string", "object", "int", "uint", "nint", "nuint", "long", "ulong", "byte", "sbyte", "short", "ushort", "char", "bool", "double", "float", "decimal" };

	/// <summary>
	/// Double-clicked item in list.
	/// </summary>
	public void Commit(SciCode doc, CiComplItem item) => _Commit(doc, item, default, default);

	/// <summary>
	/// Tab, Enter, Shift+Enter, Ctrl+Enter, Ctrl+;.
	/// </summary>
	public CiComplResult OnCmdKey_Commit(SciCode doc, KKey key) {
		var R = CiComplResult.None;
		if (_data != null) {
			var ci = _popupList.SelectedItem;
			if (ci != null) {
				R = _Commit(doc, ci, default, key);
				if (R == CiComplResult.None && key is KKey.Tab or KKey.Enter) R = CiComplResult.Simple; //always suppress Tab and Enter
			}
			_CancelUI();
		}
		return R;
	}

	/// <summary>
	/// Esc, Arrow, Page, Home, End.
	/// </summary>
	public bool OnCmdKey_SelectOrHide(KKey key) => _data != null && _popupList.OnCmdKey(key);

	static CiComplProvider _GetProvider(CompletionItem ci) {
		var s = ci.ProviderName;
		Debug_.PrintIf(s == null, "ProviderName null");
		if (s == null) return CiComplProvider.Other;
		int i = s.LastIndexOf('.') + 1;
		Debug.Assert(i > 0);
		//print.it(s[i..]);
		if (s.Eq(i, "Symbol")) return CiComplProvider.Symbol;
		if (s.Eq(i, "Keyword")) return CiComplProvider.Keyword;
		if (s.Eq(i, "Cref")) return CiComplProvider.Cref;
		if (s.Eq(i, "XmlDoc")) return CiComplProvider.XmlDoc;
		if (s.Eq(i, "EmbeddedLanguage")) return CiComplProvider.Regex;
		if (s.Eq(i, "Override")) return CiComplProvider.Override;
		//if (s.Eq(i, "ExternAlias")) return CiComplProvider.ExternAlias;
		//if (s.Eq(i, "ObjectAndWith")) return CiComplProvider.ObjectAndWithInitializer;
		//if (s.Eq(i, "AttributeNamedParameter")) return CiComplProvider.AttributeNamedParameter; //don't use because can be mixed with other symbols
		return CiComplProvider.Other;
	}

	static OptionSet s_options;
	static OptionSet _Options(Workspace ws) {
		if (s_options == null) {
			s_options = ws.Options;

			//Disable option TriggerInArgumentLists (show completions when typed '(' or '[' or space in argument list). Then eg typing a number selects something containing it in the list (it seems in VS not).
			s_options = s_options.WithChangedOption(new OptionKey(CompletionOptions.TriggerInArgumentLists, "C#"), false);

			//print.it(s_options.GetOption(new OptionKey(CompletionOptions.BlockForCompletionItems2, "C#")));
			//s_options = s_options.WithChangedOption(new OptionKey(CompletionOptions.BlockForCompletionItems, "C#"), false); //?
			//s_options = s_options.WithChangedOption(new OptionKey(CompletionOptions.BlockForCompletionItems2, "C#"), false); //?

			//s_options = s_options.WithChangedOption(new OptionKey(CompletionOptions.ShowItemsFromUnimportedNamespaces, "C#"), true); //does not work automatically

			//foreach(var v in CompletionOptions.GetDev15CompletionOptions()) {
			//	print.it(v);
			//}
		}
		return s_options;
	}
}

//Debug_.NoGcRegion can be used to prevent GC while getting completions. Reduces the time eg from 70 to 60 ms. Tested with some old Roslyn version.
//It seems Roslyn uses weak references for expensive objects, eg syntax trees and semantic model.
//Usually GC starts in the middle, and Roslyn has to rebuild them anyway.
//But Roslyn is slow in other places too, eg syntax highlighting. Need to test everything. Maybe disable GC on every added char for eg 500 ms.
//SHOULDDO: try to keep syntax and semantic trees in CodeInfo.Context.
