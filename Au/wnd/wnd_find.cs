﻿using Au.Types;
using Au.More;
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
//using System.Linq;

namespace Au
{
	public unsafe partial struct wnd
	{
		/// <summary>
		/// Finds a top-level window and returns its handle as <b>wnd</b>.
		/// </summary>
		/// <returns>Returns <c>default(wnd)</c> if not found. See also: <see cref="Is0"/>, <see cref="operator +(wnd)"/>.</returns>
		/// <param name="name">
		/// Window name. Usually it is the title bar text.
		/// String format: [](xref:wildcard_expression).
		/// null means 'can be any'. "" means 'no name'.
		/// </param>
		/// <param name="cn">
		/// Window class name.
		/// String format: [](xref:wildcard_expression).
		/// null means 'can be any'. Cannot be "".
		/// </param>
		/// <param name="of">
		/// Program file name, like <c>"notepad.exe"</c>.
		/// String format: [](xref:wildcard_expression).
		/// null means 'can be any'. Cannot be "". Cannot be path.
		/// 
		/// Or <see cref="WOwner.Process"/>(process id), <see cref="WOwner.Thread"/>(thread id), <see cref="WOwner.Window"/>(owner window).
		/// See <see cref="ProcessId"/>, <see cref="process.thisProcessId"/>, <see cref="ThreadId"/>, <see cref="process.thisThreadId"/>, <see cref="OwnerWindow"/>.
		/// </param>
		/// <param name="flags"></param>
		/// <param name="also">
		/// Callback function. Called for each matching window.
		/// It can evaluate more properties of the window and return true when they match.
		/// Example: <c>also: t =&gt; !t.IsPopupWindow</c>.
		/// Called after evaluating all other parameters except <i>contains</i>.
		/// </param>
		/// <param name="contains">
		/// Defines an object that must be in the client area of the window:
		/// - UI element: <see cref="elmFinder"/> or string like <c>"name"</c> or <c>"e 'role' name"</c> or <c>"e 'role'"</c>.
		/// - Child control: <see cref="wndChildFinder"/> or string like <c>"c 'cn' name"</c> or <c>"c '' name"</c> or <c>"c 'cn'"</c>.
		/// - Image(s) or color(s): <see cref="uiimageFinder"/> or string <c>"image:..."</c> (uses <see cref="uiimage.find"/> with flag <see cref="IFFlags.WindowDC"/>).
		/// </param>
		/// <remarks>
		/// To create code for this function, use dialog "Find window or control".
		/// 
		/// If there are multiple matching windows, gets the first in the Z order matching window, preferring visible windows.
		/// 
		/// On Windows 8 and later may skip Windows Store app Metro-style windows (on Windows 10 few such windows exist). It happens if this program does not have disableWindowFiltering true in its manifest and is not uiAccess; to find such windows you can use <see cref="findFast"/>.
		/// 
		/// To find message-only windows use <see cref="findFast"/> instead.
		/// </remarks>
		/// <exception cref="ArgumentException">
		/// - <i>cn</i> is "". To match any, use null.
		/// - <i>of</i> is "" or 0 or contains character \ or /. To match any, use null.
		/// - Invalid wildcard expression (<c>"**options "</c> or regular expression).
		/// </exception>
		/// <example>
		/// Try to find Notepad window. Return if not found.
		/// <code>
		/// wnd w = wnd.find("* Notepad");
		/// if(w.Is0) { print.it("not found"); return; }
		/// </code>
		/// Try to find Notepad window. Throw NotFoundException if not found.
		/// <code>
		/// wnd w1 = +wnd.find("* Notepad");
		/// </code>
		/// </example>
		[MethodImpl(MethodImplOptions.NoInlining)] //inlined code makes harder to debug using disassembly
		public static wnd find(
			[ParamString(PSFormat.wildex)] string name = null,
			[ParamString(PSFormat.wildex)] string cn = null,
			[ParamString(PSFormat.wildex)] WOwner of = default,
			WFlags flags = 0, Func<wnd, bool> also = null, WContains contains = default) {
			var f = new wndFinder(name, cn, of, flags, also, contains);
			f.Find();
			//LastFind = f;
			return f.Result;
		}

