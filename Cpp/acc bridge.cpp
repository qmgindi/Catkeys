#include "stdafx.h"
#include "cpp.h"
#include "acc.h"

HRESULT AccFind(AccFindCallback& callback, HWND w, Cpp_Acc* aParent, const Cpp_AccParams& ap, eAF2 flags2, out BSTR& errStr);
HRESULT AccFromPoint(POINT p, int flags, int specWnd, out Cpp_Acc& aResult);
HRESULT AccNavigate(Cpp_Acc aFrom, STR navig, out Cpp_Acc& aResult);
HRESULT AccGetProp(Cpp_Acc a, WCHAR what, out BSTR& sResult);

namespace {

#pragma region marshal

//Used for marshaling 'find AO' (IPA_AccFind) parameters when calling the get_accHelpTopic hook function.
//A flat variable-size memory structure (strings follow the fixed-size part).
struct MarshalParams_AccFind
{
	struct _FlatStr { int offs, len; };

	MarshalParams_Header hdr;
	int hwnd; //not HWND, because it must be of same size in 32 and 64 bit process
	eAF2 flags2;
private:
	//these are the same as Cpp_AccParams, except is used int instead of STR. Cannot use STR because its size can be 32 or 64 bit.
	_FlatStr _role, _name, _prop;
	eAF _flags;
	int _skip;
	WCHAR _resultProp;

	LPWSTR _SetString(STR s, int len, LPWSTR dest, out _FlatStr& r) {
		if(!s) {
			r.len = r.offs = 0;
			return dest;
		}
		memcpy(dest, s, len * 2); dest[len] = 0;
		r.offs = (int)(dest - (STR)this); r.len = len;
		return dest + len + 1;
	}

	STR _GetString(_FlatStr r, out int& len) {
		len = r.len;
		if(!r.offs) return null;
		return (STR)this + r.offs;
	}
public:
	static int CalcMemSize(const Cpp_AccParams& ap) {
		return sizeof(MarshalParams_AccFind) + (ap.roleLength + ap.nameLength + ap.propLength + 3) * 2;
	}

	void Marshal(HWND w, const Cpp_AccParams& ap, eAF2 flags2_) {
		hwnd = (int)(LPARAM)w;
		flags2 = flags2_;

		auto s = (LPWSTR)(this + 1);
		s = _SetString(ap.role, ap.roleLength, s, out _role);
		s = _SetString(ap.name, ap.nameLength, s, out _name);
		s = _SetString(ap.prop, ap.propLength, s, out _prop);
		_flags = ap.flags;
		_skip = ap.skip;
		_resultProp = ap.resultProp;
	}

	void Unmarshal(out Cpp_AccParams& ap) {
		ap.role = _GetString(_role, out ap.roleLength);
		ap.name = _GetString(_name, out ap.nameLength);
		ap.prop = _GetString(_prop, out ap.propLength);
		ap.flags = _flags;
		ap.skip = _skip;
		ap.resultProp = _resultProp;
	}
};

static long s_accMarshalWrapperCount;
//static CSimpleArray<IUnknown*> s_accMarshalWrappers;
//static concurrency::critical_section s_cs1;

//Workaround for Firefox bug in TEXT AOs in multi-process mode.
class AccessibleMarshalWrapper : public IAccessible
{
	IAccessible* _a;
public:
	bool ignoreQI;

	AccessibleMarshalWrapper(IAccessible* a)
	{
		_a = a;//SHOULDDO: try to addref/release, maybe it will solve the WindowFromAccessibleObject problem for LINK in drag&dropped FF tab window
		ignoreQI = true;

		InterlockedIncrement(&s_accMarshalWrapperCount);
		//concurrency::critical_section::scoped_lock sl(s_cs1);
		//s_accMarshalWrappers.Add(this);
	}

	~AccessibleMarshalWrapper() {
		InterlockedDecrement(&s_accMarshalWrapperCount);
		//concurrency::critical_section::scoped_lock sl(s_cs1);
		//s_accMarshalWrappers.Remove(this);
	}

