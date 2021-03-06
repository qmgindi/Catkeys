using Au.Types;
using Au.More;
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
using System.Linq;
using System.Globalization;
using System.Collections;

namespace Au.Triggers
{
	/// <summary>
	/// Flags of autotext triggers.
	/// </summary>
	/// <remarks>
	/// To avoid passing flags to each trigger as the <i>flags</i> parameter, use <see cref="AutotextTriggers.DefaultFlags"/>; its initial value is 0, which means: case-insensitive, erase the typed text with Backspace, modify the replacement text depending on the case of the typed text.
	/// </remarks>
	[Flags]
	public enum TAFlags : byte
	{
		/// <summary>
		/// Case-sensitive.
		/// </summary>
		MatchCase = 1,

		/// <summary>
		/// Let <see cref="AutotextTriggerArgs.Replace"/> don't erase the user-typed text.
		/// Without this flag it erases text with the Backspace key or selects with Shift+Left. If <b>Replace</b> not called, text is not erased/selected regardless of this flag.
		/// </summary>
		DontErase = 2,

		/// <summary>
		/// Let <see cref="AutotextTriggerArgs.Replace"/> don't modify the replacement text. Without this flag it:
		/// - If the first character of the typed text is uppercase, makes the first character of the replacement text uppercase.
		/// - If all typed text is uppercase, makes the replacement text uppercase.
		/// 
		/// Also does not modify if used flag <b>MatchCase</b> or HTML.
		/// </summary>
		ReplaceRaw = 4,

		/// <summary>
		/// Let <see cref="AutotextTriggerArgs.Replace"/> remove the postfix delimiter character.
		/// </summary>
		RemovePostfix = 8,

		/// <summary>
		/// Let <see cref="AutotextTriggerArgs.Replace"/> call <see cref="AutotextTriggerArgs.Confirm"/> and do nothing if it returns false.
		/// </summary>
		Confirm = 16,

		/// <summary>
		/// Let <see cref="AutotextTriggerArgs.Replace"/> select text with Shift+Left instead of erasing with Backspace. Except in console windows.
		/// See also <see cref="AutotextTriggerArgs.ShiftLeft"/>.
		/// </summary>
		ShiftLeft = 32,
	}

	/// <summary>
	/// Postfix type of autotext triggers.
	/// The trigger action runs only when the user ends the autotext with a postfix character or key, unless postfix type is <b>None</b>.
	/// Default: <b>CharOrKey</b>.
	/// </summary>
	public enum TAPostfix : byte
	{
		/// <summary>A postfix character (see <b>Char</b>) or key (see <b>Key</b>).</summary>
		CharOrKey,

		/// <summary>A postfix character specified in the <i>postfixChars</i> parameter or <see cref="AutotextTriggers.DefaultPostfixChars"/> property. If not specified - any non-word character.</summary>
		Char,

		/// <summary>The Ctrl or Shift key. Default is Ctrl. You can change it with <see cref="AutotextTriggers.PostfixKey"/>.</summary>
		Key,

		/// <summary>Don't need a postfix. The action runs immediately when the user types the autotext.</summary>
		None,
	}

	/// <summary>
	/// Represents an autotext trigger.
	/// </summary>
	public class AutotextTrigger : ActionTrigger
	{
		internal string text;
		internal TAFlags flags;
		internal TAPostfix postfixType;
		internal string postfixChars;
		string _paramsString;

		internal AutotextTrigger(ActionTriggers triggers, Action<AutotextTriggerArgs> action, string text, TAFlags flags, TAPostfix postfixType, string postfixChars, (string, int) source)
			: base(triggers, action, true, source) {
			this.text = text;
			this.flags = flags;
			this.postfixType = postfixType;
			this.postfixChars = postfixChars;

			if (flags == 0 && postfixType == 0 && postfixChars == null) {
				_paramsString = text;
			} else {
				using (new StringBuilder_(out var b)) {
					b.Append(text);
					if (flags != 0) b.Append("  (").Append(flags.ToString()).Append(')');
					if (postfixType != 0) b.Append("  postfixType=").Append(postfixType.ToString());
					if (postfixChars != null) b.Append("  postfixChars=").Append(postfixChars);
					_paramsString = b.ToString();
				}
			}
			//print.it(this);
		}

		internal override void Run(TriggerArgs args) => RunT(args as AutotextTriggerArgs);

		/// <summary>
		/// Returns "Autotext".
		/// </summary>
		public override string TypeString => "Autotext";