		//rejected: probably most users will not understand/use it. It's easy and more clear to create and use wndFinder instances.
		///// <summary>
		///// Gets arguments and result of this thread's last call to <see cref="Find"/> or <see cref="FindAll"/>.
		///// </summary>
		///// <remarks>
		///// <b>wnd.wait</b> and similar functions don't change this property. <see cref="FindOrRun"/> and some other functions of this library change this property because they call <see cref="Find"/> internally.
		///// </remarks>
		///// <example>
		///// This example is similar to what <see cref="FindOrRun"/> does.
		///// <code><![CDATA[
		///// wnd w = wnd.find("*- Notepad", "Notepad");
		///// if(w.Is0) { run.it("notepad.exe"); w = wnd.waitAny(60, true, wnd.LastFind).w; }
		///// ]]></code>
		///// </example>
		//[field: ThreadStatic]
		//public static wndFinder lastFind { get; set; }

		/// <summary>
		/// Finds all matching windows.
		/// Returns array containing 0 or more window handles as wnd.
		/// Parameters etc are the same as <see cref="find"/>.
		/// </summary>
		/// <exception cref="Exception">Exceptions of <see cref="find"/>.</exception>
		/// <remarks>
		/// The list is sorted to match the Z order, however hidden windows (when using <see cref="WFlags.HiddenToo"/>) are always after visible windows.
		/// </remarks>
		/// <seealso cref="getwnd.allWindows"/>
		/// <seealso cref="getwnd.mainWindows"/>
		/// <seealso cref="getwnd.threadWindows"/>
		public static wnd[] findAll(
			[ParamString(PSFormat.wildex)] string name = null,
			[ParamString(PSFormat.wildex)] string cn = null,
			[ParamString(PSFormat.wildex)] WOwner of = default,
			WFlags flags = 0, Func<wnd, bool> also = null, WContains contains = default) {
			var f = new wndFinder(name, cn, of, flags, also, contains);
			var a = f.FindAll();
			//LastFind = f;
			return a;
		}

		/// <summary>
		/// Finds a top-level window and returns its handle as <b>wnd</b>.
		/// </summary>
		/// <returns>Returns <c>default(wnd)</c> if not found. See also: <see cref="Is0"/>, <see cref="operator +(wnd)"/>.</returns>
		/// <param name="name">
		/// Name.
		/// Full, case-insensitive. Wildcard etc not supported.
		/// null means 'can be any'. "" means 'no name'.
		/// </param>
		/// <param name="cn">
		/// Class name.
		/// Full, case-insensitive. Wildcard etc not supported.
		/// null means 'can be any'. Cannot be "".
		/// </param>
		/// <param name="messageOnly">Search only message-only windows.</param>
		/// <param name="wAfter">If used, starts searching from the next window in the Z order.</param>
		/// <remarks>
		/// Calls API <msdn>FindWindowEx</msdn>.
		/// Faster than <see cref="find"/>, which uses API <msdn>EnumWindows</msdn>.
		/// Finds hidden windows too.
		/// Supports <see cref="lastError"/>.
		/// It is not recommended to use this function in a loop to enumerate windows. It would be unreliable because window positions in the Z order can be changed while enumerating. Also then it would be slower than <b>Find</b> and <b>FindAll</b>.
		/// </remarks>
		public static wnd findFast(string name = null, string cn = null, bool messageOnly = false, wnd wAfter = default) {
			return Api.FindWindowEx(messageOnly ? SpecHWND.MESSAGE : default, wAfter, cn, name);
		}

		internal struct Cached_
		{
			wnd _w;
			long _time;

			/// <summary>
			/// Calls/returns <see cref="findFast"/> and stores found hwnd and time. Returns the cached hwnd if called frequently and it's still valid.
			/// </summary>
			public wnd FindFast(string name, string cn, bool messageOnly) {
				long t = Environment.TickCount64;
				if (t - _time > 1000 || !_w.IsAlive) {
					lock ("x5rX3BZJrE+pOTqszh4ttQ") {
						if (t - _time > 1000 || !_w.IsAlive) {
							_w = findFast(name, cn, messageOnly);
						}
					}
				}
				_time = t;
				return _w;
			}
		}

