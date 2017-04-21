\Dialog_Editor

str dd=
 BEGIN DIALOG
 0 "" 0x10C80A48 0x100 0 0 265 163 "Form"
 1001 Button 0x54032000 0x0 28 28 50 16 "Button"
 1002 Edit 0x54030080 0x200 28 48 96 12 ""
 1003 Button 0x54012003 0x0 28 68 48 10 "Check"
 1004 Static 0x54000000 0x0 28 84 48 13 "Text"
 1101 Static 0x44020000 0x4 104 62 48 13 "Page1"
 1201 Static 0x44020000 0x4 110 70 48 13 "Page2"
 1 Button 0x54030001 0x4 142 146 48 14 "OK"
 2 Button 0x54030000 0x4 192 146 48 14 "Cancel"
 4 Button 0x54032000 0x4 242 146 18 14 "?"
 3 SysTabControl32 0x54000040 0x0 16 8 232 120 ""
 5 Static 0x54000010 0x20004 4 138 257 1 ""
 END DIALOG
 DIALOG EDITOR: "" 0x2040202 "" "0" "" ""

if(!ShowDialog(dd &sub.DlgProc 0 _hwndqm)) ret


#sub DlgProc
function# hDlg message wParam lParam

sel message
	case WM_INITDIALOG
	
	EnableThemeDialogTexture hDlg ETDT_ENABLETAB
	
	int htb=id(3 hDlg)
	TCITEM ti.mask=TCIF_TEXT
	ti.pszText="A"
	SendMessage htb TCM_INSERTITEMA 0 &ti
	ti.pszText="B"
	SendMessage htb TCM_INSERTITEMA 1 &ti
	ti.pszText="C"
	SendMessage htb TCM_INSERTITEMA 2 &ti
	
	DT_Page hDlg _i
	
	case WM_PAINT
	 draw dialog background here
	
	 first time create brush
	__GdiHandle- brush hbm
	int useBmp=1 ;;if 0, use color
	if !brush
		if useBmp
			hbm=LoadPictureFile("$my qm$\Copy.jpg")
			brush=CreatePatternBrush(hbm)
		else
			brush=CreateSolidBrush(0xc0ffff)
	
	 then fill background with the brush
	PAINTSTRUCT ps; BeginPaint hDlg &ps
	RECT r; GetClientRect hDlg &r
	FillRect ps.hDC &r brush
	EndPaint hDlg &ps
	
	case WM_COMMAND goto messages2
	case WM_NOTIFY goto messages3
ret
 messages2
sel wParam
	case IDOK
	case IDCANCEL
ret 1
 messages3
NMHDR* nh=+lParam
sel nh.code
	case TCN_SELCHANGE
	_i=SendMessage(nh.hwndFrom TCM_GETCURSEL 0 0)
	DT_Page hDlg _i