		/// <summary>
		/// Returns a string containing trigger parameters.
		/// </summary>
		public override string ParamsString => _paramsString;
	}

	/// <summary>
	/// Autotext triggers.
	/// </summary>
	/// <example>See <see cref="ActionTriggers"/>.</example>
	public class AutotextTriggers : ITriggers, IEnumerable<AutotextTrigger>
	{
		ActionTriggers _triggers;
		Dictionary<int, ActionTrigger> _d = new();

		internal AutotextTriggers(ActionTriggers triggers) {
			_triggers = triggers;
			_simpleReplace = new TASimpleReplace(this);
		}

		/// <summary>
		/// Adds an autotext trigger.
		/// </summary>
		/// <param name="text">The action runs when the user types this text and a postfix character or key. By default case-insensitive.</param>
		/// <param name="flags">Options. If omitted or null, uses <see cref="DefaultFlags"/>. Some flags are used by <see cref="AutotextTriggerArgs.Replace"/>.</param>
		/// <param name="postfixType">Postfix type (character, key, any or none). If omitted or null, uses <see cref="DefaultPostfixType"/>; default - a non-word character or the Ctrl key.</param>
		/// <param name="postfixChars">Postfix characters used when postfix type is <b>Char</b> or <b>CharOrKey</b> (default). If omitted or null, uses <see cref="DefaultPostfixChars"/>; default - non-word characters.</param>
		/// <param name="f_">[](xref:caller_info)</param>
		/// <param name="l_">[](xref:caller_info)</param>
		/// <exception cref="ArgumentException">
		/// - Text is empty or too long. Can be 1 - 100 characters.
		/// - Postfix characters contains letters or digits.
		/// </exception>
		/// <exception cref="InvalidOperationException">Cannot add triggers after <see cref="ActionTriggers.Run"/> was called, until it returns.</exception>
		/// <example>See <see cref="ActionTriggers"/>.</example>
		public Action<AutotextTriggerArgs> this[string text, TAFlags? flags = null, TAPostfix? postfixType = null, string postfixChars = null, [CallerFilePath] string f_ = null, [CallerLineNumber] int l_ = 0] {
			set {
				_triggers.ThrowIfRunning_();
				int len = text.Lenn(); if (len < 1 || len > 100) throw new ArgumentException("Text length must be 1 - 100.");
				if (text.Contains('\n')) { text = text.RegexReplace(@"\r?\n", "\r"); len = text.Length; }
				TAFlags fl = flags ?? DefaultFlags;
				bool matchCase = 0 != (fl & TAFlags.MatchCase);
				if (!matchCase) text = text.Lower();
				var t = new AutotextTrigger(_triggers, value, text, fl, postfixType ?? DefaultPostfixType, _CheckPostfixChars(postfixChars) ?? DefaultPostfixChars, (f_, l_));
				//create dictionary key from 1-4 last characters lowercase
				int k = 0;
				for (int i = len - 1, j = 0; i >= 0 && j <= 24; i--, j += 8) {
					var c = text[i]; if (matchCase) c = char.ToLowerInvariant(c);
					k |= (byte)c << j;
				}
				//print.it((uint)k);
				t.DictAdd(_d, k);
				_lastAdded = t;
			}
		}

		/// <summary>
		/// Allows to add triggers in a more concise way - assign a string, not a function. The string will replace the user-typed text.
		/// </summary>
		/// <example>
		/// <code><![CDATA[
		/// var ts = Triggers.Autotext.SimpleReplace;
		/// ts["#su"] = "Sunday"; //the same as Triggers.Autotext["#su"] = o => o.Replace("Sunday");
		/// ts["#mo"] = "Monday";
		/// ]]></code>
		/// </example>
		public TASimpleReplace SimpleReplace => _simpleReplace;
		TASimpleReplace _simpleReplace;

		#region options

		/// <summary>
		/// Default value for the <i>flags</i> parameter used for triggers added afterwards.
		/// </summary>
		public TAFlags DefaultFlags { get; set; }

		/// <summary>
		/// Default value for the <i>postfixType</i> parameter used for triggers added afterwards.
		/// </summary>
		public TAPostfix DefaultPostfixType { get; set; }

