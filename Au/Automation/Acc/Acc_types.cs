using System;
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
using System.Xml.Linq;
//using System.Xml.XPath;

using static Au.NoClass;

namespace Au.Types
{
	/// <summary>
	/// Flags for <see cref="Acc.Find(Wnd, string, string, string, AFFlags, Func{Acc, bool}, int)"/> and similar functions.
	/// </summary>
	[Flags]
	public enum AFFlags
	{
		/// <summary>
		/// Search in reverse order. It can make faster.
		/// When using control class name, controls are searched not in reverse order. Only accessible objects in them are searched in reverse order.
		/// </summary>
		Reverse = 1,

		/// <summary>
		/// The accessible object can be invisible.
		/// Without this flag skips objects that are invisible (have state INVISIBLE) or are descendants of invisible WINDOW, DOCUMENT, PROPERTYPAGE, GROUPING, ALERT, MENUPOPUP.
		/// Regardless of this flag, always skips invisible standard objects of nonclient area: TITLEBAR, MENUBAR, SCROLLBAR, GRIP.
		/// </summary>
		HiddenToo = 2,

		/// <summary>
		/// Always search in MENUITEM.
		/// Without this flag skips MENUITEM descendant objects (for speed), unless role is MENUITEM or MENUPOPUP or searching in web page.
		/// </summary>
		MenuToo = 4,

		/// <summary>
		/// Search without loading dll into the target process.
		/// Disadvantages: 1. Much slower. 2. Some properties are not supported, for example HTML attributes (while searching and later). 3. And more.
		/// Even without this flag, the default search method is not used with Windows Store app windows, console windows, most Java windows, windows of protected processes and processes of higher UAC integrity level, Firefox web page if its multiprocess feature is not disabled.
		/// Some windows have child controls that belong to a different process or thread than the window. Example - Internet Explorer. When searching in such windows, the default search method is not used when searching in these controls. Workaround - find the control(s) and search in it/them. For it can be used one of: 1. With Internet Explorer use role prefix "web:". 2. Find the control with <see cref="Wnd.Child"/> and search in it. 3. Use <see cref="Acc.Finder.Find(Wnd, Wnd.ChildFinder)"/>.
		/// Don't need this flag when searching in Acc (then it is inherited from the Acc variable).
		/// See also: <see cref="Acc.MiscFlags"/>.
		/// </summary>
		NotInProc = 0x100,

		/// <summary>
		/// Use UI Automation API.
		/// Need this flag to find objects in windows that don't support accessible objects but support UI Automation elements.
		/// Examples of such windows: Microsoft Edge web browser (web page), JavaFX applications.
		/// Objects found with this flag never have HtmlX properties, but can have <see cref="Acc.UiaId"/>.
		/// This flag can be used with most other windows too.
		/// Don't use this flag when searching in Acc (then it is inherited from the Acc variable).
		/// See also: <see cref="Acc.MiscFlags"/>.
		/// </summary>
		UIA = 0x200,

		/// <summary>
		/// Used internally.
		/// </summary>
		Mark = 0x10000,
	}

	/// <summary>
	/// Flags for <see cref="Acc.FromWindow"/>.
	/// </summary>
	[Flags]
	public enum AWFlags
	{
		/// <summary>Don't throw exception when fails. Then returns null.</summary>
		NoThrow = 1,

		/// <summary>
		/// Don't load dll into the target process.
		/// More info: <see cref="AFFlags.NotInProc"/>.
		/// </summary>
		NotInProc = 2,
	}

	/// <summary>
	/// Flags for <see cref="Acc.FromXY"/>.
	/// </summary>
	[Flags]
	public enum AXYFlags
	{
		/// <summary>
		/// Use UI Automation API.
		/// Need this flag with some windows that don't support accessible objects but support UI Automation elements.
		/// When this flag is not specified, the function detects most such windows and uses UI Automation API, but the detection is not perfect.
		/// More info: <see cref="AFFlags.UIA"/>.
		/// This flag can be used with most other windows too.
		/// </summary>
		UIA = 1,