		/// <summary>
		/// Finds a top-level window (calls <see cref="find"/>). If found, activates (optionally), else calls callback function and waits for the window. The callback should open the window, for example call <see cref="run.it"/>.
		/// Returns window handle as <b>wnd</b>. Returns <c>default(wnd)</c> if not found (if <i>runWaitS</i> is negative; else exception).
		/// </summary>
		/// <param name="name">See <see cref="find"/>.</param>
		/// <param name="cn">See <see cref="find"/>.</param>
		/// <param name="of">See <see cref="find"/>.</param>
		/// <param name="flags">See <see cref="find"/>.</param>
		/// <param name="also">See <see cref="find"/>.</param>
		/// <param name="contains">See <see cref="find"/>.</param>
		/// <param name="run">Callback function. See example.</param>
		/// <param name="runWaitS">How long to wait for the window after calling the callback function. Seconds. Default 60. See <see cref="wait"/>.</param>
		/// <param name="needActiveWindow">Finally the window must be active. Default: true.</param>
		/// <exception cref="TimeoutException"><i>runWaitS</i> time has expired. Not thrown if <i>runWaitS</i> &lt;= 0.</exception>
		/// <exception cref="Exception">Exceptions of <see cref="find"/>.</exception>
		/// <remarks>
		/// The algorithm is:
		/// <code>
		/// var w=wnd.find(...);
		/// if(w.Is0) { run(); w=wnd.wait(runWaitS, needActiveWindow, ...); }
		/// else if(needActiveWindow) w.Activate();
		/// return w;
		/// </code>
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// wnd w = wnd.findOrRun("* Notepad", run: () => run.it("notepad.exe"));
		/// print.it(w);
		/// ]]></code>
		/// </example>
		public static wnd findOrRun(
			[ParamString(PSFormat.wildex)] string name = null,
			[ParamString(PSFormat.wildex)] string cn = null,
			[ParamString(PSFormat.wildex)] WOwner of = default,
			WFlags flags = 0, Func<wnd, bool> also = null, WContains contains = default,
			Action run = null, double runWaitS = 60.0, bool needActiveWindow = true) {
			wnd w = default;
			var f = new wndFinder(name, cn, of, flags, also, contains);
			if (f.Find()) {
				w = f.Result;
				if (needActiveWindow) w.Activate();
			} else if (run != null) {
				run();
				w = waitAny(runWaitS, needActiveWindow, f).w;
			}
			return w;
		}

		/// <summary>
		/// Compares window name and other properties like <see cref="find"/> does.
		/// Returns true if all specified (non-null/default) properties match.
		/// </summary>
		/// <param name="name">See <see cref="find"/>.</param>
		/// <param name="cn">See <see cref="find"/>.</param>
		/// <param name="of">See <see cref="find"/>.</param>
		/// <param name="flags">See <see cref="find"/>.</param>
		/// <param name="also">See <see cref="find"/>.</param>
		/// <param name="contains">See <see cref="find"/>.</param>
		/// <exception cref="Exception">Exceptions of <see cref="find"/>.</exception>
		/// <remarks>
		/// Creates new <see cref="wndFinder"/> and calls <see cref="wndFinder.IsMatch"/>.
		/// To compare single parameter, use more lightweight code. Examples: <c>if (w.Name.Like("* Notepad"))</c>, <c>if (w.ClassNameIs("CabinetWClass"))</c>.
		/// </remarks>
		/// <seealso cref="Name"/>
		/// <seealso cref="ClassName"/>
		/// <seealso cref="ClassNameIs"/>
		/// <seealso cref="ProgramName"/>
		public bool IsMatch([ParamString(PSFormat.wildex)] string name = null,
			[ParamString(PSFormat.wildex)] string cn = null,
			[ParamString(PSFormat.wildex)] WOwner of = default,
			WFlags flags = 0, Func<wnd, bool> also = null, WContains contains = default) {
			var f = new wndFinder(name, cn, of, flags, also, contains);
			return f.IsMatch(this);
		}


