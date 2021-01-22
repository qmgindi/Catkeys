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

using Au.Types;

namespace Au.Util
{
	/// <summary>
	/// Functions for high-DPI screen support.
	/// </summary>
	/// <remarks>
	/// To find DPI % on Windows 10: Settings -> System -> Display -> Scale and layout. If not 100%, it means high DPI. On older Windows versions it is in Control Panel -> Display.
	/// </remarks>
	public static class ADpi
	{
		/// <summary>
		/// Gets DPI of the primary screen at the time this process started.
		/// </summary>
		/// <remarks>
		/// Used by API that aren't per-monitor DPI aware.
		/// </remarks>
		public static int OfThisProcess {
			get {
				if (_processDPI == 0) {
					using var dcs = new ScreenDC_();
					_processDPI = Api.GetDeviceCaps(dcs, 90); //LOGPIXELSY
				}
				return _processDPI;
			}
		}
		static int _processDPI;

		/// <summary>
		/// Gets DPI of a window.
		/// Returns <see cref="OfThisProcess"/> if fails or if not supported on this Windows version (see <see cref="SupportPMOnWin8_1"/>).
		/// </summary>
		/// <remarks>
		/// Usually it is DPI of the screen where currently is the window. If control - where is its top-level parent.
		/// On Windows 10 1607 and later uses API <msdn>GetDpiForWindow</msdn>. On Windows 8.1 and early Windows 10, if <see cref="SupportPMOnWin8_1"/> is true, uses API <msdn>GetDpiForMonitor</msdn>; it's less reliable. Older Windows versions don't support per-monitor DPI.
		/// </remarks>
		public static int OfWindow(AWnd w) {
			int R = 0;
			if (!w.Is0) {
				if (AVersion.MinWin10_1607) R = Api.GetDpiForWindow(w);
				else if (SupportPMOnWin8_1 && AVersion.MinWin8_1) return AScreen.Of(w.Window).Dpi;
			}
			return R != 0 ? R : OfThisProcess;
		}

		/// <summary>
		/// Returns <c>OfWindow(w.Hwnd())</c>.
		/// </summary>
		public static int OfWindow(System.Windows.Forms.Control w) => OfWindow(w.Hwnd());

		/// <summary>
		/// Returns <c>OfWindow(w.Hwnd())</c>.
		/// </summary>
		public static int OfWindow(System.Windows.DependencyObject w) => OfWindow(w.Hwnd());

		/// <summary>
		/// Gets DPI of a screen.
		/// Returns <see cref="OfThisProcess"/> if fails or if not supported on this Windows version (see <see cref="SupportPMOnWin8_1"/>).
		/// </summary>
		/// <remarks>
		/// Uses API <msdn>GetDpiForMonitor</msdn>.
		/// </remarks>
		/// <seealso cref="AScreen.Of"/>
		public static int OfScreen(IntPtr hMonitor) {
			return
				(SupportPMOnWin8_1 ? AVersion.MinWin8_1 : AVersion.MinWin10_1607) && 0 == Api.GetDpiForMonitor(hMonitor, 0, out int R, out _)
				? R
				: OfThisProcess;
		}

		/// <summary>
		/// If true, <see cref="OfScreen"/> and <see cref="OfWindow"/> will support per-monitor DPI on Windows 8.1 and later.
		/// If false (default) - on Windows 10 1607 and later; on 8.1 will always return <see cref="OfThisProcess"/>.
		/// </summary>
		/// <remarks>
		/// This is a per-thread property. You can change/restore it before/after calling these functions.
		/// Before Windows 10 1607 it is difficult to support per-monitor DPI because of lack of API and OS/.NET support.
		/// </remarks>
		[field: ThreadStatic]
		public static bool SupportPMOnWin8_1 { get; set; }

		///// <summary>
		///// Gets small icon size that depends on DPI of the primary screen.
		///// Width and Height are <see cref="OfThisProcess"/>/6, which is 16 if DPI is 96 (100%).
		///// </summary>
		//internal static SIZE SmallIconSize_ { get { var t = OfThisProcess / 6; return new SIZE(t, t); } } //same as AIcon.SizeSmall

		/// <summary>
		/// Scales <b>int</b> if <i>dpiOf.Dpi</i> isn't 96 (100%).
		/// </summary>
		public static int Scale(int i, DpiOf dpiOf) => AMath.MulDiv(i, dpiOf, 96);

