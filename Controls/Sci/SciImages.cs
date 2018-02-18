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

using Au;
using Au.Types;
using static Au.NoClass;

namespace Au.Controls
{
	using static Sci;

	/// <summary>
	/// Gets image file paths etc from Scintilla control text and displays the images below that lines.
	/// </summary>
	/// <remarks>
	/// Draws images in annotation areas.
	/// Supports text annotations too, below images and in no-image lines. But it is limited:
	/// 1. To set/get it use <see cref="SciText.AnnotationText(int, string)"/>, not direct Scintilla API.
	/// 2. You cannot hide all annotations (SCI_ANNOTATIONSETVISIBLE). This class sets it to show always.
	/// 3. You cannot clear all annotations (SCI_ANNOTATIONCLEARALL).
	/// 4. Setting annotation styles is currently not supported.
	/// </remarks>
	public unsafe class SciImages
	{
		class _Image
		{
			public long nameHash;
			public byte[] data;
			public int width, height;
		}

		AuScintilla _c;
		SciText _t;
		IntPtr _callbackPtr;
		bool _isEditor;

		class _ThreadSharedData
		{
			public WeakReference<byte[]> BufferForAnnot, BufferForText;
			List<_Image> _a;
			public int CacheSize { get; private set; }

			public void AddImage(_Image im)
			{
				if(_a == null) _a = new List<_Image>();
				_a.Add(im);
				CacheSize += im.data.Length;
			}

			public _Image FindImage(long nameHash)
			{
				if(_a != null)
					for(int j = 0; j < _a.Count; j++) if(_a[j].nameHash == nameHash) return _a[j];
				return null;
			}

			public void ClearCache()
			{
				if(_a == null) return;
				_a.Clear();
				CacheSize = 0;
			}

			/// <summary>
			/// If cache is large (at least MaxCacheSize and 4 images), removes about 3/4 of older cached images.
			/// Will auto-reload from files etc when need.
			/// </summary>
			public void CompactCache()
			{
				if(_a == null) return;
				//Print(_cacheSize);
				if(CacheSize < MaxCacheSize || _a.Count < 4) return;
				CacheSize = 0;
				int n = _a.Count, max = MaxCacheSize / 4;
				while(CacheSize < max && n > 2) CacheSize += _a[--n].data.Length;
				_a.RemoveRange(0, n);
			}

			public int MaxCacheSize { get; set; } = 4 * 1024 * 1024;
		}

		//All SciImages of a thread share single cache etc.
		[ThreadStatic] static _ThreadSharedData t_data;

		/// <summary>
		/// Prepares this variable and the Scintilla control to display images.
		/// Calls SCI_ANNOTATIONSETVISIBLE(ANNOTATION_STANDARD). Need it because will draw images in annotation areas.
		/// </summary>
		/// <param name="c">The control.</param>
		/// <param name="isEditor">Display images that are not in "&lt;image "path etc"&gt; tag. Then does not display icons of files that don't contain images. Then limits image height to 10 lines.</param>
		internal SciImages(AuScintilla c, bool isEditor = false)
		{
			if(t_data == null) t_data = new _ThreadSharedData();
			_c = c;
			_t = c.ST;
			_isEditor = isEditor;
			_sci_AnnotationDrawCallback = _AnnotationDrawCallback_;
			_callbackPtr = Marshal.GetFunctionPointerForDelegate(_sci_AnnotationDrawCallback);
			_c.Call(SCI_SETANNOTATIONDRAWCALLBACK, 0, _callbackPtr);
			_visible = AnnotationsVisible.ANNOTATION_STANDARD;
			_c.Call(SCI_ANNOTATIONSETVISIBLE, (int)_visible); //keep annotations always visible. Adding annotations while visible is slower, but much faster than getting images from files etc.
		}

		/// <summary>
		/// Removes all cached images.
		/// Will auto-reload from files etc when need.
		/// </summary>
		public void ClearCache() { t_data.ClearCache(); }

		/// <summary>
		/// If cache is large (at least MaxCacheSize and 4 images), removes about 3/4 of older cached images.
		/// Will auto-reload from files etc when need.
		/// </summary>
		public void CompactCache() { t_data.CompactCache(); }