		public partial struct getwnd
		{
			/// <summary>
			/// Gets top-level windows.
			/// Returns array containing window handles as <b>wnd</b>.
			/// </summary>
			/// <param name="onlyVisible">
			/// Need only visible windows.
			/// Note: this function does not check whether windows are cloaked, as it is rather slow. Use <see cref="IsCloaked"/> if need.
			/// </param>
			/// <param name="sortFirstVisible">
			/// Place hidden windows at the end of the array. If false, the order of array elements matches the Z order.
			/// Not used when <i>onlyVisible</i> is true.</param>
			/// <remarks>
			/// Calls API <msdn>EnumWindows</msdn>.
			/// <note>The array can be bigger than you expect, because there are many invisible windows, tooltips, etc. See also <see cref="mainWindows"/>.</note>
			/// Skips message-only windows; use <see cref="findFast"/> if need.
			/// On Windows 8 and later may skip Windows Store app Metro-style windows (on Windows 10 few such windows exist). It happens if this program does not have disableWindowFiltering true in its manifest and is not uiAccess; to find such windows you can use <see cref="findFast"/>.
			/// Tip: To get top-level and child windows in single array: <c>var a = wnd.getwnd.root.Get.Children();</c>.
			/// </remarks>
			/// <seealso cref="Children"/>
			/// <seealso cref="findAll"/>
			public static wnd[] allWindows(bool onlyVisible = false, bool sortFirstVisible = false) {
				return Internal_.EnumWindows(Internal_.EnumAPI.EnumWindows, onlyVisible, sortFirstVisible);
			}

			/// <summary>
			/// Gets top-level windows.
			/// </summary>
			/// <param name="a">Receives window handles as <b>wnd</b>. If null, this function creates new List, else clears before adding items.</param>
			/// <param name="onlyVisible"></param>
			/// <param name="sortFirstVisible"></param>
			/// <remarks>
			/// Use this overload to avoid much garbage when calling frequently with the same List variable. Other overload always allocates new array. This overload in most cases reuses memory allocated for the list variable.
			/// </remarks>
			public static void allWindows(ref List<wnd> a, bool onlyVisible = false, bool sortFirstVisible = false) {
				Internal_.EnumWindows2(Internal_.EnumAPI.EnumWindows, onlyVisible, sortFirstVisible, list: a ??= new List<wnd>());
			}

			/// <summary>
			/// Gets top-level windows of a thread.
			/// Returns array containing 0 or more window handles as <b>wnd</b>.
			/// </summary>
			/// <param name="threadId">
			/// Unmanaged thread id.
			/// See <see cref="process.thisThreadId"/>, <see cref="ThreadId"/>.
			/// If 0, throws exception. If other invalid value (ended thread?), returns empty list. Supports <see cref="lastError"/>.
			/// </param>
			/// <param name="onlyVisible">Need only visible windows.</param>
			/// <param name="sortFirstVisible">Place all array elements of hidden windows at the end of the array, even if the hidden windows are before some visible windows in the Z order.</param>
			/// <exception cref="ArgumentException">0 threadId.</exception>
			/// <remarks>
			/// Calls API <msdn>EnumThreadWindows</msdn>.
			/// </remarks>
			/// <seealso cref="process.thisThreadHasMessageLoop"/>
			public static wnd[] threadWindows(int threadId, bool onlyVisible = false, bool sortFirstVisible = false) {
				if (threadId == 0) throw new ArgumentException("0 threadId.");
				return Internal_.EnumWindows(Internal_.EnumAPI.EnumThreadWindows, onlyVisible, sortFirstVisible, threadId: threadId);
			}

			/// <summary>
			/// Gets top-level windows of a thread.
			/// </summary>
			/// <remarks>This overload can be used to avoid much garbage when caling frequently.</remarks>
			public static void threadWindows(ref List<wnd> a, int threadId, bool onlyVisible = false, bool sortFirstVisible = false) {
				if (threadId == 0) throw new ArgumentException("0 threadId.");
				Internal_.EnumWindows2(Internal_.EnumAPI.EnumThreadWindows, onlyVisible, sortFirstVisible, threadId: threadId, list: a ??= new List<wnd>());
			}

			/// <summary>
			/// Gets the first in Z order window of this thread.
			/// </summary>
			/// <param name="onlyVisible"></param>
			internal static wnd TopThreadWindow_(bool onlyVisible) {
				wnd r = default;
				Api.EnumThreadWindows(Api.GetCurrentThreadId(), (w, _) => {
					if (onlyVisible && !w.IsVisible) return 1;
					r = w;
					return 0;
				});
				return r;
			}
		}

