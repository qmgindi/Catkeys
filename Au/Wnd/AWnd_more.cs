﻿using Au.Types;
using Au.Util;
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

namespace Au
{
	public partial struct AWnd
	{
		/// <summary>
		/// Miscellaneous window-related functions and classes. Rarely used, or useful only for programmers.
		/// </summary>
		public static partial class More
		{
			//public void ShowAnimate(bool show)
			//{
			//	//Don't add AWnd function, because:
			//		//Rarely used.
			//		//Api.AnimateWindow() works only with windows of current thread.
			//		//Only programmers would need it, and they can call the API directly.
			//}

			/// <summary>
			/// Registers new window class in this process.
			/// </summary>
			/// <param name="className">Class name.</param>
			/// <param name="wndProc">
			/// Delegate of a window procedure. See <msdn>Window Procedures</msdn>.
			/// 
			/// Use null when you need a different delegate (method or target object) for each window instance; create windows with <see cref="CreateWindow(Native.WNDPROC, bool, string, string, WS, WS2, int, int, int, int, AWnd, LPARAM, IntPtr, LPARAM)"/> or <see cref="CreateMessageOnlyWindow(Native.WNDPROC, string)"/>.
			/// If not null, it must be a static method; create windows with any other function, including API <msdn>CreateWindowEx</msdn>.
			/// </param>
			/// <param name="etc">
			/// Can be used to specify API <msdn>WNDCLASSEX</msdn> fields.
			/// To set cursor use field <b>mCursor</b> (standard cursor) or <b>hCursor</b> (native handle of a custom cursor).
			/// If null, this function sets arrow cursor and style CS_VREDRAW | CS_HREDRAW.
			/// </param>
			/// <exception cref="ArgumentException"><i>wndProc</i> is an instance method. Must be static method or null. If need instance method, use null here and pass <i>wndProc</i> to <see cref="CreateWindow"/>.</exception>
			/// <exception cref="InvalidOperationException">The class already registered with this function and different <i>wndProc</i> (another method or another target object).</exception>
			/// <exception cref="Win32Exception">Failed, for example if the class already exists and was registered not with this function.</exception>
			/// <remarks>
			/// Calls API <msdn>RegisterClassEx</msdn>.
			/// The window class is registered until this process ends. Don't need to unregister.
			/// If called next time for the same window class, does nothing if <i>wndProc</i> is equal to the previous (or both null). Then ignores <i>etc</i>. Throws exception if different.
			/// Thread-safe.
			/// Protects the <i>wndProc</i> delegate from GC.
			/// </remarks>
			public static unsafe void RegisterWindowClass(string className, Native.WNDPROC wndProc = null, RWCEtc etc = null) {
				if (wndProc?.Target != null) throw new ArgumentException("wndProc must be static method or null. Use non-static wndProc with CreateWindow.");

				lock (s_classes) {
					if (s_classes.TryGetValue(className, out var wpPrev)) {
						if (wpPrev != wndProc) throw new InvalidOperationException("Window class already registered"); //another method or another target object
						return;
					}
					var x = new Api.WNDCLASSEX(etc);

					fixed (char* pCN = className) {
						x.lpszClassName = pCN;
						if (wndProc != null) {
							x.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc);
						} else {
							x.lpfnWndProc = s_cwProcFP;
						}
						x.style |= Api.CS_GLOBALCLASS;

						if (0 == Api.RegisterClassEx(x)) throw new Win32Exception();
						//note: we don't return atom because: 1. Rarely used. 2. If assigned to an unused field, compiler may remove the function call.

						s_classes.Add(className, wndProc);
					}
				}
			}

			internal static bool IsClassRegistered_(string name, out Native.WNDPROC wndProc) {
				lock (s_classes) {
					return s_classes.TryGetValue(name, out wndProc);
				}
			}

			static Dictionary<string, Native.WNDPROC> s_classes = new(StringComparer.OrdinalIgnoreCase); //allows to find registered classes and protects their wndProc delegates from GC
			[ThreadStatic] static Dictionary<AWnd, Native.WNDPROC> t_windows; //allows to dispatch messages and protects wndProc delegates of windows created in this thread from GC

