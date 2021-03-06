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
//using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Au.Types;
using Au.Controls;
using Au.More;

//FUTURE: add UI to format code 'w = w.Get.Right();' etc.
//FUTURE: init from code string.
//SHOULDDO: try to find and select control in current tree when captured from same window. Like in Delm.

namespace Au.Tools
{
	class Dwnd : KDialogWindow
	{
		wnd _wnd, _con;
		bool _uncheckControl;
		string _wndName;

		KSciInfoBox _info, _winInfo;
		Button _bTest, _bOK;
		Label _speed;
		KCheckBox _cCapture, _cControl;
		Separator _sepControl;
		ScrollViewer _scroller;
		KSciCodeBoxWnd _code;
		KTreeView _tree;

		Grid _gCon1, _gCon2;
		KCheckTextBox nameW, classW, programW, containsW, idC, nameC, classC, alsoW, alsoC, waitW, skipC;
		KCheckBox cHiddenTooW, cCloakedTooW, cHiddenTooC, cException;

		public Dwnd(wnd w = default, bool uncheckControl = false) {
			Title = "Find window or control";

			var b = new wpfBuilder(this).WinSize((600, 440..), (600, 460..)).Columns(-1, 0);
			b.R.Add(out _info).Height(60);
			b.R.StartOkCancel()
				.AddButton(out _bTest, "_Test", _bTest_Click).Width(70..).Tooltip("Executes the code now.\nShows rectangle of the found window/control.\nIgnores options: wait, Exception.")
				.Add(out _speed).Width(110).Tooltip("Search time (window + control). Red if not found.")
				.AddOkCancel(out _bOK, out _, out _)
				.End().Align("L")
				.Add(out _cCapture, "_Capture").Align(y: "C").Tooltip("Enables hotkeys F3 and Ctrl+F3. Shows window/control rectangles when moving the mouse.");
			_bTest.IsEnabled = false;
			_bOK.IsEnabled = false;
			b.OkApply += _bOK_Click;

			//window and control properties and search settings
			b.R.AddSeparator(false).Margin("T3 B0");
			b.Row(0); //auto height, else adds v scrollbar when textbox height changes when a textbox text is multiline or too long (with h scrollbar)
			_scroller = b.xStartPropertyGrid("L2 T3 R2 B1"); //actually never shows scrollbar because of row auto height, but sets some options etc
			_scroller.Visibility = Visibility.Hidden;
			b.Columns(-3, 0, -1);
			//window
			b.R.Add<TextBlock>("Window").Margin("T1 B3").xSetHeaderProp(); //rejected: vertical headers. Tested, looks not good, too small for vertical Control checkbox.
			b.Row(0).StartGrid().Columns(70, -1);
			nameW = b.xAddCheckText("name");
			classW = b.xAddCheckText("class");
			programW = b.xAddCheckTextDropdown("program");
			containsW = b.xAddCheckTextDropdown("contains");
			b.End();
			b.xAddSplitterV(span: 4, thickness: 12);
			b.StartGrid().Columns(44, -1);
			cHiddenTooW = b.xAddCheck("Find hidden too");
			cCloakedTooW = b.xAddCheck("Find cloaked too");
			alsoW = b.xAddCheckText("also", "o => true");
			waitW = b.xAddCheckText("wait", "5");
			b.End();
			//control
			b.R.AddSeparator(false).Margin("T4 B0"); _sepControl = b.Last as Separator;
			b.R.Add(out _cControl, "Control").Margin("T5 B3").xSetHeaderProp();
			b.Row(0).StartGrid().Columns(70, -1); _gCon1 = b.Panel as Grid;
			idC = b.xAddCheckText("id");
			nameC = b.xAddCheckTextDropdown("name");
			classC = b.xAddCheckText("class");
			b.End().Skip();
			b.StartGrid().Columns(44, -1); _gCon2 = b.Panel as Grid;
			cHiddenTooC = b.xAddCheck("Find hidden too");
			alsoC = b.xAddCheckText("also", "o => true");
			skipC = b.xAddCheckText("skip", "1");
			b.End();

			b.R.AddSeparator(false).Margin("T3 B3");
			cException = b.xAddCheck("Exception if not found"); b.Checked();
			b.xEndPropertyGrid();

			//code
			b.Row(64).xAddInBorder(out _code, "B");

			//tree and window info
			b.xAddSplitterH(span: -1);
			b.Row(-1).StartGrid().Columns(-1, 0, -1);
			b.Row(-1).xAddInBorder(out _tree, "TR");
			b.xAddSplitterV();
			b.xAddInBorder(out _winInfo, "TL"); _winInfo.ZWrapLines = false; _winInfo.Name = "window_info";
			b.End();

			b.End();

			_InitTree();

			_con = w;
			_uncheckControl = uncheckControl;

			WndSavedRect.Restore(this, App.Settings.tools_Dwnd_wndPos, o => App.Settings.tools_Dwnd_wndPos = o);
		}

