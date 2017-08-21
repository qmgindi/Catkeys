﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
using System.Drawing;
//using System.Linq;

using Catkeys;
using static Catkeys.NoClass;

namespace Catkeys.Util
{
	/// <summary>
	/// A message loop, alternative to Application.Run which does not support nested loops.
	/// </summary>
	public class MessageLoop
	{
		IntPtr _loopEndEvent;

		/// <summary>
		/// Runs a message loop.
		/// </summary>
		public void Loop()
		{
			using(new Util.LibEnsureWindowsFormsSynchronizationContext(true)) {
				_loopEndEvent = Api.CreateEvent(Zero, true, false, null);
				try {
					Application.DoEvents(); //info: with Time.DoEvents something does not work, don't remember.

					for(;;) {
						//this would not respond to Thread.Abort
						//uint k = Api.MsgWaitForMultipleObjectsEx(1, ref _loopEndEvent, Api.INFINITE, Api.QS_ALLINPUT);
						//this responds to Thread.Abort
						uint k = Api.MsgWaitForMultipleObjectsEx(1, ref _loopEndEvent, 100, Api.QS_ALLINPUT);
						if(k == Api.WAIT_TIMEOUT) continue;

						Application.DoEvents();
						if(k == Api.WAIT_OBJECT_0 || k == Api.WAIT_FAILED) break; //note: this is after DoEvents because may be posted messages when stopping loop. Although it seems that MsgWaitForMultipleObjects returns events after all messages.

						if(Api.PeekMessage(out var _, default(Wnd), Api.WM_QUIT, Api.WM_QUIT, Api.PM_NOREMOVE)) break; //DoEvents() reposts it. If we don't break, MsgWaitForMultipleObjects retrieves it before (instead) the event, causing endless loop.
					}
				}
				finally {
					Api.CloseHandle(_loopEndEvent);
					_loopEndEvent = Zero;
				}
			}
		}

		/// <summary>
		/// Ends the message loop, causing Loop() to return.
		/// </summary>
		public void Stop()
		{
			if(_loopEndEvent != Zero) {
				Api.SetEvent(_loopEndEvent);
			}
		}
	}

	/// <summary>
	/// Constructor ensures that current SynchronizationContext of this thread is WindowsFormsSynchronizationContext.
	/// Also sets WindowsFormsSynchronizationContext.AutoInstall=false to prevent Application.DoEvents etc setting wrong context.
	/// Dispose() restores both if need. Does not restore context if was null.
	/// Example: using(new Util.LibEnsureWindowsFormsSynchronizationContext()) { ... }
	/// </summary>
	[DebuggerStepThrough]
	class LibEnsureWindowsFormsSynchronizationContext :IDisposable
	{
		[ThreadStatic] static WindowsFormsSynchronizationContext t_wfContext;
		SynchronizationContext _prevContext;
		bool _restoreContext, _prevAutoInstall;

		/// <summary>
		/// See class help.
		/// </summary>
		/// <param name="onlyIfAutoInstall">Do nothing if WindowsFormsSynchronizationContext.AutoInstall==false. Normally WindowsFormsSynchronizationContext.AutoInstall is true (default).</param>
		public LibEnsureWindowsFormsSynchronizationContext(bool onlyIfAutoInstall = false)
		{
			if(onlyIfAutoInstall && !WindowsFormsSynchronizationContext.AutoInstall) return;

			//Ensure WindowsFormsSynchronizationContext for this thread.
			_prevContext = SynchronizationContext.Current;
			if(!(_prevContext is WindowsFormsSynchronizationContext)) {
				if(t_wfContext == null) t_wfContext = new WindowsFormsSynchronizationContext();
				SynchronizationContext.SetSynchronizationContext(t_wfContext);
				_restoreContext = _prevContext != null;
			}

			//Workaround for Application.DoEvents/Run bug:
			//	When returning, they set a SynchronizationContext of wrong type, even if previously was WindowsFormsSynchronizationContext.
			//	They don't do it if WindowsFormsSynchronizationContext.AutoInstall == false.
			//	Info: AutoInstall is [ThreadStatic], as well as SynchronizationContext.Current.
			_prevAutoInstall = WindowsFormsSynchronizationContext.AutoInstall;
			if(_prevAutoInstall) WindowsFormsSynchronizationContext.AutoInstall = false;
		}

		public void Dispose()
		{
#if DEBUG
			_disposed = true;
#endif
			if(_restoreContext) {
				_restoreContext = false;
				if(SynchronizationContext.Current == t_wfContext)
					SynchronizationContext.SetSynchronizationContext(_prevContext);
			}
			if(_prevAutoInstall) WindowsFormsSynchronizationContext.AutoInstall = true;
		}
#if DEBUG
		bool _disposed;
		~LibEnsureWindowsFormsSynchronizationContext() { Debug.Assert(_disposed); }
#endif