		/// <summary>
		/// Unscales <b>int</b> if <i>dpiOf.Dpi</i> isn't 96 (100%).
		/// </summary>
		public static double Unscale(int i, DpiOf dpiOf) => i * (96d / dpiOf);
		//Unscaling sometimes useful with WPF. Unscale to double, not int, else result often incorrect.

		/// <summary>
		/// Scales two int if <i>dpiOf.Dpi</i> isn't 96 (100%).
		/// </summary>
		public static (int, int) Scale(int i1, int i2, DpiOf dpiOf) {
			int dpi = dpiOf;
			return (AMath.MulDiv(i1, dpi, 96), AMath.MulDiv(i2, dpi, 96));
		}

		/// <summary>
		/// Scales <b>SIZE</b> if <i>dpiOf.Dpi</i> isn't 96 (100%).
		/// </summary>
		public static SIZE Scale(SIZE z, DpiOf dpiOf) {
			int dpi = dpiOf;
			z.width = AMath.MulDiv(z.width, dpi, 96);
			z.height = AMath.MulDiv(z.height, dpi, 96);
			return z;
		}

		/// <summary>
		/// Scales <b>System.Windows.Size</b> if <i>dpiOf.Dpi</i> isn't 96 (100%).
		/// </summary>
		public static SIZE Scale(System.Windows.Size z, DpiOf dpiOf) {
			double f = (int)dpiOf / 96d;
			z.Width *= f;
			z.Height *= f;
			return (SIZE)z;
		}

		/// <summary>
		/// Unscales <b>SIZE</b> if <i>dpiOf.Dpi</i> isn't 96 (100%).
		/// </summary>
		public static System.Windows.Size Unscale(SIZE z, DpiOf dpiOf) {
			double f = 96d / dpiOf;
			return new System.Windows.Size(z.width * f, z.height * f);
		}

		/// <summary>
		/// Scales <b>RECT</b> if <i>dpiOf.Dpi</i> isn't 96 (100%).
		/// </summary>
		public static RECT Scale(RECT r, DpiOf dpiOf) {
			int dpi = dpiOf;
			r.left = AMath.MulDiv(r.left, dpi, 96);
			r.top = AMath.MulDiv(r.top, dpi, 96);
			r.right = AMath.MulDiv(r.right, dpi, 96);
			r.bottom = AMath.MulDiv(r.bottom, dpi, 96);
			return r;
		}

		/// <summary>
		/// Unscales <b>RECT</b> if <i>dpiOf.Dpi</i> isn't 96 (100%).
		/// </summary>
		public static System.Windows.Rect Unscale(RECT r, DpiOf dpiOf) {
			double f = 96d / dpiOf;
			return new System.Windows.Rect(r.left * f, r.top * f, r.Width * f, r.Height * f);
		}

		/// <summary>
		/// Calls API <msdn>GetSystemMetricsForDpi</msdn> if available, else <msdn>GetSystemMetrics</msdn>.
		/// </summary>
		public static int GetSystemMetrics(int nIndex, DpiOf dpiOf)
			=> AVersion.MinWin10_1607
			? Api.GetSystemMetricsForDpi(nIndex, dpiOf)
			: Api.GetSystemMetrics(nIndex);

		/// <summary>
		/// Calls API <msdn>SystemParametersInfoForDpi</msdn> if available, else <msdn>SystemParametersInfo</msdn>.
		/// </summary>
		public static bool SystemParametersInfo(uint uiAction, int uiParam, LPARAM pvParam, uint fWinIni, DpiOf dpiOf)
			=> AVersion.MinWin10_1607
			? Api.SystemParametersInfoForDpi(uiAction, uiParam, pvParam, fWinIni, dpiOf)
			: Api.SystemParametersInfo(uiAction, uiParam, pvParam, fWinIni);

		/// <summary>
		/// Calls API <msdn>AdjustWindowRectExForDpi</msdn> if available, else <msdn>AdjustWindowRectEx</msdn>.
		/// </summary>
		public static bool AdjustWindowRectEx(DpiOf dpiOf, ref RECT r, WS style, WS2 exStyle, bool hasMenu = false)
			=> AVersion.MinWin10_1607
			? Api.AdjustWindowRectExForDpi(ref r, style, hasMenu, exStyle, dpiOf)
			: Api.AdjustWindowRectEx(ref r, style, hasMenu, exStyle);

