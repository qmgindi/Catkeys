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

using Au.Types;

namespace Au
{
	/// <summary>
	/// Timer that calls callback function in same thread, which must have a message loop.
	/// </summary>
	/// <remarks>
	/// Uses API <msdn>SetTimer</msdn> and <msdn>WM_TIMER</msdn>.
	/// Works only in threads that have a message loop which retrieves/dispatches posted messages. For example threads with windows (except console).
	/// Timer action delegates are protected from GC.
	/// </remarks>
	/// <example>
	/// This example sets 3 timers.
	/// <code><![CDATA[
	/// timerm.after(500, _ => print.it("after 500 ms"));
	/// timerm.every(1000, _ => print.it("every 1000 ms"));
	/// var t3 = new timerm(_ => print.it("after 3000 ms")); t3.After(3000); //the same as timerm.after
	/// dialog.show("timer"); //shows a window and waits until closed. While waiting, retrieves/dispatches messages in its own message loop.
	/// ]]></code>
	/// </example>
	public class timerm
	{
		readonly Action<timerm> _action;
		nint _id;
		int _threadId;
		bool _singlePeriod;

		//To control object lifetime we use a thread-static Dictionary.
		//Tried GCHandle, but could not find a way to delete object when thread ends.
		//Calling KillTimer when thread ends is optional. Need just to re-enable garbage collection for this object.
		[ThreadStatic] static Dictionary<nint, timerm> t_timers;

		/// <summary>
		/// Sets callback function.
		/// </summary>
		public timerm(Action<timerm> timerAction) {
			_action = timerAction;
		}
		//SHOULDDO: add overload with hwnd. Or optional parameter.

		/// <summary>
		/// Something to attach to this variable.
		/// </summary>
		public object Tag { get; set; }

		/// <summary>
		/// true if the timer is started and not stopped.
		/// Note: single-period timer is automatically stopped before calling the callback function.
		/// </summary>
		public bool IsRunning => _id != default;

		/// <summary>
		/// Starts one-time timer. If already started, resets and changes its period.
		/// </summary>
		/// <param name="milliseconds">Time interval after which to call the callback function. The actual minimal interval is 10-20 ms.</param>
		/// <exception cref="ArgumentOutOfRangeException">Negative.</exception>
		/// <exception cref="InvalidOperationException">Called not in the same thread as previous <b>Start</b>.</exception>
		/// <exception cref="Win32Exception">API <msdn>SetTimer</msdn> returned 0. Unlikely.</exception>
		/// <remarks>
		/// The timer will be stopped before calling the callback function. The callback function can start it again.
		/// If already started, this function must be called in the same thread as when started.
		/// </remarks>
		public void After(int milliseconds) => _Start(true, milliseconds);

		/// <summary>
		/// Starts periodic timer. If already started, resets and changes its period.
		/// </summary>
		/// <param name="milliseconds">Time interval (period) of calling the callback function. The actual minimal period is 10-20 ms.</param>
		/// <exception cref="ArgumentOutOfRangeException">Negative.</exception>
		/// <exception cref="InvalidOperationException">Called not in the same thread as previous <b>Start</b>.</exception>
		/// <exception cref="Win32Exception">API <msdn>SetTimer</msdn> returned 0. Unlikely.</exception>
		/// <remarks>
		/// The callback function can stop the timer or restart with different period.
		/// If already started, this function must be called in the same thread as when started.
		/// </remarks>
		public void Every(int milliseconds) => _Start(false, milliseconds);

		void _Start(bool singlePeriod, int milliseconds) {
			if (milliseconds < 0) throw new ArgumentOutOfRangeException();
			bool isNew = _id == 0;
			if (!isNew) _ThreadTrap();
			nint r = Api.SetTimer(default, _id, milliseconds, s_timerProc);
			if (r == 0) throw new Win32Exception();
			Debug.Assert(isNew || r == _id);
			_id = r;
			_singlePeriod = singlePeriod;
			if (isNew) {
				_threadId = Thread.CurrentThread.ManagedThreadId;
				(t_timers ??= new Dictionary<nint, timerm>()).Add(_id, this);
			}
			//print.it($"Start: {_id}  isNew={isNew}  singlePeriod={singlePeriod}  _threadId={_threadId}");
		}

