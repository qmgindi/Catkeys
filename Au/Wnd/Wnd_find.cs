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
using Microsoft.Win32;
using System.Runtime.ExceptionServices;
using System.Linq;

using Au.Types;
using static Au.NoClass;

namespace Au
{
	public unsafe partial struct Wnd
	{
		/// <summary>
		/// Contains top-level window properties and can be used to find the window.
		/// </summary>
		/// <remarks>
		/// Can be used instead of <see cref="Wnd.Find"/> or <see cref="Wnd.FindAll"/>.
		/// These codes are equivalent:
		/// <code>Wnd w = Wnd.Find(a, b, c, d, e); if(!w.Is0) Print(w);</code>
		/// <code>var p = new Wnd.Finder(a, b, c, d, e); if(p.Find()) Print(p.Result);</code>
		/// Also can find in a list of windows.
		/// </remarks>
		public class Finder
		{
			Wildex _name;
			Wildex _className;
			Wildex _program;
			Func<Wnd, bool> _also;
			WFFlags _flags;
			int _processId;
			int _threadId;
			Wnd _owner;
			object _contains;

			/// <summary>
			/// See <see cref="Wnd.Find"/>.
			/// </summary>
			/// <exception cref="ArgumentException">
			/// className is "". To match any, use null.
			/// programEtc is "" or 0. To match any, use null.
			/// Invalid wildcard expression ("**options " or regular expression).
			/// </exception>
			public Finder(
				string name = null, string className = null, WFEtc programEtc = default,
				WFFlags flags = 0, Func<Wnd, bool> also = null, object contains = null)
			{
				_name = name;
				if(className != null) _className = className.Length != 0 ? className : throw new ArgumentException("Class name cannot be \"\". Use null to match any.");
				programEtc.GetValue(out _program, out _processId, out _threadId, out _owner);
				_flags = flags;
				_also = also;
				if(contains != null) _ParseContains(contains);
			}

			void _ParseContains(object contains)
			{
				if(contains is string s) { //accessible object. Format: "'role' name" or "name".
					if(s.Length == 0) return;
					string role = null, name = s;
					if(s.RegexMatch_(@"^'(.+?)?' (.+)?$", out var m)) { role = m[1].Value; name = m[2].Value; }
					_contains = new Acc.Finder(role, name, flags: AFFlags.ClientArea) { ResultGetProperty = '-' };
				} else if(contains is Acc.Finder || contains is ChildFinder || contains is System.Drawing.Image) _contains = contains;
				else throw new ArgumentException("Bad type.", nameof(contains));
			}

			/// <summary>
			/// The found window.
			/// </summary>
			public Wnd Result { get; internal set; }

			/// <summary>
			/// Finds the specified window, like <see cref="Wnd.Find"/>.
			/// Returns true if found.
			/// The <see cref="Result"/> property will be the window.
			/// </summary>
			public bool Find()
			{
				using(var k = new _WndList(_AllWindows()))
					return _FindInList(k) >= 0;
			}

			Util.LibArrayBuilder<Wnd> _AllWindows()
			{
				//FUTURE: optimization: if className not wildcard etc, at first find atom. If not found, don't search. If found, compare atom, not class name string.

				var f = _threadId != 0 ? Lib.EnumWindowsAPI.EnumThreadWindows : Lib.EnumWindowsAPI.EnumWindows;
				return Lib.EnumWindows2(f, 0 == (_flags & WFFlags.HiddenToo), true, wParent: _owner, threadId: _threadId);
			}

			/// <summary>
			/// Finds the specified window in a list of windows.
			/// Returns 0-based index, or -1 if not found.
			/// The <see cref="Result"/> property will be the window.
			/// </summary>
			/// <param name="a">Array or list of windows, for example returned by <see cref="GetWnd.AllWindows"/>.</param>
			public int FindInList(IEnumerable<Wnd> a)
			{
				using(var k = new _WndList(a))
					return _FindInList(k);
			}

			/// <summary>
			/// Finds all matching windows, like <see cref="Wnd.FindAll"/>.
			/// Returns array containing 0 or more window handles as Wnd.
			/// </summary>
			public Wnd[] FindAll()
			{
				return _FindAll(new _WndList(_AllWindows()));
			}