		/// <summary>
		/// Internal static functions.
		/// </summary>
		internal static partial class Internal_
		{
			internal enum EnumAPI { EnumWindows, EnumThreadWindows, EnumChildWindows, }

			internal static wnd[] EnumWindows(EnumAPI api,
				bool onlyVisible, bool sortFirstVisible, wnd wParent = default, bool directChild = false, int threadId = 0) {
				using var a = EnumWindows2(api, onlyVisible, sortFirstVisible, wParent, directChild, threadId);
				return a.ToArray();
			}

			/// <summary>
			/// This version creates much less garbage.
			/// The caller must dispose the returned ArrayBuilder_, unless list is not null.
			/// If list is not null, adds windows there (clears at first) and returns default(ArrayBuilder_).
			/// </summary>
			internal static ArrayBuilder_<wnd> EnumWindows2(EnumAPI api,
				bool onlyVisible, bool sortFirstVisible = false, wnd wParent = default, bool directChild = false, int threadId = 0,
				Func<wnd, object, bool> predicate = null, object predParam = default, List<wnd> list = null) {
				if (directChild && wParent == getwnd.root) { api = EnumAPI.EnumWindows; wParent = default; }

				ArrayBuilder_<wnd> ab = default;
				bool disposeArray = true;
				var d = new _EnumData { api = api, onlyVisible = onlyVisible, directChild = directChild, wParent = wParent };
				try {
					switch (api) {
					case EnumAPI.EnumWindows:
						Api.EnumWindows(_wndEnumProc, &d);
						break;
					case EnumAPI.EnumThreadWindows:
						Api.EnumThreadWindows(threadId, _wndEnumProc, &d);
						break;
					case EnumAPI.EnumChildWindows:
						Api.EnumChildWindows(wParent, _wndEnumProc, &d);
						break;
					}

					int n = d.len;
					if (n > 0) {
						if (predicate != null) {
							n = 0;
							for (int i = 0; i < d.len; i++) {
								if (predicate((wnd)d.a[i], predParam)) d.a[n++] = d.a[i];
							}
						}

						if (list != null) {
							list.Clear();
							if (list.Capacity < n) list.Capacity = n + n / 2;
						} else {
							ab.Alloc(n, zeroInit: false, noExtra: true);
						}
						if (sortFirstVisible && !onlyVisible) {
							int j = 0;
							for (int i = 0; i < n; i++) {
								var w = (wnd)d.a[i];
								if (!_EnumIsVisible(w, api, wParent)) continue;
								if (list != null) list.Add(w); else ab[j++] = w;
								d.a[i] = 0;
							}
							for (int i = 0; i < n; i++) {
								int wi = d.a[i];
								if (wi == 0) continue;
								var w = (wnd)wi;
								if (list != null) list.Add(w); else ab[j++] = w;
							}
						} else if (list != null) {
							for (int i = 0; i < n; i++) list.Add((wnd)d.a[i]);
						} else {
							for (int i = 0; i < n; i++) ab[i] = (wnd)d.a[i];
						}
					}
					disposeArray = false;
					return ab;
				}
				finally {
					MemoryUtil.Free(d.a);
					if (disposeArray) ab.Dispose();
				}
			}
			static Api.WNDENUMPROC _wndEnumProc = (w, p) => ((_EnumData*)p)->Proc(w);

			struct _EnumData
			{
				public int* a;
				public int len;
				int _cap;
				public EnumAPI api;
				public bool onlyVisible, directChild;
				public wnd wParent;

				public int Proc(wnd w) {
					if (onlyVisible && !_EnumIsVisible(w, api, wParent)) return 1;
					if (api == EnumAPI.EnumChildWindows) {
						if (directChild && w.ParentGWL_ != wParent) return 1;
					} else {
						if (!wParent.Is0 && w.OwnerWindow != wParent) return 1;
					}
					if (a == null) a = MemoryUtil.Alloc<int>(_cap = onlyVisible ? 200 : 1000);
					else if (len == _cap) MemoryUtil.ReAlloc(ref a, _cap *= 2);
					a[len++] = (int)w;
					return 1;
				}

