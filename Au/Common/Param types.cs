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
using Microsoft.Win32;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
//using System.Linq;
//using System.Xml.Linq;

using static Au.NoClass;

namespace Au.Types
{
	/// <summary>
	/// This namespace contains:
	/// <list type="bullet">
	/// <item>Types of function parameters and return values.</item>
	/// <item>Exception classes, some base classes, some other types.</item>
	/// <item>Extension methods for various .NET classes.</item>
	/// </list>
	/// </summary>
	[CompilerGenerated()]
	class NamespaceDoc
	{
		//SHFB uses this for namespace documentation.
	}

	//currently not used
	///// <summary>Infrastructure.</summary>
	///// <tocexclude />
	//[EditorBrowsable(EditorBrowsableState.Never)]
	//public class JustNull { }

	///// <summary>Infrastructure.</summary>
	///// <tocexclude />
	//[EditorBrowsable(EditorBrowsableState.Never)]
	//public class JustNull2 { }

	/// <summary>
	/// Contains x or y coordinate. Used for parameters of functions like Mouse.Move, Wnd.Move.
	/// Allows to easily specify coordinates of these types: normal, reverse (from right or bottom of a rectangle), fractional (fraction of width or height of a rectangle), null.
	/// Also has functions to convert to normal coodinates.
	/// </summary>
	[DebuggerStepThrough]
	public struct Coord
	{
		/// <summary>
		/// Coord variable value type.
		/// </summary>
		public enum CoordType
		{
			/// <summary>
			/// No value. The variable is default(Coord).
			/// </summary>
			None,

			/// <summary>
			/// <see cref="Value"/> is pixel offset from left or top of a rectangle.
			/// </summary>
			Normal,

			/// <summary>
			/// <see cref="Value"/> is pixel offset from right or bottom of a rectangle, towards left or top.
			/// </summary>
			Reverse,

			/// <summary>
			/// <see cref="FractionValue"/> is fraction of a rectangle, where 0.0 is left or top, and 1.0 is right or bottom (outside of the rectangle).
			/// </summary>
			Fraction,
		}

		//Use single long field that packs int and CoordType.
		//If 2 fields (int and CoordType), 64-bit compiler creates huge calling code.
		//This version is good in 32-bit, very good in 64-bit. Even better than packing in single int (30 bits value and 2 bits type).
		//Don't use struct or union with both int and float fields. It creates slow and huge calling code.
		long _v;

		/// <summary>
		/// Value type.
		/// </summary>
		public CoordType Type => (CoordType)(_v >> 32);

		/// <summary>
		/// Non-fraction value.
		/// </summary>
		public int Value => (int)_v;

		/// <summary>
		/// Fraction value.
		/// </summary>
		public unsafe float FractionValue { get { int i = Value; return *(float*)&i; } }

		/// <summary>
		/// Returns true if Type == None (when assigned default(Coord)).
		/// </summary>
		public bool IsEmpty => Type == CoordType.None;

		Coord(CoordType type, int value) { _v = ((long)type << 32) | (uint)value; }
		//info: if type and value are constants, compiler knows the final value and does not use the << and | operators in the compiled code.

		/// <summary>
		/// Creates Coord of Normal type.
		/// </summary>
		//[MethodImpl(MethodImplOptions.NoInlining)] //makes bigger/slower
		public static implicit operator Coord(int v) => new Coord(CoordType.Normal, v);

		//rejected. Because would be used when assigning uint, long, ulong. Or need functions for these too.
		///// <summary>
		///// Creates Coord of Fraction type.
		///// </summary>
		//public static implicit operator Coord(float v) => Fraction(v);

		/// <summary>
		/// Creates Coord of Reverse type.
		/// Value 0 is at the right or bottom, and does not belong to the rectangle. Positive values are towards left or top.
		/// </summary>
		public static Coord Reverse(int v)
		{
			return new Coord(CoordType.Reverse, v);
		}

		/// <summary>
		/// Creates Coord of Fraction type.
		/// Value 0.0 is the left or top of the rectangle. Value 1.0 is the right or bottom of the rectangle. Values &lt;0.0 and &gt;=1.0 are outside of the rectangle.
		/// </summary>
		public static unsafe Coord Fraction(double v)
		{
			float f = (float)v;
			return new Coord(CoordType.Fraction, *(int*)&f);
		}

		/// <summary>
		/// Returns <c>Fraction(0.5)</c>.
		/// </summary>
		/// <seealso cref="Fraction"/>
		public static Coord Center => Fraction(0.5);