			static LPARAM _CWProc(AWnd w, int msg, LPARAM wParam, LPARAM lParam) {
				//PrintMsg(w, msg, wParam, lParam);
				if (t_cwUnsafe) {
					t_cwUnsafe = false;
					var wndProc = t_cwProc;
					t_cwProc = null;
					Api.SetWindowLongPtr(w, Native.GWL.WNDPROC, Marshal.GetFunctionPointerForDelegate(wndProc));
					//AOutput.Write("subclassed", w);
					return wndProc(w, msg, wParam, lParam);
				} else {
					var a = t_windows;
					if (a == null || !a.TryGetValue(w, out var wndProc)) {
						wndProc = t_cwProc;
						if (wndProc == null) {
							//AOutput.Write("DefWindowProc", w);
							return Api.DefWindowProc(w, msg, wParam, lParam); //creating not with our CreateWindow(wndProc, ...)
						}
						a[w] = wndProc;
						//AOutput.Write("added", a.Count, w);
						t_cwProc = null;
					}

					var R = wndProc(w, msg, wParam, lParam);

					if (msg == Api.WM_NCDESTROY) {
						a.Remove(w);
						//AOutput.Write("removed", a.Count, w);
					}

					return R;
				}
			}
			static Native.WNDPROC s_cwProc; //GC
			static IntPtr s_cwProcFP = Marshal.GetFunctionPointerForDelegate(s_cwProc = _CWProc);
			[ThreadStatic] static Native.WNDPROC t_cwProc;
			[ThreadStatic] static bool t_cwUnsafe;

			/// <summary>
			/// Creates native/unmanaged window (API <msdn>CreateWindowEx</msdn>) and sets its window procedure.
			/// </summary>
			/// <param name="wndProc">Window procedure.</param>
			/// <param name="keepAlive">
			/// Protect <i>wndProc</i> from GC (garbage collector) until the window is destroyed (message <msdn>WM_NCDESTROY</msdn> recived or thread ended).
			/// <note type="important">In some cases it may prevent destroying the window until thread ends, and it can be a big memory leak. For example WPF then does not destroy HwndHost-ed controls. Then let <i>keepAlive</i>=false and manually manage <i>wndProc</i> lifetime, for example keep it as a field of the wrapper class.</note>
			/// </param>
			/// <exception cref="AuException">Failed to create window. Unlikely.</exception>
			/// <remarks>
			/// If the class was registered with <see cref="RegisterWindowClass"/> with null <i>wndProc</i>, the <i>wndProc</i> function will receive all messages. Else will not receive messages sent before <b>CreateWindowEx</b> returns (WM_CREATE etc).
			/// 
			/// To destroy the window can be used any function, including API <msdn>DestroyWindow</msdn>, <see cref="DestroyWindow"/>, <see cref="Close"/>, API <msdn>WM_CLOSE</msdn>.
			/// </remarks>
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
			public static AWnd CreateWindow(Native.WNDPROC wndProc, bool keepAlive, string className, string name = null, WS style = 0, WS2 exStyle = 0, int x = 0, int y = 0, int width = 0, int height = 0, AWnd parent = default, LPARAM controlId = default, IntPtr hInstance = default, LPARAM param = default) {
#pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
				if (wndProc is null || className is null) throw new ArgumentNullException();

				t_windows ??= new();
				AWnd w;
				if (IsClassRegistered_(className, out var wp) && wp == null) {
					//if keepAlive, need to cubclass the new window, else add hwnd+wndProc to t_windows.
					//	But not after CreateWindowEx, because wndProc must receive all messages.
					//	Let _CWProc do it on first message.
					t_cwProc = wndProc;
					t_cwUnsafe = !keepAlive;
					try { w = Api.CreateWindowEx(exStyle, className, name, style, x, y, width, height, parent, controlId, hInstance, param); }
					finally { t_cwProc = null; t_cwUnsafe = false; } //if CreateWindowEx failed and _CWProc not called
					if (w.Is0) throw new AuException(0);
				} else {
					w = Api.CreateWindowEx(exStyle, className, name, style, x, y, width, height, parent, controlId, hInstance, param);
					if (w.Is0) throw new AuException(0);
					if (keepAlive) {
						t_windows[w] = wndProc;
						Api.SetWindowLongPtr(w, Native.GWL.WNDPROC, s_cwProcFP);
					} else {
						Api.SetWindowLongPtr(w, Native.GWL.WNDPROC, Marshal.GetFunctionPointerForDelegate(wndProc));
					}
				}

				return w;
			}

			/// <summary>
			/// Creates native/unmanaged window.
			/// </summary>
			/// <exception cref="AuException">Failed to create window. Unlikely.</exception>
			/// <remarks>
			/// Calls API <msdn>CreateWindowEx</msdn>.
			/// To destroy the window can be used any function, including API <msdn>DestroyWindow</msdn>, <see cref="DestroyWindow"/>, <see cref="Close"/>, API <msdn>WM_CLOSE</msdn>.
			/// </remarks>
			/// <seealso cref="RegisterWindowClass"/>
			public static AWnd CreateWindow(string className, string name = null, WS style = 0, WS2 exStyle = 0, int x = 0, int y = 0, int width = 0, int height = 0, AWnd parent = default, LPARAM controlId = default, IntPtr hInstance = default, LPARAM param = default) {
				var w = Api.CreateWindowEx(exStyle, className, name, style, x, y, width, height, parent, controlId, hInstance, param);
				if (w.Is0) throw new AuException(0);
				return w;
			}

