\Dialog_Editor
function# hDlg message wParam lParam
if(hDlg) goto messages

if(!ShowDialog("Dialog9" &Dialog9)) ret

 BEGIN DIALOG
 0 "" 0x90C80A44 0x100 0 0 223 135 "Form"
 1 Button 0x54030001 0x4 120 116 48 14 "OK"
 2 Button 0x54030000 0x4 170 116 48 14 "Cancel"
 END DIALOG
 DIALOG EDITOR: "" 0x2010900 "" ""

ret
 messages
if(message=WM_INITDIALOG) DT_Init(hDlg lParam)
 int param=DT_GetParam(hDlg)

sel message
	case WM_SETCURSOR
	SetTimer hDlg 1 50 0
	
	case WM_TIMER
	if(win(mouse)=hDlg) ret
	KillTimer hDlg 1
	out "leave"
	
	case WM_INITDIALOG
	ret 1
	case WM_DESTROY DT_DeleteData(hDlg)
	case WM_COMMAND goto messages2
ret
 messages2
sel wParam
	case IDOK DT_Ok hDlg
	case IDCANCEL DT_Cancel hDlg
ret 1