			/// <summary>
			/// Finds all matching windows in a list of windows.
			/// Returns array containing 0 or more window handles as Wnd.
			/// </summary>
			/// <param name="a">Array or list of windows, for example returned by <see cref="GetWnd.AllWindows"/>.</param>
			public Wnd[] FindAllInList(IEnumerable<Wnd> a)
			{
				return _FindAll(new _WndList(a));
			}

			Wnd[] _FindAll(_WndList k)
			{
				using(k) {
					using(var ab = new Util.LibArrayBuilder<Wnd>()) {
						_FindInList(k, w => ab.Add(w)); //CONSIDER: ab could be part of _WndList. Now the delegate creates garbage.
						return ab.ToArray();
					}
				}
			}

			/// <summary>
			/// Returns index of matching element or -1.
			/// Returns -1 if using getAll.
			/// </summary>
			/// <param name="a">List of Wnd. Does not dispose it.</param>
			/// <param name="getAll">If not null, calls it for all matching and returns -1.</param>
			int _FindInList(_WndList a, Action<Wnd> getAll = null)
			{
				Result = default;
				if(a.Type == _WndList.ListType.None) return -1;
				bool inList = a.Type != _WndList.ListType.ArrayBuilder;
				bool mustBeVisible = inList && (_flags & WFFlags.HiddenToo) == 0;
				bool isOwner = inList && !_owner.Is0;
				bool isTid = inList ? _threadId != 0 : false;
				List<int> pids = null; bool programNamePlanB = false; //variables for faster getting/matching program name

				for(int index = 0; a.Next(out Wnd w); index++) {
					if(w.Is0) continue;

					//speed of 1000 times getting:
					//name 400, class 400 (non-cached), foreign pid/tid 400,
					//owner 55, rect 55, style 50, exstyle 50, cloaked 280,
					//GetProp(string) 1700, GetProp(atom) 300, GlobalFindAtom 650,
					//program >=2500

					if(mustBeVisible) {
						if(!w.IsVisibleEx) continue;
					}

					if(isOwner) {
						if(_owner != w.Owner) continue;
					}

					if(_name != null) {
						if(!_name.Match(w.GetText(false, false))) continue;
						//note: name is before classname. It makes faster in slowest cases (HiddenToo), because most windows are nameless.
					}

					if(_className != null) {
						if(!_className.Match(w.ClassName)) continue;
					}

					int pid = 0, tid = 0;
					if(_program != null || _processId != 0 || isTid) {
						tid = w.GetThreadProcessId(out pid);
						if(tid == 0) continue;
						//speed: with foreign processes the same speed as getting name or class name. Much faster if same process.
					}

					if(isTid) {
						if(_threadId != tid) continue;
					}

					if(_processId != 0) {
						if(_processId != pid) continue;
					}

					if(_program != null) {
						//Getting program name is one of slowest parts.
						//Usually it does not slow down much because need to do it only 1 or several times, only when window name, class etc match.
						//The worst case is when only program is specified, and the very worst case is when also using flag HiddenToo.
						//We are prepared for the worst case.
						//Normally we call Process_.GetName. In most cases it is quite fast.
						//Anyway, we use this optimization:
						//	Add pid of processes that don't match the specified name in the pids list (bad pids).
						//	Next time, if pid is in the bad pids list, just continue, don't need to get program name again.
						//However in the worst case we would encounter some processes that Process_.GetName cannot get name using the fast API.
						//For each such process it would then use the much slower 'get all processes' API, which is almost as slow as Process.GetProcessById(pid).ProgramName.
						//To solve this:
						//We tell Process_.GetName to not use the slow API, but just return null when the fast API fails.
						//When it happens (Process_.GetName returns null):
						//	If need full path: continue, we cannot do anything more.
						//	Switch to plan B and no longer use all the above. Plan B:
						//	Get list of pids of all processes that match _program. For it we call Process_.LibGetProcessesByName, which uses the same slow API, but we call it just one time.
						//	If it returns null (it means there are no matching processes), break (window not found).
						//	From now, in each loop will need just to find pid in the returned list, and continue if not found.

						g1:
						if(programNamePlanB) {
							if(!pids.Contains(pid)) continue;
						} else {
							if(pids != null && pids.Contains(pid)) continue; //is known bad pid?

							//string pname = Process_.GetName(pid, 0!=(_flags&WFFlags.ProgramPath), true);
							string pname = Process_.GetName(pid, false, true);

							if(pname == null) {
								//if(0!=(_flags&WFFlags.ProgramPath)) continue;

								//switch to plan B
								Process_.LibGetProcessesByName(ref pids, _program);
								if(pids == null || pids.Count == 0) break;
								programNamePlanB = true;
								goto g1;
							}

							if(!_program.Match(pname)) {
								if(a.Type == _WndList.ListType.SingleWnd) break;
								if(pids == null) pids = new List<int>(16);
								pids.Add(pid); //add bad pid
								continue;
							}
						}
					}

					if(0 != (_flags & WFFlags.SkipCloaked)) {
						if(w.IsCloaked) continue;
					}

					if(_also != null && !_also(w)) continue;

					if(_contains != null) {
						bool found = false;
						switch(_contains) {
						case Acc.Finder f: found = f.Find(w); break;
						case ChildFinder f: found = f.Find(w); break;
						case System.Drawing.Image f: found = null != WinImage.Find(w, f, WIFlags.WindowDC); break; //FUTURE: optimize
						}
						if(!found) continue;
					}

					if(getAll != null) {
						getAll(w);
						continue;
					}

					Result = w;
					return index;
				}

				return -1;
			}

