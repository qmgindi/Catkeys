int hh=SetWinEventHook(EVENT_SYSTEM_ALERT EVENT_SYSTEM_ALERT 0 &Hook_SetWinEventHook2 0 0 WINEVENT_OUTOFCONTEXT)
 int hh=SetWinEventHook(EVENT_SYSTEM_FOREGROUND EVENT_SYSTEM_FOREGROUND 0 &Hook_SetWinEventHook2 0 0 WINEVENT_OUTOFCONTEXT)
if(!hh) end F"{ERR_FAILED}. {_s.dllerror}"
opt waitmsg 1
wait -1
UnhookWinEvent hh