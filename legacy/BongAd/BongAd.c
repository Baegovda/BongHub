/* BongAd - hides the KakaoTalk PC ad banner. user32 window show/hide/resize only.
   No injection, no network, no file writes except %TEMP%\BongAd.hidden.txt (restore list).
   v2: compact layout (moves/resizes KakaoTalk's own container windows) + dark overlay
   (v2.2: GDI PrintWindow + in-place darkening into a layered window; no Magnification/D3D).
   (Windows Magnification API color-effect window over the main window; click-through).
   Usage: BongAd.exe [/once] [/restore] [/dark] [/nocompact]   (MIT License) */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define SWPF (SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE)
#define MAXH 64
static HWND hidden[MAXH]; static int nhidden;
static HWND gMsg; static DWORD kakaoPid, badPid[8]; static int nbad;
static WCHAR stateFile[MAX_PATH];

void *memset(void *d, int c, size_t n){ unsigned char *p=d; while(n--) *p++=(unsigned char)c; return d; }
static int wlen(const WCHAR*s){int n=0;while(s[n])n++;return n;}
static int has(const WCHAR*s,const WCHAR*sub){int m=wlen(sub);for(;*s;s++){int i=0;while(i<m&&s[i]==sub[i])i++;if(i==m)return 1;}return 0;}
static int starts(const WCHAR*s,const WCHAR*p){while(*p){if(*s++!=*p++)return 0;}return 1;}
static int ieq_tail(const WCHAR*s,const WCHAR*t){int a=wlen(s),b=wlen(t);if(a<b)return 0;s+=a-b;
  while(*t){WCHAR x=*s++,y=*t++;if(x>='A'&&x<='Z')x+=32;if(y>='A'&&y<='Z')y+=32;if(x!=y)return 0;}return 1;}
static void cls(HWND h,WCHAR*b){b[0]=0;GetClassNameW(h,b,128);}
static void txt(HWND h,WCHAR*b){b[0]=0;GetWindowTextW(h,b,128);}
static int isAdTitle(const WCHAR*t){return has(t,L"\xAD11\xACE0")||has(t,L"AdView")||has(t,L"BannerAd")||has(t,L"AdFit")||has(t,L"MOMENT");} /* 광고 */

static int isKakao(DWORD pid){
  if(!pid) return 0; if(pid==kakaoPid) return 1;
  for(int i=0;i<nbad;i++) if(badPid[i]==pid) return 0;
  int ok=0; HANDLE p=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);
  if(p){WCHAR n[MAX_PATH];DWORD sz=MAX_PATH;if(QueryFullProcessImageNameW(p,0,n,&sz))ok=ieq_tail(n,L"\\KakaoTalk.exe");CloseHandle(p);}
  if(ok) kakaoPid=pid; else badPid[nbad++&7]=pid; if(nbad>=8)nbad=0;
  return ok;
}
static void saveState(void){
  char buf[MAXH*24]; int n=0;
  for(int i=0;i<nhidden;i++){ unsigned long long v=(unsigned long long)(ULONG_PTR)hidden[i]; char t[24]; int k=0; do{t[k++]='0'+v%10;v/=10;}while(v); while(k)buf[n++]=t[--k]; buf[n++]='\n'; }
  HANDLE f=CreateFileW(stateFile,GENERIC_WRITE,0,0,CREATE_ALWAYS,0,0);
  if(f!=INVALID_HANDLE_VALUE){DWORD w;WriteFile(f,buf,n,&w,0);CloseHandle(f);}
}
static void hide(HWND h){
  if(!IsWindowVisible(h)) return;
  ShowWindow(h,SW_HIDE);
  for(int i=0;i<nhidden;i++) if(hidden[i]==h) return;
  if(nhidden<MAXH){hidden[nhidden++]=h;saveState();}
}
static void relayout(HWND main){ RECT r; GetWindowRect(main,&r); int w=r.right-r.left,h=r.bottom-r.top;
  SetWindowPos(main,0,0,0,w,h+1,SWPF); SetWindowPos(main,0,0,0,w,h,SWPF); }

/* ---- compact layout: shift KakaoTalk's own container windows. Each move only fires while the window sits
   at KakaoTalk's original position, so re-running is idempotent and a KakaoTalk relayout is simply redone. ---- */
