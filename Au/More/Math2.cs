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

namespace Au.More
{
	/// <summary>
	/// Simple calculation functions.
	/// </summary>
	//[DebuggerStepThrough]
	public static class Math2
	{
		/// <summary>
		/// Creates uint by placing (ushort)loWord in bits 1-16 and (ushort)hiWord in bits 17-32. Returns it as nint, ready to use with Windows message API as lParam or wParam or return value.
		/// Like C macro MAKELONG, MAKEWPARAM, MAKELPARAM, MAKELRESULT.
		/// </summary>
		public static nint MakeLparam(uint loWord, uint hiWord) => (nint)(((hiWord & 0xffff) << 16) | (loWord & 0xffff));
		//Returns nint, because usually used as sendmessage etc parameter. If uint, would need to explicitly cast to nint. If somebody casts to int, the result may be incorrect, ie negative.

		//Why named MakeLparam, MakeWord, LoWord, HiWord:
		//	1. Like C macros MAKELPARAM/MAKEWORD/LOWORD/HIWORD.
		//	2. MakeLparam used mostly as lParam of sendmessage etc.
		
		/// <summary>
		/// Creates uint by placing (ushort)loWord in bits 1-16 and (ushort)hiWord in bits 17-32. Returns it as nint, ready to use with Windows message API as lParam or wParam or return value.
		/// Like C macro MAKELONG, MAKEWPARAM, MAKELPARAM, MAKELRESULT.
		/// </summary>
		public static nint MakeLparam(int loWord, int hiWord) => MakeLparam((uint)loWord, (uint)hiWord);
		
		/// <summary>
		/// Creates uint by placing (ushort)p.x in bits 1-16 and (ushort)p.y in bits 17-32. Returns it as nint, ready to use with Windows message API as lParam or wParam or return value.
		/// Like C macro MAKELONG, MAKEWPARAM, MAKELPARAM, MAKELRESULT.
		/// </summary>
		public static nint MakeLparam(POINT p) => MakeLparam((uint)p.x, (uint)p.y);

		/// <summary>
		/// Creates ushort by placing (byte)loByte in bits 1-8 and (byte)hiByte in bits 9-16.
		/// Like C macro MAKEWORD.
		/// </summary>
		public static ushort MakeWord(uint loByte, uint hiByte) => (ushort)(((hiByte & 0xff) << 8) | (loByte & 0xff));
		
		/// <summary>
		/// Creates ushort by placing (byte)loByte in bits 1-8 and (byte)hiByte in bits 9-16.
		/// Like C macro MAKEWORD.
		/// </summary>
		public static ushort MakeWord(int loByte, int hiByte) => MakeWord((uint)loByte, (uint)hiByte);

		/// <summary>
		/// Gets bits 1-16 as ushort.
		/// Like C macro LOWORD.
		/// </summary>
		/// <remarks>
		/// The parameter is interpreted as uint. The parameter type nint allows to avoid explicit cast from int and IntPtr (and avoid OverflowException).
		/// </remarks>
		public static ushort LoWord(nint x) => (ushort)((uint)x & 0xFFFF);

		/// <summary>
		/// Gets bits 17-32 as ushort.
		/// Like C macro HIWORD.
		/// </summary>
		/// <remarks>
		/// The parameter is interpreted as uint. The parameter type nint allows to avoid explicit cast from int and IntPtr (and avoid OverflowException).
		/// </remarks>
		public static ushort HiWord(nint x) => (ushort)((uint)x >> 16);

		/// <summary>
		/// Gets bits 1-16 as short.
		/// Like C macro GET_X_LPARAM.
		/// </summary>
		/// <remarks>
		/// The parameter is interpreted as uint. The parameter type nint allows to avoid explicit cast from int and IntPtr (and avoid OverflowException).
		/// </remarks>
		public static short LoShort(nint x) => (short)((uint)x & 0xFFFF);

		/// <summary>
		/// Gets bits 17-32 as short.
		/// Like C macro GET_Y_LPARAM.
		/// </summary>
		/// <remarks>
		/// The parameter is interpreted as uint. The parameter type nint allows to avoid explicit cast from int and IntPtr (and avoid OverflowException).
		/// </remarks>
		public static short HiShort(nint x) => (short)((uint)x >> 16);

		/// <summary>
		/// Gets bits 1-8 as byte.
		/// Like C macro LOBYTE.
		/// </summary>
		public static byte LoByte(ushort x) => (byte)((uint)x & 0xFF);

		/// <summary>
		/// Gets bits 9-16 as byte.
		/// Like C macro HIBYTE.
		/// </summary>
		public static byte HiByte(ushort x) => (byte)((uint)x >> 8);

		/// <summary>
		/// Converts <b>nint</b> containing x and y coordinates to <b>POINT</b>.
		/// </summary>
		public static POINT NintToPOINT(nint xy) => new(LoShort(xy), HiShort(xy));

		/// <summary>
		/// Returns <c>number * multiply / divide</c>.
		/// Multiplies without overflow and rounds up or down to the nearest integer.
		/// </summary>
		/// <exception cref="OverflowException"></exception>
		/// <exception cref="DivideByZeroException"></exception>
		public static int MulDiv(int number, int multiply, int divide)
		{
			if(divide == multiply) return number;
			long r = (long)number * multiply;
			int d = divide / 2; if(r < 0 == divide < 0) r += d; else r -= d; //round
			return checked((int)(r / divide));

			//This code produces the same results as API MulDiv. Tested with millions of random and edge values. Faster.
			//The only difference, API does not support int.MinValue.
		}
		//public static int MulDiv(int number, int multiply, int divide) => Api.MulDiv(number, multiply, divide);

