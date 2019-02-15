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
//using System.Windows.Forms;
//using System.Drawing;
//using System.Linq;
//using System.Xml.Linq;

using Au;
using Au.Types;
using static Au.NoClass;

#pragma warning disable CS1591 // Missing XML comment //TODO

namespace Au.Triggers
{
	public class WindowTriggers : ITriggers
	{
		class _TriggerEtc : TriggerBase
		{
			public readonly Wnd.Finder finder;

			public _TriggerEtc(Triggers triggers, Action<WindowTriggerArgs> action, Wnd.Finder finder) : base(triggers, action, false)
			{
				this.finder = finder;
			}

			public override void Run(TriggerArgs args) => RunT(args as WindowTriggerArgs);

			public override string TypeString() => "Window"; //TODO

			public override string ShortString() => finder.ToString(); //TODO
		}

		Triggers _triggers;
		ETriggerType _ttype;
		List<_TriggerEtc> _a = new List<_TriggerEtc>();

		internal WindowTriggers(Triggers triggers, ETriggerType ttype)
		{
			_triggers = triggers;
			_ttype = ttype;
		}

		public Action<WindowTriggerArgs> this[string name, string cn = null, WF3 program = default, Func<Wnd, bool> also = null, object contains = null] {
			set {
				var f = new Wnd.Finder(name, cn, program, WFFlags.HiddenToo, also, contains);
				var t = new _TriggerEtc(_triggers, value, f);
				_a.Add(t);
			}
		}

		bool ITriggers.HasTriggers => _a.Count > 0;

		void ITriggers.StartStop(bool start)
		{

		}
	}

	public class WindowTriggerArgs : TriggerArgs
	{
		public Wnd Window { get; set; }
		//public WindowTriggers.TFlags Flags { get; set; }
		//public WindowTriggerArgs(Wnd w, WindowTriggers.TFlags flags)
		//{
		//	Window = w; Flags = flags;
		//}
	}
}