static int compact=1, dark=0;
typedef HANDLE (WINAPI*SetT_t)(HANDLE); typedef HANDLE (WINAPI*GetW_t)(HWND); static SetT_t pSetT; static GetW_t pGetW;
#define S(v) (v) /* pixels in KakaoTalk's own (system-DPI) layout, measured at 125% */
static void rel(HWND h,RECT*r){ GetWindowRect(h,r); MapWindowPoints(0,GetParent(h),(POINT*)r,2); }
static void setRel(HWND h,int x,int y,int w,int hh){ SetWindowPos(h,0,x,y,w,hh,SWP_NOZORDER|SWP_NOACTIVATE); }

typedef struct { HWND main; RECT mr; } Ctx;
static BOOL CALLBACK innerKid(HWND k,LPARAM l){ /* children of ChatRoomListView: filter row + list */
  HWND p=(HWND)l; if(GetParent(k)!=p||!IsWindowVisible(k)) return TRUE;
  WCHAR cn[128],t[128]; cls(k,cn); txt(k,t); RECT r; rel(k,&r);
  if(!lstrcmpW(cn,L"EVA_Window_Dblclk")&&!t[0]&&r.top>=S(52)&&r.top<=S(60)) setRel(k,r.left,r.top-S(8),r.right-r.left,r.bottom-r.top);
  else if(starts(t,L"ChatRoomListCtrl")&&r.top>=S(100)) setRel(k,r.left,r.top-S(14),r.right-r.left,r.bottom-r.top+S(14));
  return TRUE; }
static BOOL CALLBACK viewKid(HWND k,LPARAM l){ /* list views inside OnlineMainView: pull left over the sidebar gap */
  HWND p=(HWND)l; if(GetParent(k)!=p||!IsWindowVisible(k)) return TRUE;
  WCHAR t[128]; txt(k,t); if(!starts(t,L"ChatRoomListView")&&!starts(t,L"ContactListView")) return TRUE;
  RECT r; rel(k,&r); /* horizontal shift dropped in v2.1: it clipped the sidebar badge and misaligned the top strip */
  if(starts(t,L"ChatRoomListView")) EnumChildWindows(k,innerKid,(LPARAM)k);
  return TRUE; }
static BOOL CALLBACK fixMainKid(HWND k,LPARAM l){ Ctx*c=(Ctx*)l; if(GetParent(k)!=c->main) return TRUE;
  WCHAR cn[128],t[128]; cls(k,cn); txt(k,t); RECT r; GetWindowRect(k,&r);
  LONG target=c->mr.bottom-1; /* KakaoTalk's own geometry: views end 1px above the window bottom */
  if(starts(cn,L"EVA_ChildWindow")&&!t[0]&&IsWindowVisible(k)){
    LONG hgt=r.bottom-r.top; if(target-r.bottom<=20&&hgt>20&&hgt<250) hide(k);
  } else if((starts(t,L"OnlineMainView")||starts(t,L"LockModeView"))&&IsWindowVisible(k)){
    RECT q; rel(k,&q); int up=(compact&&q.top>=S(34))?S(8):0; LONG db=target-r.bottom;
    if(up||db>2||db<-1){ setRel(k,q.left,q.top-up,q.right-q.left,target-(r.top-up));  }
    if(compact) EnumChildWindows(k,viewKid,(LPARAM)k);
  }
  return TRUE; }

/* ---- dark overlay (v2.2, GDI only): PrintWindow KakaoTalk into a small DIB, darken it in place
   (luminance invert, hue kept), show it in a click-through layered window one z-step above KakaoTalk.
   No Magnification/D3D. Refresh 1 fps idle, 15 fps right after input on KakaoTalk,
   and everything is freed while KakaoTalk is minimized or hidden. ---- */
static HWND ovHost,ovOwner; static HDC ovDC; static HBITMAP ovBmp,ovOld; static DWORD*ovPx; static int ovW,ovH,ovMs; static DWORD ovSum;
static void darkFree(void){ if(ovDC){ SelectObject(ovDC,ovOld); DeleteObject(ovBmp); DeleteDC(ovDC); } ovDC=0; ovBmp=0; ovPx=0; ovW=ovH=0; ovSum=0; }
static void darkKill(void){ if(ovHost) DestroyWindow(ovHost); ovHost=ovOwner=0; darkFree(); SetProcessWorkingSetSize(GetCurrentProcess(),(SIZE_T)-1,(SIZE_T)-1); }
static RECT ovR; static DWORD ovBurst;
/* PrintWindow makes KakaoTalk repaint, so capture only when something likely changed: 1 fps idle,
   15 fps for 0.8 s after mouse/keyboard input on KakaoTalk (low-level hooks run in BongAd, no injection). */