		static Dwnd() {
			TUtil.OnAnyCheckTextBoxValueChanged<Dwnd>((d, o) => d._AnyCheckTextBoxValueChanged(o));
		}

		//public static void Dialog(wnd w = default, bool uncheckControl = false) {
		//	new Dwnd(w, uncheckControl).Show();
		//}

		protected override void OnSourceInitialized(EventArgs e) {
			base.OnSourceInitialized(e);

			if (!_con.Is0) _SetWnd(false);
			_InitInfo();
			_cCapture.IsChecked = true;
		}

		protected override void OnClosing(CancelEventArgs e) {
			_cCapture.IsChecked = false;

			base.OnClosing(e);
		}

		void _SetWnd(bool captured) {
			_bTest.IsEnabled = true; _bOK.IsEnabled = true;

			var wndOld = _wnd;
			_wnd = _con.Window;
			if (_wnd == _con) _con = default;
			bool newWindow = _wnd != wndOld;

			_ClearTree();
			if (!_FillProperties(newWindow)) return;
			_FormatCode(false, newWindow);
			_FillTree();
		}

		bool _FillProperties(bool newWindow) {
			bool isCon = !_con.Is0;

			_WinInfo f = default;

			if (!_GetClassName(_wnd, out f.wClass)) return false; //note: get even if !newWindow, to detect closed window
			if (isCon && !_GetClassName(_con, out f.cClass)) return false;

			bool _GetClassName(wnd w, out string cn) {
				cn = w.ClassName; if (cn != null) return true;
				_winInfo.zText = "Failed to get " + (w == _wnd ? "window" : "control") + " properties: \r\n" + lastError.message;
				_scroller.Visibility = Visibility.Hidden;
				return false;
			}

			_noeventValueChanged = true;

			var wndName = _wnd.NameTL_;
			if (newWindow) {
				nameW.Set(true, TUtil.EscapeWindowName(wndName, true));
				classW.Set(true, TUtil.StripWndClassName(f.wClass, true));
				f.wProg = _wnd.ProgramName;
				var ap = new List<string> { f.wProg, "WOwner.Process(processId)", "WOwner.Thread(threadId)" }; if (!_wnd.OwnerWindow.Is0) ap.Add("WOwner.Window(ow)");
				programW.Set(wndName.NE(), f.wProg, ap);
				containsW.Set(false, null, _ContainsCombo_DropDown);
			} else if (wndName != _wndName) {
				if (TUtil.ShouldChangeTextBoxWildex(nameW.t.Text, wndName))
					nameW.Set(true, TUtil.EscapeWindowName(wndName, true));
			}
			f.wName = _wndName = wndName;

			if (isCon) {
				//name combo
				f.cName = _con.Name;
				int iSel = f.cName.NE() ? -1 : 0;
				var an = new List<string> { TUtil.EscapeWildex(f.cName) };
				_ConNameAdd("***wfName ", f.cWF = _con.NameWinforms);
				/*bool isElm =*/
				_ConNameAdd("***elmName ", f.cElm = _con.NameElm);
				//bool isLabel = _ConNameAdd("***label ", f.cLabel = _con.NameLabel);
				//if(isElm && isLabel && iSel == an.Count - 2 && f.cElm == f.cLabel) iSel++; //if label == elmName, prefer label
				if (iSel < 0) iSel = 0; //never select text, even if all others unavailable
				_ConNameAdd("***text ", f.cText = _con.ControlText);
				bool _ConNameAdd(string prefix, string value) {
					if (value.NE()) return false;
					if (iSel < 0) iSel = an.Count;
					an.Add(prefix + TUtil.EscapeWildex(value));
					return true;
				}

				bool idUseful = TUtil.GetUsefulControlId(_con, _wnd, out f.cId);
				idC.Visible = idUseful;
				if (idUseful) idC.Set(true, f.cId.ToS()); else an.Add("***id " + f.cId + " (probably not useful)");
				nameC.Set(!idUseful, an[iSel], an);
				classC.Set(!idUseful, TUtil.StripWndClassName(f.cClass, true));
			}

			bool checkControl = isCon && !_uncheckControl;
			_uncheckControl = false;
			_cControl.IsChecked = checkControl;
			_ShowControlProperties(showGrid: checkControl, showAll: isCon);

			_noeventValueChanged = false;

			_scroller.Visibility = Visibility.Visible;
			_FillWindowInfo(f);
			return true;

			List<string> _ContainsCombo_DropDown() {
				try {
					var a1 = new List<string>();
					//child
					foreach (var c in _wnd.Get.Children(onlyVisible: true)) {
						var cn = c.Name; if (cn.NE()) continue;
						cn = "c '" + TUtil.StripWndClassName(c.ClassName, true) + "' " + TUtil.EscapeWildex(cn);
						if (!a1.Contains(cn)) a1.Add(cn);
					}
					//elm
					var a2 = new List<string>();
					var a3 = elm.findAll(_wnd, name: "?*", prop: "notin=SCROLLBAR\0maxcc=100", flags: EFFlags.ClientArea); //all that have a name
					string prevName = null;
					for (int i = a3.Length; --i >= 0;) {
						if (!a3[i].GetProperties("Rn", out var prop)) continue;
						if (prop.Name == prevName && prop.Role == "WINDOW") continue; prevName = prop.Name; //skip parent WINDOW
						string rn = "e '" + prop.Role + "' " + TUtil.EscapeWildex(prop.Name);
						if (!a2.Contains(rn)) a2.Add(rn);
					}
					a2.Reverse();
					a1.AddRange(a2);

					return a1;
					//rejected: sort
				}
				catch (Exception ex) { Debug_.Print(ex); return null; }
			}
		}