		/// <summary>
		/// Default value for the <i>postfixChars</i> parameter used for triggers added afterwards.
		/// Default: null.
		/// </summary>
		/// <remarks>
		/// If null (default), postfix characters are all except alpha-numeric (see <see cref="char.IsLetterOrDigit"/>).
		/// The value cannot contain alpha-numeric characters (exception) and <see cref="WordCharsPlus"/> characters (triggers will not work).
		/// For Enter use \r.
		/// </remarks>
		/// <exception cref="ArgumentException">The value contains letters or digits.</exception>
		public string DefaultPostfixChars {
			get => _defaultPostfixChars;
			set => _defaultPostfixChars = _CheckPostfixChars(value);
		}
		string _defaultPostfixChars;

		static string _CheckPostfixChars(string s) {
			if (s.NE()) return null;
			int k = 0;
			for (int i = 0; i < s.Length; i++) {
				char c = s[i];
				if (char.IsLetterOrDigit(c)) throw new ArgumentException("Postfix characters contains letters or digits.");
				if (c == '\r') k |= 1;
				if (c == '\n') k |= 2;
			}
			if (k == 2) print.warning("Postfix characters contains \\n (Ctrl+Enter) but no \\r (Enter).");
			return s;
		}

		/// <summary>
		/// The postfix key for all triggers where postfix type is <see cref="TAPostfix.Key"/> or <see cref="TAPostfix.CharOrKey"/> (default).
		/// Can be Ctrl (default), Shift, LCtrl, RCtrl, LShift or RShift.
		/// </summary>
		/// <exception cref="ArgumentException">The value is not Ctrl or Shift.</exception>
		/// <remarks>
		/// This property is applied to all triggers, not just to those added afterwards.
		/// </remarks>
		public KKey PostfixKey {
			get => _postfixKey;
			set {
				var mod = keys.Internal_.KeyToMod(value);
				switch (mod) {
				case KMod.Ctrl: case KMod.Shift: break;
				default: throw new ArgumentException("Must be Ctrl, Shift, LCtrl, RCtrl, LShift or RShift.");
				}
				_postfixMod = mod; _postfixKey = value;
			}
		}
		KKey _postfixKey = KKey.Ctrl;
		KMod _postfixMod = KMod.Ctrl;

		/// <summary>
		/// Additional word characters (non-delimiters).
		/// Default: null.
		/// </summary>
		/// <remarks>
		/// By default, only alpha-numeric characters (<see cref="char.IsLetterOrDigit"/> returns true) are considered word characters. You can use this property to add more word characters, for example "_#".
		/// This is used to avoid activating triggers when a trigger text found inside a word.
		/// This property is applied to all triggers, not just to those added afterwards.
		/// </remarks>
		public string WordCharsPlus { get; set; }

		/// <summary>
		/// Clears all options.
		/// </summary>
		public void ResetOptions() {
			this.DefaultFlags = 0;
			this.DefaultPostfixType = 0;
			this._defaultPostfixChars = null;
			this.PostfixKey = KKey.Ctrl;
			this.WordCharsPlus = null;
		}

		#endregion

		/// <summary>
		/// The last added trigger.
		/// </summary>
		public AutotextTrigger Last => _lastAdded;
		AutotextTrigger _lastAdded;

		bool ITriggers.HasTriggers => _lastAdded != null;

		void ITriggers.StartStop(bool start) {
			this._len = 0;
			this._singlePK = false;
			this._wFocus = default;
			this._deadKey = default;
		}

		internal unsafe void HookProc(HookData.Keyboard k, TriggerHookContext thc) {
			Debug.Assert(!k.IsInjectedByAu); //server must ignore

			//print.it(k);
			//perf.first();

			if (ResetEverywhere) { //set by mouse hooks on click left|right and by keyboard hooks on Au-injected key events. In shared memory.
				ResetEverywhere = false;
				_Reset();
			}

			if (k.IsUp) {
				if (_singlePK) {
					_singlePK = false;
					if (_IsPostfixMod(thc.ModThis)) {
						//print.it("< Ctrl up >");
						_Trigger(default, true, _GetFocusedWindow(), thc);
						//goto gReset; //no, resets if triggered, else don't reset
					}
				}
				return;
			}

			bool _IsPostfixMod(KMod mod) => mod == _postfixMod && (_postfixKey <= KKey.Ctrl || k.vkCode == _postfixKey) && !k.IsInjected;

			var modd = thc.ModThis;
			if (modd != 0) {
				_singlePK = _IsPostfixMod(modd) && thc.Mod == KMod.Ctrl;
				return;
			}
			_singlePK = false;

			if (k.IsAlt && 0 == (thc.Mod & (KMod.Ctrl | KMod.Shift))) goto gReset; //Alt+key without other modifiers. Info: AltGr can add Ctrl, therefore we process it. Info: still not menu mode. Tested: never types a character, except Alt+numpad numbers.

			var vk = k.vkCode;
			if (vk >= KKey.PageUp && vk <= KKey.Down) goto gReset; //PageUp, PageDown, End, Home, Left, Up, Right, Down

			wnd wFocus = _GetFocusedWindow();
			if (wFocus.Is0) goto gReset;

			var c = stackalloc char[8]; int n;
			if (vk == KKey.Packet) {
				c[0] = (char)k.scanCode;
				n = 1;
			} else {
				n = _KeyToChar(c, vk, k.scanCode, wFocus, thc.Mod);
				if (n == 0) { //non-char key
					if (thc.Mod == 0) switch (vk) { case KKey.CapsLock: case KKey.NumLock: case KKey.ScrollLock: case KKey.Insert: case KKey.Delete: return; }
					goto gReset;
				}
				if (n < 0) return; //dead key
			}
			//print.it(n, c[0], c[1]);

			for (int i = 0; i < n; i++) _Trigger(c[i], false, wFocus, thc);

			return;
			gReset:
			_Reset();
		}