static void darkPoke(void){ ovBurst=GetTickCount(); if(ovMs!=66){ ovMs=66; SetTimer(gMsg,3,66,0); } }
static LRESULT CALLBACK llMouse(int c,WPARAM w,LPARAM l){ if(c>=0&&ovHost&&w!=WM_MOUSEMOVE){ POINT p=((MSLLHOOKSTRUCT*)l)->pt; if(PtInRect(&ovR,p)) darkPoke(); }
  else if(c>=0&&ovHost&&w==WM_MOUSEMOVE&&ovMs!=66){ POINT p=((MSLLHOOKSTRUCT*)l)->pt; if(PtInRect(&ovR,p)) darkPoke(); } return CallNextHookEx(0,c,w,l); }
static LRESULT CALLBACK llKey(int c,WPARAM w,LPARAM l){ if(c>=0&&ovHost){ HWND f=GetForegroundWindow(); DWORD pid=0; if(f) GetWindowThreadProcessId(f,&pid); if(pid&&pid==kakaoPid) darkPoke(); } return CallNextHookEx(0,c,w,l); }
static void darkRate(int ms){ if(ms!=ovMs){ ovMs=ms; SetTimer(gMsg,3,ms,0); } }
/* ---- v2.4 avatars: inside KakaoTalk's list controls, scan the avatar column of the captured bitmap for
   tall blocks of non-background pixels (avatars, incl. 2x2 group tiles) and mark them "keep original" (bit 0x04).
   Detected from the pixels every frame, so it follows scrolling. Squircle corners and the gaps between group
   tiles are excluded per row/column, so the white background there is still darkened. ---- */
typedef struct { HWND k; RECT kr; RECT l[4]; int n; } LCtx;
static BOOL CALLBACK findLists(HWND c,LPARAM lp){ LCtx*x=(LCtx*)lp; if(x->n>=4||!IsWindowVisible(c)) return TRUE;
  WCHAR t[128]; txt(c,t); if(starts(t,L"ChatRoomListCtrl")||starts(t,L"ContactListCtrl")){ RECT r; GetWindowRect(c,&r);
    OffsetRect(&r,-x->kr.left,-x->kr.top); x->l[x->n++]=r; } return TRUE; }
static int isBg(DWORD p){ int R=(p>>16)&255,G=(p>>8)&255,B=p&255, mx=R>G?R:G, mn=R<G?R:G; if(B>mx)mx=B; if(B<mn)mn=B;
  return ((R*77+G*150+B*29)>>8)>=232&&mx-mn<=24; }
static void markAvatars(HWND k,RECT*kr,int w,int h){
  LCtx c={k,*kr}; EnumChildWindows(k,findLists,(LPARAM)&c);
  for(int li=0;li<c.n;li++){ RECT L=c.l[li]; int x0=L.left+8, x1=L.left+96, y0=L.top<0?0:L.top, y1=L.bottom>h?h:L.bottom;
    if(x0<0) x0=0; if(x1>w) x1=w; if(x1-x0<20) continue;
    int run=-1;
    for(int y=y0;y<=y1;y++){ int cnt=0; if(y<y1) for(int x=x0;x<x1;x++) if(!isBg(ovPx[y*w+x])) cnt++;
      if(cnt>=6){ if(run<0) run=y; continue; }
      if(run>=0&&y-run>=28){ /* block rows run..y-1 */
        int ry0=run, ry1=y, bx0=x1, bx1=x0;
        for(int yy=ry0;yy<ry1;yy++) for(int x=x0;x<x1;x++) if(!isBg(ovPx[yy*w+x])){ if(x<bx0)bx0=x; if(x>bx1)bx1=x; }
        if(bx1-bx0>=24){ int bh=ry1-ry0;
          static unsigned char gc[128], gr[256]; /* gap columns / rows: almost no content across the block */
          for(int x=bx0;x<=bx1&&x-bx0<128;x++){ int q=0; for(int yy=ry0;yy<ry1;yy++) if(!isBg(ovPx[yy*w+x])) q++; gc[x-bx0]=q*10<bh; }
          for(int yy=ry0;yy<ry1&&yy-ry0<256;yy++){ int q=0; for(int x=bx0;x<=bx1;x++) if(!isBg(ovPx[yy*w+x])) q++; gr[yy-ry0]=q*10<(bx1-bx0+1); }
          for(int yy=ry0+1;yy<ry1-1&&yy-ry0<256;yy++){ if(gr[yy-ry0]) continue; int x=bx0;
            while(x<=bx1&&x-bx0<128){ while(x<=bx1&&x-bx0<128&&gc[x-bx0]) x++; int s0=x; while(x<=bx1&&x-bx0<128&&!gc[x-bx0]) x++; int s1=x; /* segment [s0,s1) */
              int a=s0,b=s1-1; while(a<=b&&isBg(ovPx[yy*w+a])) a++; while(b>=a&&isBg(ovPx[yy*w+b])) b--;
              for(int xx=a+1;xx<b;xx++) ovPx[yy*w+xx]|=0x04000000u; } } } }
      run=-1; } } }