		//when checked/unchecked any checkbox, and when text changed of any textbox
		void _AnyCheckTextBoxValueChanged(object source) {
			if (source == _cCapture) {
				_cCapture_CheckedChanged();
			} else if (!_noeventValueChanged) {
				_noeventValueChanged = true;
				if (source is KCheckBox c) {
					bool on = c.IsChecked;
					if (c == _cControl) {
						_ShowControlProperties(on, null);
					} else if (c == nameC.c) {
						if (on) idC.c.IsChecked = false;
					} else if (c == idC.c) {
						if (on) nameC.c.IsChecked = false;
					}
				} else if (source is TextBox t && t.Tag is KCheckTextBox k) {
					_noeventValueChanged = _formattedOnValueChanged = false; //allow auto-check but prevent formatting twice
					k.CheckIfTextNotEmpty();
					if (_formattedOnValueChanged) return;
				}
				_noeventValueChanged = false;
				_formattedOnValueChanged = true;
				_FormatCode();
			}
		}
		bool _noeventValueChanged = true, _formattedOnValueChanged;

		void _ShowControlProperties(bool showGrid, bool? showAll) {
			if (showAll != null) {
				var vis = showAll.Value ? Visibility.Visible : Visibility.Hidden;
				_cControl.Visibility = vis;
				_sepControl.Visibility = vis;
				_gCon1.Visibility = vis;
				_gCon2.Visibility = vis;
			}
			_gCon1.IsEnabled = showGrid;
			_gCon2.IsEnabled = showGrid;
		}

#if true
		(string code, string wndVar) _FormatCode(bool forTest = false, bool newWindow = false) {
			if (!_scroller.IsVisible) return default; //failed to get window props

			var f = new TUtil.WindowFindCodeFormatter {
				Test = forTest,
				NeedControl = !_con.Is0 && _cControl.IsChecked,
				Throw = cException.IsChecked,
				hiddenTooW = cHiddenTooW.IsChecked,
				cloakedTooW = cCloakedTooW.IsChecked,
				hiddenTooC = cHiddenTooC.IsChecked,
			};

			nameW.GetText(out f.nameW, emptyToo: true);
			classW.GetText(out f.classW);
			programW.GetText(out f.programW);
			alsoW.GetText(out f.alsoW);
			containsW.GetText(out f.containsW);
			if(!forTest) waitW.GetText(out f.waitW, emptyToo: true);

			if (f.NeedControl) {
				if (!idC.GetText(out f.idC)) nameC.GetText(out f.nameC, emptyToo: true);
				classC.GetText(out f.classC);
				alsoC.GetText(out f.alsoC);
				skipC.GetText(out f.skipC);
				f.nameC_comments = nameC.t.Text;
				f.classC_comments = classC.t.Text;
			}

			var R = f.Format();

			if (!forTest) {
				_code.ZSetText(R);
			}

			return (R, "w");
		}
#else
		(string code, string wndVar) _FormatCode(bool forTest = false, bool newWindow = false) {
			if (!_scroller.IsVisible) return default; //failed to get window props

			var b = new StringBuilder();

			bool isCon = !_con.Is0 && _cControl.IsChecked;
			bool orThrow = !forTest && cException.IsChecked;

			b.Append(forTest ? "wnd w;\r\n" : "var ").Append("w = ");

			string waitTime = null;
			bool isWait = !forTest && waitW.GetText(out waitTime, emptyToo: true);
			if (isWait) {
				b.Append("wnd.wait(").AppendWaitTime(waitTime, orThrow || isCon);
				b.Append(", active: true");
			} else {
				if (orThrow || isCon) b.Append('+');
				b.Append("wnd.find(");
			}

			int m = 0;
			string sName, sClass, sAlso;
			nameW.GetText(out sName, emptyToo: true); b.AppendStringArg(sName, noComma: !isWait);
			if (classW.GetText(out sClass)) m |= 1;
			if (programW.GetText(out var sProg)) m |= 2;
			if (m != 0) b.AppendStringArg(sClass);
			if (0 != (m & 2)) {
				if (!sProg.Starts("WOwner.")) b.AppendStringArg(sProg);
				else if (!forTest) b.AppendOtherArg(sProg);
				else m &= ~2;
			}
			b.AppendFlags(m < 2 ? "flags" : null, nameof(WFlags), (cHiddenTooW, nameof(WFlags.HiddenToo)), (cCloakedTooW, nameof(WFlags.CloakedToo)));
			if (alsoW.GetText(out sAlso)) b.AppendOtherArg(sAlso, "also");
			if (containsW.GetText(out var sContains)) b.AppendStringArg(sContains, "contains");

			if (isCon) {
				b.AppendLine(");");
				m = 0;
				if (idC.GetText(out var sId)) m |= 1;
				else if (nameC.GetText(out sName, emptyToo: true)) m |= 2;
				if (classC.GetText(out sClass)) m |= 4;
				if (alsoC.GetText(out sAlso)) m |= 8;
				if (skipC.GetText(out var sSkip)) m |= 16;
				if (!forTest) b.Append("var c = ");
				if (orThrow) b.Append('+');
				b.Append("w.Child");
				if (m == 1) {
					b.Append("ById(").Append(sId);
					b.AppendFlags(null, nameof(WCFlags), (cHiddenTooC, nameof(WCFlags.HiddenToo)));
				} else {
					b.Append('(');
					if (0 != (m & 1)) b.Append("\"***id ").Append(sId).Append('\"');
					else b.AppendStringArg(sName, noComma: true);
					if (0 != (m & 4)) b.AppendStringArg(sClass);
					b.AppendFlags((0 == (m & 4)) ? "null" : null, nameof(WCFlags), (cHiddenTooC, nameof(WCFlags.HiddenToo)));
					if (0 != (m & 8)) b.AppendOtherArg(sAlso, "also");
					if (0 != (m & 16)) b.AppendOtherArg(sSkip, "skip");
				}
			}

			b.Append(");");

			if (!forTest && isCon && 0 == (m & 2)) { //add comments controlClass controlName
				sName = sClass = null;
				if (0 == (m & 2)) sName = nameC.t.Text;
				if (0 == (m & 4)) sClass = classC.t.Text;
				m = 0; if (!sName.NE()) m |= 1; if (!sClass.NE()) m |= 2;
				if (m != 0) {
					b.Append(" // ");
					if (0 != (m & 2)) b.Append(sClass);
					if (0 != (m & 1)) {
						if (0 != (m & 2)) b.Append(' ');
						sName = sName.Limit(100).RegexReplace(@"^\*\*\*\w+ (.+)", "$1");
						b.AppendStringArg(sName, noComma: true);
					}
				}
			}

			if (!orThrow && !forTest) b.AppendLine().Append("if(").Append(isCon ? "c" : "w").Append(".Is0) { print.it(\"not found\"); }");

			var R = b.ToString();

			if (!forTest) {
				_code.ZSetText(R);
			}

			return (R, "w");
		}
#endif

#region capture