		/// <summary>
		/// Maximal size of the image cache.
		/// Default 4 MB.
		/// </summary>
		public int MaxCacheSize { get => t_data.MaxCacheSize; set => t_data.MaxCacheSize = value; }

		/// <summary>
		/// Sets image annotations for one or more lines of text.
		/// </summary>
		/// <param name="firstLine">First line index.</param>
		/// <param name="text">Text that starts at line firstLine.</param>
		/// <param name="length">Text length.</param>
		/// <param name="allText">Added all text (not edited or appended).</param>
		/// <param name="textPos">Position where the text starts.</param>
		public void _SetImagesForTextRange(int firstLine, byte* text, int length, bool allText, int textPos)
		{
			if(length < 10) return; //"C:\x.ico"

			bool annotAdded = false;
			int iLine = firstLine;
			for(int i = 0, iTo = i + length; i < iTo; iLine++) {
				int maxHeight = 0, totalWidth = 0;
				_Image u; bool isMulti = false; int imStrStart = 0;
				while(null != (u = _GetImageInLine(text, ref i, iTo, ref isMulti, ref imStrStart))) {
					if(maxHeight < u.height) maxHeight = u.height;
					if(totalWidth > 0) totalWidth += 30;
					totalWidth += u.width;

					//if(!isMulti) {
					//	bool hide = true; int imStrEnd = i;
					//	if(_isEditor) {
					//		if(text[imStrStart + 1] == '~') { imStrStart += 3; imStrEnd--; } else hide = false;
					//	} else {
					//		if(imStrEnd < iTo && text[imStrEnd] == '>') imStrEnd++;
					//		//PrintList(imStrStart, imStrEnd);
					//	}
					//	if(hide) {
					//		int len = imStrEnd - imStrStart;
					//		_c.Call(SCI_STARTSTYLING, imStrStart + textPos);
					//		_c.Call(SCI_SETSTYLING, len, STYLE_HIDDEN);
					//	}
					//}
					if(!isMulti && _isEditor && text[imStrStart + 1] == '~') { //if !_isEditor, the <image...> tags are already hidden by SciTags
						_c.Call(SCI_STARTSTYLING, imStrStart + 2 + textPos);
						_c.Call(SCI_SETSTYLING, i - imStrStart - 3, STYLE_HIDDEN);
					}
				}

				if(maxHeight == 0) continue;

				int annotLen = _c.Call(SCI_ANNOTATIONGETTEXT, iLine); //we'll need old annotation text later, and we'll get it into the same buffer after the new image info

				//calculate n annotation lines from image height
				int lineHeight = _t.LineHeight(iLine); if(lineHeight <= 0) continue;
				int nAnnotLines = Math.Min((maxHeight + (lineHeight - 1)) / lineHeight, 255);
				//PrintList(lineHeight, maxHeight, nAnnotLines);

				fixed (byte* b0 = Au.Util.Buffers.Get(annotLen + nAnnotLines + 20, ref t_data.BufferForAnnot)) {
					var b = b0;
					*b++ = 3; Api._ltoa(totalWidth << 8 | nAnnotLines, b, 16); while(*(++b) != 0) { }
					while(nAnnotLines-- > 1) *b++ = (byte)'\n';
					*b = 0;

					//An annotation possibly already exists. Possible cases:
					//1. No annotation. Need to add our image annotation.
					//2. A text-only annotation. Need to add our image annotation + that text.
					//3. Different image, no text. Need to replace it with our image annotation.
					//4. Different image + text. Need to replace it with our image annotation + that text.
					//5. This image, with or without text. Don't need to change.
					if(annotLen > 0) {
						//get existing annotation into the same buffer after our image info
						var a = b + 1;
						_c.Call(SCI_ANNOTATIONGETTEXT, iLine, a);
						a[annotLen] = 0;
						//Print($"OLD: '{new string((sbyte*)a)}'");

						//is it our image info?
						int imageLen = (int)(b - b0);
						if(annotLen >= imageLen) {
							int j;
							for(j = 0; j < imageLen; j++) if(a[j] != b0[j]) goto g1;
							if(annotLen == imageLen || a[imageLen] == '\n') continue; //case 5
						}
						g1:
						//contains image?
						if(a[0] == 3) {
							int j = _ParseAnnotText(a, annotLen, out var _);
							if(j < annotLen) { //case 4
								Api.memmove(a, a + j, annotLen - j + 1);
								b[0] = (byte)'\n';
							} //else case 3
						} else { //case 2
							b[0] = (byte)'\n';
						}
					} //else case 1

					//Print($"NEW: '{new string((sbyte*)b0)}'");
					//Perf.First();
					if(!annotAdded) {
						annotAdded = true;
						if(allText) _c.Call(SCI_ANNOTATIONSETVISIBLE, (int)AnnotationsVisible.ANNOTATION_HIDDEN);
					}
					_c.Call(SCI_ANNOTATIONSETTEXT, iLine, b0);
					//Perf.NW();
				}
			}

			if(annotAdded && allText) {
				//Perf.First();
				_c.Call(SCI_ANNOTATIONSETVISIBLE, (int)_visible);
				//Perf.NW();
			}

			//never mind: scintilla prints without annotations, therefore without images too.
		}