		static wnd _GetFocusedWindow() {
			if (!miscInfo.getGUIThreadInfo(out var gt)) return wnd.active;
			if (0 != (gt.flags & (GTIFlags.INMENUMODE | GTIFlags.INMOVESIZE))) return default; //the character will not be typed when showing menu (or just Alt or F10 pressed) or moving/resizing window. Of course this will not work with nonstandard menus, eg in Word, as well as with other controls that don't accept text.
			return gt.hwndFocus; //if no focus, the thread will not receive wm-keydown etc
		}

		int _len; //count of valid user-typed characters in _text
		bool _singlePK; //used to detect postfix key (Ctrl or Shift)
		wnd _wFocus; //the focused window/control. Used to reset if focus changed.

		void _Reset() {
			_len = 0;
			_singlePK = false;
			//_wFocus = default;
		}

		internal static unsafe bool ResetEverywhere {
			get => SharedMemory_.Ptr->triggers.resetAutotext;
			set => SharedMemory_.Ptr->triggers.resetAutotext = value;
		}

		unsafe void _Trigger(char c, bool isPK, wnd wFocus, TriggerHookContext thc) {
			//perf.next();
			if (wFocus != _wFocus) {
				_Reset();
				_wFocus = wFocus;
			}
			if (wFocus.Is0) return;

			int nc = _len;
			_DetectedPostfix postfixType;
			char postfixChar = default;
			if (isPK) {
				postfixType = _DetectedPostfix.Key;
			} else {
				//print.it((int)c);

				if (c < ' ' || c == 127) {
					switch (c) {
					case (char)8: //Backspace
						if (_len > 0) _len--;
						return;
					case '\t':
					case '\r':
					case '\n':
						break;
					default: //Ctrl+C etc generate control characters. Also Esc.
						_Reset();
						return;
						//tested: control codes <32 in most windows don't type characters
						//tested: Ctrl+Backspace (127) in some windows types a rectangle, in others erases previous word
					}
				}

				bool isWordChar = _IsWordChar(c);
				postfixType = isWordChar ? _DetectedPostfix.None : _DetectedPostfix.Delim;

				const int c_bufLen = 127;
				if (nc >= c_bufLen) { //buffer full. Remove word from beginning.
					int i;
					for (i = 0; i < c_bufLen; i++) if (!_text[i].isWordChar) break;
					if (i == c_bufLen) {
						if (!isWordChar) { _len = 0; return; }
						i = c_bufLen - 20; //remove several first chars. Triggers will not match anyway, because max string lenhth is 100.
					}
					nc = c_bufLen - ++i;
					fixed (_Char* p = _text) Api.memmove(p, p + i, nc * sizeof(_Char));
				}

				_text[nc] = new _Char(c, isWordChar);
				_len = nc + 1;
				if (isWordChar) nc++; else postfixChar = c;

				//DebugPrintText();
			}

			if (nc == 0) return;
			//perf.next();
			g1:
			for (int k = 0, ii = nc - 1, jj = 0; ii >= 0 && jj <= 24; ii--, jj += 8) { //create dictionary key from 1-4 last characters lowercase
				k |= (byte)_text[ii].cLow << jj;
				//print.it((uint)k);
				if (_d.TryGetValue(k, out var v)) {
					AutotextTriggerArgs args = null;
					for (; v != null; v = v.next) {
						var x = v as AutotextTrigger;

						var s = x.text;
						int i = nc - s.Length;
						if (i < 0) continue;
						if (i > 0 && _text[i - 1].isWordChar) continue;

						if (0 != (x.flags & TAFlags.MatchCase)) {
							for (int j = 0; i < nc; i++, j++) if (_text[i].c != s[j]) break;
						} else {
							for (int j = 0; i < nc; i++, j++) if (_text[i].cLow != s[j]) break;
						}
						if (i < nc) continue;

						switch (x.postfixType) {
						case TAPostfix.CharOrKey:
							if (postfixType == _DetectedPostfix.None) continue;
							break;
						case TAPostfix.Char:
							if (postfixType != _DetectedPostfix.Delim) continue;
							break;
						case TAPostfix.Key:
							if (postfixType != _DetectedPostfix.Key) continue;
							break;
						}

						if (x.postfixChars != null && postfixType == _DetectedPostfix.Delim && x.postfixChars.IndexOf(c) < 0) continue;

						if (v.DisabledThisOrAll) continue;

						if (args == null) { //may need for scope callbacks too
							bool hasPChar = postfixType == _DetectedPostfix.Delim;
							int n = s.Length, to = nc; if (hasPChar) { n++; to++; }
							var tt = new string('\0', n);
							i = to - n; fixed (char* p = tt) for (int j = 0; i < to;) p[j++] = _text[i++].c;
							thc.args = args = new AutotextTriggerArgs(x, thc.Window, tt, hasPChar);
						} else args.Trigger = x;

						if (!x.MatchScopeWindowAndFunc(thc)) continue;

						_Reset(); //CONSIDER: flag DontReset. If the action generates keyboard events or mouse clicks, our kooks will reset.

						thc.trigger = x;
						return;
					}

				}
			}
			//maybe there are items where text ends with delim and no postfix
			if (postfixType == _DetectedPostfix.Delim) {
				postfixType = _DetectedPostfix.None;
				postfixChar = '\0';
				nc++;
				goto g1;
			}
			//perf.nw(); //about 90% of time takes _KeyToChar (ToUnicodeEx and GetKeyboardLayout).
		}