		TUtil.CaptureWindowEtcWithHotkey _capt;

		void _cCapture_CheckedChanged() {
			_capt ??= new TUtil.CaptureWindowEtcWithHotkey(_cCapture, _Capture, () => wnd.fromMouse().Rect);
			_capt.Capturing = _cCapture.IsChecked;
		}

		void _Capture() {
			var c = wnd.fromMouse(); if (c.Is0) return;
			_con = c;
			_uncheckControl = false;
			_SetWnd(true);
			var w = this.Hwnd();
			if (w.IsMinimized) {
				w.ShowNotMinMax();
				w.ActivateL();
			}
		}

#endregion

#region OK, Test

		/// <summary>
		/// When OK clicked, the top-level window (even when <see cref="ZResultUseControl"/> is true).
		/// </summary>
		public wnd ZResultWindow => _wnd;

		/// <summary>
		/// When OK clicked, the control (even when <see cref="ZResultUseControl"/> is false) or default(wnd).
		/// </summary>
		public wnd ZResultControl => _con;

		/// <summary>
		/// When OK clicked, true if a control was selected and the 'Control' checkbox checked.
		/// Use <see cref="ZResultWindow"/> or <see cref="ZResultControl"/>, depending on this property.
		/// </summary>
		public bool ZResultUseControl { get; private set; }