		/// <summary>
		/// Get the direct parent object if it's LINK or PUSHBUTTON.
		/// Usually links have one or more children of type TEXT, STATICTEXT, GRAPHIC or other.
		/// </summary>
		PreferLink = 2,

		/// <summary>
		/// Don't load dll into the target process.
		/// More info: <see cref="AFFlags.NotInProc"/>.
		/// </summary>
		NotInProc = 4,

		/// <summary>Don't throw exception when fails. Then returns null.</summary>
		NoThrow = 0x100,

		//note: don't change values. They are passed to the cpp function.
	}

	/// <summary>
	/// Flags returned by <see cref="Acc.MiscFlags"/>.
	/// </summary>
	[Flags]
	public enum AccMiscFlags :byte
	{
		/// <summary>
		/// This accessible object was retrieved by the dll loaded into its process.
		/// More info: <see cref="AFFlags.NotInProc"/>.
		/// </summary>
		InProc = 1,

		/// <summary>
		/// This accessible object was retrieved using UI Automation API.
		/// More info: <see cref="AFFlags.UIA"/>.
		/// </summary>
		UIA = 2,

		/// <summary>
		/// This accessible object was retrieved using Java Access Bridge API.
		/// More info: <see cref="Acc"/>.
		/// </summary>
		Java = 4,

		/// <summary>
		/// Used internally.
		/// </summary>
		Marked = 128,
	}

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

	/// <summary>
	/// Accessible object ids of window parts and some special objects.
	/// Used with <see cref="Acc.FromWindow"/>
	/// </summary>
	/// <remarks>
	/// The names are as in API <msdn>AccessibleObjectFromWindow</msdn> documentation but without prefix "OBJID_". Except Java and UIA.
	/// </remarks>
	public enum AccOBJID
	{
		WINDOW = 0,
		SYSMENU = -1,
		TITLEBAR = -2,
		MENU = -3,
		CLIENT = -4,
		VSCROLL = -5,
		HSCROLL = -6,
		SIZEGRIP = -7,
		CARET = -8,
		CURSOR = -9,
		ALERT = -10,
		SOUND = -11,
		QUERYCLASSNAMEIDX = -12,
		NATIVEOM = -16,

		//ours

		/// <summary>
		/// The root Java object. Can be used when the window's class name starts with "SunAwt".
		/// </summary>
		Java = -100,

		/// <summary>
		/// Use UI Automation API.
		/// More info: <see cref="AFFlags.UIA"/>.
		/// </summary>
		UIA = -101,
	}

	/// <summary>
	/// Standard roles of accessible objects.
	/// Used with <see cref="Acc.RoleInt"/>
	/// </summary>
	/// <remarks>
	/// The names are as in API <msdn>IAccessible.get_accRole</msdn> documentation but without prefix "ROLE_SYSTEM_".
	/// </remarks>
	public enum AccROLE
	{
		TITLEBAR = 0x1,
		MENUBAR = 0x2,
		SCROLLBAR = 0x3,
		GRIP = 0x4,
		SOUND = 0x5,
		CURSOR = 0x6,
		CARET = 0x7,
		ALERT = 0x8,
		WINDOW = 0x9,
		CLIENT = 0xA,
		MENUPOPUP = 0xB,
		MENUITEM = 0xC,
		TOOLTIP = 0xD,
		APPLICATION = 0xE,
		DOCUMENT = 0xF,
		PANE = 0x10,
		CHART = 0x11,
		DIALOG = 0x12,
		BORDER = 0x13,
		GROUPING = 0x14,
		SEPARATOR = 0x15,
		TOOLBAR = 0x16,
		STATUSBAR = 0x17,
		TABLE = 0x18,
		COLUMNHEADER = 0x19,
		ROWHEADER = 0x1A,
		COLUMN = 0x1B,
		ROW = 0x1C,
		CELL = 0x1D,
		LINK = 0x1E,
		HELPBALLOON = 0x1F,
		CHARACTER = 0x20,
		LIST = 0x21,
		LISTITEM = 0x22,
		OUTLINE = 0x23,
		OUTLINEITEM = 0x24,
		PAGETAB = 0x25,
		PROPERTYPAGE = 0x26,
		INDICATOR = 0x27,
		GRAPHIC = 0x28,
		STATICTEXT = 0x29,
		TEXT = 0x2A,
		PUSHBUTTON = 0x2B,
		CHECKBUTTON = 0x2C,
		RADIOBUTTON = 0x2D,
		COMBOBOX = 0x2E,
		DROPLIST = 0x2F,
		PROGRESSBAR = 0x30,
		DIAL = 0x31,
		HOTKEYFIELD = 0x32,
		SLIDER = 0x33,
		SPINBUTTON = 0x34,
		DIAGRAM = 0x35,
		ANIMATION = 0x36,
		EQUATION = 0x37,
		BUTTONDROPDOWN = 0x38,
		BUTTONMENU = 0x39,
		BUTTONDROPDOWNGRID = 0x3A,
		WHITESPACE = 0x3B,
		PAGETABLIST = 0x3C,
		CLOCK = 0x3D,
		SPLITBUTTON = 0x3E,
		IPADDRESS = 0x3F,
		OUTLINEBUTTON = 0x40,
	}