		unsafe int _KeyToChar(char* c, KKey vk, uint sc, wnd wFocus, KMod mod) {
			var hkl = Api.GetKeyboardLayout(wFocus.ThreadId);
			var ks = stackalloc byte[256];
			_SetKS(mod);
			int n = Api.ToUnicodeEx((uint)vk, sc, ks, c, 8, 0, hkl); //bad: resets dead key

			//if need, set dead key again
			var d = stackalloc char[8];
			if (_deadKey.vk != 0 && _deadKey.hkl == hkl) {
				_SetKS(_deadKey.mod);
				Api.ToUnicodeEx((uint)_deadKey.vk, _deadKey.sc, ks, d, 8, 0, hkl);
				_deadKey.vk = 0;
			} else if (n < 0) {
				_deadKey.vk = vk; _deadKey.sc = sc; _deadKey.mod = mod; _deadKey.hkl = hkl;
				Api.ToUnicodeEx((uint)vk, sc, ks, d, 8, 0, hkl);
			}

			void _SetKS(KMod m) {
				ks[(int)KKey.Shift] = (byte)((0 != (m & KMod.Shift)) ? 0x80 : 0);
				ks[(int)KKey.Ctrl] = (byte)((0 != (m & KMod.Ctrl)) ? 0x80 : 0);
				ks[(int)KKey.Alt] = (byte)((0 != (m & KMod.Alt)) ? 0x80 : 0);
				ks[(int)KKey.Win] = (byte)((0 != (m & KMod.Win)) ? 0x80 : 0);
				ks[(int)KKey.CapsLock] = (byte)(keys.isCapsLock ? 1 : 0); //don't need this for num lock
			}

			return n;

			//info: this works, but:
			//1. Does not work with eg Chinese input method.
			//2. Catches everything that would later be changed by the app, or by a next hook, etc.
			//3. Don't know how to get Alt+numpad characters. Ignore them.
			//	On Alt up could call tounicodeex with sc with flag 0x8000. It gets the char, but resets keyboard state, and the char is not typed.
			//4. In console windows does not work with Unicode characters.

			//if(MapVirtualKeyEx(vk, MAPVK_VK_TO_CHAR, hkl)&0x80000000) { print.it("DEAD"); return -1; } //this cannot be used because resets dead key
		}