		/// <summary>
		/// Parses annotation text.
		/// If it starts with image info string ("\x3NNN\n\n..."), returns its length. Else returns 0.
		/// </summary>
		/// <param name="s">Annotation text. Can start with image info string or not.</param>
		/// <param name="length">s length.</param>
		/// <param name="imageInfo">The NNN part of image info, or 0.</param>
		static int _ParseAnnotText(byte* s, int length, out int imageInfo)
		{
			imageInfo = 0;
			if(s == null || length < 4 || s[0] != '\x3') return 0;
			byte* s2;
			int k = Api.strtoi(s + 1, &s2, 16);
			int len = (int)(s2 - s); if(len < 4) return 0;
			int n = k & 0xff;
			len += (n - 1);
			if(n < 1 || length < len) return 0;
			if(length > len) len++; //\n between image info and visible annotation text
			imageInfo = k;
			return len;
		}

		/// <summary>
		/// Sets annotation text, preserving existing image info.
		/// </summary>
		/// <param name="line"></param>
		/// <param name="s">New text without image info.</param>
		internal void LibAnnotationText(int line, string s)
		{
			int n = _c.Call(SCI_ANNOTATIONGETTEXT, line);
			if(n > 0) {
				int lens = (s == null) ? 0 : s.Length;
				fixed (byte* b = Au.Util.Buffers.Get(n + 1 + lens * 3, ref t_data.BufferForAnnot)) {
					_c.Call(SCI_ANNOTATIONGETTEXT, line, b); b[n] = 0;
					int imageLen = _ParseAnnotText(b, n, out var _);
					if(imageLen > 0) {
						//info: now len<=n
						if(lens == 0) {
							if(imageLen == n) return; //no "\nPrevText"
							b[--imageLen] = 0; //remove "\nPrevText"
						} else {
							if(imageLen == n) b[imageLen++] = (byte)'\n'; //no "\nPrevText"
							Convert_.Utf8FromString(s, b + imageLen, lens * 3);
						}
						_c.Call(SCI_ANNOTATIONSETTEXT, line, b);
						return;
					}
				}
			}
			_t.LibAnnotationText(line, s);
		}

		/// <summary>
		/// Gets annotation text without image info.
		/// </summary>
		internal string LibAnnotationText(int line)
		{
			int n = _c.Call(SCI_ANNOTATIONGETTEXT, line);
			if(n > 0) {
				fixed (byte* b0 = Au.Util.Buffers.Get(n, ref t_data.BufferForAnnot)) {
					var b = b0;
					_c.Call(SCI_ANNOTATIONGETTEXT, line, b); b[n] = 0;
					int imageLen = _ParseAnnotText(b, n, out var _);
					//info: now len<=n
					if(imageLen < n) {
						if(imageLen != 0) { b += imageLen; n -= imageLen; }
						return Convert_.Utf8ToString(b, n);
					}
				}
			}
			return "";
		}