	static bool Disconnect() {
		//This process would crash later when releasing wrappers if this dll is unloaded then.
#if true
		//Don't allow to unload this dll if there are unreleased wrappers.
		return s_accMarshalWrapperCount == 0;
#else
		//Disconnect unreleased acc wrappers. If fails don't allow to unload this dll.
		//Does not work as expected. Just increments dll refcount, and then FreeLibraryAndExitThread does not unload it. Later is called Release().
		concurrency::critical_section::scoped_lock sl(s_cs1);
		int n = s_accMarshalWrappers.GetSize();
		//PRINTI(n);
		if(n == 0) return true;
		bool failed = false;
		for(int i = 0; i < n; i++) {
			if(s_accMarshalWrappers[i] == null) continue;
			int hr = CoDisconnectObject(s_accMarshalWrappers[i], 0);
			if(hr != 0) { PRINTF(L"CoDisconnectObject, 0x%x", hr); failed = true; break; }
			s_accMarshalWrappers[i] = null;
		}
		if(!failed) s_accMarshalWrappers.RemoveAll();
		return !failed;
#endif
	}

	virtual STDMETHODIMP QueryInterface(REFIID riid, void** ppvObject) override
	{
		if(riid == IID_IAccessible || riid == IID_IUnknown || riid == IID_IDispatch) {
			_a->AddRef();
			*ppvObject = this;
			return 0;
			//Firefox bug: TEXT AOs return E_NOINTERFACE for IID_IUnknown, that is why CoMarshalInterface fails.
			//	Also they don't give custom IMarshal, although all other AOs do.
		}
		if(ignoreQI) { //don't give IMarshal and other interfaces while in CoMarshalInterface
			*ppvObject = null;
			return E_NOINTERFACE;
		}

		return _a->QueryInterface(riid, ppvObject);
	}
	virtual STDMETHODIMP_(ULONG) AddRef(void) override
	{
		return _a->AddRef();
	}
	virtual STDMETHODIMP_(ULONG) Release(void) override
	{
		auto r = _a->Release();
		//Print((int)r);
		if(r == 0) delete this;
		return r;
	}
#pragma region IAccessible
	virtual STDMETHODIMP GetTypeInfoCount(UINT* pctinfo) override
	{
		return E_NOTIMPL;
	}
	virtual STDMETHODIMP GetTypeInfo(UINT iTInfo, LCID lcid, ITypeInfo** ppTInfo) override
	{
		return E_NOTIMPL;
	}
	virtual STDMETHODIMP GetIDsOfNames(REFIID riid, LPOLESTR* rgszNames, UINT cNames, LCID lcid, DISPID* rgDispId) override
	{
		return E_NOTIMPL;
	}
	virtual STDMETHODIMP Invoke(DISPID dispIdMember, REFIID riid, LCID lcid, WORD wFlags, DISPPARAMS* pDispParams, VARIANT* pVarResult, EXCEPINFO* pExcepInfo, UINT* puArgErr) override
	{
		return E_NOTIMPL;
	}
	virtual STDMETHODIMP get_accParent(IDispatch** ppdispParent) override
	{
		return _a->get_accParent(ppdispParent);
	}
	virtual STDMETHODIMP get_accChildCount(long* pcountChildren) override
	{
		return _a->get_accChildCount(pcountChildren);
	}
	virtual STDMETHODIMP get_accChild(VARIANT varChild, IDispatch** ppdispChild) override
	{
		return _a->get_accChild(varChild, ppdispChild);
	}
	virtual STDMETHODIMP get_accName(VARIANT varChild, BSTR* pszName) override
	{
		return _a->get_accName(varChild, pszName);
	}
	virtual STDMETHODIMP get_accValue(VARIANT varChild, BSTR* pszValue) override
	{
		return _a->get_accValue(varChild, pszValue);
	}
	virtual STDMETHODIMP get_accDescription(VARIANT varChild, BSTR* pszDescription) override
	{
		return _a->get_accDescription(varChild, pszDescription);
	}
	virtual STDMETHODIMP get_accRole(VARIANT varChild, VARIANT* pvarRole) override
	{
		return _a->get_accRole(varChild, pvarRole);
	}
	virtual STDMETHODIMP get_accState(VARIANT varChild, VARIANT* pvarState) override
	{
		return _a->get_accState(varChild, pvarState);
	}
	virtual STDMETHODIMP get_accHelp(VARIANT varChild, BSTR* pszHelp) override
	{
		return _a->get_accHelp(varChild, pszHelp);
	}
	virtual STDMETHODIMP get_accHelpTopic(BSTR* pszHelpFile, VARIANT varChild, long* pidTopic) override
	{
		return E_NOTIMPL;
	}
	virtual STDMETHODIMP get_accKeyboardShortcut(VARIANT varChild, BSTR* pszKeyboardShortcut) override
	{
		return _a->get_accKeyboardShortcut(varChild, pszKeyboardShortcut);
	}
	virtual STDMETHODIMP get_accFocus(VARIANT* pvarChild) override
	{
		return _a->get_accFocus(pvarChild);
	}
	virtual STDMETHODIMP get_accSelection(VARIANT* pvarChildren) override
	{
		return _a->get_accSelection(pvarChildren);
	}
	virtual STDMETHODIMP get_accDefaultAction(VARIANT varChild, BSTR* pszDefaultAction) override
	{
		return _a->get_accDefaultAction(varChild, pszDefaultAction);
	}
	virtual STDMETHODIMP accSelect(long flagsSelect, VARIANT varChild) override
	{
		return _a->accSelect(flagsSelect, varChild);
	}
	virtual STDMETHODIMP accLocation(long* pxLeft, long* pyTop, long* pcxWidth, long* pcyHeight, VARIANT varChild) override
	{
		return _a->accLocation(pxLeft, pyTop, pcxWidth, pcyHeight, varChild);
	}
	virtual STDMETHODIMP accNavigate(long navDir, VARIANT varStart, VARIANT* pvarEndUpAt) override
	{
		return _a->accNavigate(navDir, varStart, pvarEndUpAt);
	}
	virtual STDMETHODIMP accHitTest(long xLeft, long yTop, VARIANT* pvarChild) override
	{
		return _a->accHitTest(xLeft, yTop, pvarChild);
	}
	virtual STDMETHODIMP accDoDefaultAction(VARIANT varChild) override
	{
		return _a->accDoDefaultAction(varChild);
	}
	virtual STDMETHODIMP put_accName(VARIANT varChild, BSTR szName) override
	{
		return _a->put_accName(varChild, szName);
	}
	virtual STDMETHODIMP put_accValue(VARIANT varChild, BSTR szValue) override
	{
		return _a->put_accValue(varChild, szValue);
	}
#pragma endregion
};

//The BSTR returned by our get_accHelpTopic hook contains data of one or more accessible objects (AO).
//	Each AO data can have IAccessible object data (created by CoMarshalInterface), child element id, level, role.
//	These flags tell what is in the data.
//[Flags]
enum class eAccResult {
	Elem = 1,
	Role = 4,
	Level = 8,
	UsePrevAcc = 0x10,
	UsePrevLevel = 0x20,
};
ENABLE_BITMASK_OPERATORS(eAccResult);

#if true
bool WriteAccToStream(ref Smart<IStream>& stream, Cpp_Acc a, Cpp_Acc* aPrev = null)
{
	if(stream == null) CreateStreamOnHGlobal(0, true, &stream);

	eAccResult has = (eAccResult)0;
	if(aPrev != null) {
		if(a.acc == aPrev->acc && a.elem != 0) has |= eAccResult::UsePrevAcc; else aPrev->acc = a.acc;
		if(a.misc.level != 0) has |= a.misc.level == aPrev->misc.level ? eAccResult::UsePrevLevel : eAccResult::Level;
		aPrev->misc.level = a.misc.level;
	} else {
		if(a.misc.level != 0) has |= eAccResult::Level;
	}
	if(a.elem != 0) has |= eAccResult::Elem;
	if(a.misc.role != 0) has |= eAccResult::Role;

	if(stream->Write(&has, 1, null)) return false;

	a.misc.flags |= eAccMiscFlags::InProc;
	if(!(has & eAccResult::UsePrevAcc)) {
		//problem: with some AO the hook is not called when we try to do something inproc, eg get all props.
		//	They use a custom IMarshal, which redirects to another (not hooked) IAccessible interface. In most cases it is even in another process.
		//	Known apps: 1. Firefox, when multiprocess not disabled. 2. Some hidden AO in IE. 3. Windows store apps, but we don't use inproc.
		//	Known apps where is custom IMarshal but the hook works: 1. Task Scheduler MMC: controls of other process.
		//	Workarounds:
		//		Tested, fails: replace CoMarshalInterface with CoGetStandardMarshal/MarshalInterface. Chrome works, Firefox crashes.
		//		Old, rejected: remove InProc flag. But with new Firefox then all AOs are not inproc.
		//		Now using: wrap the AO in AccessibleMarshalWrapper and marshal it instead, like we always did for Firefox TEXT AOs.
		HRESULT hr = 1;
		IMarshal* m = null;
		if(0 == a.acc->QueryInterface(&m)) m->Release();
		else hr = CoMarshalInterface(stream, IID_IAccessible, a.acc, MSHCTX_LOCAL, null, MSHLFLAGS_NORMAL);
		//ao::PrintAcc(a.acc, a.elem);
		if(hr) {
			//Firefox bug when multi-process: fails to marshal all TEXT AOs.
			//	Workaround: wrap a.acc into an AccessibleMarshalWrapper and marshal it instead.
			//	Or could detect Firefox multi-process and use NotInProc implicitly. But slower almost 2 times. Also then cannot get HTML attributes etc later.
			//	With new Firefox we have to use this for all Firefox AOs.
#if true
			HRESULT hr1 = hr;
			auto wrap = new AccessibleMarshalWrapper(a.acc);
			a.acc = wrap;
			hr = CoMarshalInterface(stream, IID_IAccessible, a.acc, MSHCTX_LOCAL, null, MSHLFLAGS_NORMAL);
			if(hr == 0) wrap->ignoreQI = false; else delete wrap;
			//Print((UINT)hr);

			if(hr) PRINTF(L"failed to marshal AO: 0x%X 0x%X", hr1, hr);
#endif
			//ao::PrintAcc(a.acc, a.elem);
		} //else Print(L"OK");
		if(hr) return false;
		inproc::s_hookIAcc.Hook(a.acc);
	}

	if(!!(has & eAccResult::Elem))
		if(stream->Write(&a.elem, 4, null)) return false;

	if(stream->Write(&a.misc.flags, 1, null)) return false;

	if(!!(has & eAccResult::Role))
		if(stream->Write(&a.misc.role, 1, null)) return false;

	if(!!(has & eAccResult::Level))
		if(stream->Write(&a.misc.level, 2, null)) return false;

	return true;
}
#else
bool WriteAccToStream(ref Smart<IStream>& stream, Cpp_Acc a, Cpp_Acc* aPrev = null)
{
	if(stream == null) CreateStreamOnHGlobal(0, true, &stream);

	eAccResult has = (eAccResult)0;
	if(aPrev != null) {
		if(a.acc == aPrev->acc && a.elem != 0) has |= eAccResult::UsePrevAcc; else aPrev->acc = a.acc;
		if(a.misc.level != 0) has |= a.misc.level == aPrev->misc.level ? eAccResult::UsePrevLevel : eAccResult::Level;
		aPrev->misc.level = a.misc.level;
	} else {
		if(a.misc.level != 0) has |= eAccResult::Level;
	}
	if(a.elem != 0) has |= eAccResult::Elem;
	if(a.misc.role != 0) has |= eAccResult::Role;

	if(stream->Write(&has, 1, null)) return false;

	a.misc.flags |= eAccMiscFlags::InProc;
	if(!(has & eAccResult::UsePrevAcc)) {
		//problem: with some AO the hook is not called when we try to do something inproc, eg get all props.
		//	They use a custom IMarshal, which redirects to another (not hooked) IAccessible interface. In most cases it is even in another process.
		//	Known apps: 1. Firefox, when multiprocess not disabled. 2. Some hidden AO in IE. 3. Windows store apps, but we don't use inproc.
		//	Known apps where is custom IMarshal but the hook works: 1. Task Scheduler MMC: controls of other process.
		//	Workaround: don't add InProc flag. With all known such apps it does not make faster anyway.
		//	Other workarounds:
		//		Tested, fails: replace CoMarshalInterface with CoGetStandardMarshal/MarshalInterface. Chrome works, Firefox crashes.
		//		Not tested: wrap the AO in our AO (like we do with Java and UIA). Makes no sense, just would make slower.
		IMarshal* m = null;
		if(0 == a.acc->QueryInterface(&m)) {
			m->Release();
			a.misc.flags &= ~eAccMiscFlags::InProc;
			//PRINTS(L"custom IMarshal. Using NotInProc.");
		}

		HRESULT hr = CoMarshalInterface(stream, IID_IAccessible, a.acc, MSHCTX_LOCAL, null, MSHLFLAGS_NORMAL);
		//ao::PrintAcc(a.acc, a.elem);
		if(hr) {
			//Firefox bug when multi-process: fails to marshal all TEXT AOs.
			//	Workaround: wrap a.acc into an AccessibleMarshalWrapper and marshal it instead.
			//	Or could detect Firefox multi-process and use NotInProc implicitly. But slower almost 2 times. Also then cannot get HTML attributes etc later.
#if true
			HRESULT hr1 = hr;
			auto wrap = new AccessibleMarshalWrapper(a.acc);
			a.acc = wrap;
			hr = CoMarshalInterface(stream, IID_IAccessible, a.acc, MSHCTX_LOCAL, null, MSHLFLAGS_NORMAL);
			if(hr == 0) wrap->ignoreQI = false; else delete wrap;
			//Print((UINT)hr);

			if(hr) PRINTF(L"failed to marshal AO: 0x%X 0x%X", hr1, hr);
#endif
			//ao::PrintAcc(a.acc, a.elem);
		} //else Print(L"OK");
		if(hr) return false;
		inproc::s_hookIAcc.Hook(a.acc);
	}

	if(!!(has & eAccResult::Elem))
		if(stream->Write(&a.elem, 4, null)) return false;

	if(stream->Write(&a.misc.flags, 1, null)) return false;

	if(!!(has & eAccResult::Role))
		if(stream->Write(&a.misc.role, 1, null)) return false;

	if(!!(has & eAccResult::Level))
		if(stream->Write(&a.misc.level, 2, null)) return false;

	return true;
}
#endif

#pragma endregion

} //namespace