		/// <summary>
		/// Calculates how many % of <i>whole</i> is <i>part</i>: <c>100L * part / whole</c>.
		/// </summary>
		/// <param name="whole"></param>
		/// <param name="part"></param>
		/// <param name="canRoundUp">Round down or up. If false (default), can only round down.</param>
		/// <exception cref="OverflowException"></exception>
		public static int PercentFromValue(int whole, int part, bool canRoundUp = false)
			=> whole == default ? default : (canRoundUp ? MulDiv(100, part, whole) : checked((int)(100L * part / whole)));

		/// <summary>
		/// Calculates how many % of <i>whole</i> is <i>part</i>: <c>100 * part / whole</c>.
		/// </summary>
		public static double PercentFromValue(double whole, double part)
			=> whole == default ? default : (100.0 * part / whole);

		/// <summary>
		/// Returns <i>percent</i> % of <i>whole</i>: <c>(long)whole * percent / 100</c>.
		/// </summary>
		/// <param name="whole"></param>
		/// <param name="percent"></param>
		/// <param name="canRoundUp">Use <see cref="MulDiv"/>, which can round down or up. If false (default), can only round down.</param>
		/// <exception cref="OverflowException"></exception>
		public static int PercentToValue(int whole, int percent, bool canRoundUp = false)
			=> canRoundUp ? MulDiv(whole, percent, 100) : checked((int)((long)whole * percent / 100L));

		/// <summary>
		/// Returns <i>percent</i> % of <i>whole</i>: <c>whole * percent / 100</c>.
		/// </summary>
		public static double PercentToValue(double whole, double percent)
			=> whole * percent / 100.0;

		/// <summary>
		/// If value is divisible by alignment, returns value. Else returns nearest bigger number that is divisible by alignment.
		/// </summary>
		/// <param name="value">An integer value.</param>
		/// <param name="alignment">Alignment. Must be a power of two (2, 4, 8, 16...).</param>
		/// <remarks>
		/// For example if alignment is 4, returns 4 if value is 1-4, returns 8 if value is 5-8, returns 12 if value is 9-10, and so on.
		/// </remarks>
		public static uint AlignUp(uint value, uint alignment) => (value + (alignment - 1)) & ~(alignment - 1);
		//shorter: (value + --alignment) & ~alignment. But possibly less optimized. Now (alignment - 1) and ~(alignment - 1) usually are constants.

		/// <summary>
		/// If value is divisible by alignment, returns value. Else returns nearest bigger number that is divisible by alignment.
		/// </summary>
		/// <param name="value">An integer value.</param>
		/// <param name="alignment">Alignment. Must be a power of two (2, 4, 8, 16...).</param>
		/// <remarks>
		/// For example if alignment is 4, returns 4 if value is 1-4, returns 8 if value is 5-8, returns 12 if value is 9-10, and so on.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// for (int i = 0; i <= 20; i++) print.it(i, Math2.AlignUp(i, 4));
		/// ]]></code>
		/// </example>
		public static int AlignUp(int value, uint alignment) => (int)AlignUp((uint)value, alignment);

		/// <summary>
		/// Swaps values of variables a and b: <c>T t = a; a = b; b = t;</c>
		/// </summary>
		public static void Swap<T>(ref T a, ref T b)
		{
			T t = a; a = b; b = t;
		}

		/// <summary>
		/// Swaps two ranges of bits.
		/// </summary>
		/// <param name="value"></param>
		/// <param name="i">Position of first range of bits.</param>
		/// <param name="j">Position of second range of bits.</param>
		/// <param name="n">Number of bits in each range.</param>
		public static int SwapBits(int value, int i, int j, int n) => (int)SwapBits((uint)value, i, j, n);

		/// <summary>
		/// Swaps two ranges of bits.
		/// </summary>
		/// <param name="value"></param>
		/// <param name="i">Position of first range of bits.</param>
		/// <param name="j">Position of second range of bits.</param>
		/// <param name="n">Number of bits in each range.</param>
		public static uint SwapBits(uint value, int i, int j, int n) {
			// http://graphics.stanford.edu/~seander/bithacks.html#SwappingBitsXOR
			uint x = ((value >> i) ^ (value >> j)) & ((1U << n) - 1); // XOR temporary
			return value ^ ((x << i) | (x << j));
		}

		/// <summary>
		/// Calculates angle degrees from coordinates x and y.
		/// </summary>
		public static double AngleFromXY(int x, int y) => Math.Atan2(y, x) * (180 / Math.PI);

		/// <summary>
		/// Calculates distance between two points.
		/// </summary>
		public static double Distance(POINT p1, POINT p2)
		{
			if(p1.y == p2.y) return Math.Abs(p2.x - p1.x); //horizontal line
			if(p1.x == p2.x) return Math.Abs(p2.y - p1.y); //vertical line

			long dx = p2.x - p1.x, dy = p2.y - p1.y;
			return Math.Sqrt(dx * dx + dy * dy);
		}

		/// <summary>
		/// Calculates distance between rectangle and point.
		/// Returns 0 if point is in rectangle.
		/// </summary>
		public static double Distance(RECT r, POINT p)
		{
			r.Normalize(swap: true);
			if(r.Contains(p)) return 0;
			int x = p.x < r.left ? r.left : (p.x > r.right ? r.right : p.x);
			int y = p.y < r.top ? r.top : (p.y > r.bottom ? r.bottom : p.y);
			return Distance((x, y), p);
		}
	}
}