		_Image _GetImageInLine(byte* s, ref int iFrom, int iTo, ref bool isMulti, ref int imageStringStartPos)
		{
			g1:
			int i = iFrom;
			if(i >= iTo - 4) { iFrom = iTo; isMulti = false; return null; }
			//find next "string". If not found, return next line or the end of whole string.
			if(!isMulti) //else i is at image2 in "image1|image2"
			{
				i = _FindQuoteInLine(s, i, iTo);
				if(i == iTo || s[i++] != '\"') { iFrom = i; isMulti = false; return null; }
			}
			iFrom = _FindQuoteInLine(s, i, iTo);
			if(iFrom == iTo || s[iFrom++] != '\"') { isMulti = false; return null; }
			int i2 = iFrom - 1;

			//if not editor, skip if not <image "..."
			if(!isMulti) {
				if(_isEditor) imageStringStartPos = i - 1;
				else if(i >= 8 && Au.Util.LibCharPtr.AsciiStartsWith(s + i - 8, "<image ")) imageStringStartPos = i - 8;
				else goto g1;
			}

			//support "image1|image2|..."
			int i3 = Au.Util.LibCharPtr.AsciiFindChar(s + i, i2 - i, (byte)'|') + i;
			if(i3 >= i) { i2 = i3; iFrom = i3 + 1; isMulti = true; } else isMulti = false;

			//is it an image string?
			var imType = ImageUtil.ImageTypeFromString(!_isEditor, s + i, i2 - i);
			if(imType == ImageUtil.ImageType.None) goto g1;

			var d = t_data;

			//is already loaded?
			long hash = Convert_.HashFnv1_64(s + i, i2 - i);
			var im = d.FindImage(hash);
			//PrintList(im != null, new string((sbyte*)s, i, i2 - i));
			if(im != null) return im;

			//var test = s.Substring(i, i2 - i);
			//PrintList(test, EImageUtil.ImageToString(test));

			switch(imType) {
			case ImageUtil.ImageType.EmbeddedCompressedBmp: i += 2; break; //~:
			case ImageUtil.ImageType.EmbeddedPngGifJpg: i += 6; break; //image:
			case ImageUtil.ImageType.Resource: i += 9; break; //resource:
			}

			string path = new string((sbyte*)s, i, i2 - i, Encoding.UTF8); //CONSIDER: it this the fastest way to convert UTF8 to string?

			//load
			byte[] b = ImageUtil.BmpFileDataFromString(path, imType, !_isEditor);
			if(b == null) goto g1;
			if(!ImageUtil.GetBitmapFileInfo(b, out var q)) goto g1;

			//create _Image
			im = new _Image() {
				data = b,
				nameHash = hash,
				width = q.width,
				height = Math.Min(q.height + IMAGE_MARGIN_TOP + IMAGE_MARGIN_BOTTOM, _isEditor ? 200 : 2000)
			};

			//add to cache
			//Compact cache to avoid memory problems when loaded many big images, eg showing all png in Program Files.
			//Will auto reload when need, it does not noticeably slow down.
			//Cache even very large images, because we draw each line separately, would need to load whole image for each line, which is VERY slow.
			d.CompactCache();
			d.AddImage(im);

			return im;
		}

		const int IMAGE_MARGIN_TOP = 2; //frame + 1
		const int IMAGE_MARGIN_BOTTOM = 1; //just for frame. It is minimal margin, in most cases will be more.

		//Searches for '\"' or '\n' until iTo.
		int _FindQuoteInLine(byte* s, int i, int iTo)
		{
			for(; i < iTo && s[i] != '\n'; i++) if(s[i] == '\"') break;
			return i;
		}