			/// <summary>
			/// Returns true if window w properties match the specified properties.
			/// </summary>
			/// <param name="w">A top-level window. Can be 0/invalid, then returns false.</param>
			public bool IsMatch(Wnd w)
			{
				return 0 == _FindInList(new _WndList(w));
			}
		}

		/// <summary>
		/// Finds a top-level window and returns its handle as Wnd.
		/// Returns default(Wnd) if not found. See examples.
		/// </summary>
		/// <param name="name">
		/// Window name. Usually it is the title bar text.
		/// String format: <conceptualLink target="0248143b-a0dd-4fa1-84f9-76831db6714a">wildcard expression</conceptualLink>.
		/// null means 'can be any'. "" means 'must not have name'.
		/// </param>
		/// <param name="className">
		/// Window class name.
		/// String format: <conceptualLink target="0248143b-a0dd-4fa1-84f9-76831db6714a">wildcard expression</conceptualLink>.
		/// null means 'can be any'. Cannot be "".
		/// </param>
		/// <param name="programEtc">
		/// Program file name, like "notepad.exe".
		/// String format: <conceptualLink target="0248143b-a0dd-4fa1-84f9-76831db6714a">wildcard expression</conceptualLink>.
		/// null means 'can be any'. Cannot be "". Cannot be path.
		/// Or <see cref="WFEtc.Process"/>(process id), <see cref="WFEtc.Thread"/>(thread id), <see cref="WFEtc.Owner"/>(owner window).
		/// See <see cref="ProcessId"/>, <see cref="Process_.CurrentProcessId"/>, <see cref="ThreadId"/>, <see cref="Thread_.NativeId"/>, <see cref="Owner"/>.
		/// </param>
		/// <param name="flags"></param>
		/// <param name="also">
		/// Callback function. Called for each matching window.
		/// It can evaluate more properties of the window and return true when they match.
		/// Example: <c>also: t =&gt; !t.IsPopupWindow</c>.
		///
		/// Called after evaluating all other parameters except <paramref name="contains"/>.
		/// </param>
		/// <param name="contains">
		/// Text, image or other object in the client area of the window. Depends on type:
		/// string - an accessible object that must be in the window. Format: "'role' name" or "name". See <see cref="Acc.Find"/>.
		/// <see cref="Acc.Finder"/> - arguments for <see cref="Acc.Find"/>. Defines an accessible object that must be in the window.
		/// <see cref="Wnd.ChildFinder"/> - arguments for <see cref="Wnd.Child"/>. Defines a child control that must be in the window.
		/// <see cref="System.Drawing.Image"/> or <see cref="System.Drawing.Bitmap"/> - image that must be visible in the window. To find it, this function calls <see cref="WinImage.Find"/> with flag <see cref="WIFlags.WindowDC"/>. See also <see cref="WinImage.LoadImage"/>.
		///
		/// This parameter is evaluated after <paramref name="also"/>.
		/// </param>
		/// <remarks>
		/// To create code for this function, use dialog "Find window or control". It is form <b>Au.Tools.Form_Wnd</b> in Au.Tools.dll.
		/// 
		/// If there are multiple matching windows, gets the first in the Z order matching window, preferring visible windows.
		/// On Windows 8 and later finds only desktop windows, not Windows Store app Metro-style windows (on Windows 10 only few such windows exist), unless this process has uiAccess; to find such windows you can use <see cref="FindFast"/>.
		/// To find message-only windows use <see cref="Misc.FindMessageWindow"/> instead.
		/// </remarks>
		/// <exception cref="ArgumentException">
		/// <paramref name="className"/> is "". To match any, use null.
		/// <paramref name="programEtc"/> is "" or 0. To match any, use null.
		/// Invalid wildcard expression ("**options " or regular expression).
		/// </exception>
		/// <example>
		/// Try to find Notepad window. Return if not found.
		/// <code>
		/// Wnd w = Wnd.Find("* Notepad");
		/// if(w.Is0) { Print("not found"); return; }
		/// </code>
		/// Try to find Notepad window. Throw NotFoundException if not found.
		/// <code>
		/// Wnd w1 = Wnd.Find("* Notepad").OrThrow();
		/// </code>
		/// </example>
		[MethodImpl(MethodImplOptions.NoInlining)] //inlined code makes harder to debug using disassembly
		public static Wnd Find(
			string name = null, string className = null, WFEtc programEtc = default,
			WFFlags flags = 0, Func<Wnd, bool> also = null, object contains = null)
		{
			var f = new Finder(name, className, programEtc, flags, also, contains);
			f.Find();
			//LastFind = f;
			return f.Result;
		}