			/// <summary>
			/// Creates native/unmanaged <msdn>message-only window</msdn>.
			/// </summary>
			/// <param name="className">Window class name. Can be any existing class.</param>
			/// <exception cref="AuException">Failed to create window. Unlikely.</exception>
			/// <remarks>
			/// Styles: WS_POPUP, WS_EX_NOACTIVATE.
			/// To destroy the window can be used any function, including API <msdn>DestroyWindow</msdn>, <see cref="DestroyWindow"/>, <see cref="Close"/>, API <msdn>WM_CLOSE</msdn>.
			/// </remarks>
			public static AWnd CreateMessageOnlyWindow(string className) {
				return CreateWindow(className, null, WS.POPUP, WS2.NOACTIVATE, parent: Native.HWND.MESSAGE);
				//note: WS_EX_NOACTIVATE is important.
			}

			/// <summary>
			/// Creates native/unmanaged <msdn>message-only window</msdn> and sets its window procedure.
			/// </summary>
			/// <param name="className">Window class name.</param>
			/// <param name="wndProc"></param>
			/// <exception cref="AuException">Failed to create window. Unlikely.</exception>
			/// <remarks>
			/// Styles: WS_POPUP, WS_EX_NOACTIVATE.
			/// Calls <see cref="CreateWindow(Native.WNDPROC, bool, string, string, WS, WS2, int, int, int, int, AWnd, LPARAM, IntPtr, LPARAM)"/> with <i>keepAlive</i>=true.
			/// </remarks>
			public static AWnd CreateMessageOnlyWindow(Native.WNDPROC wndProc, string className) {
				return CreateWindow(wndProc, true, className, null, WS.POPUP, WS2.NOACTIVATE, parent: Native.HWND.MESSAGE);
				//note: WS_EX_NOACTIVATE is important.
			}

			/// <summary>
			/// Destroys a native window of this thread.
			/// Calls API <msdn>DestroyWindow</msdn>.
			/// Returns false if failed. Supports <see cref="ALastError"/>.
			/// </summary>
			/// <seealso cref="Close"/>
			public static bool DestroyWindow(AWnd w) {
				return Api.DestroyWindow(w);
			}

			/// <summary>
			/// Sets font.
			/// </summary>
			/// <param name="w"></param>
			/// <param name="font">
			/// Native font handle.
			/// If default(IntPtr), sets font that is used by most windows and controls on this computer, usually Segoe UI, 9, DPI-scaled for w screen.
			/// </param>
			/// <remarks>
			/// Sends <msdn>WM_SETFONT</msdn> message.
			/// </remarks>
			public static void SetFont(AWnd w, IntPtr font = default) {
				w.Send(Api.WM_SETFONT, font != default ? font : NativeFont_.RegularCached(ADpi.OfWindow(w)).Handle);
			}

			/// <summary>
			/// Gets window Windows Store app user model id, like "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App".
			/// Returns null if fails. Returns null if called on Windows 7 unless <i>getExePathIfNotWinStoreApp</i> true.
			/// </summary>
			/// <param name="w"></param>
			/// <param name="prependShellAppsFolder">Prepend <c>@"shell:AppsFolder\"</c> (to run or get icon).</param>
			/// <param name="getExePathIfNotWinStoreApp">Get program path if it is not a Windows Store app.</param>
			/// <remarks>
			/// Windows Store app window class name can be "Windows.UI.Core.CoreWindow" or "ApplicationFrameWindow".
			/// </remarks>
			public static string GetWindowsStoreAppId(AWnd w, bool prependShellAppsFolder = false, bool getExePathIfNotWinStoreApp = false) {
				if (0 != Internal_.GetWindowsStoreAppId(w, out var R, prependShellAppsFolder, getExePathIfNotWinStoreApp)) return R;
				return null;
			}

			//rejected. Rarely used. Easy to send message.
			///// <summary>
			///// Sets native font handle.
			///// Sends message API <msdn>WM_SETFONT</msdn> with lParam 1.
			///// Does not copy the font; don't dispose it while the window is alive.
			///// Use this function only with windows of current process.
			///// </summary>
			//public static void SetFontHandle(AWnd w, IntPtr fontHandle)
			//{
			//	w.Send(Api.WM_SETFONT, fontHandle, 1);
			//}

			///// <summary>
			///// Gets native font handle.
			///// Sends message API <msdn>WM_GETFONT</msdn>.
			///// Does not copy the font; don't need to dispose.
			///// Use this function only with windows of current process.
			///// </summary>
			//public static IntPtr GetFontHandle(AWnd w)
			//{
			//	return w.Send(Api.WM_GETFONT);
			//}