		Sci_AnnotationDrawCallback _sci_AnnotationDrawCallback;
		unsafe int _AnnotationDrawCallback_(void* cbParam, ref Sci_AnnotationDrawCallbackData c)
		{
			//Function info:
			//Called for all annotations, not just for images.
			//Returns image width. Returns 0 if there is no image or when called for annotation text line below image.
			//Called for each line of annotation, not once for whole image. Draws each image slice separately.
			//Called 2 times for each line: step 0 - to get width; step 1 - to draw that image slice on that line.

			//Get image info from annotation text. Return 0 if there is no image info, ie no image.
			//Image info is at the start. Format "\x3XXX", where XXX is a hex number that contains image width and number of lines.
			byte* s = c.text;
			if(c.textLen < 4 || s[0] != '\x3') return 0;
			int k = Api.strtoi(++s, null, 16); if(k < 256) return 0;
			int nLines = k & 0xff, width = k >> 8;

			if(c.step == 0) return width + 1; //just get width
			if(c.annotLine >= nLines) return 0; //an annotation text line below the image lines

			//Get line text, to find image strings.
			//Cannot store array indices in annotation, because they may change.
			//Also cannot store image strings in annotation, because then boxed annotation would be too wide (depends on text length).
			//Getting/parsing text takes less than 20% time. Other time - drawing image.
			int from = _t.LineStart(c.line), to = _t.LineEnd(c.line), len = to - from;
			var text = _GetTextRange(from, to);

			//find image strings and draw the images
			bool hasImages = false;
			var hdc = c.hdc;
			RECT r = c.rect;
			IntPtr pen = Zero, oldPen = Zero;
			try {
				//Handle exceptions because SetDIBitsToDevice may read more than need, like CreateDIBitmap, although I never noticed this.

				//Call _GetImageInLine repeatedly. It finds next image string, finds its cached image or loads and addd to the cache.
				//Then draw the image, and finally frame. Actually just single slice, for this line.

				_Image u; bool isMulti = false; int imStrStart = 0;
				int x = r.left + 1;
				for(int i = 0; null != (u = _GetImageInLine(text, ref i, len, ref isMulti, ref imStrStart));) {
					hasImages = true;

					//draw image
					if(!ImageUtil.GetBitmapFileInfo(u.data, out var q)) { Debug.Assert(false); continue; }
					int isFirstLine = (c.annotLine == 0) ? 1 : 0, hLine = r.bottom - r.top;
					int currentTop = c.annotLine * hLine, currentBottom = currentTop + hLine, imageBottom = q.height + IMAGE_MARGIN_TOP;
					int y = r.top + isFirstLine * IMAGE_MARGIN_TOP, yy = Math.Min(currentBottom, imageBottom) - currentTop;

					if(imageBottom > currentTop && q.width > 0 && q.height > 0) {
						fixed (byte* bp = u.data) {
							ImageUtil.BITMAPFILEHEADER* f = (ImageUtil.BITMAPFILEHEADER*)bp;
							byte* pBits = bp + f->bfOffBits;
							int bytesInLine = Math_.AlignUp(q.width * q.bitCount, 32) / 8;
							int sizF = u.data.Length - f->bfOffBits, siz = bytesInLine * q.height;
							if(q.isCompressed) {
								//this is slow with big images. It seems processes current line + all remaining lines. Such bitmaps are rare.
								int yOffs = -c.annotLine * hLine; if(isFirstLine == 0) yOffs += IMAGE_MARGIN_TOP;
								var ok = SetDIBitsToDevice(hdc, x, r.top + isFirstLine * IMAGE_MARGIN_TOP,
									q.width, q.height, 0, yOffs, 0, q.height,
									pBits, q.biHeader, 0); //DIB_RGB_COLORS
								Debug.Assert(ok > 0);
							} else if(siz <= sizF) {
								//this is fast, but cannot use with compressed bitmaps
								int hei = yy - y, bmY = q.height - (currentTop - ((isFirstLine ^ 1) * IMAGE_MARGIN_TOP) + hei);
								var ok = SetDIBitsToDevice(hdc, x, r.top + isFirstLine * IMAGE_MARGIN_TOP,
									q.width, hei, 0, 0, 0, hei,
									pBits + bmY * bytesInLine, q.biHeader, 0); //DIB_RGB_COLORS
								Debug.Assert(ok > 0);
							} else Debug.Assert(false);

							//could use this instead, but very slow with big images. It seems always processes whole bitmap, not just current line.
							//int hei=yy-y, bmY=q.height-(currentTop-((isFirstLine ^ 1)*IMAGE_MARGIN_TOP)+hei);
							//StretchDIBits(hdc,
							//	x, y, q.width, hei,
							//	0, bmY, q.width, hei,
							//	pBits, h, 0, SRCCOPY);
						}
					}

					//draw frame
					if(pen == Zero) oldPen = Api.SelectObject(hdc, pen = CreatePen(0, 1, 0x60C060)); //quite fast. Caching in a static or ThreadStatic var is difficult.
					int xx = x + q.width;
					if(isFirstLine != 0) y--;
					if(yy > y) {
						MoveToEx(hdc, x - 1, y); LineTo(hdc, x - 1, yy); //left |
						MoveToEx(hdc, xx, y); LineTo(hdc, xx, yy); //right |
						if(isFirstLine != 0) { MoveToEx(hdc, x, y); LineTo(hdc, xx, y); } //top _
					}
					if(yy >= y && yy < hLine) { MoveToEx(hdc, x - 1, yy); LineTo(hdc, xx + 1, yy); } //bottom _

					x += u.width + 30;
				}
			}
			catch(Exception ex) { Debug_.Print(ex.Message); }
			finally { if(pen != Zero) Api.DeleteObject(Api.SelectObject(hdc, oldPen)); }
			//Perf.NW();

			//If there are no image strings (text edited), delete the annotation or just its part containing image info and '\n's.
			if(!hasImages && c.annotLine == 0) {
				int line = c.line; var annot = LibAnnotationText(line);
				//_t.LibAnnotationText(line, annot); //dangerous
				_c.BeginInvoke(new Action(() => { _t.LibAnnotationText(line, annot); }));
				return 1;
			}

			return width + 1;

			//speed: fast. The fastest way. Don't need bitmap handle, memory DC, etc.
			//tested: don't know what ColorUse param of SetDIBitsToDevice does, but DIB_RGB_COLORS works for any h_biBitCount.
			//speed if drawing frame: multiple LineTo is faster than single PolyPolyline.
			//tested: GDI+ much slower, particularly DrawImage().
			//tested: in QM2 was used LZO compression, now ZIP (DeflateStream). ZIP compresses better, but not so much. LZO is faster, but ZIP is fast enough. GIF and JPG in most cases compress less than ZIP and sometimes less than LZO.
			//tested: saving in 8-bit format in most cases does not make much smaller when compressed. For screenshots we reduce colors to 4-bit.
		}