		/// <summary>
		/// Returns <c>Reverse(0)</c>.
		/// This point will be outside of the rectangle. See also <see cref="MaxInside"/>.
		/// </summary>
		/// <seealso cref="Reverse"/>
		public static Coord Max => Reverse(0);

		/// <summary>
		/// Returns <c>Reverse(1)</c>.
		/// This point will be inside of the rectangle, at the very right or bottom, assuming the rectangle is not empty.
		/// </summary>
		/// <seealso cref="Reverse"/>
		public static Coord MaxInside => Reverse(1);

		//rejected: this could be used like Coord.Max + 1. Too limited usage.
		//public static Coord operator +(Coord c, int i) { return ...; }

		static bool _NeedRect(Coord x, Coord y)
		{
			return (x.Type > CoordType.Normal) || (y.Type > CoordType.Normal);
		}

		int _Normalize(int min, int max)
		{
			switch(Type) {
			case CoordType.Normal: return min + Value;
			case CoordType.Reverse: return max - Value; //info: could subtract 1, then 0 would be in rectangle, but in some contexts it is not good
			case CoordType.Fraction: return min + (int)((max - min) * FractionValue);
			default: return 0;
			}
		}

		/// <summary>
		/// Converts fractional/reverse coordinates to normal coordinates in a rectangle.
		/// </summary>
		/// <param name="x">X coordinate relative to r.</param>
		/// <param name="y">Y coordinate relative to r.</param>
		/// <param name="r">The rectangle.</param>
		/// <param name="widthHeight">Use only width and height of r. If false (default), the function adds r offset (left and top).</param>
		/// <param name="centerIfEmpty">If x or y is default(Coord), use Coord.Center. Not used with widthHeight.</param>
		public static POINT NormalizeInRect(Coord x, Coord y, RECT r, bool widthHeight = false, bool centerIfEmpty = false)
		{
			if(widthHeight) r.Offset(-r.left, -r.top);
			else if(centerIfEmpty) {
				if(x.IsEmpty) x = Center;
				if(y.IsEmpty) y = Center;
			}
			return (x._Normalize(r.left, r.right), y._Normalize(r.top, r.bottom));
		}

		/// <summary>
		/// Returns normal coordinates relative to the client area of a window. Converts fractional/reverse coordinates etc.
		/// </summary>
		/// <param name="x">X coordinate relative to the client area of w.</param>
		/// <param name="y">Y coordinate relative to the client area of w.</param>
		/// <param name="w">The window.</param>
		/// <param name="nonClient">x y are relative to the entire w rectangle, not to its client area.</param>
		/// <param name="centerIfEmpty">If x or y is default(Coord), use Coord.Center.</param>
		public static POINT NormalizeInWindow(Coord x, Coord y, Wnd w, bool nonClient = false, bool centerIfEmpty = false)
		{
			//info: don't need widthHeight parameter because client area left/top are 0. With non-client don't need in this library and probably not useful. But if need, caller can explicitly offset the rect before calling this func.

			if(centerIfEmpty) {
				if(x.IsEmpty) x = Center;
				if(y.IsEmpty) y = Center;
			}
			POINT p = default;
			if(!x.IsEmpty || !y.IsEmpty) {
				RECT r;
				if(nonClient) {
					w.GetRectInClientOf(w, out r);
				} else if(_NeedRect(x, y)) {
					r = w.ClientRect;
				} else r = default;
				p.x = x._Normalize(r.left, r.right);
				p.y = y._Normalize(r.top, r.bottom);
			}
			return p;
		}

		/// <summary>
		/// Returns normal coordinates relative to the primary screen. Converts fractional/reverse coordinates etc.
		/// </summary>
		/// <param name="x">X coordinate relative to the specified screen (default - primary).</param>
		/// <param name="y">Y coordinate relative to the specified screen (default - primary).</param>
		/// <param name="workArea">x y are relative to the work area.</param>
		/// <param name="screen">If used, x y are relative to this screen. Default - primary screen.</param>
		/// <param name="widthHeight">Use only width and height of the screen rectangle. If false, the function adds its offset (left and top, which can be nonzero if using the work area or a non-primary screen).</param>
		/// <param name="centerIfEmpty">If x or y is default(Coord), use Coord.Center.</param>
		public static POINT Normalize(Coord x, Coord y, bool workArea = false, Screen_ screen = default, bool widthHeight = false, bool centerIfEmpty = false)
		{
			if(centerIfEmpty) {
				if(x.IsEmpty) x = Center;
				if(y.IsEmpty) y = Center;
			}
			POINT p = default;
			if(!x.IsEmpty || !y.IsEmpty) {
				RECT r;
				if(workArea || !screen.IsNull || _NeedRect(x, y)) {
					r = Screen_.GetRect(screen, workArea);
					if(widthHeight) r.Offset(-r.left, -r.top);
				} else r = default;
				p.x = x._Normalize(r.left, r.right);
				p.y = y._Normalize(r.top, r.bottom);
			}
			return p;
		}