	/// <summary>
	/// Accessible object state flags.
	/// Used by <see cref="Acc.State"/>.
	/// </summary>
	/// <remarks>
	/// Most names are as in API <msdn>IAccessible.get_accState</msdn> documentation but without prefix "STATE_SYSTEM_".
	/// </remarks>
	[Flags]
	public enum AccSTATE
	{
		//NORMAL = 0x0,
		DISABLED = 0x1, //original name UNAVAILABLE
		SELECTED = 0x2,
		FOCUSED = 0x4,
		PRESSED = 0x8,
		CHECKED = 0x10,
		//MIXED = 0x20,
		INDETERMINATE = 0x20,
		READONLY = 0x40,
		HOTTRACKED = 0x80,
		DEFAULT = 0x100,
		EXPANDED = 0x200,
		COLLAPSED = 0x400,
		BUSY = 0x800,
		FLOATING = 0x1000,
		MARQUEED = 0x2000,
		ANIMATED = 0x4000,
		INVISIBLE = 0x8000,
		OFFSCREEN = 0x10000,
		SIZEABLE = 0x20000,
		MOVEABLE = 0x40000,
		SELFVOICING = 0x80000,
		FOCUSABLE = 0x100000,
		SELECTABLE = 0x200000,
		LINKED = 0x400000,
		TRAVERSED = 0x800000,
		MULTISELECTABLE = 0x1000000,
		EXTSELECTABLE = 0x2000000,
		ALERT_LOW = 0x4000000,
		ALERT_MEDIUM = 0x8000000,
		ALERT_HIGH = 0x10000000,
		PROTECTED = 0x20000000,
		HASPOPUP = 0x40000000,
	}

	/// <summary>
	/// Accessible object selection flags.
	/// Used by <see cref="Acc.Select"/>.
	/// </summary>
	/// <remarks>
	/// The names are as in API <msdn>IAccessible.accSelect</msdn> documentation but without prefix "SELFLAG_".
	/// </remarks>
	[Flags]
	public enum AccSELFLAG
	{
		TAKEFOCUS = 0x1,
		TAKESELECTION = 0x2,
		EXTENDSELECTION = 0x4,
		ADDSELECTION = 0x8,
		REMOVESELECTION = 0x10,
	}

	/// <summary>
	/// Event constants for API <msdn>SetWinEventHook</msdn>.
	/// </summary>
	/// <remarks>
	/// The names are as in API documentation but without prefix "EVENT_".
	/// </remarks>
	public enum AccEVENT
	{
		MIN = 0x1,
		MAX = 0x7FFFFFFF,