			///// <summary>
			///// Sets native icon handle.
			///// Sends message API <msdn>WM_SETICON</msdn>.
			///// Does not copy the icon; don't dispose it while the window is alive.
			///// Use this function only with windows of current process.
			///// </summary>
			///// <seealso cref="AIcon"/>
			//public static void SetIconHandle(AWnd w, IntPtr iconHandle, bool size32 = false)
			//{
			//	w.Send(Api.WM_SETICON, size32, iconHandle);
			//}

			/// <summary>
			/// Calls API <msdn>GetClassLongPtr</msdn>.
			/// </summary>
			/// <remarks>
			/// Supports <see cref="ALastError"/>.
			/// For index can be used constants from <see cref="Native.GCL"/>. All values are the same in 32-bit and 64-bit process.
			/// In 32-bit process actually calls <b>GetClassLong</b>, because <b>GetClassLongPtr</b> is unavailable.
			/// </remarks>
			public static LPARAM GetClassLong(AWnd w, int index) => Api.GetClassLongPtr(w, index);

			//probably not useful. Dangerous.
			///// <summary>
			///// Calls API <msdn>SetClassLongPtr</msdn> (SetClassLong in 32-bit process).
			///// </summary>
			///// <exception cref="AuWndException"/>
			//public static LPARAM SetClassLong(AWnd w, int index, LPARAM newValue)
			//{
			//	ALastError.Clear();
			//	LPARAM R = Api.SetClassLongPtr(w, index, newValue);
			//	if(R == 0 && ALastError.Code != 0) w.ThrowUseNative();
			//	return R;
			//}

			//Rejected. Does not work with many windows. Unreliable. Rarely used.
			///// <summary>
			///// Gets atom of a window class.
			///// To get class atom when you have a window w, use <c>AWnd.More.GetClassLong(w, Native.GCL.ATOM)</c>.
			///// </summary>
			///// <param name="className">Class name.</param>
			///// <param name="moduleHandle">Native module handle of the exe or dll that registered the class. Don't use if it is a global class (CS_GLOBALCLASS style).</param>
			//public static ushort GetClassAtom(string className, IntPtr moduleHandle = default)
			//{
			//	var x = new Api.WNDCLASSEX();
			//	x.cbSize = Api.SizeOf(x);
			//	return Api.GetClassInfoEx(moduleHandle, className, ref x);
			//}

			/// <summary>
			/// Calls API <msdn>RegisterWindowMessage</msdn>.
			/// </summary>
			/// <param name="name">Message name. Can be any unique string.</param>
			/// <param name="uacEnable">Also call API <msdn>ChangeWindowMessageFilter</msdn> for the message. More info: <see cref="UacEnableMessages"/>.</param>
			public static int RegisterMessage(string name, bool uacEnable = false) {
				var m = Api.RegisterWindowMessage(name);
				if (uacEnable && m != 0) Api.ChangeWindowMessageFilter(m, 1);
				return m;
			}

			/// <summary>
			/// Calls API <msdn>ChangeWindowMessageFilter</msdn> for each message in the list of messages.
			/// It allows processes of lower [](xref:uac) integrity level to send these messages to this process.
			/// </summary>
			public static void UacEnableMessages(params int[] messages) {
				foreach (var m in messages) Api.ChangeWindowMessageFilter(m, 1);
			}

			#region print msg