		/// <summary>
		/// When OK clicked, contains C# code. Else null.
		/// </summary>
		public string ZResultCode { get; private set; }

		/// <summary>
		/// Don't insert code on OK.
		/// See also <see cref="ZResultCode"/>.
		/// </summary>
		public bool ZDontInsertCodeOnOK { get; set; }

		//rejected. Can use Closed event; then ZResultCode not null if OK.
		///// <summary>
		///// When dialog closed with OK button.
		///// </summary>
		//public event Action OK;

		private void _bOK_Click(WBButtonClickArgs e) {
			ZResultCode = _code.zText;
			if (ZResultCode.NE()) { ZResultCode = null; e.Cancel = true; return; }
			ZResultUseControl = !_con.Is0 && _cControl.IsChecked;

			if (!ZDontInsertCodeOnOK) InsertCode.Statements(ZResultCode);

			//OK?.Invoke();
		}

		private void _bTest_Click(WBButtonClickArgs ea) {
			var (code, wndVar) = _FormatCode(true); if (code == null) return;
			TUtil.RunTestFindObject(code, wndVar, _wnd, _bTest, _speed, o => {
				var w = (wnd)o;
				var r = w.Rect;
				if (w.IsMaximized && !w.IsChild) {
					var k = w.Screen.Rect; k.Inflate(-2, -2);
					r.Intersect(k);
				}
				return r;
			});
		}

#endregion

#region tree

		void _InitTree() {
			_tree.SingleClickActivate = true;
			_tree.ItemActivated += (_, e) => {
				var x = e.Item as _TreeItem;
				_con = x.c == _wnd ? default : x.c;
				if (!_FillProperties(false)) return;
				_FormatCode();
				if (!_con.Is0) TUtil.ShowOsdRect(_con.Rect);
			};
		}

		void _ClearTree() {
			_tree.SetItems(null, false);
		}