static void darkTick(void){
  HWND k=FindWindowW(L"EVA_Window_Dblclk",L"\xCE74\xCE74\xC624\xD1A1");
  if(k!=ovOwner) darkKill();
  if(!k||!IsWindowVisible(k)||IsIconic(k)){ if(ovHost) darkKill(); darkRate(1000); return; }
  if(pSetT) pSetT((HANDLE)-4); GetWindowRect(k,&ovR); /* physical, for the low-level hooks */
  if(pSetT&&pGetW) pSetT(pGetW(k)); /* same DPI mode as KakaoTalk: same (scaled) pixels, no resampling */
  if(!ovHost){ ovHost=CreateWindowExW(WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"BongAdDark",0,WS_POPUP,0,0,1,1,0,0,GetModuleHandleW(0),0); ovOwner=k; }
  RECT r; GetWindowRect(k,&r); int w=r.right-r.left,h=r.bottom-r.top;
  if(w!=ovW||h!=ovH){ darkFree(); HDC s=GetDC(0); ovDC=CreateCompatibleDC(s); ReleaseDC(0,s);
    BITMAPINFO bi={0}; bi.bmiHeader.biSize=sizeof bi.bmiHeader; bi.bmiHeader.biWidth=w; bi.bmiHeader.biHeight=-h; bi.bmiHeader.biPlanes=1; bi.bmiHeader.biBitCount=32;
    ovBmp=CreateDIBSection(ovDC,&bi,DIB_RGB_COLORS,(void**)&ovPx,0,0); if(!ovBmp){ DeleteDC(ovDC); ovDC=0; return; } ovOld=SelectObject(ovDC,ovBmp); ovW=w; ovH=h; }
  darkRate(GetTickCount()-ovBurst<800?66:1000);
  if(!PrintWindow(k,ovDC,2/*PW_RENDERFULLCONTENT*/)) return;
  DWORD sum=0; int n=w*h; for(int i=0;i<n;i+=3) sum=(sum*31)^ovPx[i]; /* cheap change check before any work */
  HWND above=GetWindow(k,GW_HWNDPREV);
  if(above!=ovHost) SetWindowPos(ovHost,above?above:HWND_TOP,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_NOOWNERZORDER);
  if(sum==ovSum&&IsWindowVisible(ovHost)){ RECT o; GetWindowRect(ovHost,&o); if(o.left==r.left&&o.top==r.top) return; }
  /* v2.3.1: a pixel counts as "colour" only if it is saturated AND a left/right neighbour has the same dominant
     channel and similar brightness. ClearType text fringes (blue|orange pairs) fail that test, photos and
     accents pass. Colour pixels keep their colour; inside 8x8 blocks that are mostly colour (photo interiors)
     every pixel is kept. Everything else becomes a dark grey by luminance: white -> #1C1C1C, black -> #E6E6E6. */
  for(int y=0;y<h;y++){ DWORD*row=ovPx+y*w; int pa=-1,pl=0,ps=0;
    for(int x=0;x<w;x++){ DWORD p=row[x]; int R=(p>>16)&255,G=(p>>8)&255,B=p&255, mx=R>G?R:G, mn=R<G?R:G; if(B>mx)mx=B; if(B<mn)mn=B;
      int am=(mx==R)?0:(mx==G)?1:2, l=(R*77+G*150+B*29)>>8, sat=mx-mn;
      int keep=sat>=40&&ps>=40&&am==pa&&(l-pl<40&&pl-l<40);
      row[x]=(p&0xFFFFFFu)|(keep?0x01000000u:0); if(keep&&x>0) row[x-1]|=0x01000000u;
      pa=am; pl=l; ps=sat; } }
  markAvatars(k,&r,w,h);
  for(int by=0;by<h;by+=8) for(int bx=0;bx<w;bx+=8){
    int ey=by+8<h?by+8:h, ex=bx+8<w?bx+8:w, col=0, tot=(ey-by)*(ex-bx);
    for(int y=by;y<ey;y++) for(int x=bx;x<ex;x++) col+=(ovPx[y*w+x]>>24)&1;
    int photo=col*2>=tot;
    for(int y=by;y<ey;y++) for(int x=bx;x<ex;x++){ DWORD p=ovPx[y*w+x];
      if(photo||(p>>24)){ ovPx[y*w+x]=p|0xFF000000u; continue; }
      int v=((int)((p>>16)&255)*77+(int)((p>>8)&255)*150+(int)(p&255)*29)>>8, nv=230-((v*202)>>8);
      ovPx[y*w+x]=0xFF000000u|(nv<<16)|(nv<<8)|nv; } }
  ovSum=sum; POINT dst={r.left,r.top},src={0,0}; SIZE sz={w,h}; BLENDFUNCTION bf={AC_SRC_OVER,0,255,0};
  UpdateLayeredWindow(ovHost,0,&dst,&sz,ovDC,&src,0,&bf,ULW_ALPHA);
  if(!IsWindowVisible(ovHost)) ShowWindow(ovHost,SW_SHOWNA);
}

