﻿using System;
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

class CiComplItem : ITreeViewItem
{
	public CompletionItem ci;
	public readonly CiItemKind kind;
	public readonly CiItemAccess access;
	readonly CiComplProvider _provider;
	public CiComplItemHiddenBy hidden;
	public CiComplItemMoveDownBy moveDown;
	public ulong hilite; //bits for max 64 characters
	public int group;
	public int commentOffset;
	string _dtext;
	object _symbols; //ISymbol or List<ISymbol> or IReadonlyList<ISymbol> or null

	public CiComplItem(CiComplProvider provider, CompletionItem ci) {
		_provider = provider;
		_symbols = ci.Symbols;
		this.ci = ci;
		CiUtil.TagsToKindAndAccess(ci.Tags, out kind, out access);
		//ci.DebugPrint();
	}

	public CiComplItem(CiComplProvider provider, TextSpan span, ISymbol sym, CiItemKind kind, string name = null) {
		_provider = provider;
		_symbols = sym;
		this.kind = kind;
		bool gen = sym switch { INamedTypeSymbol nt => nt.IsGenericType, IMethodSymbol ms => ms.IsGenericMethod, _ => false };
		ci = CompletionItem.Create(name ?? sym.Name, displayTextSuffix: gen ? "<>" : null);
	}

	public CiComplItem(CiComplProvider provider, TextSpan span, string name, CiItemKind kind, CiItemAccess access = default) {
		_provider = provider;
		this.kind = kind;
		this.access = access;
		ci = CompletionItem.Create(name);
		ci.Span = span;
	}

	internal void AddOverload(ISymbol sym) {
		switch (_symbols) {
		case ISymbol s:
			_symbols = new List<ISymbol> { s, sym };
			break;
		case List<ISymbol> a:
			a.Add(sym);
			break;
		default:
			Debug.Assert(false);
			break;
		}
	}

	public IEnumerable<ISymbol> Symbols {
		get {
			if (_symbols is ISymbol sym) _symbols = new ISymbol[1] { sym };
			return _symbols as IEnumerable<ISymbol>;
		}
	}

	public ISymbol FirstSymbol => _symbols switch { ISymbol sym => sym, IEnumerable<ISymbol> en => en.FirstOrDefault(), _ => null };

	/// <summary>
	/// Gets displayed text without prefix, suffix (eg geric) and green comments (group or inline description).
	/// In most cases it is simple name, but in some cases can be eg "Namespace.Name", "Name(parameters)", etc.
	/// </summary>
	public string Text => ci.DisplayText;

	public CiComplProvider Provider => _provider;

	#region ITreeViewItem
	string ITreeViewItem.DisplayText => _dtext;

	string ITreeViewItem.ImageSource => ImageResource(kind);

	#endregion

	public void SetDisplayText(string comment) {
		var desc = ci.InlineDescription; if (desc.NE()) desc = comment;
		bool isComment = !desc.NE();
		if (_dtext != null && !isComment && commentOffset == 0) return;
		_dtext = this.Text + ci.DisplayTextSuffix + (isComment ? "    //" : null) + desc;
		if (!ci.DisplayTextPrefix.NE()) {
			_dtext = ci.DisplayTextPrefix + _dtext;
			Debug_.PrintIf(ci.DisplayTextPrefix != "(", $"{_dtext}, {ci.DisplayTextPrefix}"); //seen only of casts, eg "(" + "int" + ")"
		}
		commentOffset = isComment ? _dtext.Length - desc.Length - 6 : 0;
	}

	public static string ImageResource(CiItemKind kind) => kind switch {
		CiItemKind.Class => "resources/ci/class.xaml",
		CiItemKind.Constant => "resources/ci/constant.xaml",
		CiItemKind.Delegate => "resources/ci/delegate.xaml",
		CiItemKind.Enum => "resources/ci/enum.xaml",
		CiItemKind.EnumMember => "resources/ci/enummember.xaml",
		CiItemKind.Event => "resources/ci/event.xaml",
		CiItemKind.ExtensionMethod => "resources/ci/extensionmethod.xaml",
		CiItemKind.Field => "resources/ci/field.xaml",
		CiItemKind.Interface => "resources/ci/interface.xaml",
		CiItemKind.Keyword => "resources/ci/keyword.xaml",
		CiItemKind.Label => "resources/ci/label.xaml",
		CiItemKind.LocalVariable => "resources/ci/localvariable.xaml",
		CiItemKind.Method => "resources/ci/method.xaml",
		CiItemKind.Namespace => "resources/ci/namespace.xaml",
		CiItemKind.Operator => "resources/ci/operator.xaml",
		CiItemKind.Property => "resources/ci/property.xaml",
		CiItemKind.Snippet => "resources/ci/snippet.xaml",
		CiItemKind.Structure => "resources/ci/structure.xaml",
		CiItemKind.TypeParameter => "resources/ci/typeparameter.xaml",
		_ => null
	};