		[DllImport("gdi32.dll")]
		internal static extern int SetDIBitsToDevice(IntPtr hdc, int xDest, int yDest, int w, int h, int xSrc, int ySrc, int StartScan, int cLines, byte* lpvBits, void* lpbmi, uint ColorUse); //BITMAPINFO*

		[DllImport("gdi32.dll")]
		internal static extern IntPtr CreatePen(int iStyle, int cWidth, uint color);

		[DllImport("gdi32.dll")]
		internal static extern bool MoveToEx(IntPtr hdc, int x, int y, Point* lppt = null);

		[DllImport("gdi32.dll")]
		internal static extern bool LineTo(IntPtr hdc, int x, int y);

		internal void LibOnTextChanged(bool inserted, ref SCNotification n)
		{
			if(_visible == AnnotationsVisible.ANNOTATION_HIDDEN) return;

			//info: maybe half of this code is to avoid SCI_GETTEXTRANGE when we can use n.textUTF8.
			//	Eg can use n.textUTF8 when added all text or appended lines. These cases are the most important for good performance.
			//	Cannot use n.textUTF8 eg when editing in a middle of a line, because we need whole line, not just the inserted part.

			byte* s = null;
			int from = n.position, to = from + (inserted ? n.length : 0), len = 0, firstLine = 0, textPos = 0;
			bool allText = false;
			if(from == 0) {
				len = _t.TextLengthBytes;
				if(len < 10) return; //eg 0 when deleted all text
				if(inserted && len == n.length) { //added all text
					allText = true;
					s = n.textUTF8;
				}
			}
			if(s == null) {
				firstLine = _t.LineIndexFromPosition(from);
				int from2 = _t.LineStart(firstLine);
				if(!inserted && from2 == from) return; //deleted whole lines or characters at line start, which cannot create new image string in text
				int to2 = (inserted && n.textUTF8[n.length - 1] == '\n') ? to : _t.LineEndFromPosition(to);
				len = to2 - from2;
				//PrintList(inserted, from, to, from2, to2, len);
				if(len < 10) return;
				if(from2 == from && to2 == to) {
					s = n.textUTF8;
				} else {
					//Debug_.Print("need to get text");
					s = _GetTextRange(from2, to2); if(s == null) return;
				}
				textPos = from2;
			}

			int r = _isEditor ? Au.Util.LibCharPtr.AsciiFindChar(s, len, (byte)'\"') : Au.Util.LibCharPtr.AsciiFindString(s, len, "<image \"");
			if(r < 0) return;
			//tested: all this is faster than SCI_FINDTEXT. Much faster when need to search in big text.

			//PrintList(firstLine, $"'{new string((sbyte*)s, 0, len, Encoding.UTF8)}'");
			this._SetImagesForTextRange(firstLine, s, len, allText, textPos);
		}