		_DeadKey _deadKey;

		struct _DeadKey
		{
			public KKey vk;
			public KMod mod;
			public uint sc;
			public nint hkl;
		}

		//User-typed characters. _len characters are valid.
		_Char[] _text = new _Char[128];

		struct _Char
		{
			public char c, cLow;
			public bool isWordChar;

			public _Char(char ch, bool isWordChar) {
				c = ch;
				cLow = char.ToLowerInvariant(ch);
				this.isWordChar = isWordChar;
			}
		}

		enum _DetectedPostfix { None, Delim, Key }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		bool _IsWordChar(char c) {
			if (char.IsLetterOrDigit(c)) return true; //speed: 4 times faster than Api.IsCharAlphaNumeric. Tested with a string containing 90% ASCII chars.
			var v = WordCharsPlus;
			return v != null && v.Contains(c);
		}

		[Conditional("DEBUG")]
		unsafe void _DebugPrintText() {
			var s = new string(' ', _len);
			fixed (char* p = s) for (int i = 0; i < s.Length; i++) p[i] = _text[i].c;
			print.it(s);
		}

		internal static unsafe void JitCompile() {
			Jit_.Compile(typeof(AutotextTriggers), nameof(HookProc), nameof(_Trigger), nameof(_KeyToChar));
		}

		/// <summary>
		/// Used by foreach to enumerate added triggers.
		/// </summary>
		public IEnumerator<AutotextTrigger> GetEnumerator() {
			foreach (var kv in _d) {
				for (var v = kv.Value; v != null; v = v.next) {
					var x = v as AutotextTrigger;
					yield return x;
				}
			}
		}

		IEnumerator IEnumerable.GetEnumerator() {
			throw new NotImplementedException();
		}
	}

	/// <summary>
	/// Arguments for actions of autotext triggers.
	/// Use function <see cref="Replace"/> to replace user-typed text.
	/// </summary>
	public class AutotextTriggerArgs : TriggerArgs
	{
		///
		public AutotextTrigger Trigger { get; internal set; }

		///
		[EditorBrowsable(EditorBrowsableState.Never)]
		public override ActionTrigger TriggerBase => Trigger;

		/// <summary>
		/// The active window.
		/// </summary>
		public wnd Window { get; }

		/// <summary>
		/// The user-typed text. If <see cref="HasPostfixChar"/>==true, the last character is the postfix delimiter character.
		/// </summary>
		public string Text { get; }

		/// <summary>
		/// true if the autotext activated when the user typed a postfix delimiter character. Then it is the last character in <see cref="Text"/>.
		/// </summary>
		public bool HasPostfixChar { get; }

		/// <summary>
		/// If true, <see cref="Replace"/> will select text with Shift+Left instead of erasing with Backspace. Except in console windows.
		/// Initially true if flag <see cref="TAFlags.ShiftLeft"/> is set. Can be changed by a callback function, for example to use or not use Shift+Left only with some windows.
		/// </summary>
		public bool ShiftLeft { get; set; }

		///
		public AutotextTriggerArgs(AutotextTrigger trigger, wnd w, string text, bool hasPChar) {
			Trigger = trigger;
			Window = w;
			Text = text;
			HasPostfixChar = hasPChar;
			ShiftLeft = trigger.flags.Has(TAFlags.ShiftLeft);

			//print.it($"'{text}'", hasPChar);
		}

		/// <summary>
		/// Replaces the user-typed text with the specified text or/and HTML.
		/// </summary>
		/// <param name="text">The replacement text. Can be null.</param>
		/// <param name="html">
		/// The replacement HTML. Can be full HTML or fragment. See <see cref="clipboardData.AddHtml"/>.
		/// Can be specified only <i>text</i> or only <i>html</i> or both. If both, will paste <i>html</i> in apps that support it, elsewhere <i>text</i>. If only <i>html</i>, in apps that don't support HTML will paste <i>html</i> as text.
		/// </param>
		/// <remarks>
		/// Options for this function can be specified when adding triggers, in the <i>flags</i> parameter. Or before adding triggers, with <see cref="AutotextTriggers.DefaultFlags"/>.
		/// 
		/// If the replacement text contains substring "[[|]]", removes it and moves the text cursor (caret) there with the Left key. See example. Not if <i>html</i> specified.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// Triggers.Autotext["#exa"] = o => o.Replace("<example>[[|]]</example>");
		/// ]]></code>
		/// More examples: <see cref="ActionTriggers"/>.
		/// </example>
		public void Replace(string text, string html = null) {
			if (text == "") text = null;
			if (html == "") html = null;
			_Replace(text, html, null);
		}