		//rejected: probably most users will not understand what is it, and will not use. It's easy and more clear to create and use Finder instances.
		///// <summary>
		///// Gets arguments and result of this thread's last call to <see cref="Find"/> or <see cref="FindAll"/>.
		///// </summary>
		///// <remarks>
		///// <b>Wnd.Wait</b> and similar functions don't change this property. <see cref="FindOrRun"/> and some other functions of this library change this property because they call <see cref="Find"/> internally.
		///// </remarks>
		///// <example>
		///// This example is similar to what <see cref="FindOrRun"/> does.
		///// <code><![CDATA[
		///// Wnd w = Wnd.Find("*- Notepad", "Notepad");
		///// if(w.Is0) { Shell.Run("notepad.exe"); w = Wnd.WaitAny(60, true, Wnd.LastFind); }
		///// ]]></code>
		///// </example>
		//[field: ThreadStatic]
		//public static Finder LastFind { get; set; }

		/// <inheritdoc cref="Find"/>
		/// <summary>
		/// Finds all matching windows.
		/// Returns array containing 0 or more window handles as Wnd.
		/// Parameters etc are the same as <see cref="Find"/>.
		/// </summary>
		/// <remarks>
		/// The list is sorted to match the Z order, however hidden windows (when using <see cref="WFFlags.HiddenToo"/>) are always after visible windows.
		/// </remarks>
		/// <seealso cref="GetWnd.AllWindows"/>
		/// <seealso cref="GetWnd.MainWindows"/>
		/// <seealso cref="GetWnd.ThreadWindows"/>
		public static Wnd[] FindAll(
			string name = null, string className = null, WFEtc programEtc = default,
			WFFlags flags = 0, Func<Wnd, bool> also = null, object contains = null)
		{
			var f = new Finder(name, className, programEtc, flags, also, contains);
			var a = f.FindAll();
			//LastFind = f;
			return a;
		}