				//note: need this in exe manifest. Else EnumWindows skips "immersive" windows if this process is not admin/uiAccess.
				/*
<asmv3:application>
...
<asmv3:windowsSettings xmlns="http://schemas.microsoft.com/SMI/2011/WindowsSettings">
  <disableWindowFiltering>true</disableWindowFiltering>
</asmv3:windowsSettings>
</asmv3:application>
				*/
			}

			static bool _EnumIsVisible(wnd w, EnumAPI api, wnd wParent)
				=> api == EnumAPI.EnumChildWindows ? w.IsVisibleIn_(wParent) : w.IsVisible;
		}
	}
}

namespace Au.Types
{
	/// <summary>
	/// Flags of <see cref="wnd.find"/> and similar functions.
	/// </summary>
	[Flags]
	public enum WFlags
	{
		/// <summary>
		/// Can find invisible windows. See <see cref="wnd.IsVisible"/>.
		/// Use this carefully. Always use <i>cn</i> (class name), not just <i>name</i>, to avoid finding a wrong window with the same name.
		/// </summary>
		HiddenToo = 1,

		/// <summary>
		/// Can find cloaked windows. See <see cref="wnd.IsCloaked"/>.
		/// Cloaked are windows hidden not in the classic way, therefore <see cref="wnd.IsVisible"/> does not detect it, but <see cref="wnd.IsCloaked"/> detects. For example, windows on inactive Windows 10 virtual desktops, ghost windows of inactive Windows Store apps, various hidden system windows.
		/// Use this carefully. Always use <i>cn</i> (class name), not just <i>name</i>, to avoid finding a wrong window with the same name.
		/// </summary>
		CloakedToo = 2,
	}

	/// <summary>
	/// Used with <see cref="wnd.find"/> and similar functions to specify an owner object of the window.
	/// Can be program name (like <c>"notepad.exe"</c>), process id (<see cref="Process"/>), thread id (<see cref="Thread"/> or <see cref="ThisThread"/>) or owner window (<see cref="Window"/>).
	/// </summary>
	public struct WOwner
	{
		readonly object _o;
		WOwner(object o) => _o = o;

		/// <summary>Program name like "notepad.exe", or null. See <see cref="wnd.ProgramName"/>.</summary>
		public static implicit operator WOwner([ParamString(PSFormat.wildex)] string program) => new WOwner(program);

		//rejected. Cannot check whether it is default(WOwner) or 0 window handle. And less readable code.
		///// <summary>Owner window. See <see cref="wnd.OwnerWindow"/>.</summary>
		//public static implicit operator WOwner(wnd ownerWindow) => new WOwner(ownerWindow);

		/// <summary>Process id. See <see cref="wnd.ProcessId"/>.</summary>
		public static WOwner Process(int processId) => new WOwner(processId);

		/// <summary>Thread id. See <see cref="wnd.ThreadId"/>.</summary>
		public static WOwner Thread(int threadId) => new WOwner((uint)threadId);

		/// <summary>Thread id of this thread.</summary>
		public static WOwner ThisThread => new WOwner((uint)Api.GetCurrentThreadId());

		/// <summary>Owner window. See <see cref="wnd.OwnerWindow"/>.</summary>
		public static WOwner Window(AnyWnd ownerWindow) => new WOwner(ownerWindow);

		/// <summary>
		/// Gets program name or process id or thread id or owner window.
		/// Other variables will be null/0.
		/// </summary>
		/// <exception cref="ArgumentException">The value is "" or 0 or contains characters \ or /.</exception>
		public void GetValue(out wildex program, out int pid, out int tid, out wnd owner) {
			program = null; pid = 0; tid = 0; owner = default;
			switch (_o) {
			case string s:
				if (s.Length == 0) throw new ArgumentException("Program name cannot be \"\". Use null.");
				if (!s.Starts("**")) { //can be regex
					if (s.FindAny(@"\/") >= 0) throw new ArgumentException("Program name contains \\ or /.");
					if (pathname.findExtension(s) < 0 && !wildex.hasWildcardChars(s)) print.warning("Program name without .exe.");
				}
				program = s;
				break;
			case int i:
				if (i == 0) throw new ArgumentException("0 process id");
				pid = i;
				break;
			case uint i:
				if (i == 0) throw new ArgumentException("0 thread id");
				tid = (int)i;
				break;
			case AnyWnd aw:
				var w = aw.Hwnd;
				if (w.Is0) throw new ArgumentException("0 window handle");
				owner = w;
				break;
			}
		}

