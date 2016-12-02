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

using static Catkeys.NoClass;
using Util = Catkeys.Util;
using Catkeys.Winapi;

namespace Catkeys
{
	/// <summary>
	/// Writes text to the output pane.
	/// In console process writes to the console window by default.
	/// </summary>
	[DebuggerStepThrough]
	public static class Output
	{
		static readonly bool _isConsole = Console.OpenStandardInput(1) != Stream.Null;

		/// <summary>
		/// Returns true if this is a console process.
		/// </summary>
		public static bool IsConsoleProcess { get { return _isConsole; } }

		/// <summary>
		/// If true, Output.WriteX and Output.Clear functions will use output even in console process.
		/// This is not applied to Console.Write and other Console class functions.
		/// </summary>
		public static bool AlwaysOutput { get; set; }

		static Wnd _hwndEditor;

		static bool _InitHwndEditor()
		{
			if(!_hwndEditor.IsValid) _hwndEditor = Api.FindWindow("QM_Editor", null);
			return !_hwndEditor.Is0;
		}

		/// <summary>
		/// Clears output pane or console text.
		/// </summary>
		public static void Clear()
		{
			if(_isConsole && !AlwaysOutput) Console.Clear();
			else if(_InitHwndEditor()) _hwndEditor.Send(Api.WM_SETTEXT, -1, null);
		}

		/// <summary>
		/// Writes value to the output pane or console.
		/// </summary>
		public static void Write(string value) { Writer.WriteLine(value); }

		/// <summary>
		/// Writes value.ToString() to the output pane or console.
		/// </summary>
		public static void Write(object value)
		{
			Write(value?.ToString());
		}

		/// <summary>
		/// Writes array, List˂T˃ or other generic collection as a list of element values.
		/// </summary>
		public static void Write<T>(IEnumerable<T> value, string separator = "\r\n")
		{
			Write((value==null) ? "" : string.Join(separator, value));
		}

		/// <summary>
		/// Writes non-generic collection as a list of element values.
		/// </summary>
		public static void Write(System.Collections.IEnumerable value, string separator = "\r\n")
		{
			if(value==null) { Write(""); return; }
			var b = new StringBuilder();
			foreach(object o in value) {
				if(b.Length != 0) b.Append(separator);
				b.Append(o?.ToString());
			}
			Write(b.ToString());
		}

		/// <summary>
		/// Writes dictionary as a list of [key, value].
		/// </summary>
		public static void Write<K, V>(IDictionary<K, V> value, string separator = "\r\n")
		{
			Write((value == null) ? "" : string.Join(separator, value));
		}

		/// <summary>
		/// Writes multiple argument values using separator ", ".
		/// </summary>
		public static void WriteList(params object[] values) { Write(String_.Join(", ", values)); }

		/// <summary>
		/// Writes multiple argument values using the specified separator.
		/// </summary>
		public static void WriteListSep(string separator, params object[] values) { Write(String_.Join(separator, values)); }

		/// <summary>
		/// Writes an integer in hexadecimal format, like "0x5A".
		/// </summary>
		public static void WriteHex(object value) { Write($"0x{value:X}"); }
		//note: this is slower and less "correct" way, but with Write("0x" + value.ToString("X")); we'd need overloads for all 8 integer types. This func is not so important.

		/// <summary>
		/// Writes a string prefixed with "Warning: " and optionally followed by the stack trace.
		/// </summary>
		/// <param name="s">Warning text.</param>
		/// <param name="showStackFromThisFrame">If ˃= 0, appends stack trace, skipping this number of frames.</param>
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Warning(string s, int showStackFromThisFrame = -1)
		{
			var sb = new StringBuilder("Warning: ");
			sb.Append(s);
			if(showStackFromThisFrame >= 0) {
				var x = new StackTrace(showStackFromThisFrame + 1, false);
				sb.AppendLine();
				sb.Append(x.ToString());
			}
			Out(sb.ToString());
			sb.Clear();
		}

		//Note:
		//In library don't redirect console and don't use Console.WriteLine.
		//Because there is no way to auto-run a class library initialization code that would redirect console.
		//Static ctors run before the class is used first time, not when assembly loaded.
		//Instead use Out() (Output.Write()).
		//To redirect Console.WriteLine used in scripts, call Output.RedirectConsoleOutput in script code.

		//Used to redirect Console and Debug/Trace output.
		class _OutputWriter :TextWriter
		{
			public override void WriteLine(string value) { WriteDirectly(value); }
			public override void Write(string value) { WriteDirectly(value); }
			public override Encoding Encoding { get { return Encoding.Unicode; } }
		}

		static TextWriter _writer;

		/// <summary>
		/// Gets or sets object that actually writes text when your script or the automation library calls Output.Write or Write.
		/// If you want to redirect or modify output text (for example write to a file or add time), use code like this:
		/// <c>Output.Writer=myWriter;</c>, where myWriter is a variable of your class that is derived from TextWriter and overrides its functions WriteLine, Write and Encoding.
		/// It is like redirecting console output with code <c>Console.SetOut(myWriter);</c> (google for more info).
		/// Usually the best place to redirect is in static ScriptClass() {  }.
		/// Redirection is applied to whole script appdomain, and does not affect other scripts.
		/// Redirection affects Write, RedirectConsoleOutput, RedirectDebugOutput, AlwaysOutput and all OutX. It does not affect WriteDirectly and Clear.
		/// Don't call Output.Write and Write in your writer class; it would create stack overflow; if need, use Output.WriteDirectly.
		/// </summary>
		public static TextWriter Writer
		{
			get { return _writer ?? (_writer = new _OutputWriter()); }
			set { _writer = value; }
		}

		/// <summary>
		/// Writes string value to the output or console.
		/// Unlike other Write methods, this function ignores custom Writer used to redirect output, therefore can be used in your custom writer class.
		/// </summary>
		public static void WriteDirectly(string value)
		{
			if(_isConsole && !AlwaysOutput) Console.WriteLine(value);
			else if(_InitHwndEditor()) _hwndEditor.SendS(Api.WM_SETTEXT, -1, value == null ? "" : value);
		}

		static bool _consoleRedirected, _debugRedirected;

		/// <summary>
		/// Let Console.WriteX methods write to the output, unless this is a console process (even if AlwaysOutput is true).
		/// Console.Write will write line, like Console.WriteLine.
		/// Note: Console.Clear will not clear output; it will throw exception; use Output.Clear or try/catch.
		/// </summary>
		public static void RedirectConsoleOutput()
		{
			if(_consoleRedirected || _isConsole) return;
			_consoleRedirected = true;
			Console.SetOut(Writer);
			//speed: 870
		}

		/// <summary>
		/// Let Debug.WriteX and Trace.WriteX methods write to the output or console.
		/// To write to the output even in console process, set Output.AlwaysOutput=true; before calling this method first time.
		/// </summary>
		public static void RedirectDebugOutput()
		{
			if(_debugRedirected) return;
			_debugRedirected = true;
			Trace.Listeners.Add((_isConsole && !AlwaysOutput) ? (new ConsoleTraceListener()) : (new TextWriterTraceListener(Writer)));
			//speed: 6100
		}

		//TODO: WriteStatusBar()
	}
}