typedef struct { HWND top; HWND found; } Ad;
static BOOL CALLBACK findAd(HWND k,LPARAM l){ WCHAR t[128]; txt(k,t); if(isAdTitle(t)){((Ad*)l)->found=k;return FALSE;} return TRUE; }
static BOOL CALLBACK topProc(HWND h,LPARAM l){
  DWORD pid; GetWindowThreadProcessId(h,&pid); if(!isKakao(pid)||!IsWindowVisible(h)) return TRUE;
  WCHAR cn[128],t[128]; cls(h,cn); txt(h,t);
  if(!lstrcmpW(cn,L"EVA_Window_Dblclk")&&!lstrcmpW(t,L"\xCE74\xCE74\xC624\xD1A1")){ /* 카카오톡 */
    Ctx c={h}; GetWindowRect(h,&c.mr); EnumChildWindows(h,fixMainKid,(LPARAM)&c); }
  Ad a={h,0}; EnumChildWindows(h,findAd,(LPARAM)&a);
  if(a.found){
    if(!t[0]) hide(h); /* untitled ad popup */
    else { HWND x=a.found,p; while((p=GetParent(x))&&p!=h) x=p; hide(x); } /* ad inside chat room */
  }
  return TRUE; }
/* Layout numbers are in KakaoTalk's own coordinate space: run in the same DPI awareness as its main window
   (it is system-DPI-aware and gets bitmap-scaled on a monitor with another scale). Dark overlay uses physical. */
static void kakaoDpi(void){ HWND m=FindWindowW(L"EVA_Window_Dblclk",L"\xCE74\xCE74\xC624\xD1A1"); if(m&&pSetT&&pGetW) pSetT(pGetW(m)); }
static void tick(void){ kakaoDpi(); EnumWindows(topProc,0); if(pSetT) pSetT((HANDLE)-4); }

static void restoreAll(void){
  for(int i=0;i<nhidden;i++) if(IsWindow(hidden[i])) ShowWindow(hidden[i],SW_SHOWNA);
  nhidden=0;
  HANDLE f=CreateFileW(stateFile,GENERIC_READ,FILE_SHARE_READ,0,OPEN_EXISTING,0,0);
  if(f!=INVALID_HANDLE_VALUE){ char b[MAXH*24]; DWORD n=0; ReadFile(f,b,sizeof b,&n,0); CloseHandle(f);
    unsigned long long v=0; int any=0;
    for(DWORD i=0;i<=n;i++){ if(i<n&&b[i]>='0'&&b[i]<='9'){v=v*10+(b[i]-'0');any=1;} else { if(any){HWND h=(HWND)(ULONG_PTR)v; if(IsWindow(h)) ShowWindow(h,SW_SHOWNA);} v=0;any=0; } }
  }
  DeleteFileW(stateFile);
  HWND m=FindWindowW(L"EVA_Window_Dblclk",L"\xCE74\xCE74\xC624\xD1A1");
  if(m){ for(HWND v=0;(v=FindWindowExW(m,v,0,0));){ WCHAR t[128]; txt(v,t); if(starts(t,L"OnlineMainView")||starts(t,L"LockModeView")) SetWindowRgn(v,0,TRUE); }
    relayout(m); }
}