			/// <summary>
			/// Writes a Windows message to a string.
			/// If the message is specified in <i>options</i>, sets <c>s=null</c> and returns false.
			/// </summary>
			public static bool PrintMsg(out string s, AWnd w, int msg, LPARAM wParam, LPARAM lParam, PrintMsgOptions options = null, [CallerMemberName] string caller = null) {
				//Could instead use System.Windows.Forms.Message.ToString, but its list is incomplete, eg no dpichange messages.
				//	https://referencesource.microsoft.com/#System.Windows.Forms/winforms/Managed/System/WinForms/MessageDecoder.cs,b19021e2f4480d57

				if (options?.Skip is int[] a) {
					s = null;
					int prev = 0;
					foreach (var v in a) {
						if (v < 0) {
							if (msg >= prev && msg <= (v == int.MinValue ? int.MaxValue : -v)) return false;
							prev = int.MaxValue;
						} else {
							if (v == msg) return false;
							prev = v;
						}
					}
				}

				var (name, plus) = _Name(ref msg, out bool reflect);

				using (new StringBuilder_(out var b)) {
					if (options?.Number ?? true) {
						//uint counter = (uint)w.Prop["PrintMsg"]; w.Prop.Set("PrintMsg", ++counter);
						//b.Append(counter).Append(". ");
						b.Append(++s_pm_counter).Append(". ");
					}

					if (options?.Indent ?? true) { //makes ~10 times slower, but not too slow
						int i = 0;
						MethodBase m0 = null;
						foreach (var f in new StackTrace(1).GetFrames()) {
							var m1 = f.GetMethod();
							if (m1.Name != caller) continue;
							if (m0 == null) m0 = m1; else if ((object)m1 == m0) i += 4;
						}
						if (i > 0) b.Append(' ', i);
					}

					if (reflect) b.Append("WM_REFLECT+");
					if (name == null) b.AppendFormat("0x{0:X}", msg);
					else if (plus != 0) b.AppendFormat("{0}+0x{1:X}", name, plus);
					else if (msg >= 0xc000 && msg <= 0xffff) b.AppendFormat("\"{0}\"", name);
					else b.Append(name);

					b.AppendFormat(", 0x{0:X8}, 0x{1:X8}, hwnd={2}", (int)wParam, (int)lParam, w.Handle);
					if (options?.WindowProperties ?? false) {
						if (!w.Is0) b.AppendFormat(" ({0} \"{1}\" {{{2}}})", w.ClassName?.Limit(30), w.Name?.Limit(30), w.Rect.ToStringSimple());
					}

					s = b.ToString();
					return true;
				}

				static (string name, int plus) _Name(ref int m, out bool reflect) {
					reflect = false;
					if (m >= 0x10000) return default; //reserved by the system
					if (m >= 0xC000) return (AClipboard.GetFormatName_(m, orNull: true), 0); //registered
					if (m >= Api.WM_APP) return ("WM_APP", m - Api.WM_APP); //0x8000
					if (reflect = m >= Api.WM_REFLECT && m < Api.WM_REFLECT * 2) m -= Api.WM_REFLECT; //0x2000
					if (m >= Api.WM_USER) return ("WM_USER", m - Api.WM_USER); //0x400
					#region switch
					var s = m switch {
						0x0 => "WM_NULL",
						0x1 => "WM_CREATE",
						0x2 => "WM_DESTROY",
						0x3 => "WM_MOVE",
						0x5 => "WM_SIZE",
						0x6 => "WM_ACTIVATE",
						0x7 => "WM_SETFOCUS",
						0x8 => "WM_KILLFOCUS",
						0xA => "WM_ENABLE",
						0xB => "WM_SETREDRAW",
						0xC => "WM_SETTEXT",
						0xD => "WM_GETTEXT",
						0xE => "WM_GETTEXTLENGTH",
						0xF => "WM_PAINT",
						0x10 => "WM_CLOSE",
						0x11 => "WM_QUERYENDSESSION",
						0x13 => "WM_QUERYOPEN",
						0x16 => "WM_ENDSESSION",
						0x12 => "WM_QUIT",
						0x14 => "WM_ERASEBKGND",
						0x15 => "WM_SYSCOLORCHANGE",
						0x18 => "WM_SHOWWINDOW",
						0x1A => "WM_SETTINGCHANGE",
						0x1B => "WM_DEVMODECHANGE",
						0x1C => "WM_ACTIVATEAPP",
						0x1D => "WM_FONTCHANGE",
						0x1E => "WM_TIMECHANGE",
						0x1F => "WM_CANCELMODE",
						0x20 => "WM_SETCURSOR",
						0x21 => "WM_MOUSEACTIVATE",
						0x22 => "WM_CHILDACTIVATE",
						0x23 => "WM_QUEUESYNC",
						0x24 => "WM_GETMINMAXINFO",
						0x26 => "WM_PAINTICON",
						0x27 => "WM_ICONERASEBKGND",
						0x28 => "WM_NEXTDLGCTL",
						0x2A => "WM_SPOOLERSTATUS",
						0x2B => "WM_DRAWITEM",
						0x2C => "WM_MEASUREITEM",
						0x2D => "WM_DELETEITEM",
						0x2E => "WM_VKEYTOITEM",
						0x2F => "WM_CHARTOITEM",
						0x30 => "WM_SETFONT",
						0x31 => "WM_GETFONT",
						0x32 => "WM_SETHOTKEY",
						0x33 => "WM_GETHOTKEY",
						0x37 => "WM_QUERYDRAGICON",
						0x39 => "WM_COMPAREITEM",
						0x3D => "WM_GETOBJECT",
						0x41 => "WM_COMPACTING",
						0x44 => "WM_COMMNOTIFY",
						0x46 => "WM_WINDOWPOSCHANGING",
						0x47 => "WM_WINDOWPOSCHANGED",
						0x48 => "WM_POWER",
						0x4A => "WM_COPYDATA",
						0x4B => "WM_CANCELJOURNAL",
						0x4E => "WM_NOTIFY",
						0x50 => "WM_INPUTLANGCHANGEREQUEST",
						0x51 => "WM_INPUTLANGCHANGE",
						0x52 => "WM_TCARD",
						0x53 => "WM_HELP",
						0x54 => "WM_USERCHANGED",
						0x55 => "WM_NOTIFYFORMAT",
						0x7B => "WM_CONTEXTMENU",
						0x7C => "WM_STYLECHANGING",
						0x7D => "WM_STYLECHANGED",
						0x7E => "WM_DISPLAYCHANGE",
						0x7F => "WM_GETICON",
						0x80 => "WM_SETICON",
						0x81 => "WM_NCCREATE",
						0x82 => "WM_NCDESTROY",
						0x83 => "WM_NCCALCSIZE",
						0x84 => "WM_NCHITTEST",
						0x85 => "WM_NCPAINT",
						0x86 => "WM_NCACTIVATE",
						0x87 => "WM_GETDLGCODE",
						0x88 => "WM_SYNCPAINT",
						0xA0 => "WM_NCMOUSEMOVE",
						0xA1 => "WM_NCLBUTTONDOWN",
						0xA2 => "WM_NCLBUTTONUP",
						0xA3 => "WM_NCLBUTTONDBLCLK",
						0xA4 => "WM_NCRBUTTONDOWN",
						0xA5 => "WM_NCRBUTTONUP",
						0xA6 => "WM_NCRBUTTONDBLCLK",
						0xA7 => "WM_NCMBUTTONDOWN",
						0xA8 => "WM_NCMBUTTONUP",
						0xA9 => "WM_NCMBUTTONDBLCLK",
						0xAB => "WM_NCXBUTTONDOWN",
						0xAC => "WM_NCXBUTTONUP",
						0xAD => "WM_NCXBUTTONDBLCLK",
						0xFE => "WM_INPUT_DEVICE_CHANGE",
						0xFF => "WM_INPUT",
						0x100 => "WM_KEYDOWN",
						0x101 => "WM_KEYUP",
						0x102 => "WM_CHAR",
						0x103 => "WM_DEADCHAR",
						0x104 => "WM_SYSKEYDOWN",
						0x105 => "WM_SYSKEYUP",
						0x106 => "WM_SYSCHAR",
						0x107 => "WM_SYSDEADCHAR",
						0x109 => "WM_UNICHAR",
						0x10D => "WM_IME_STARTCOMPOSITION",
						0x10E => "WM_IME_ENDCOMPOSITION",
						0x10F => "WM_IME_COMPOSITION",
						0x110 => "WM_INITDIALOG",
						0x111 => "WM_COMMAND",
						0x112 => "WM_SYSCOMMAND",
						0x113 => "WM_TIMER",
						0x114 => "WM_HSCROLL",
						0x115 => "WM_VSCROLL",
						0x116 => "WM_INITMENU",
						0x117 => "WM_INITMENUPOPUP",
						0x119 => "WM_GESTURE",
						0x11A => "WM_GESTURENOTIFY",
						0x11F => "WM_MENUSELECT",
						0x120 => "WM_MENUCHAR",
						0x121 => "WM_ENTERIDLE",
						0x122 => "WM_MENURBUTTONUP",
						0x123 => "WM_MENUDRAG",
						0x124 => "WM_MENUGETOBJECT",
						0x125 => "WM_UNINITMENUPOPUP",
						0x126 => "WM_MENUCOMMAND",
						0x127 => "WM_CHANGEUISTATE",
						0x128 => "WM_UPDATEUISTATE",
						0x129 => "WM_QUERYUISTATE",
						0x132 => "WM_CTLCOLORMSGBOX",
						0x133 => "WM_CTLCOLOREDIT",
						0x134 => "WM_CTLCOLORLISTBOX",
						0x135 => "WM_CTLCOLORBTN",
						0x136 => "WM_CTLCOLORDLG",
						0x137 => "WM_CTLCOLORSCROLLBAR",
						0x138 => "WM_CTLCOLORSTATIC",
						0x200 => "WM_MOUSEMOVE",
						0x201 => "WM_LBUTTONDOWN",
						0x202 => "WM_LBUTTONUP",
						0x203 => "WM_LBUTTONDBLCLK",
						0x204 => "WM_RBUTTONDOWN",
						0x205 => "WM_RBUTTONUP",
						0x206 => "WM_RBUTTONDBLCLK",
						0x207 => "WM_MBUTTONDOWN",
						0x208 => "WM_MBUTTONUP",
						0x209 => "WM_MBUTTONDBLCLK",
						0x20A => "WM_MOUSEWHEEL",
						0x20B => "WM_XBUTTONDOWN",
						0x20C => "WM_XBUTTONUP",
						0x20D => "WM_XBUTTONDBLCLK",
						0x20E => "WM_MOUSEHWHEEL",
						0x210 => "WM_PARENTNOTIFY",
						0x211 => "WM_ENTERMENULOOP",
						0x212 => "WM_EXITMENULOOP",
						0x213 => "WM_NEXTMENU",
						0x214 => "WM_SIZING",
						0x215 => "WM_CAPTURECHANGED",
						0x216 => "WM_MOVING",
						0x218 => "WM_POWERBROADCAST",
						0x219 => "WM_DEVICECHANGE",
						0x220 => "WM_MDICREATE",
						0x221 => "WM_MDIDESTROY",
						0x222 => "WM_MDIACTIVATE",
						0x223 => "WM_MDIRESTORE",
						0x224 => "WM_MDINEXT",
						0x225 => "WM_MDIMAXIMIZE",
						0x226 => "WM_MDITILE",
						0x227 => "WM_MDICASCADE",
						0x228 => "WM_MDIICONARRANGE",
						0x229 => "WM_MDIGETACTIVE",
						0x230 => "WM_MDISETMENU",
						0x231 => "WM_ENTERSIZEMOVE",
						0x232 => "WM_EXITSIZEMOVE",
						0x233 => "WM_DROPFILES",
						0x234 => "WM_MDIREFRESHMENU",
						0x238 => "WM_POINTERDEVICECHANGE",
						0x239 => "WM_POINTERDEVICEINRANGE",
						0x23A => "WM_POINTERDEVICEOUTOFRANGE",
						0x240 => "WM_TOUCH",
						0x241 => "WM_NCPOINTERUPDATE",
						0x242 => "WM_NCPOINTERDOWN",
						0x243 => "WM_NCPOINTERUP",
						0x245 => "WM_POINTERUPDATE",
						0x246 => "WM_POINTERDOWN",
						0x247 => "WM_POINTERUP",
						0x249 => "WM_POINTERENTER",
						0x24A => "WM_POINTERLEAVE",
						0x24B => "WM_POINTERACTIVATE",
						0x24C => "WM_POINTERCAPTURECHANGED",
						0x24D => "WM_TOUCHHITTESTING",
						0x24E => "WM_POINTERWHEEL",
						0x24F => "WM_POINTERHWHEEL",
						0x251 => "WM_POINTERROUTEDTO",
						0x252 => "WM_POINTERROUTEDAWAY",
						0x253 => "WM_POINTERROUTEDRELEASED",
						0x281 => "WM_IME_SETCONTEXT",
						0x282 => "WM_IME_NOTIFY",
						0x283 => "WM_IME_CONTROL",
						0x284 => "WM_IME_COMPOSITIONFULL",
						0x285 => "WM_IME_SELECT",
						0x286 => "WM_IME_CHAR",
						0x288 => "WM_IME_REQUEST",
						0x290 => "WM_IME_KEYDOWN",
						0x291 => "WM_IME_KEYUP",
						0x2A1 => "WM_MOUSEHOVER",
						0x2A3 => "WM_MOUSELEAVE",
						0x2A0 => "WM_NCMOUSEHOVER",
						0x2A2 => "WM_NCMOUSELEAVE",
						0x2B1 => "WM_WTSSESSION_CHANGE",
						0x2E0 => "WM_DPICHANGED",
						0x2E2 => "WM_DPICHANGED_BEFOREPARENT",
						0x2E3 => "WM_DPICHANGED_AFTERPARENT",
						0x2E4 => "WM_GETDPISCALEDSIZE",
						0x300 => "WM_CUT",
						0x301 => "WM_COPY",
						0x302 => "WM_PASTE",
						0x303 => "WM_CLEAR",
						0x304 => "WM_UNDO",
						0x305 => "WM_RENDERFORMAT",
						0x306 => "WM_RENDERALLFORMATS",
						0x307 => "WM_DESTROYCLIPBOARD",
						0x308 => "WM_DRAWCLIPBOARD",
						0x309 => "WM_PAINTCLIPBOARD",
						0x30A => "WM_VSCROLLCLIPBOARD",
						0x30B => "WM_SIZECLIPBOARD",
						0x30C => "WM_ASKCBFORMATNAME",
						0x30D => "WM_CHANGECBCHAIN",
						0x30E => "WM_HSCROLLCLIPBOARD",
						0x30F => "WM_QUERYNEWPALETTE",
						0x310 => "WM_PALETTEISCHANGING",
						0x311 => "WM_PALETTECHANGED",
						0x312 => "WM_HOTKEY",
						0x317 => "WM_PRINT",
						0x318 => "WM_PRINTCLIENT",
						0x319 => "WM_APPCOMMAND",
						0x31A => "WM_THEMECHANGED",
						0x31D => "WM_CLIPBOARDUPDATE",
						0x31E => "WM_DWMCOMPOSITIONCHANGED",
						0x31F => "WM_DWMNCRENDERINGCHANGED",
						0x320 => "WM_DWMCOLORIZATIONCOLORCHANGED",
						0x321 => "WM_DWMWINDOWMAXIMIZEDCHANGE",
						0x323 => "WM_DWMSENDICONICTHUMBNAIL",
						0x326 => "WM_DWMSENDICONICLIVEPREVIEWBITMAP",
						0x33F => "WM_GETTITLEBARINFOEX",
						0x8000 => "WM_APP",
						0x400 => "WM_USER",
						_ => null
					};
					#endregion
					return (s, 0);
				}
			}
			[ThreadStatic] static uint s_pm_counter;

#if !true //this script creates the switch { ... }
void _WmDeclTextToCode() {
//	var a=new List<string>();
	var b = new StringBuilder("var s = m switch {\r\n");
	var s1=File.ReadAllText(@"?:\?\au\other\api\api.cs");
	foreach (var m in s1.RegexFindAll(@"(?m)^internal const uint (WM_\w+) = (\w+);")) {
		var s=m[1].Value;
		if(s.Ends("FIRST") || s.Ends("LAST") || s.Starts("WM_PSD_") || s.Starts("WM_DDE_") || s.Starts("WM_CHOOSEFONT_") || s=="WM_WININICHANGE") {
//			AOutput.Write(s);
			continue;
		}
//		AOutput.Write(s, m[2]);
//		a.Add(s);
		b.AppendFormat("{0} => \"{1}\",\r\n", m[2].Value, s);
	}
	b.Append("_ => null};\r\nreturn (s, 0);");
//	a.Sort();
//	AOutput.Write(a);
	var s2 = b.ToString();
	AOutput.Write(s2);
}
#endif