		/// <summary>
		/// Finds a top-level window and returns its handle as Wnd.
		/// Returns default(Wnd) if not found. See also: <see cref="Is0"/>, <see cref="ExtensionMethods.OrThrow(Wnd)" r=""/>.
		/// </summary>
		/// <param name="name">
		/// Name.
		/// Use null to match any.
		/// Full, case-insensitive. Wildcard etc not supported.
		/// </param>
		/// <param name="className">
		/// Class name.
		/// Use null to match any. Cannot be "".
		/// Full, case-insensitive. Wildcard etc not supported.
		/// </param>
		/// <param name="wAfter">If used, starts searching from the next window in the Z order.</param>
		/// <remarks>
		/// Calls API <msdn>FindWindowEx</msdn>.
		/// Faster than <see cref="Find"/>, which uses API <msdn>EnumWindows</msdn>.
		/// Can be used only full name and/or class name.
		/// Finds hidden windows too.
		/// To find message-only windows use <see cref="Misc.FindMessageWindow"/> instead.
		/// Supports <see cref="Native.GetError"/>.
		/// It is not recommended to use this function in a loop to enumerate windows. It would be unreliable because window positions in the Z order can be changed while enumerating. Also then it would be slower than <b>Find</b> and <b>FindAll</b>.
		/// </remarks>
		public static Wnd FindFast(string name, string className, Wnd wAfter = default)
		{
			return Api.FindWindowEx(default, wAfter, className, name);
		}

		public static partial class Misc
		{
			/// <summary>
			/// Finds a message-only window and returns its handle as Wnd. Returns default(Wnd) if not found.
			/// Calls API <msdn>FindWindowEx</msdn>.
			/// Faster than <see cref="Find"/>, which does not find message-only windows.
			/// Can be used only when you know full name and/or class name.
			/// Finds hidden windows too.
			/// </summary>
			/// <param name="name">
			/// Name.
			/// Use null to match any.
			/// Full, case-insensitive. Wildcard etc not supported.
			/// </param>
			/// <param name="className">
			/// Class name.
			/// Use null to match any. Cannot be "".
			/// Full, case-insensitive. Wildcard etc not supported.
			/// </param>
			/// <param name="wAfter">If used, starts searching from the next window in the Z order.</param>
			/// <remarks>
			/// Supports <see cref="Native.GetError"/>.
			/// </remarks>
			public static Wnd FindMessageWindow(string name, string className, Wnd wAfter = default)
			{
				return Api.FindWindowEx(Native.HWND.MESSAGE, wAfter, className, name);
			}
		}

		/// <inheritdoc cref="Find"/>
		/// <summary>
		/// Finds a top-level window (<see cref="Find"/>). If found, activates (optionally), else calls callback function and waits for the window. The callback should open the window, for example call <see cref="Shell.Run"/>.
		/// Returns window handle as Wnd. Returns default(Wnd) if not found (if <paramref name="runWaitS"/> is negative; else exception).
		/// </summary>
		/// <param name="run">Callback function. See example.</param>
		/// <param name="runWaitS">How long to wait for the window after calling the callback function. Seconds. Default 60. See <see cref="Wait"/>.</param>
		/// <param name="needActiveWindow">Finally the window must be active. Default: true.</param>
		/// <exception cref="TimeoutException"><paramref name="runWaitS"/> time has expired. Not thrown if <paramref name="runWaitS"/> &lt;= 0.</exception>
		/// <remarks>
		/// The algorithm is:
		/// <code>
		/// var w=Wnd.Find(...);
		/// if(w.Is0) { run(); w=Wnd.Wait(runWaitS, needActiveWindow, ...); }
		/// else if(needActiveWindow) w.Activate();
		/// return w;
		/// </code>
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// Wnd w = Wnd.FindOrRun("* Notepad", run: () => Shell.Run("notepad.exe"));
		/// Print(w);
		/// ]]></code>
		/// </example>
		public static Wnd FindOrRun(
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
			string name = null, string className = null, WFEtc programEtc = default,
			WFFlags flags = 0, Func<Wnd, bool> also = null, object contains = null,
#pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
			Action run = null, double runWaitS = 60.0, bool needActiveWindow = true)
		{
			Wnd w = default;
			var f = new Finder(name, className, programEtc, flags, also, contains);
			if(f.Find()) {
				w = f.Result;
				if(needActiveWindow) w.Activate();
			} else if(run != null) {
				run();
				w = WaitAny(runWaitS, needActiveWindow, f);
			}
			return w;
		}