		static readonly Api.TIMERPROC s_timerProc = _TimerProc;
		static void _TimerProc(wnd w, int msg, nint idEvent, uint time) {
			//print.it(t_timers.Count, idEvent);
			if (!t_timers.TryGetValue(idEvent, out var t)) {
				//Debug_.Print($"timer id {idEvent} not in t_timers");
				return;
				//It is possible after killing timer.
				//	Normally API KillTimer removes WM_TIMER message from queue (tested), but in some conditions our callback can still be called several times.
				//	For example if multiple messages are retrieved from the OS queue without dispatching each, and then all are dispatched.
				//	Usually we can safely ignore it. But not good if the same timer id is reused for another timer. Tested on Win10: OS does not reuse ids soon.
			}
			if (t._singlePeriod) t.Stop();

			try { t._action(t); }
			catch (Exception ex) { print.warning(ex.ToString(), -1); }
			//info: OS handles exceptions in timer procedure.
		}

		/// <summary>
		/// Stops the timer.
		/// </summary>
		/// <exception cref="InvalidOperationException">Called not in the same thread as <b>Start</b>.</exception>
		/// <remarks>
		/// The callback function will not be called after this.
		/// Later you can start the timer again (call <see cref="After(int)"/> or <see cref="Every(int)"/>).
		/// Don't need to call this function for single-period timers. For periodic timers it is optional; the timer stops when the thread ends.
		/// This function must be called in the same thread as <b>Start</b>.
		/// </remarks>
		public void Stop() {
			if (_id != 0) {
				//print.it($"Stop: {_id}          _threadId={_threadId}");
				_ThreadTrap();
				Api.KillTimer(default, _id);
				//tested: KillTimer removes pending WM_TIMER messages from queue. MSDN lies. Tested on Win 10 and 7.
				t_timers.Remove(_id);
				_id = 0;
			}
		}

		/// <summary>
		/// Execute the timer action now.
		/// </summary>
		/// <remarks>
		/// Does not change any properties. Just calls the callback function. Does not handle exceptions.
		/// </remarks>
		public void Now() => _action(this);

		void _ThreadTrap() {
			bool isSameThread = _threadId == Thread.CurrentThread.ManagedThreadId;
			Debug.Assert(isSameThread);
			if (!isSameThread) throw new InvalidOperationException(nameof(timerm) + " used in multiple threads.");
			//FUTURE: somehow allow other thread. It is often useful.
		}

		//~timerm() { print.it("dtor"); } //don't call Stop() here, we are in other thread

		static timerm _StartNew(bool singlePeriod, int milliseconds, Action<timerm> timerAction, object tag = null) {
			var t = new timerm(timerAction) { Tag = tag };
			t._Start(singlePeriod, milliseconds);
			return t;
		}

		/// <summary>
		/// Creates and starts new one-time timer.
		/// Returns new <see cref="timerm"/> object. Usually you don't need it.
		/// </summary>
		/// <param name="milliseconds">Time interval after which to call the callback function. The actual minimal interval is 10-20 ms.</param>
		/// <param name="timerAction">Callback function.</param>
		/// <param name="tag">Something to pass to the callback function as <see cref="Tag"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Negative.</exception>
		/// <exception cref="Win32Exception">API <msdn>SetTimer</msdn> returned 0. Unlikely.</exception>
		/// <remarks>
		/// The timer will be stopped before calling the callback function. The callback function can start it again.
		/// The callback function will be called in this thread.
		/// This thread must must get/dispatch posted messages, eg call Application.Run() or Form.ShowModal() or dialog.show(). The callback function is not called while this thread does not do it.
		/// </remarks>
		public static timerm after(int milliseconds, Action<timerm> timerAction, object tag = null)
			=> _StartNew(true, milliseconds, timerAction, tag);

		/// <summary>
		/// Creates and starts new periodic timer.
		/// Returns new <see cref="timerm"/> object that can be used to modify timer properties if you want to do it not in the callback function; usually don't need it.
		/// </summary>
		/// <param name="milliseconds">Time interval (period) of calling the callback function. The actual minimal period is 10-20 ms.</param>
		/// <param name="timerAction">Callback function.</param>
		/// <param name="tag">Something to pass to the callback function as <see cref="Tag"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Negative.</exception>
		/// <exception cref="Win32Exception">API <msdn>SetTimer</msdn> returned 0. Unlikely.</exception>
		/// <remarks>
		/// The callback function can stop the timer or restart with different period.
		/// The callback function will be called in this thread.
		/// This thread must must get/dispatch posted messages, eg call Application.Run() or Form.ShowModal() or dialog.show(). The callback function is not called while this thread does not do it.
		/// </remarks>
		public static timerm every(int milliseconds, Action<timerm> timerAction, object tag = null)
			=> _StartNew(false, milliseconds, timerAction, tag);
	}
}