			/// <summary>
			/// Writes a Windows message to the output, unless it is specified in <i>options</i>.
			/// </summary>
			public static void PrintMsg(AWnd w, int msg, LPARAM wParam, LPARAM lParam, PrintMsgOptions options = null, [CallerMemberName] string caller = null) {
				if (PrintMsg(out string s, w, msg, wParam, lParam, options, caller)) AOutput.Write(s);
			}

			/// <summary>
			/// Writes a Windows message to a string.
			/// If the message is specified in <i>options</i>, sets <c>s=null</c> and returns false.
			/// </summary>
			/// <remarks>
			/// The <i>m</i> parameter also accepts <b>System.Windows.Interop.MSG</b> (WPF) and <b>System.Windows.Forms.Message</b>.
			/// </remarks>
			public static bool PrintMsg(out string s, in Native.MSG m, PrintMsgOptions options = null, [CallerMemberName] string caller = null) {
				return PrintMsg(out s, m.hwnd, m.message, m.wParam, m.lParam, options, caller);
			}

			/// <summary>
			/// Writes a Windows message to the output, unless it is specified in <i>options</i>.
			/// </summary>
			/// <remarks>
			/// The <i>m</i> parameter also accepts <b>System.Windows.Interop.MSG</b> (WPF) and <b>System.Windows.Forms.Message</b>.
			/// </remarks>
			public static void PrintMsg(in Native.MSG m, PrintMsgOptions options = null, [CallerMemberName] string caller = null) {
				PrintMsg(m.hwnd, m.message, m.wParam, m.lParam, options, caller);
			}