		public partial struct GetWnd
		{
			/// <summary>
			/// Gets top-level windows.
			/// Returns array containing window handles as Wnd.
			/// </summary>
			/// <param name="onlyVisible">Need only visible windows.</param>
			/// <param name="sortFirstVisible">Place all array elements of hidden windows at the end of the array, even if the hidden windows are before some visible windows in the Z order.</param>
			/// <remarks>
			/// Calls API <msdn>EnumWindows</msdn>.
			/// <note>The list can be bigger than you expect, because there are many invisible windows, tooltips, etc. See also <see cref="MainWindows"/>.</note>
			/// By default array elements are sorted to match the Z order.
			/// On Windows 8 and later gets only desktop windows, not Windows Store app Metro-style windows (on Windows 10 only few such windows exist), unless this process has <see cref="Process_.UacInfo">UAC</see> integrity level uiAccess; to get such windows you can use <see cref="FindFast"/>.
			/// </remarks>
			/// <seealso cref="FindAll"/>
			public static Wnd[] AllWindows(bool onlyVisible = false, bool sortFirstVisible = false)
			{
				return Lib.EnumWindows(Lib.EnumWindowsAPI.EnumWindows, onlyVisible, sortFirstVisible);

				//rejected: add a flag to skip tooltips, IME, etc.
			}

			/// <summary>
			/// Gets top-level windows of a thread.
			/// Returns array containing 0 or more window handles as Wnd.
			/// </summary>
			/// <param name="threadId">
			/// Unmanaged thread id.
			/// See <see cref="Thread_.NativeId"/>, <see cref="ThreadId"/>.
			/// If 0, throws exception. If other invalid value (ended thread?), returns empty list. Supports <see cref="Native.GetError"/>.
			/// </param>
			/// <param name="onlyVisible">Need only visible windows.</param>
			/// <param name="sortFirstVisible">Place all array elements of hidden windows at the end of the array, even if the hidden windows are before some visible windows in the Z order.</param>
			/// <exception cref="ArgumentException">0 threadId.</exception>
			/// <remarks>
			/// Calls API <msdn>EnumThreadWindows</msdn>.
			/// </remarks>
			/// <seealso cref="Thread_.IsUI"/>
			public static Wnd[] ThreadWindows(int threadId, bool onlyVisible = false, bool sortFirstVisible = false)
			{
				if(threadId == 0) throw new ArgumentException("0 threadId.");
				return Lib.EnumWindows(Lib.EnumWindowsAPI.EnumThreadWindows, onlyVisible, sortFirstVisible, threadId: threadId);

				//speed: 2.5 times faster than EnumWindows. Tested with a foreign thread with 30 windows.
			}
		}

		/// <summary>
		/// Internal static functions.
		/// </summary>
		internal static partial class Lib
		{
			internal enum EnumWindowsAPI { EnumWindows, EnumThreadWindows, EnumChildWindows, }

			internal static Wnd[] EnumWindows(EnumWindowsAPI api,
				bool onlyVisible, bool sortFirstVisible, Wnd wParent = default, bool directChild = false, int threadId = 0)
			{
				using(var a = EnumWindows2(api, onlyVisible, sortFirstVisible, wParent, directChild, threadId)) {
					return a.ToArray();
				}
			}

			/// <summary>
			/// For EnumWindows2.
			/// </summary>
			internal delegate bool EnumCallback(Wnd w, object param);