namespace inproc
{

//Called from the hook to find or get AO.
//Common for Cpp_AccFind, Cpp_AccFromWindow and other functions that return AO.
HRESULT AccFindOrGet(MarshalParams_Header* h, IAccessible* iacc, out BSTR& sResult)
{
	Smart<IStream> stream;
	auto action = h->action;
	if(action == InProcAction::IPA_AccNavigate) {
		auto p = (MarshalParams_AccElem*)h;
		Cpp_Acc aFrom(iacc, p->elem, h->miscFlags), aResult;

		HRESULT hr = AccNavigate(aFrom, (STR)(p + 1), out aResult);
		if(hr) return hr;
		aResult.SetRole();

		if(!WriteAccToStream(ref stream, aResult)) return RPC_E_SERVER_CANTMARSHAL_DATA;

		if(aResult.acc != iacc) aResult.acc->Release();
	} else if(action == InProcAction::IPA_AccFromWindow) {
		auto p = (MarshalParams_AccFromWindow*)h;
		Smart<IAccessible> a;

		HRESULT hr = ao::AccFromWindowSR((HWND)(LPARAM)p->hwnd, p->objid, &a);
		if(hr) return hr;

		if(p->flags & 2) { //get name
			return a->get_accName(ao::VE(), out & sResult);
		}

		Cpp_Acc aResult(a, 0);
		aResult.SetRole();

		if(!WriteAccToStream(ref stream, aResult)) return RPC_E_SERVER_CANTMARSHAL_DATA;

	} else if(action == InProcAction::IPA_AccFromPoint) {
		auto x = (MarshalParams_AccFromPoint*)h;
		Cpp_Acc aResult;
		HRESULT hr = AccFromPoint(x->p, x->flags, x->specWnd, out aResult);
		if(hr) return hr;
		if(!WriteAccToStream(ref stream, aResult)) return RPC_E_SERVER_CANTMARSHAL_DATA;

		//Workaround for AO leak: the final Release called in the client process somehow does not release the true AO. Releases only the proxy.
		//	But FromWindow() and Find() work well without this Release, although use the same marshaling code etc.
		//	Tested raw AccessibleObjectFromPoint, the same.
		//	Actually there are 2 proxies: one is created on WM_GETOBJECT, other by marshaling to the client process. Probably the first proxy would leak too.
		//aResult.acc->Release(); aResult.acc->Release(); //somehow works even with this. But FromWindow() still works with 1 less Release.
		//Printf(L"----- %i", aResult.acc->Release());//3
		aResult.acc->Release();

	} else { //IPA_AccFind
		Cpp_AccParams ap;
		auto p = (MarshalParams_AccFind*)h; p->Unmarshal(out ap);
		HWND w = (HWND)(LPARAM)p->hwnd;
		eAF2 flags2 = p->flags2;
		bool findAll = !!(flags2 & eAF2::FindAll);
		auto resultProp = ap.resultProp;
		HRESULT hr = (HRESULT)eError::NotFound;
		Cpp_Acc aParent(iacc, 0, h->miscFlags), aPrev;

		HRESULT hr2 = AccFind(
			[&hr, &stream, &aPrev, resultProp, findAll, skip = ap.skip, &sResult](Cpp_Acc a) mutable
		{
			if(!findAll && skip-- > 0) return eAccFindCallbackResult::Continue;

			if(resultProp) {
				if(resultProp != '-') AccGetProp(a, resultProp, out sResult);
			} else {
				if(!stream) CreateStreamOnHGlobal(0, true, &stream);

				DWORD pos = 0;
				if(findAll) istream::GetPos(stream, out pos);

				if(!WriteAccToStream(ref stream, a, &aPrev)) {
					if(!findAll) goto ge;
					stream->Seek(istream::LI(pos), STREAM_SEEK_SET, null);
				}
			}

			hr = 0;
			return findAll ? eAccFindCallbackResult::Continue : eAccFindCallbackResult::StopFound;
		ge:
			hr = RPC_E_SERVER_CANTMARSHAL_DATA;
			return eAccFindCallbackResult::StopNotFound;
		}, w, w ? null : &aParent, ref ap, flags2, out sResult);

		if(hr2 && hr2 != (HRESULT)eError::NotFound) return hr2;
		if(hr) return hr;
		if(resultProp) return 0;
	}

	DWORD streamSize, readSize;
	if(istream::GetPos(stream, out streamSize) && istream::ResetPos(stream)) {
		sResult = SysAllocStringByteLen(null, streamSize);
		if(0 == stream->Read(sResult, streamSize, &readSize) && readSize == streamSize) return 0;
		SysFreeString(sResult); sResult = null;
	}
	return RPC_E_SERVER_CANTMARSHAL_DATA;
}

bool AccDisconnectWrappers() { return AccessibleMarshalWrapper::Disconnect(); }

} //namespace inproc