	public string AccessImageSource => AccessImageResource(access);

	public static string AccessImageResource(CiItemAccess access) => access switch {
		CiItemAccess.Private => "resources/ci/overlayprivate.xaml",
		CiItemAccess.Protected => "resources/ci/overlayprotected.xaml",
		CiItemAccess.Internal => "resources/ci/overlayinternal.xaml",
		_ => null
	};

	public string ModifierImageSource => _ModifierImageResource(this);

	static string _ModifierImageResource(CiComplItem ci) {
		var sym = ci.FirstSymbol;
		if (sym != null) {
			if (sym.IsStatic && ci.kind is not (CiItemKind.Constant or CiItemKind.EnumMember or CiItemKind.Namespace)) return "resources/ci/overlaystatic.xaml";
			if (ci.kind == CiItemKind.Class && sym.IsAbstract) return "resources/ci/overlayabstract.xaml";
		} else {
			//if (ci.Provider == CiComplProvider.Winapi && ci.kind == CiItemKind.Method) return "resources/ci/overlaystatic.xaml"; //no
		}
		return null;
	}
}

enum CiComplProvider : byte
{
	Other,
	Symbol,
	Keyword,
	Cref,
	XmlDoc,
	Regex,
	Override,
	//ExternAlias,
	//ObjectAndWithInitializer,
	//AttributeNamedParameter,

	//ours
	Snippet,
	Favorite,
	Winapi,
}

enum CiComplResult
{
	/// <summary>
	/// No completion.
	/// </summary>
	None,

	/// <summary>
	/// Inserted text displayed in the popup list. Now caret is after it.
	/// </summary>
	Simple,

	/// <summary>
	/// Inserted more text than displayed in the popup list, eg "(" or "{  }" or override. Now caret probably is somewhere in middle of it. Also if regex.
	/// Only if ch == ' ', '\n' (Enter) or default (Tab).
	/// </summary>
	Complex,
}

[Flags]
enum CiComplItemHiddenBy : byte { FilterText = 1, Kind = 2, Always = 4 }

[Flags]
enum CiComplItemMoveDownBy : sbyte { Name = 1, Obsolete = 2, FilterText = 4 }

//don't reorder! Must match CiUtil.ItemKindNames. In this order are displayed group buttons in the completion popup. See also code in CiWinapi.cs: " WHERE kind<=" + (int)CiItemKind.Delegate;.
enum CiItemKind : sbyte { Class, Structure, Enum, Delegate, Interface, Method, ExtensionMethod, Property, Operator, Event, Field, LocalVariable, Constant, EnumMember, Namespace, Keyword, Label, Snippet, TypeParameter, None }

//don't reorder!
enum CiItemAccess : sbyte { Public, Private, Protected, Internal }

class CiNamespaceSymbolEqualityComparer : IEqualityComparer<INamespaceSymbol>
{
	public bool Equals(INamespaceSymbol x, INamespaceSymbol y) {
		for (; ; ) {
			if (x.MetadataName != y.MetadataName) return false;
			x = x.ContainingNamespace;
			y = y.ContainingNamespace;
			if (x == null) return y == null;
			if (y == null) return false;
		}
	}

	public int GetHashCode(INamespaceSymbol obj) {
		for (int r = obj.MetadataName.GetHashCode(); ;) {
			obj = obj.ContainingNamespace;
			if (obj == null) return r;
			r ^= obj.MetadataName.GetHashCode();
		}
	}
}

class CiNamespaceOrTypeSymbolEqualityComparer : IEqualityComparer<INamespaceOrTypeSymbol>
{
	public bool Equals(INamespaceOrTypeSymbol x, INamespaceOrTypeSymbol y) {
		for (; ; ) {
			if (x.MetadataName != y.MetadataName) return false;
			if ((x is INamespaceSymbol) != (y is INamespaceSymbol)) return false;
			x = x.ContainingSymbol as INamespaceOrTypeSymbol;
			y = y.ContainingSymbol as INamespaceOrTypeSymbol;
			if (x == null) return y == null;
			if (y == null) return false;
		}
	}

	public int GetHashCode(INamespaceOrTypeSymbol obj) {
		for (int r = obj.MetadataName.GetHashCode(); ;) {
			obj = obj.ContainingSymbol as INamespaceOrTypeSymbol;
			if (obj == null) return r;
			r ^= obj.MetadataName.GetHashCode();
		}
	}
}

struct CiStringRange
{
	public readonly string code;
	public readonly int start, end;
	public readonly bool verbatim;

	public CiStringRange(string code, int start, int end, bool verbatim) {
		this.code = code; this.start = start; this.end = end; this.verbatim = verbatim;
	}

	public override string ToString() {
		var s = code[start..end];
		if (!verbatim) s.Unescape(out s);
		return s;
	}

	public int Length => end - start;
}