		/// <summary>
		/// Replaces the user-typed text with the specified text, keys, cliboard data, etc.
		/// </summary>
		/// <param name="keysEtc">The same as with <see cref="keys.send"/>.</param>
		/// <remarks>
		/// Options for this function can be specified when adding triggers, in the <i>flags</i> parameter. Or before adding triggers, with <see cref="AutotextTriggers.DefaultFlags"/>. This function uses <see cref="TAFlags.Confirm"/>, <see cref="TAFlags.DontErase"/>, <see cref="TAFlags.ShiftLeft"/>, <see cref="TAFlags.RemovePostfix"/>.
		/// 
		/// If used flag <see cref="TAFlags.Confirm"/>, for label can be used first argument with prefix "!!"; else displays all string arguments.
		/// </remarks>
		public void Replace2([ParamString(PSFormat.keys)] params KKeysEtc[] keysEtc) {
			_Replace(null, null, keysEtc ?? throw new ArgumentNullException());
		}

		void _Replace(string r, string html, KKeysEtc[] ke) {
			bool onlyText = r != null && html == null;
			var flags = this.Trigger.flags;

			string t = this.Text;

			int caret = -1;
			if (onlyText) {
				caret = r.Find("[[|]]");
				if (caret >= 0) r = r.Remove(caret, 5);

				if (!flags.HasAny(TAFlags.ReplaceRaw | TAFlags.MatchCase)) {
					int len = t.Length; if (this.HasPostfixChar) len--;
					int i; for (i = 0; i < len; i++) if (char.IsLetterOrDigit(t[i])) break; //eg if t is "#abc", we need a, not #
					if (i < len && char.IsUpper(t[i])) {
						bool allUpper = false; //make r ucase if t contains 0 lcase chars and >=2 ucase chars
						while (++i < len) {
							var uc = char.GetUnicodeCategory(t[i]);
							if (uc == UnicodeCategory.LowercaseLetter) { allUpper = false; break; }
							if (uc == UnicodeCategory.UppercaseLetter) allUpper = true;
						}
						r = r.Upper(allUpper ? SUpper.AllChars : SUpper.FirstChar);
					}
				}
			}

			if (flags.Has(TAFlags.Confirm)) {
				string confirmText;
				if (!ke.NE_()) {
					confirmText = null;
					if (ke[0].Value is string s2 && s2.Starts("!!")) {
						confirmText = s2[2..];
						ke = ke.RemoveAt(0);
					} else {
						foreach (var v in ke) if (v.Value is string s1) { if (confirmText != null) confirmText += ", "; confirmText += s1; }
					}
				} else confirmText = r ?? html;
				if (!Confirm(confirmText)) return;
			}

			var k = new keys(opt.key);
			var optk = k.Options;
			bool uwp = 0 != this.Window.Window.IsUwpApp;
			if (uwp) {
				optk.KeySpeed = Math.Clamp(optk.KeySpeed * 2, 20, 100); //default 1
				optk.KeySpeedClipboard = Math.Clamp(optk.KeySpeedClipboard * 2, 20, 100);
				optk.TextSpeed = Math.Clamp(optk.TextSpeed * 2, 10, 50); //default 0
				int n1 = optk.PasteLength - 100; if (n1 > 0) optk.PasteLength = 100 + n1 / 5; //default 200 -> 120
			} else {
				optk.KeySpeed = Math.Clamp(optk.KeySpeed, 2, 20);
				optk.TextSpeed = Math.Min(optk.TextSpeed, 10);
			}
			optk.PasteWorkaround = true;
			//info: later Options.Hook can override these values.

			int erase = flags.Has(TAFlags.DontErase) ? (this.HasPostfixChar ? 1 : 0) : t.Length;
			if (erase > 0) {
				bool shiftLeft = this.ShiftLeft && !wnd.active.IsConsole;
				if (shiftLeft) { k.AddKey(KKey.Shift, true); k.AddKey(KKey.Left); } else k.AddKey(KKey.Back);
				if (erase > 1) k.AddRepeat(erase);
				if (shiftLeft) k.AddKey(KKey.Shift, false);
				//note: Back down down ... up does not work with some apps

				//some apps have async input and eg don't erase all if too fast.
				//	UWP is the champion. Also noticed in Chrome address bar (rare), Dreamweaver when pasting (1/5 times), etc.
				int sleep = 5 + erase; if (uwp) sleep *= 10;
				k.AddSleep(sleep);
				k.Pasting += (_, _) => wait.ms(sleep * 2);
			} else if (uwp) {
				k.Pasting += (_, _) => wait.ms(50);
			}

			KKey pKey = default; char pChar = default;
			if (this.HasPostfixChar && !flags.Has(TAFlags.RemovePostfix)) {
				char ch = t[^1];
				if (ch == ' ' || ch == '\r' || ch == '\t') pKey = (KKey)ch; //avoid trimming of pasted text or pasting '\r'; here VK_ == ch.
				else if (onlyText) r += ch.ToString();
				else pChar = ch;
			}

			if (ke != null) k.Add(ke); else k.AddText(r, html);

			if (pKey != default) k.AddKey(pKey); else if (pChar != default) k.AddText(pChar.ToString(), OKeyText.KeysOrChar);

			if (caret >= 0) {
				int keyLeft = 0;
				for (int i = caret; i < r.Length; keyLeft++) {
					char c = r[i++];
					if (c == '\r') {
						if (i < r.Length && r[i] == '\n') i++;
					} else if (char.IsHighSurrogate(c)) {
						if (i < r.Length && char.IsLowSurrogate(r[i])) i++;
					}
				}
				if (pKey != default || pChar != default) keyLeft++;
				if (keyLeft > 0) {
					k.AddKey(KKey.Left);
					if (keyLeft > 1) k.AddRepeat(keyLeft);
				}
			}

			k.SendIt();
		}