		/// <summary>
		/// Returns true if nothing was assigned to this variable.
		/// </summary>
		public bool IsEmpty => _o == null;
	}

	/// <summary>
	/// The <i>contains</i> parameter of <see cref="wnd.find"/> and similar functions.
	/// Specifies text, image or other object that must be in the window.
	/// </summary>
	public struct WContains
	{
		readonly object _o;
		WContains(object o) => _o = o;

		///
		public static implicit operator WContains(wndChildFinder f) => new(f);

		///
		public static implicit operator WContains(elmFinder f) => new(f);

		///
		public static implicit operator WContains(uiimageFinder f) => new(f);

		/// <summary>
		/// Converts from string to <see cref="wndChildFinder"/>, <see cref="elmFinder"/> or <see cref="uiimageFinder"/>.
		/// See <see cref="wnd.find"/>.
		/// </summary>
		/// <exception cref="Exception">Exceptions of constructor of <see cref="wndChildFinder"/>, <see cref="elmFinder"/> or <see cref="uiimageFinder"/>.</exception>
		public static implicit operator WContains(string s) => new(_ParseString(s));

		static object _ParseString(string s) {
			if (s.NE()) return null;
			string role = null, name = s;
			switch (s[0]) {
			case 'e': //"e 'role' name" or just "name"
			case 'c': //"c 'class' text"
				if (s.RegexMatch(@"^. ?'(.+?)?' ?((?s).+)?$", out var m)) {
					role = m[1].Value; name = m[2].Value;
					if (s[0] == 'c') return new wndChildFinder(name, role);
				}
				break;
			case 'i' when s.Starts("image:"):
				return new uiimageFinder(s, IFFlags.WindowDC);
			}
			return new elmFinder(role, name, flags: EFFlags.ClientArea) { ResultGetProperty = '-' };
		}

		/// <summary>
		/// Gets object stored in this variable. Can be null, <see cref="wndChildFinder"/>, <see cref="elmFinder"/> or <see cref="uiimageFinder"/>.
		/// </summary>
		public object Value => _o;
	}

	/// <summary>
	/// Can be used with <see cref="wndFinder.IsMatch"/>.
	/// </summary>
	public class WFCache
	{
		wnd _w;
		long _time;
		internal string Name, Class, Program;
		internal int Tid, Pid;

		/// <summary>
		/// Cache window name.
		/// Default: false.
		/// </summary>
		/// <remarks>
		/// Window name is not cached by default because can be changed. Window class name and program name are always cached because cannot be changed.
		/// </remarks>
		public bool CacheName { get; set; }

		/// <summary>
		/// Don't auto-clear cached properties on timeout.
		/// </summary>
		public bool NoTimeout { get; set; }

		internal void Begin(wnd w) {
			if (NoTimeout) {
				if (w != _w) {
					Clear();
					_w = w;
				}
			} else {
				var t = Api.GetTickCount64();
				if (w != _w || t - _time > 2500) {
					Clear();
					if (w.IsAlive) { _w = w; _time = t; }
				}
				//else if(CacheName && t - _time > 100) Name = null; //no, instead let call Clear if need
			}
		}

		/// <summary>
		/// Clears all cached properties, or only name.
		/// </summary>
		/// <remarks>
		/// Usually don't need to call this function. It is implicitly called when the variable is used with a new window.
		/// </remarks>
		/// <param name="onlyName">Clear only name (because it may change, unlike other cached properties).</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Clear(bool onlyName = false) {
			if (onlyName) Name = null;
			else { _w = default; _time = 0; Name = Class = Program = null; Tid = Pid = 0; }
		}

		/// <summary>
		/// Match invisible and cloaked windows too, even if the flags are not set (see <see cref="WFlags"/>).
		/// </summary>
		public bool IgnoreVisibility { get; set; }
	}
}