		SYSTEM_SOUND = 0x1,
		SYSTEM_ALERT = 0x2,
		SYSTEM_FOREGROUND = 0x3,
		SYSTEM_MENUSTART = 0x4,
		SYSTEM_MENUEND = 0x5,
		SYSTEM_MENUPOPUPSTART = 0x6,
		SYSTEM_MENUPOPUPEND = 0x7,
		SYSTEM_CAPTURESTART = 0x8,
		SYSTEM_CAPTUREEND = 0x9,
		SYSTEM_MOVESIZESTART = 0xA,
		SYSTEM_MOVESIZEEND = 0xB,
		SYSTEM_CONTEXTHELPSTART = 0xC,
		SYSTEM_CONTEXTHELPEND = 0xD,
		SYSTEM_DRAGDROPSTART = 0xE,
		SYSTEM_DRAGDROPEND = 0xF,
		SYSTEM_DIALOGSTART = 0x10,
		SYSTEM_DIALOGEND = 0x11,
		SYSTEM_SCROLLINGSTART = 0x12,
		SYSTEM_SCROLLINGEND = 0x13,
		SYSTEM_SWITCHSTART = 0x14,
		SYSTEM_SWITCHEND = 0x15,
		SYSTEM_MINIMIZESTART = 0x16,
		SYSTEM_MINIMIZEEND = 0x17,
		SYSTEM_DESKTOPSWITCH = 0x20,
		SYSTEM_SWITCHER_APPGRABBED = 0x24,
		SYSTEM_SWITCHER_APPOVERTARGET = 0x25,
		SYSTEM_SWITCHER_APPDROPPED = 0x26,
		SYSTEM_SWITCHER_CANCELLED = 0x27,
		SYSTEM_IME_KEY_NOTIFICATION = 0x29,
		SYSTEM_END = 0xFF,

		OEM_DEFINED_START = 0x101,
		OEM_DEFINED_END = 0x1FF,

		UIA_EVENTID_START = 0x4E00,
		UIA_EVENTID_END = 0x4EFF,
		UIA_PROPID_START = 0x7500,
		UIA_PROPID_END = 0x75FF,

		CONSOLE_CARET = 0x4001,
		CONSOLE_UPDATE_REGION = 0x4002,
		CONSOLE_UPDATE_SIMPLE = 0x4003,
		CONSOLE_UPDATE_SCROLL = 0x4004,
		CONSOLE_LAYOUT = 0x4005,
		CONSOLE_START_APPLICATION = 0x4006,
		CONSOLE_END_APPLICATION = 0x4007,
		CONSOLE_END = 0x40FF,

		OBJECT_CREATE = 0x8000,
		OBJECT_DESTROY = 0x8001,
		OBJECT_SHOW = 0x8002,
		OBJECT_HIDE = 0x8003,
		OBJECT_REORDER = 0x8004,
		OBJECT_FOCUS = 0x8005,
		OBJECT_SELECTION = 0x8006,
		OBJECT_SELECTIONADD = 0x8007,
		OBJECT_SELECTIONREMOVE = 0x8008,
		OBJECT_SELECTIONWITHIN = 0x8009,
		OBJECT_STATECHANGE = 0x800A,
		OBJECT_LOCATIONCHANGE = 0x800B,
		OBJECT_NAMECHANGE = 0x800C,
		OBJECT_DESCRIPTIONCHANGE = 0x800D,
		OBJECT_VALUECHANGE = 0x800E,
		OBJECT_PARENTCHANGE = 0x800F,
		OBJECT_HELPCHANGE = 0x8010,
		OBJECT_DEFACTIONCHANGE = 0x8011,
		OBJECT_ACCELERATORCHANGE = 0x8012,
		OBJECT_INVOKED = 0x8013,
		OBJECT_TEXTSELECTIONCHANGED = 0x8014,
		OBJECT_CONTENTSCROLLED = 0x8015,
		SYSTEM_ARRANGMENTPREVIEW = 0x8016,
		OBJECT_CLOAKED = 0x8017,
		OBJECT_UNCLOAKED = 0x8018,
		OBJECT_LIVEREGIONCHANGED = 0x8019,
		OBJECT_HOSTEDOBJECTSINVALIDATED = 0x8020,
		OBJECT_DRAGSTART = 0x8021,
		OBJECT_DRAGCANCEL = 0x8022,
		OBJECT_DRAGCOMPLETE = 0x8023,
		OBJECT_DRAGENTER = 0x8024,
		OBJECT_DRAGLEAVE = 0x8025,
		OBJECT_DRAGDROPPED = 0x8026,
		OBJECT_IME_SHOW = 0x8027,
		OBJECT_IME_HIDE = 0x8028,
		OBJECT_IME_CHANGE = 0x8029,
		OBJECT_TEXTEDIT_CONVERSIONTARGETCHANGED = 0x8030,
		OBJECT_END = 0x80FF,