		void _FillTree() {
			_TreeItem xWindow = new() { c = _wnd }, xSelect = _con.Is0 ? xWindow : null;
			_AddChildren(_wnd, xWindow);
			void _AddChildren(wnd wParent, _TreeItem nParent) {
				for (wnd t = wParent.Get.FirstChild; !t.Is0; t = t.Get.Next()) {
					var x = new _TreeItem() { c = t };
					nParent.AddChild(x);
					if (t == _con && xSelect == null) xSelect = x;
					_AddChildren(t, x);
				}
			}

			_tree.SetItems(new _TreeItem[1] { xWindow }, false);

			//print.it(xWindow.Descendants().Select(o=>o.c));

			if (xSelect != null) {
				_tree.EnsureVisible(xSelect);
				_tree.SelectSingle(xSelect, true);
			}
		}

		class _TreeItem : TreeBase<_TreeItem>, ITreeViewItem
		{
			public wnd c;
			string _displayText;
			bool _isExpanded;
			bool _isFailed;

#region ITreeViewItem

			string ITreeViewItem.DisplayText {
				get {
					if (_displayText == null) {
						var cn = c.ClassName;
						if (cn == null) {
							_isFailed = true;
							return _displayText = "Failed: " + lastError.message;
						}

						var name = c.Name;
						if (name.NE()) _displayText = cn;
						else {
							using (new StringBuilder_(out var b)) {
								name = name.Escape(limit: 250);
								b.Append(cn).Append("  \"").Append(name).Append('\"');
								_displayText = b.ToString();
							}
						}
					}
					return _displayText;
				}
			}

			void ITreeViewItem.SetIsExpanded(bool yes) { _isExpanded = yes; }

			bool ITreeViewItem.IsExpanded => _isExpanded;

			IEnumerable<ITreeViewItem> ITreeViewItem.Items => base.Children();

			bool ITreeViewItem.IsFolder => _IsFolder;
			bool _IsFolder => base.HasChildren;

			string ITreeViewItem.ImageSource => _isExpanded ? @"resources/images/expanddown_16x.xaml" : (_IsFolder ? @"resources/images/expandright_16x.xaml" : null);

			int ITreeViewItem.TextColor => _isFailed ? 0xff : (c.IsVisible ? Api.GetSysColor(Api.COLOR_WINDOWTEXT) : Api.GetSysColor(Api.COLOR_GRAYTEXT));

#endregion
		}

#endregion

#region info

		struct _WinInfo
		{
			public string wClass, wName, wProg, cClass, cName, cText, /*cLabel,*/ cElm, cWF;
			public int cId;

			public string Format(wnd w, wnd c, int wcp) {
				var b = new StringBuilder();

				if (wcp == 2 && c.Is0) wcp = 1;
				if (!w.IsAlive) return "";
				if (wcp == 1) { //window
					b.AppendLine("<Z #B0E0B0><b>Window<>    <+switch 2>Control<>    <+switch 3>Process<><>");
					if (wClass == null) {
						wClass = w.ClassName;
						wName = w.Name;
					}
					_Common(false, b, w, wName, wClass);
				} else if (wcp == 2) { //control
					b.AppendLine("<Z #B0E0B0><+switch 1>Window<>    <b>Control<>    <+switch 3>Process<><>");
					if (c.IsAlive) {
						if (cClass == null) {
							cClass = c.ClassName;
							cName = c.Name;
							cText = c.ControlText;
							//cLabel = c.NameLabel;
							cElm = c.NameElm;
							cWF = c.NameWinforms;
							cId = c.ControlId;
						}
						_Common(true, b, c, cName, cClass);
					}
				} else { //program
					b.AppendLine("<Z #B0E0B0><+switch 1>Window<>    <+switch 2>Control<>    <b>Process<><>");
					g1:
					if (wProg == null) {
						wProg = w.ProgramName;
					}
					b.Append("<i>ProgramName<>:    ").AppendLine(wProg);
					b.Append("<i>ProgramPath<>:    ").AppendLine(w.ProgramPath);
					b.Append("<i>ProgramDescription<>:    ").AppendLine(w.ProgramDescription);
					int pid = w.ProcessId, tid = w.ThreadId;
					b.Append("<i>ProcessId<>:    ").AppendLine(pid.ToString());
					b.Append("<i>ThreadId<>:    ").AppendLine(tid.ToString());
					b.Append("<i>Is32Bit<>:    ").AppendLine(w.Is32Bit ? "true" : "false");
					using (var uac = uacInfo.ofProcess(pid)) {
						b.Append("<i><help articles/UAC>UAC<> IL, elevation<>:    ")
							.Append(uac.IntegrityLevel.ToString())
							.Append(", ").AppendLine(uac.Elevation.ToString());
					}

					//if control's process or thread is different...
					if (!c.Is0) {
						int pid2 = c.ProcessId;
						if (pid2 != pid && pid2 != 0) {
							b.AppendLine("\r\n<c red>Control is in other process:<>");
							w = c; wProg = null;
							goto g1;
						}
						int tid2 = c.ThreadId;
						if (tid2 != tid && tid2 != 0) {
							b.AppendLine("\r\n<c red>Control is in other thread:<>");
							b.Append("<i>ThreadId<>:    ").AppendLine(tid2.ToString());
						}
					}
				}

				return b.ToString();
			}