		/// <summary>
		/// If synchronization context of this thread is null or not WindowsFormsSynchronizationContext, makes it WindowsFormsSynchronizationContext.
		/// Use this instead of creating instance when will not need to restore previous synchronization context.
		/// </summary>
		/// <exception cref="InvalidOperationException">This thread has a synchronization context other than WindowsFormsSynchronizationContext or null. Or it is null and thread's GetApartmentState is not STA.</exception>
		public static WindowsFormsSynchronizationContext EnsurePermanently()
		{
			var c = SynchronizationContext.Current;
			if(!(c is WindowsFormsSynchronizationContext wfc)) {
				if(!(c == null && Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)) {
					Debug.Assert(false);
					throw new InvalidOperationException("this thread has wrong SynchronizationContext type or is not STA");
				}
				if(t_wfContext == null) t_wfContext = new WindowsFormsSynchronizationContext();
				SynchronizationContext.SetSynchronizationContext(t_wfContext);
				WindowsFormsSynchronizationContext.AutoInstall = false; //prevent Application.Run/DoEvents setting wrong context
				wfc = t_wfContext;
			}
			return wfc;
		}
	}

	/// <summary>
	/// Drag-drop functions.
	/// </summary>
	public static class DragDrop
	{
		/// <summary>
		/// Simple non-OLE drag operation.
		/// Returns true if dropped, false if cancelled.
		/// </summary>
		/// <param name="w">Window or control that owns the drag operation.</param>
		/// <param name="mouseButton">Mouse button that is used for the drag operation: Left, Right.</param>
		/// <param name="onMouseKeyMessage">Callback function, called on each received mouse/key message. Optional.</param>
		public static bool SimpleDragDrop(Wnd w, MouseButtons mouseButton = MouseButtons.Left, Action<SimpleDragDropCallbackArgs> onMouseKeyMessage = null)
		{
			Api.SetCapture(w);

			bool R = false;
			var x = new SimpleDragDropCallbackArgs();
			for(;;) {
				if(Api.GetCapture() != w) return false;
				if(Api.GetMessage(out x.Msg, default(Wnd), 0, 0) <= 0) {
					if(x.Msg.message == Api.WM_QUIT) Api.PostQuitMessage(x.Msg.wParam);
					break;
				}

				bool call = false;
				uint m = x.Msg.message;
				if(m >= Api.WM_MOUSEFIRST && m <= Api.WM_MOUSELAST) {
					if(m == Api.WM_LBUTTONUP) {
						if(R = (mouseButton & MouseButtons.Left) != 0) break;
					} else if(m == Api.WM_RBUTTONUP) {
						if(R = (mouseButton & MouseButtons.Right) != 0) break;
					}
					call = true;
				} else if(m == Api.WM_KEYDOWN || m == Api.WM_KEYUP || m == Api.WM_SYSKEYDOWN || m == Api.WM_SYSKEYUP) {
					//on key down/up caller may want to update cursor when eg Ctrl pressed/released
					if(x.Msg.wParam == Api.VK_ESCAPE) break;
					call = true;
				}

				if(call && onMouseKeyMessage != null) {
					onMouseKeyMessage(x);
					if(x._stopped) break;
					if(x.Cursor != null) {
						Api.SetCursor(x.Cursor.Handle);
						x.Cursor = null;
					}
				}

				Api.DispatchMessage(ref x.Msg);
			}

			Api.ReleaseCapture();
			return R;
		}

		/// <summary>
		/// Simple non-OLE drag operation.
		/// Returns true if dropped, false if cancelled.
		/// </summary>
		/// <param name="c">Window or control that owns the drag operation.</param>
		/// <param name="mouseButton">Mouse button that is used for the drag operation: Left, Right.</param>
		/// <param name="onMouseKeyMessage">Callback function, called on each received mouse/key message. Optional.</param>
		public static bool SimpleDragDrop(Control c, MouseButtons mouseButton = MouseButtons.Left, Action<SimpleDragDropCallbackArgs> onMouseKeyMessage = null)
		{
			return SimpleDragDrop((Wnd)c, mouseButton, onMouseKeyMessage);
		}

		/// <summary>
		/// <see cref="SimpleDragDrop(Control, MouseButtons, Action{SimpleDragDropCallbackArgs})"/> callback function arguments.
		/// </summary>
		/// <tocexclude />
		public class SimpleDragDropCallbackArgs
		{
			/// <summary>
			/// Current message retrieved by API <msdn>GetMessage</msdn>.
			/// </summary>
			public Native.MSG Msg;

			/// <summary>
			/// The callback function can set this to temporarily set cursor.
			/// </summary>
			public Cursor Cursor;

			/// <summary>
			/// The callback function can call this to end the operation.
			/// </summary>
			public void Stop() { _stopped = true; }
			internal bool _stopped;
		}
	}
}