			#endregion
		}
	}
}

namespace Au.Types
{
	/// <summary>
	/// Options for <see cref="AWnd.More.PrintMsg"/>.
	/// </summary>
	public class PrintMsgOptions
	{
		///
		public PrintMsgOptions() { }

		/// <summary>
		/// Sets <see cref="Skip"/>.
		/// </summary>
		public PrintMsgOptions(params int[] skip) { Skip = skip; }

		/// <summary>
		/// Prepend counter 1, 2, 3...
		/// Default true.
		/// </summary>
		public bool Number { get; set; } = true;

		/// <summary>
		/// Prepend one or more tabs if the caller function (usually WndProc) is called recursively.
		/// Default true.
		/// </summary>
		public bool Indent { get; set; } = true;

		/// <summary>
		/// Ignore these messages.
		/// To specify a range of messages, use two array elements: first message and negative last message.
		/// </summary>
		public int[] Skip { get; set; }

		/// <summary>
		/// Append window classname, name and rectangle.
		/// </summary>
		public bool WindowProperties { get; set; }
	}

	/// <summary>
	/// Used with <see cref="AWnd.More.RegisterWindowClass"/>.
	/// </summary>
	[NoDoc]
	public class RWCEtc
	{
#pragma warning disable 1591 //XML doc
		public uint style;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public MCursor mCursor;
		public nint hbrBackground;
		public IntPtr hIconSm;
#pragma warning restore 1591 //XML doc
	}
}