			/// <summary>
			/// This version creates much less garbage (the garbage would be the returned managed array).
			/// The caller must dispose the returned LibArrayBuilder.
			/// </summary>
			internal static Util.LibArrayBuilder<Wnd> EnumWindows2(EnumWindowsAPI api,
				bool onlyVisible, bool sortFirstVisible, Wnd wParent = default, bool directChild = false, int threadId = 0,
				EnumCallback predicate = null, object param = default)
			{
				using(var d = new _WndEnum(api, onlyVisible, directChild, wParent, predicate, param)) {
					d.Enumerate(threadId);

					//CONSIDER: sort not only visible first, but also non-popup, non-toolwindow. Eg now finds a QM toolbar instead of Firefox.
					if(sortFirstVisible && !onlyVisible) {
						if(t_sort == null) t_sort = new WeakReference<List<Wnd>>(null);
						if(!t_sort.TryGetTarget(out var aVisible)) t_sort.SetTarget(aVisible = new List<Wnd>(250));
						else aVisible.Clear();
						int n = d.a.Count;
						for(int i = 0; i < n; i++) {
							var w = d.a[i]; if(!(api == EnumWindowsAPI.EnumChildWindows ? w.IsVisible : w.IsVisibleEx)) continue;
							aVisible.Add(w);
							d.a[i] = default;
						}
						for(int i = n - 1, j = i; i >= 0; i--) {
							var w = d.a[i];
							if(!w.Is0) d.a[j--] = w;
						}
						for(int i = 0; i < aVisible.Count; i++) d.a[i] = aVisible[i];
					}

					d.DoNotDisposeArray();
					return d.a;
				}
			}
			[ThreadStatic] static WeakReference<List<Wnd>> t_sort;

			//Used for API EnumWindows etc lParam instead of lambda, to avoid garbage.
			struct _WndEnum :IDisposable
			{
				public Util.LibArrayBuilder<Wnd> a;
				Wnd _wParent;
				EnumWindowsAPI _api;
				bool _onlyVisible, _directChild, _disposeArray;
				EnumCallback _predicate;
				object _param;

				public _WndEnum(EnumWindowsAPI api, bool onlyVisible, bool directChild, Wnd wParent, EnumCallback predicate, object param)
				{
					a = default;
					_disposeArray = true;
					_api = api;
					_onlyVisible = onlyVisible;
					_directChild = directChild;
					_wParent = wParent;
					_predicate = predicate;
					_param = param;
				}

				public void Dispose()
				{
					if(_disposeArray) a.Dispose();
				}

				public void DoNotDisposeArray() => _disposeArray = false;

				delegate int WndEnumProcT(Wnd w, ref _WndEnum d);

				static int _WndEnumProc(Wnd w, ref _WndEnum d) => d._WndEnumProc(w);
				static WndEnumProcT _wndEnumProc = _WndEnumProc;

				int _WndEnumProc(Wnd w)
				{
					if(_api == EnumWindowsAPI.EnumChildWindows) {
						if(_onlyVisible && !w.IsVisible) return 1;
						if(_directChild && Api.GetParent(w) != _wParent) return 1;
					} else {
						if(_onlyVisible && !w.IsVisibleEx) return 1;
						if(!_wParent.Is0 && w.Owner != _wParent) return 1;
					}
					if(_predicate != null && !_predicate(w, _param)) return 1;
					a.Add(w);
					return 1;
				}

				public bool Enumerate(int threadId)
				{
					bool ok = false;
					switch(_api) {
					case EnumWindowsAPI.EnumWindows:
						ok = EnumWindows(_wndEnumProc, ref this);
						break;
					case EnumWindowsAPI.EnumThreadWindows:
						ok = EnumThreadWindows(threadId, _wndEnumProc, ref this);
						break;
					case EnumWindowsAPI.EnumChildWindows:
						ok = EnumChildWindows(_wParent, _wndEnumProc, ref this);
						break;
					}
					return ok;

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

				[DllImport("user32.dll", SetLastError = true)]
				static extern bool EnumWindows(WndEnumProcT lpEnumFunc, ref _WndEnum d);

				[DllImport("user32.dll", SetLastError = true)]
				static extern bool EnumThreadWindows(int dwThreadId, WndEnumProcT lpfn, ref _WndEnum d);

				[DllImport("user32.dll", SetLastError = true)]
				static extern bool EnumChildWindows(Wnd hWndParent, WndEnumProcT lpEnumFunc, ref _WndEnum d);
			}
		}

		/// <summary>
		/// An enumerable list of Wnd for <see cref="Finder._FindInList"/> and <see cref="ChildFinder._FindInList"/>.
		/// Holds Util.LibArrayBuilder or IEnumerator or single Wnd or none.
		/// Must be disposed if it is Util.LibArrayBuilder or IEnumerator, else disposing is optional.
		/// </summary>
		struct _WndList :IDisposable
		{
			internal enum ListType { None, ArrayBuilder, Enumerator, SingleWnd }