		///
		public override string ToString()
		{
			switch(Type) {
			case CoordType.Normal: return Value.ToString() + ", Normal";
			case CoordType.Reverse: return Value.ToString() + ", Reverse";
			case CoordType.Fraction: return FractionValue.ToString_() + ", Fraction";
			default: return "default";
			}
		}
	}

	/// <summary>
	/// Can be used to specify coordinates for various popup windows and other UI objects.
	/// </summary>
	public class PopupXY
	{
#pragma warning disable 1591 //XML doc
		public Coord x, y;
		public Screen_ screen;
		public bool workArea;
		//public bool rawXY;
		public RECT? rect;

		public PopupXY() { }

		public PopupXY(Coord x, Coord y, bool workArea = true, Screen_ screen = default)
		{
			this.x = x; this.y = y; this.workArea = workArea; this.screen = screen;
		}

		public PopupXY(RECT r, Coord x, Coord y)
		{
			this.x = x; this.y = y; this.rect = r;
		}
#pragma warning restore 1591 //XML doc

		/// <summary>Specifies position relative to the work area of the primary screen.</summary>
		public static implicit operator PopupXY((Coord x, Coord y) p) => new PopupXY(p.x, p.y, true);
		/// <summary>Specifies position relative to the primary screen or its work area.</summary>
		public static implicit operator PopupXY((Coord x, Coord y, bool workArea) p) => new PopupXY(p.x, p.y, p.workArea);
		/// <summary>Specifies position relative to the work area of the specified screen.</summary>
		public static implicit operator PopupXY((Coord x, Coord y, Screen_ screen) p) => new PopupXY(p.x, p.y, true, p.screen);
		/// <summary>Specifies position relative to the specified screen or its work area.</summary>
		public static implicit operator PopupXY((Coord x, Coord y, bool workArea, Screen_ screen) p) => new PopupXY(p.x, p.y, p.workArea, p.screen);
		/// <summary>Specifies position relative to the specified screen or its work area.</summary>
		public static implicit operator PopupXY((Coord x, Coord y, Screen_ screen, bool workArea) p) => new PopupXY(p.x, p.y, p.workArea, p.screen);
		/// <summary>Specifies position relative to the primary screen.</summary>
		public static implicit operator PopupXY(POINT p) => new PopupXY(p.x, p.y, false);
		/// <summary>Specifies the center of the work area of the specified screen.</summary>
		public static implicit operator PopupXY(Screen_ screen) => new PopupXY(default, default, true, screen);
		/// <summary>Specifies the center of the specified screen or its work area.</summary>
		public static implicit operator PopupXY((Screen_ screen, bool workArea) t) => new PopupXY(default, default, t.workArea, t.screen);
		/// <summary>Specifies the center of the specified screen or its work area.</summary>
		public static implicit operator PopupXY((bool workArea, Screen_ screen) t) => new PopupXY(default, default, t.workArea, t.screen);
		/// <summary>Specifies position in the specified rectangle which is relative to the primary screen.</summary>
		public static implicit operator PopupXY((RECT r, Coord x, Coord y) t) => new PopupXY(t.r, t.x, t.y);
		/// <summary>Specifies the center of the specified rectangle which is relative to the primary screen.</summary>
		public static implicit operator PopupXY(RECT r) => new PopupXY { rect = r };
		/// <summary>Specifies position in the specified window.</summary>
		public static implicit operator PopupXY((Wnd w, Coord x, Coord y) t) => new PopupXY(t.w.Rect, t.x, t.y);
		/// <summary>Specifies the center of the specified window.</summary>
		public static implicit operator PopupXY(Wnd w) => new PopupXY { rect = w.Rect };
		/// <summary>Specifies the center of the specified control or form.</summary>
		public static implicit operator PopupXY(Control c) => new PopupXY { rect = ((Wnd)c).Rect };

		//public bool IsRawXY => screen.IsNull && workArea == false && x.Type == Coord.CoordType.Normal && y.Type == Coord.CoordType.Normal;