		/// <summary>
		/// Can be used to temporarily change thread's DPI awareness context with API <msdn>SetThreadDpiAwarenessContext</msdn>.
		/// Does nothing if the API is unavailable (added in Windows 10 version 1607).
		/// </summary>
		/// <remarks>
		/// Programs that use this library should use manifest with dpiAwareness = PerMonitorV2 and dpiAware = True/PM. Then default DPI awareness context is DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// using var dac = new ADpi.AwarenessContext(ADpi.AwarenessContext.DPI_AWARENESS_CONTEXT_UNAWARE);
		/// ]]></code>
		/// </example>
		public struct AwarenessContext : IDisposable
		{
			LPARAM _dac;

			/// <summary>
			/// Calls API <msdn>SetThreadDpiAwarenessContext</msdn> if available.
			/// </summary>
			/// <param name="dpiContext">One of <b>DPI_AWARENESS_CONTEXT_X</b> constants, for example <see cref="DPI_AWARENESS_CONTEXT_UNAWARE"/>.</param>
			public AwarenessContext(LPARAM dpiContext) {
				_dac = AVersion.MinWin10_1607 ? Api.SetThreadDpiAwarenessContext(dpiContext) : default;
			}

			/// <summary>
			/// Restores previous DPI awareness context.
			/// </summary>
			public void Dispose() {
				if (_dac == default) return;
				Api.SetThreadDpiAwarenessContext(_dac);
				_dac = default;
			}

			///
			public const int DPI_AWARENESS_CONTEXT_UNAWARE = -1;
			///
			public const int DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = -2;
			///
			public const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = -3;
			///
			public const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
			///
			public const int DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED = -5;
		}
	}
}

namespace Au.Types
{
	using Au.Util;

	/// <summary>
	/// Used for <i>DPI</i> parameter of functions.
	/// Has implicit conversions from int (DPI), AWnd (DPI of window), IntPtr (DPI of screen handle), POINT (DPI of screen containing point), RECT (DPI of screen containing rectangle), forms Control, WPF DependencyObject. The conversion operators set the <see cref="Dpi"/> property and the function can use it.
	/// </summary>
	public struct DpiOf
	{
		int _dpi;

		///
		public DpiOf(int dpi) { _dpi = dpi; }

		/// <exception cref="AuWndException">Invalid window handle.</exception>
		public DpiOf(AWnd w) {
			w.ThrowIfInvalid();
			_dpi = ADpi.OfWindow(w);
		}

		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="AuWndException">Invalid window handle.</exception>
		public DpiOf(System.Windows.Forms.Control c) : this(c?.Hwnd() ?? throw new ArgumentNullException()) { }

		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="AuWndException">Invalid window handle.</exception>
		public DpiOf(System.Windows.DependencyObject c) : this(c?.Hwnd() ?? throw new ArgumentNullException()) { }

		///
		public DpiOf(IntPtr hMonitor) { _dpi = ADpi.OfScreen(hMonitor); }

		///
		public DpiOf(POINT screenOf) : this(AScreen.Of(screenOf).Handle) { }

		///
		public DpiOf(RECT screenOf) : this(AScreen.Of(screenOf).Handle) { }

		///
		public static implicit operator DpiOf(int dpi) => new DpiOf(dpi);

		/// <exception cref="AuWndException">Invalid window handle.</exception>
		public static implicit operator DpiOf(AWnd w) => new DpiOf(w);

		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="AuWndException">Invalid window handle.</exception>
		public static implicit operator DpiOf(System.Windows.Forms.Control c) => new DpiOf(c);

		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="AuWndException">Invalid window handle.</exception>
		public static implicit operator DpiOf(System.Windows.DependencyObject c) => new DpiOf(c);

		///
		public static implicit operator DpiOf(IntPtr hMonitor) => new DpiOf(hMonitor);

		///
		public static implicit operator DpiOf(POINT screenOf) => new DpiOf(screenOf);

		///
		public static implicit operator DpiOf(RECT screenOf) => new DpiOf(screenOf);

		///
		public int Dpi => _dpi;

		///
		public static implicit operator int(DpiOf d) => d._dpi;
	}
}