		AIA_START = 0xA000,
		AIA_END = 0xAFFF,

		IA2_ACTION_CHANGED = 0x101,
		IA2_ACTIVE_DESCENDANT_CHANGED = 0x102,
		IA2_DOCUMENT_ATTRIBUTE_CHANGED = 0x103,
		IA2_DOCUMENT_CONTENT_CHANGED = 0x104,
		IA2_DOCUMENT_LOAD_COMPLETE = 0x105,
		IA2_DOCUMENT_LOAD_STOPPED = 0x106,
		IA2_DOCUMENT_RELOAD = 0x107,
		IA2_HYPERLINK_END_INDEX_CHANGED = 0x108,
		IA2_HYPERLINK_NUMBER_OF_ANCHORS_CHANGED = 0x109,
		IA2_HYPERLINK_SELECTED_LINK_CHANGED = 0x10a,
		IA2_HYPERTEXT_LINK_ACTIVATED = 0x10b,
		IA2_HYPERTEXT_LINK_SELECTED = 0x10c,
		IA2_HYPERLINK_START_INDEX_CHANGED = 0x10d,
		IA2_HYPERTEXT_CHANGED = 0x10e,
		IA2_HYPERTEXT_NLINKS_CHANGED = 0x10f,
		IA2_OBJECT_ATTRIBUTE_CHANGED = 0x110,
		IA2_PAGE_CHANGED = 0x111,
		IA2_SECTION_CHANGED = 0x112,
		IA2_TABLE_CAPTION_CHANGED = 0x113,
		IA2_TABLE_COLUMN_DESCRIPTION_CHANGED = 0x114,
		IA2_TABLE_COLUMN_HEADER_CHANGED = 0x115,
		IA2_TABLE_MODEL_CHANGED = 0x116,
		IA2_TABLE_ROW_DESCRIPTION_CHANGED = 0x117,
		IA2_TABLE_ROW_HEADER_CHANGED = 0x118,
		IA2_TABLE_SUMMARY_CHANGED = 0x119,
		IA2_TEXT_ATTRIBUTE_CHANGED = 0x11a,
		IA2_TEXT_CARET_MOVED = 0x11b,
		IA2_TEXT_CHANGED = 0x11c,
		IA2_TEXT_COLUMN_CHANGED = 0x11d,
		IA2_TEXT_INSERTED = 0x11e,
		IA2_TEXT_REMOVED = 0x11f,
		IA2_TEXT_UPDATED = 0x120,
		IA2_TEXT_SELECTION_CHANGED = 0x121,
		IA2_VISIBLE_DATA_CHANGED = 0x122,
	}

	/// <summary>
	/// Flags for API <msdn>SetWinEventHook</msdn>.
	/// </summary>
	/// <remarks>
	/// The names are as in API documentation but without prefix "WINEVENT_".
	/// There are no flags for OUTOFCONTEXT and INCONTEXT. OUTOFCONTEXT is default (0). INCONTEXT cannot be used in managed code.
	/// </remarks>
	[Flags]
	internal enum LibAccHookFlags
	{
		//OUTOFCONTEXT = 0x0,
		SKIPOWNTHREAD = 0x1,
		SKIPOWNPROCESS = 0x2,
		//INCONTEXT = 0x4,
	}

	/// <summary>
	/// Used with <see cref="Acc.GetProperties"/>.
	/// </summary>
	public struct AccProperties
	{
		public string Role, Name, Value, Description, Help, DefaultAction, KeyboardShortcut, UiaId, OuterHtml, InnerHtml;
		public AccSTATE State;
		public RECT Rect;
		public Wnd WndContainer;
		public Dictionary<string, string> HtmlAttributes;
	}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