		/// <summary>
		/// Gets point coordinates below mouse cursor, for showing a tooltip-like popup.
		/// </summary>
		public static POINT Mouse
		{
			get
			{
				int cy = Api.GetSystemMetrics(Api.SM_CYCURSOR);
				var p = Au.Mouse.XY;
				if(Util.Cursors_.GetCurrentCursor(out var hCursor) && Api.GetIconInfo(hCursor, out var u)) {
					if(u.hbmColor != default) Api.DeleteObject(u.hbmColor);
					if(u.hbmMask != default) Api.DeleteObject(u.hbmMask);

					//Print(u.xHotspot, u.yHotspot);
					p.y += cy - u.yHotspot - 1; //not perfect, but better than just to add SM_CYCURSOR or some constant value.
					return p;
				}
				return (p.x, p.y + cy - 5);
			}
		}

		/// <summary>
		/// Gets <see cref="Screen"/> specified in <see cref="screen"/>. If not specified, gets that of the screen that contains the specified point.
		/// </summary>
		public Screen GetScreen()
		{
			if(!screen.IsNull) return screen.GetScreen();
			POINT p;
			if(rect.HasValue) p = Coord.NormalizeInRect(x, y, rect.GetValueOrDefault(), centerIfEmpty: true);
			else p = Coord.Normalize(x, y, workArea);
			return Screen.FromPoint(p);
		}
	}

	//FUTURE
	//[Flags]
	//public enum PopupAlignment
	//{
	//	XAtLeft = 0,
	//	XAtRight = 1,
	//	XAtCenter = 2,
	//	YAtTop = 0,
	//	YAtBottom = 1,
	//	YAtCenter = 2,
	//}

	/// <summary>
	/// Window handle.
	/// Used for function parameters where the function needs a window handle as <see cref="Au.Wnd"/> but also allows to pass a variable of any of these types: System.Windows.Forms.Control (Form or any control class), System.Windows.Window (WPF window), IntPtr (window handle).
	/// </summary>
	[DebuggerStepThrough]
	public struct AnyWnd
	{
		object _o;
		AnyWnd(object o) { _o = o; }

		/// <summary> Assignment of a value of type Wnd. </summary>
		public static implicit operator AnyWnd(Wnd w) => new AnyWnd(w);
		/// <summary> Assignment of a value of type Wnd. </summary>
		public static implicit operator AnyWnd(IntPtr hwnd) => new AnyWnd((Wnd)hwnd);
		/// <summary> Assignment of a value of type System.Windows.Forms.Control (Form or any control class). </summary>
		public static implicit operator AnyWnd(Control c) => new AnyWnd(c);
		/// <summary> Assignment of a value of type System.Windows.Window (WPF window). </summary>
		public static implicit operator AnyWnd(System.Windows.Window w) => new AnyWnd(w);

		/// <summary>
		/// Gets the window or control handle as Wnd.
		/// Returns default(Wnd) if not assigned.
		/// </summary>
		public Wnd Wnd
		{
			[MethodImpl(MethodImplOptions.NoInlining)] //prevents loading Forms dll when don't need
			get
			{
				if(_o == null) return default;
				if(_o is Wnd) return (Wnd)_o;
				if(_o is Control c) return (Wnd)c;
				return _Wpf();
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)] //prevents loading WPF dlls when don't need
		Wnd _Wpf() => (Wnd)(System.Windows.Window)_o;

		/// <summary>
		/// true if this is default(AnyWnd).
		/// </summary>
		public bool IsEmpty => _o == null;
	}

	/// <summary>
	/// Used for function parameters to specify multiple strings.
	/// Contains a string like "One|Two|Three" or string[] or List&lt;string&gt;. Has implicit conversion operators from these types.
	/// </summary>
	[DebuggerStepThrough]
	public struct MultiString
	{
		MultiString(object o) => Value = o;

		/// <summary>
		/// The raw value.
		/// </summary>
		public object Value;

		/// <summary> Assignment of a value of type string. </summary>
		public static implicit operator MultiString(string s) => new MultiString(s);
		/// <summary> Assignment of a value of type string[]. </summary>
		public static implicit operator MultiString(string[] e) { return new MultiString(e); }
		/// <summary> Assignment of a value of type List&lt;string&gt;. </summary>
		public static implicit operator MultiString(List<string> e) { return new MultiString(e); }

		/// <summary>
		/// Converts the value to string[].
		/// </summary>
		/// <param name="separator">If the value is string, use this character to split it. Default '|'.</param>
		/// <remarks>
		/// If the value was string or List, converts to string[] and stores the string[] in <b>Value</b>. If null, returns empty array.
		/// </remarks>
		public string[] ToArray(char separator = '|')
		{
			switch(Value) {
			case null:
				return Array.Empty<string>(); //for safe foreach
			case string s:
				Value = s.Split(separator);
				break;
			case List<string> a:
				Value = a.ToArray();
				break;
			}
			return Value as string[];
		}
	}
}