		/// <summary>
		/// If <see cref="HasPostfixChar"/>==true, sends the postfix character (last character of <see cref="Text"/>) to the active window.
		/// </summary>
		public void SendPostfix() {
			if (this.HasPostfixChar) new keys(opt.key).AddText(this.Text[^1..], OKeyText.KeysOrChar).SendIt();
		}

		/// <summary>
		/// Shows a 1-item menu below the text cursor (caret) or mouse cursor.
		/// Returns true if the user clicked the item or pressed Enter or Tab. Other keys close the menu.
		/// </summary>
		/// <param name="text">Menu item text. This function limits it to 300 characters. If null, uses "Autotext".</param>
		/// <remarks>
		/// This function is used by <see cref="Replace"/> when used flag <see cref="TAFlags.Confirm"/>.
		/// </remarks>
		/// <example>
		/// Note: the Triggers in examples is a field or property like <c>readonly ActionTriggers Triggers = new();</c>.
		/// <code><![CDATA[
		/// var tt = Triggers.Autotext;
		/// tt["con1", TAFlags.Confirm] = o => o.Replace("Flag Confirm");
		/// tt["con2"] = o => { if(o.Confirm("Example")) o.Replace("Function Confirm"); };
		/// Triggers.Run();
		/// ]]></code>
		/// </example>
		public bool Confirm(string text = "Autotext") {
			text ??= "Autotext";
			var m = new popupMenu { RawText = true };
			m.Add(1, text.Limit(300));
			m.KeyboardHook = (m, g) => {
				if (g.Key is KKey.Enter or KKey.Tab or KKey.Escape) {
					if (g.Key != KKey.Escape) m.FocusedItem = m.Items.First();
					return MKHook.Default;
				}
				return MKHook.Close;
			};
			return 1 == m.Show(MSFlags.ByCaret);
			//CONSIDER: one QM2 user wanted, if cannot show at caret, option to show in screen center
		}

		//FUTURE: Menu.
	}

	/// <remarks>Infrastructure.</remarks>
	public class TASimpleReplace
	{
		AutotextTriggers _host;

		internal TASimpleReplace(AutotextTriggers host) {
			_host = host;
		}

		/// <summary>
		/// Adds an autotext trigger. Its action calls <see cref="AutotextTriggerArgs.Replace"/>.
		/// More info: <see cref="AutotextTriggers.this[string, TAFlags?, TAPostfix?, string, string, int]"/>.
		/// </summary>
		/// <exception cref="Exception">Exceptions of <see cref="AutotextTriggers.this[string, TAFlags?, TAPostfix?, string, string, int]"/>.</exception>
		public string this[string text, TAFlags? flags = null, TAPostfix? postfixType = null, string postfixChars = null, [CallerFilePath] string f_ = null, [CallerLineNumber] int l_ = 0] {
			set {
				_host[text, flags, postfixType, postfixChars, f_, l_] = o => o.Replace(value);
			}
		}
	}
}