static void CALLBACK evt(HWINEVENTHOOK hk,DWORD ev,HWND h,LONG obj,LONG ch,DWORD th,DWORD tm){
  if(obj!=OBJID_WINDOW||!h) return; DWORD pid; GetWindowThreadProcessId(h,&pid);
  if(isKakao(pid)) SetTimer(gMsg,1,150,0); /* debounce */
}
static LRESULT CALLBACK wndProc(HWND h,UINT m,WPARAM w,LPARAM l){
  if(m==WM_TIMER){ if(w==3){ darkTick(); return 0; } if(w==1) KillTimer(h,1); tick(); SetProcessWorkingSetSize(GetCurrentProcess(),(SIZE_T)-1,(SIZE_T)-1); return 0; }
  if(m==WM_CLOSE){ PostQuitMessage(0); return 0; }
  return DefWindowProcW(h,m,w,l);
}

void BongAdEntry(void){
  const WCHAR*cl=GetCommandLineW(); int once=has(cl,L"/once")||has(cl,L"-once"), rest=has(cl,L"/restore")||has(cl,L"-restore");
  dark=has(cl,L"/dark"); compact=!has(cl,L"/nocompact");
  { HMODULE u=GetModuleHandleW(L"user32.dll"); FARPROC f=GetProcAddress(u,"SetProcessDpiAwarenessContext"); if(f) ((BOOL(WINAPI*)(HANDLE))f)((HANDLE)-4);
    pSetT=(SetT_t)GetProcAddress(u,"SetThreadDpiAwarenessContext"); pGetW=(GetW_t)GetProcAddress(u,"GetWindowDpiAwarenessContext"); }
  DWORD n=GetTempPathW(MAX_PATH-32,stateFile); lstrcpyW(stateFile+n,L"BongAd.hidden.txt");
  if(rest){ HWND o=FindWindowExW(HWND_MESSAGE,0,L"BongAdMsg",0); if(o){ DWORD pid; GetWindowThreadProcessId(o,&pid);
      HANDLE p=OpenProcess(SYNCHRONIZE,FALSE,pid); PostMessageW(o,WM_CLOSE,0,0); if(p){WaitForSingleObject(p,3000);CloseHandle(p);} }
    kakaoDpi(); restoreAll(); ExitProcess(0); }
  if(once){ tick(); ExitProcess(0); }
  /* a new launch replaces a running BongAd (so "BongAd.exe /dark" works even if one is already running) */
  { HWND o=FindWindowExW(HWND_MESSAGE,0,L"BongAdMsg",0); if(o){ DWORD pid; GetWindowThreadProcessId(o,&pid);
      HANDLE p=OpenProcess(SYNCHRONIZE,FALSE,pid); PostMessageW(o,WM_CLOSE,0,0); if(p){WaitForSingleObject(p,3000);CloseHandle(p);} } }
  CreateMutexW(0,TRUE,L"Local\\BongAd_SingleInstance"); if(GetLastError()==ERROR_ALREADY_EXISTS) ExitProcess(0);
  WNDCLASSW wc={0}; wc.lpfnWndProc=wndProc; wc.hInstance=GetModuleHandleW(0); wc.lpszClassName=L"BongAdMsg"; RegisterClassW(&wc);
  gMsg=CreateWindowExW(0,L"BongAdMsg",0,0,0,0,0,0,HWND_MESSAGE,0,wc.hInstance,0);
  SetWinEventHook(EVENT_OBJECT_CREATE,EVENT_OBJECT_HIDE,0,evt,0,0,WINEVENT_OUTOFCONTEXT|WINEVENT_SKIPOWNPROCESS);
  SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE,EVENT_OBJECT_LOCATIONCHANGE,0,evt,0,0,WINEVENT_OUTOFCONTEXT|WINEVENT_SKIPOWNPROCESS);
  if(dark){ WNDCLASSW dc={0}; dc.lpfnWndProc=DefWindowProcW; dc.hInstance=wc.hInstance; dc.lpszClassName=L"BongAdDark"; RegisterClassW(&dc); darkRate(66); SetWindowsHookExW(WH_MOUSE_LL,llMouse,wc.hInstance,0); SetWindowsHookExW(WH_KEYBOARD_LL,llKey,wc.hInstance,0); }
  SetTimer(gMsg,2,10000,0); /* safety net: re-check every 10 s */
  tick(); SetProcessWorkingSetSize(GetCurrentProcess(),(SIZE_T)-1,(SIZE_T)-1);
  MSG m; while(GetMessageW(&m,0,0,0)>0){TranslateMessage(&m);DispatchMessageW(&m);}
  darkKill(); restoreAll(); ExitProcess(0);
}
