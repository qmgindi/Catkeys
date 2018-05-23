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
using System.Drawing;
//using System.Linq;
//using System.Xml.Linq;

using Au.Types;
using static Au.NoClass;

namespace Au
{
	/// <summary>
	/// Provides function to get ASCII character type (is digit etc) etc.
	/// </summary>
	/// <remarks>
	/// Unlike <see cref="Char.IsDigit"/> etc, these functions never return true for non-ASCII characters. Also they are faster.
	/// </remarks>
	[DebuggerStepThrough]
	public static class Char_
	{
		//rejected: make these extension methods (then also need to add suffix to the names).

		/// <summary>
		/// Returns true if character is '0' to '9'.
		/// </summary>
		public static bool IsAsciiDigit(char c) { return c <= '9' && c >= '0'; }

		/// <summary>
		/// Returns true if character is '0' to '9'.
		/// </summary>
		public static bool IsAsciiDigit(byte c) { return c <= '9' && c >= '0'; }

		/// <summary>
		/// Returns true if character is 'A' to 'Z' or 'a' to 'z'.
		/// </summary>
		public static bool IsAsciiAlpha(char c) { return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'); }

		/// <summary>
		/// Returns true if character is 'A' to 'Z' or 'a' to 'z'.
		/// </summary>
		public static bool IsAsciiAlpha(byte c) { return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'); }

		/// <summary>
		/// Returns true if character is 'A' to 'Z' or 'a' to 'z' or '0' to '9'.
		/// </summary>
		public static bool IsAsciiAlphaDigit(char c) { return IsAsciiAlpha(c) || IsAsciiDigit(c); }

		/// <summary>
		/// Returns true if character is 'A' to 'Z' or 'a' to 'z' or '0' to '9'.
		/// </summary>
		public static bool IsAsciiAlphaDigit(byte c) { return IsAsciiAlpha(c) || IsAsciiDigit(c); }

	}
}