			ListType _t;
			int _i;
			Wnd _w;
			IEnumerator<Wnd> _en;
			Util.LibArrayBuilder<Wnd> _ab;

			internal _WndList(Util.LibArrayBuilder<Wnd> ab) : this()
			{
				_ab = ab;
				_t = ListType.ArrayBuilder;
			}

			internal _WndList(IEnumerable<Wnd> en) : this()
			{
				var e = en?.GetEnumerator();
				if(e != null) {
					_en = e;
					_t = ListType.Enumerator;
				}
			}

			internal _WndList(Wnd w) : this()
			{
				if(!w.Is0) {
					_w = w;
					_t = ListType.SingleWnd;
				}
			}

			internal ListType Type => _t;

			internal bool Next(out Wnd w)
			{
				w = default;
				switch(_t) {
				case ListType.ArrayBuilder:
					if(_i == _ab.Count) return false;
					w = _ab[_i++];
					break;
				case ListType.Enumerator:
					if(!_en.MoveNext()) return false;
					w = _en.Current;
					break;
				case ListType.SingleWnd:
					if(_i > 0) return false;
					_i = 1; w = _w;
					break;
				default:
					return false;
				}
				return true;
			}

			public void Dispose()
			{
				switch(_t) {
				case ListType.ArrayBuilder: _ab.Dispose(); break;
				case ListType.Enumerator: _en.Dispose(); break;
				}
			}
		}
	}
}

namespace Au.Types
{
	/// <summary>
	/// 'flags' parameter of <see cref="Wnd.Find"/>.
	/// </summary>
	[Flags]
	public enum WFFlags
	{
		/// <summary>
		/// Can find hidden windows. See <see cref="Wnd.IsVisibleEx"/>.
		/// Use this carefully. Always use className, not just name, because there are many hidden tooltip windows etc that could match the name.
		/// </summary>
		HiddenToo = 1,

		/// <summary>
		/// Skip cloaked windows. See <see cref="Wnd.IsCloaked"/>.
		/// Cloaked are windows hidden not in the classic way, therefore Wnd.IsVisible does not detect it, but Wnd.IsCloaked detects. For example, windows on inactive Windows 10 virtual desktops; inactive Windows Store apps on Windows 8.
		/// </summary>
		SkipCloaked = 2,
	}

	/// <summary>
	/// <i>programEtc</i> of <see cref="Wnd.Find"/>.
	/// </summary>
	public struct WFEtc
	{
		object _o;
		WFEtc(object o) => _o = o;

		/// <summary>Program name like "notepad.exe", or null.</summary>
		public static implicit operator WFEtc(string program) => new WFEtc(program);

		/// <summary>Process id.</summary>
		public static WFEtc Process(int processId) => new WFEtc(processId);

		/// <summary>Thread id.</summary>
		public static WFEtc Thread(int threadId) => new WFEtc((uint)threadId);

		/// <summary>Owner window.</summary>
		public static WFEtc Owner(AnyWnd ownerWindow) => new WFEtc(ownerWindow);

		/// <summary>
		/// Gets program name or process id or thread id or owner window.
		/// Other variables will be null/0.
		/// </summary>
		/// <exception cref="ArgumentException">The value is "" or 0.</exception>
		public void GetValue(out Wildex program, out int pid, out int tid, out Wnd owner)
		{
			program = null; pid = 0; tid = 0; owner = default;
			switch(_o) {
			case string s:
				if(s.Length == 0) throw new ArgumentException("Program name cannot be \"\". Use null to match any.");
				program = s;
				break;
			case int i:
				if(i == 0) throw new ArgumentException("0 process id");
				pid = i;
				break;
			case uint i:
				if(i == 0) throw new ArgumentException("0 thread id");
				tid = (int)i;
				break;
			case AnyWnd aw:
				var w = aw.Wnd;
				if(w.Is0) throw new ArgumentException("0 window handle");
				owner = w;
				break;
			}
		}
	}
}