			void _Common(bool isCon, StringBuilder b, wnd w, string name, string className) {
				string s, sh = w.Handle.ToString();
				b.Append("<i>Handle<>:    ").AppendLine(sh);
				b.Append("<i>ClassName<>:    ").AppendLine(className);
				if (!isCon || !name.NE()) b.Append("<i>Name<>:    ").AppendLine(name);
				if (isCon) {
					//if(!cLabel.NE()) b.Append("<i>NameLabel<>:    ").AppendLine(cLabel);
					if (!cElm.NE()) b.Append("<i>NameElm<>:    ").AppendLine(cElm);
					if (!cWF.NE()) b.Append("<i>NameWinForms<>:    ").AppendLine(cWF);
					if (!cText.NE()) b.Append("<i>ControlText<>:    ").Append("<\a>").Append(cText.Escape(10000, true)).AppendLine("</\a>");
					b.Append("<i>ControlId<>:    ").AppendLine(cId.ToString());
					b.AppendFormat("<+rect {0}><i>RectInWindow<><>:    ", sh).AppendLine(w.RectInWindow.ToString());
				} else {
					var wo = w.OwnerWindow;
					if (!wo.Is0) b.AppendFormat("<+rect {0}><i>Owner<><>:    ", wo.Handle.ToString()).AppendLine(wo.ToString());
					b.AppendFormat("<+rect {0}><i>Rect<><>:    ", sh).AppendLine(w.Rect.ToString());
				}
				b.AppendFormat("<+rect {0} 1><i>ClientRect<><>:    ", sh).AppendLine(w.ClientRect.ToString());
				var style = w.Style;
				s = (style & (WS)0xffff0000).ToString();
				if (isCon) s = s.Replace("MINIMIZEBOX", "GROUP").Replace("MAXIMIZEBOX", "TABSTOP");
				uint style2 = ((uint)style) & 0xffff; //unknown styles of that class
				b.Append("<i>Style<>:  0x").Append(((uint)style).ToString("X8")).Append(" (").Append(s);
				if (style2 != 0) b.Append(", 0x").Append(style2.ToString("X4"));
				b.AppendLine(")");
				var estyle = w.ExStyle;
				b.Append("<i>ExStyle<>:  0x").Append(((uint)estyle).ToString("X8")).Append(" (").Append(estyle.ToString()).AppendLine(")");
				//b.Append("<i>Class style<>:  0x").AppendLine(((uint)WndUtil.GetClassLong(w, GCL.STYLE)).ToString("X8"));
				if (!isCon) {
					b.Append("<i>Is...<>:    ");
					_AppendIs(w.IsPopupWindow, "IsPopupWindow");
					_AppendIs(w.IsToolWindow, "IsToolWindow");
					_AppendIs(w.IsTopmost, "IsTopmost");
					_AppendIs(w.IsFullScreen, "IsFullScreen");
					_AppendIs(0 != w.IsUwpApp, "IsUwpApp");
					_AppendIs(w.IsWindows8MetroStyle, "IsWindows8MetroStyle");
					b.AppendLine();
				}
				b.Append("<i>Prop[\"...\"]<>:    "); bool isProp = false;
				foreach (var p in w.Prop.GetList()) {
					if (p.Key.Starts('#')) continue;
					if (!isProp) isProp = true; else b.Append(", ");
					b.Append(p.Key).Append(" = ").Append(p.Value.ToString());
				}
				b.AppendLine();

				void _AppendIs(bool yes, string prop) {
					if (b[^1] != ' ') b.Append(", ");
					if (!yes) b.Append('!');
					b.Append(prop);
				}
			}
		}