		/// <summary>
		/// Copies text range to BufferForText.
		/// If cannot get from-to length text, asserts and returns null.
		/// Does not validate arguments.
		/// </summary>
		byte* _GetTextRange(int from, int to)
		{
			Debug.Assert(to >= from);
			int len = to - from;
			fixed (byte* s = Au.Util.Buffers.Get(len, ref t_data.BufferForText)) {
				if(len > 0) {
					var tr = new Sci_TextRange() { chrg = new Sci_CharacterRange() { cpMin = from, cpMax = to }, lpstrText = s };
					var r = _c.Call(SCI_GETTEXTRANGE, 0, &tr);
					Debug.Assert(r == len);
					if(r != len) return null;
				} else *s = 0;
				return s;
			}
			//tested: this is faster than SCI_GETGAPPOSITION/SCI_GETRANGEPOINTER.
		}

		/// <summary>
		/// Hides/shows all images, or changes the display style of annotation areas.
		/// Default is ANNOTATION_STANDARD (images visible).
		/// When hiding, it just removes images, does not hide text annotations (SCI_ANNOTATIONGETVISIBLE remains unchanged).
		/// </summary>
		public AnnotationsVisible Visible
		{
			get => _visible;
			set
			{
				if(value == _visible) return;
				if(value == AnnotationsVisible.ANNOTATION_HIDDEN) {
					_c.Call(SCI_SETANNOTATIONDRAWCALLBACK);
					int len = _c.Call(SCI_GETTEXTLENGTH);
					if(len >= 10) {
						bool tempHidden = false;
						var buf = t_data.BufferForAnnot;
						for(int iLine = 0, nLines = _c.Call(SCI_GETLINECOUNT); iLine < nLines; iLine++) {
							len = _c.Call(SCI_ANNOTATIONGETTEXT, iLine); //fast
							if(len < 4) continue;
							//Perf.First();
							if(!tempHidden) {
								tempHidden = true;
								_c.Call(SCI_ANNOTATIONSETVISIBLE, (int)AnnotationsVisible.ANNOTATION_HIDDEN); //makes many times faster
							}
							fixed (byte* a0 = Au.Util.Buffers.Get(len, ref buf)) {
								var a = a0;
								_c.Call(SCI_ANNOTATIONGETTEXT, iLine, a); a[len] = 0;
								var imageLen = _ParseAnnotText(a, len, out var _);
								if(imageLen > 0) {
									if(len > imageLen) a += imageLen; else a = null;
									_c.Call(SCI_ANNOTATIONSETTEXT, iLine, a);
								}
							}
							//Perf.NW(); //surprisingly fast
						}
						//Perf.First();
						if(tempHidden) _c.Call(SCI_ANNOTATIONSETVISIBLE, (int)_visible);
						//Perf.NW(); //fast
					}
				} else if(_visible == AnnotationsVisible.ANNOTATION_HIDDEN) {
					_c.Call(SCI_SETANNOTATIONDRAWCALLBACK, 0, _callbackPtr);
					int len = _c.Call(SCI_GETTEXTLENGTH);
					if(len >= 10) {
						var s = _GetTextRange(0, len);
						if(s != null) this._SetImagesForTextRange(0, s, len, true, 0);
					}
				}

				_visible = value;
				if(value != AnnotationsVisible.ANNOTATION_HIDDEN) _c.Call(SCI_ANNOTATIONSETVISIBLE, (int)value);
			}
		}
		AnnotationsVisible _visible;

		//[Flags]
		//enum _TimerTasks
		//{
		//	UpdateScrollBars = 1
		//}

		//void _SetTimer(_TimerTasks task)
		//{
		//	if(_timer10 == null) _timer10 = new Timer_(t =>
		//	{
		//		Perf.First();
		//		if(0 != (_timerTasks & _TimerTasks.UpdateScrollBars)) _c.Call(SCI_UPDATESCROLLBARS);
		//		Perf.NW();
		//		_timerTasks = 0;
		//	});
		//	if(_timerTasks == 0) _timer10.Start(10, true);
		//	_timerTasks |= task;
		//}
		//Timer_ _timer10;
		//_TimerTasks _timerTasks;
	}
}