namespace outproc
{
//Reads one AO from results.
//When FindAll, the caller must call this in loop, until returns a non-zero. If returns NotFound, there are no more AO to read.
//a - receives the AO, elem, etc. When FindAll, the caller must use the same variable for all, because this function uses it as an input parameter too (previous AO).
//dontNeedAO - don't need AO. Only release marshal data if need.
HRESULT InProcCall::ReadResultAcc(ref Cpp_Acc& a, bool dontNeedAO/* = false*/) {
	if(!_stream) {
		_resultSize = _br.ByteLength(); if(_resultSize == 0) return RPC_E_CLIENT_CANTUNMARSHAL_DATA;
		HGLOBAL hg = GlobalAlloc(GMEM_MOVEABLE, _resultSize); if(hg == 0) return RPC_E_CLIENT_CANTUNMARSHAL_DATA;
		LPVOID mem = GlobalLock(hg); memcpy(mem, _br, _resultSize); GlobalUnlock(mem);
		CreateStreamOnHGlobal(hg, true, &_stream);
		//Print(_resultSize);
	}

	DWORD pos; if(istream::GetPos(_stream, out pos) && pos == _resultSize) return (HRESULT)eError::NotFound; //no more results when FindAll. Fast.

	eAccResult has = (eAccResult)0;
	if(0 != _stream->Read(&has, 1, null)) return RPC_E_CLIENT_CANTUNMARSHAL_DATA;

	if(!(has & eAccResult::UsePrevAcc)) {
		HRESULT hr;
		if(dontNeedAO) {
			//Perf.First();
			hr = CoReleaseMarshalData(_stream);
			//Perf.NW(); //slow, because calls Release in the server process
			a.acc = null;
		} else {
			hr = CoUnmarshalInterface(_stream, IID_IAccessible, (void**)&a.acc);
		}
		if(hr) return RPC_E_CLIENT_CANTUNMARSHAL_DATA;
	} else if(!dontNeedAO) {
		assert(!!(has & eAccResult::Elem));
		a.acc->AddRef();
	}

	if(!(has & eAccResult::Elem)) a.elem = 0;
	else if(_stream->Read(&a.elem, 4, null)) return RPC_E_CLIENT_CANTUNMARSHAL_DATA;

	if(_stream->Read(&a.misc.flags, 1, null)) return RPC_E_CLIENT_CANTUNMARSHAL_DATA;

	if(!(has & eAccResult::Role)) a.misc.role = 0;
	else if(_stream->Read(&a.misc.role, 1, null)) return RPC_E_CLIENT_CANTUNMARSHAL_DATA;

	if(!(has & eAccResult::UsePrevLevel)) {
		if(!(has & eAccResult::Level)) a.misc.level = 0;
		else if(_stream->Read(&a.misc.level, 2, null)) return RPC_E_CLIENT_CANTUNMARSHAL_DATA;
	}

	return 0;
}

//Gets AO of window (calls AccessibleObjectFromWindow).
//flags:
//	1 - not inproc. If used this flag, or if failed to inject dll, the returned AO will not be suitable for in-proc search.
//	2 - get name instead. Results: aResult = empty, sResult = name.
EXPORT HRESULT Cpp_AccFromWindow(DWORD flags, HWND w, DWORD objid, out Cpp_Acc& aResult, out BSTR& sResult)
{
	aResult.Zero(); sResult = null;

	if(objid == OBJID_JAVA) {
		auto iacc = AccJavaFromWindow(w);
		if(iacc == null) return 1;
		aResult.acc = iacc;
		aResult.misc.flags = eAccMiscFlags::Java;
		return 0;
	} else if(objid == OBJID_UIA) {
		HRESULT hr = AccUiaFromWindow(w, &aResult.acc);
		if(hr == 0) aResult.misc.flags = eAccMiscFlags::UIA;
		return hr;
		//never mind: inproc. Maybe in the future.
	}

	HRESULT R;
g1:
	if(flags & 1) { //not inproc
		R = ao::AccFromWindowSR(w, objid, &aResult.acc);
		if(R == 0 && flags & 2) {
			R = aResult.acc->get_accName(ao::VE(), out & sResult);
			aResult.acc->Release(); aResult.acc = null;
		}
		return R;
	}

	//Perf.First();
	Cpp_Acc_Agent aAgent;
	if(0 != (R = InjectDllAndGetAgent(w, out aAgent.acc))) {
		switch((eError)R) {
		case eError::WindowOfThisThread: case eError::UseNotInProc: case eError::Inject: break;
		default: return R;
		}
		flags |= 1; goto g1;
	}
	//Perf.Next();

	InProcCall c;
	auto p = (MarshalParams_AccFromWindow*)c.AllocParams(&aAgent, InProcAction::IPA_AccFromWindow, sizeof(MarshalParams_AccFromWindow));
	p->hwnd = (int)(LPARAM)w;
	p->objid = objid;
	p->flags = flags;
	if(0 != (R = c.Call())) return R;
	//Perf.Next();
	if(flags & 2) sResult = c.DetachResultBSTR();
	else R = c.ReadResultAcc(ref aResult);
	//Perf.NW();
	return R;
}

//Finds a descendant AO of w or aParent.
//By default searches in the target process. If flag NotInProc (or if cannot inject), searches from this process (slow); then the returned AO will not be suitable for in-proc search.
//w - parent window or 0 (if aParent used).
//aParent - parent AO or null (if w used). Must be retrieved in-proc.
//ap - AO parameters.
//also - if not null, this func calls the callback function for each matching AO.
//	Need to Release the AO, preferably later, maybe in another thread.
//	If the callback returns true, it is not called again (unless 'skip' is used), and this function returns 0 (found).
//	If the callback always returns false, this function returns eError::NotFound.
//aResult - receives the found AO (if this function returns 0 (found)).
//	Need to Release.
//	If used 'also', it can be the same AO as the callback received the last time. Need to Release both.
//	It is empty if this func returns not 0 or if used ap.resultProp.
//sResult - error string or a property of the found AO.
//	When this func returns eError::InvalidParameter, it is error string.
//	When this func returns 0 and used ap.resultProp, it is the property (string, or binary struct); null if '-'.
//	Else null.
EXPORT HRESULT Cpp_AccFind(HWND w, Cpp_Acc* aParent, const Cpp_AccParams& ap, Cpp_AccCallbackT also, out Cpp_Acc& aResult, out BSTR& sResult)
{
	//Perf.First();
	aResult.Zero(); sResult = null;
	bool inProc = !(ap.flags & eAF::NotInProc), findAll = (also != null), useWnd = (aParent == null);
	eAF2 flags2 = findAll ? eAF2::FindAll : (eAF2)0;
	HRESULT R;

	assert(!!w == !aParent);
	assert(!ap.resultProp || !findAll);

	if(useWnd) {
		//If role has prefix "web:" and w is IE, need to find the web browser control at first, because it's in a different process than IE.
		//We cannot detect IE by window class name. It can be any, because IE-based web browser controls can be used anywhere.
		//To detect it, we look for an "Internet Explorer_Server" control.
		//If it is Firefox or Chrome, this makes slightly slower.
		if(ap.roleLength >= 4 && CMP4(ap.role, L"web:")) {
			HWND wIES = wn::FindChildByClassName(w, c_IES, true);
			if(wIES) { w = wIES; flags2 |= eAF2::InIES; }
		}
	}

	Cpp_Acc_Agent aAgent;
	if(inProc && useWnd) {
		IAccessible* iagent = null;
		if(0 != (R = InjectDllAndGetAgent(w, out iagent))) {
			switch((eError)R) {
			case eError::WindowOfThisThread: case eError::UseNotInProc: case eError::Inject: break;
			default: return R;
			}
			inProc = false;
		} else {
			aAgent.acc = iagent;
			aParent = &aAgent;
		}
		//Perf.Next();
	}

	if(inProc) {
		InProcCall c;
		auto sizeofParams = MarshalParams_AccFind::CalcMemSize(ref ap);
		auto p = (MarshalParams_AccFind*)c.AllocParams(aParent, InProcAction::IPA_AccFind, sizeofParams);
		p->Marshal(useWnd ? w : 0, ref ap, flags2);

		if(0 != (R = c.Call())) {
			if(R == (HRESULT)eError::InvalidParameter) sResult = c.DetachResultBSTR();
		} else if(!findAll) {
			if(!ap.resultProp) R = c.ReadResultAcc(ref aResult);
			else if(ap.resultProp != '-') sResult = c.DetachResultBSTR();
		} else {
			Cpp_Acc a;
			int skip = ap.skip;
			for(;;) {
				R = c.ReadResultAcc(ref a);
				if(R) break; //NotFound when end of stream
				if(!also(a)) continue; //must Release u.acc, preferably later
				if(skip-- == 0) {
					a.acc->AddRef();
					aResult = a;
					break;
				}
			}
			//release the marshal data of remaining AO
			for(auto k = R; k == 0; ) k = c.ReadResultAcc(ref a, true);
		}
		//Perf.Next();
	} else {
		flags2 |= eAF2::NotInProc;
		bool found = false;
		R = AccFind(
			[&found, &aResult, &sResult, &ap, skip = ap.skip, also](Cpp_Acc a) mutable
		{
			if(also) {
				a.acc->AddRef(); //of proxy (fast)
				if(!also(a)) return eAccFindCallbackResult::Continue;
			}

			if(skip-- > 0) return eAccFindCallbackResult::Continue;
			found = true;

			if(ap.resultProp) {
				if(ap.resultProp != '-') AccGetProp(a, ap.resultProp, out sResult);
			} else {
				aResult = a;
				a.acc->AddRef();
			}

			return eAccFindCallbackResult::StopFound;
		}, w, aParent, ref ap, flags2, out sResult);

		if(!R && !found) R = (HRESULT)eError::NotFound;
	}
	//Perf.NW();

	return R;
}

} //namespace outproc