		void _FillWindowInfo(in _WinInfo f) {
			if (_wiWCP == 0) {
				_wiWCP = 1;
				_winInfo.ZTags.AddLinkTag("+switch", s => {
					_wiWCP = s.ToInt();
					_SetText(default);
				});
				_winInfo.ZTags.AddLinkTag("+rect", s => {
					var w = (wnd)s.ToInt(0, out int e);
					int client = s.ToInt(e);
					var r = client == 1 ? w.ClientRectInScreen : w.Rect;
					TUtil.ShowOsdRect(r, limitToScreen: w.IsMaximized);
				});
			}
			_SetText(f);

			void _SetText(in _WinInfo wi) {
				var s1 = wi.Format(_wnd, _con, _wiWCP);
				_winInfo.zText = s1.TrimEnd("\r\n");
			}
		}
		int _wiWCP; //0 not inited, 1 window, 2 control, 3 program

		TUtil.CommonInfos _commonInfos;
		void _InitInfo() {
			_commonInfos = new TUtil.CommonInfos(_info);

			_info.zText = c_dialogInfo;
			_info.AddElem(this, c_dialogInfo);

			_info.InfoCT(nameW, "Window name.", true);
			_info.InfoCT(classW, "Window class name.", true);
			_info.InfoCT(programW, "Program.", true);
			_info.InfoCT(containsW,
@"A UI element in the window. Format: e 'role' name.
Or a control in the window. Format: c 'class' text.", true, "name/class/text");
			_info.InfoCT(idC, "Control id.");
			_info.InfoCT(nameC, "Control name.", true);
			_info.InfoCT(classC, "Control class name.", true);

			_info.InfoC(cHiddenTooW, "Flag <help>Au.Types.WFlags<>.HiddenToo.");
			_info.InfoC(cCloakedTooW, "Flag <help>Au.Types.WFlags<>.CloakedToo.");
			_info.InfoCT(waitW,
@"Wait timeout, seconds.
If unchecked, does not wait. Else if 0 or empty, waits infinitely. Else waits max this time interval; on timeout returns default(wnd) or throws exception, depending on the 'Exception...' checkbox.");
			_info.InfoCT(alsoW,
@"<help>wnd.find<> <i>also<> lambda.
Can be multiline.");
			_info.InfoC(cHiddenTooC, "Flag <help>Au.Types.WCFlags<>.HiddenToo.");
			_info.InfoCT(alsoC,
@"<help>wnd.Child<> <i>also<> lambda.
Can be multiline.");
			_info.InfoCT(skipC,
@"0-based index of matching control.
For example, if 1, gets the second matching control.");
			_info.InfoC(cException,
@"Throw exception if not found.
If unchecked, returns default(wnd).");

			_info.Info(_tree, "Tree view", "All child and descendant controls of the window.");

			//SHOULDDO: now no info for HwndHost
			//			_Info(_code, "Code",
			//@"Created code to find the window or control.
			//Some parts can be edited directly.
			//");
			//			_Info(_winInfo, "Window info",
			//@"Various properties of the selected window, control and process.
			//For example can be useful when creating <i>also<> function.
			//");
		}

		const string c_dialogInfo =
@"This dialog creates code to <help wnd.find>find window<> or <help wnd.Child>control<>.
1. Move the mouse to a window or control. Press key <b>F3<> or <b>Ctrl+F3<>.
2. Click the Test button. It finds and shows the window/control and the search time.
3. If need, check/uncheck/edit some fields or select another window/control; click Test.
4. Click OK, it inserts C# code in editor. Or copy/paste.
5. In editor add code to use the window/control. If need, rename variables, delete duplicate wnd.find lines, replace part of window name with *, etc.

If F3 does not work when the target window is active, probably its process is admin and this process isn't. Ctrl+F3 should still work, but may fail to get some properties.";

#endregion
	}
}
