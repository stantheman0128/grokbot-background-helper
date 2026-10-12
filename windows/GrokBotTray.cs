// GrokBot Background Helper (Windows) - MIT License. Unofficial; not affiliated with SpaceXAI/xAI.
using System; using System.Diagnostics; using System.Drawing; using System.IO; using System.Runtime.InteropServices; using System.Text; using System.Threading; using System.Windows.Forms;
class GrokBotTray : ApplicationContext {
  // Config (helper.ini next to the exe): AppName, ExePath, WindowTitle, ProcessName, WindowClass
  const long MaxLogBytes=256*1024;
  const int WM_HIDE=0x8001;
  const int SM_CXSIZE=30, SM_CYSIZE=31, SM_CXFRAME=32, SM_CYFRAME=33, SM_CXPADDEDBORDER=92;
  static string AppName="Grok Bot", ExePath, WindowTitle="Grok Bot", ProcName="Grok Bot", WinClass="Chrome_WidgetWin_1";
  static string Dir, dpiMode="unaware";
  static int MyPid;
  static void LoadConfig(){
    ExePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Grok Bot","Grok Bot.exe");
    if(string.IsNullOrEmpty(Dir)) return;
    var f=Path.Combine(Dir,"helper.ini"); if(!File.Exists(f)) return;
    foreach(var raw in File.ReadAllLines(f)){ var line=raw.Trim(); if(line.Length==0||line[0]==';'||line[0]=='#'||line[0]=='[') continue;
      int i=line.IndexOf('='); if(i<1) continue; var k=line.Substring(0,i).Trim().ToLowerInvariant(); var v=Environment.ExpandEnvironmentVariables(line.Substring(i+1).Trim());
      if(v.Length==0) continue;
      if(k=="appname") AppName=v; else if(k=="exepath") ExePath=v; else if(k=="windowtitle") WindowTitle=v; else if(k=="processname") ProcName=v; else if(k=="windowclass") WinClass=v; } }
  delegate IntPtr HookProc(int n, IntPtr w, IntPtr l); delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr m, uint t);
  [DllImport("user32")] static extern IntPtr CallNextHookEx(IntPtr h,int n,IntPtr w,IntPtr l);
  [DllImport("user32")] static extern bool UnhookWindowsHookEx(IntPtr h);
  [DllImport("kernel32")] static extern IntPtr GetModuleHandle(string n);
  [DllImport("user32")] static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr h,uint f);
  [DllImport("user32")] static extern IntPtr SendMessageTimeout(IntPtr h,uint m,IntPtr w,IntPtr l,uint f,uint t,out IntPtr r);
  [DllImport("user32")] static extern bool ShowWindow(IntPtr h,int c);
  [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32")] static extern bool IsWindow(IntPtr h);
  [DllImport("user32")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32")] static extern bool EnumWindows(EnumProc p,IntPtr l);
  [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
  [DllImport("user32",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder s,int n);
  [DllImport("user32")] static extern bool GetWindowRect(IntPtr h,out RECT r);
  [DllImport("user32")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32")] static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
  [DllImport("user32")] static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32")] static extern bool SetProcessDPIAware();
  [DllImport("user32")] static extern IntPtr GetThreadDpiAwarenessContext();
  [DllImport("user32")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
  [DllImport("user32")] static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32")] static extern int GetSystemMetrics(int n);
  [DllImport("user32")] static extern int GetSystemMetricsForDpi(int n,uint dpi);
  [DllImport("shcore")] static extern int SetProcessDpiAwareness(int awareness);
  RECT closeRc; bool haveClose=false; DateTime goneSince=DateTime.MaxValue, startedAt, lastHookLog=DateTime.MinValue;
  [StructLayout(LayoutKind.Sequential)] struct POINT{public int x,y;}
  [StructLayout(LayoutKind.Sequential)] struct RECT{public int l,t,r,b;}
  // Published as one immutable object so the hook can read it without calling into user32.
  sealed class CloseSnap{ public readonly int l,t,r,b; public readonly bool on;
    public CloseSnap(int l,int t,int r,int b,bool on){ this.l=l; this.t=t; this.r=r; this.b=b; this.on=on; } }
  volatile CloseSnap closeSnap;
  sealed class UiWin:NativeWindow{
    readonly GrokBotTray owner;
    public UiWin(GrokBotTray o){ owner=o; }
    public void Open(){ var cp=new CreateParams(); cp.Caption="GrokBotTray.msg"; cp.Parent=new IntPtr(-3); cp.Style=0; cp.ExStyle=0; CreateHandle(cp); }
    protected override void WndProc(ref Message m){ if(m.Msg==WM_HIDE){ try{ owner.OnUiHide(); }catch{ owner.hookFault=true; } } base.WndProc(ref m); }
  }
  NotifyIcon tray; ContextMenuStrip menu; System.Windows.Forms.Timer timer; UiWin ui;
  IntPtr hwnd=IntPtr.Zero, uiHwnd, mHook, kHook, pendHwnd; HookProc mProc,kProc;
  bool userQuit=false, swallowUp=false, hideNew=false, seenRunning=false, quitSawGone=false, booted=false, exiting=false, ownIcon=false;
  bool altF4Held=false, hookFault=false, hooksSuspect=false, inTick=false, dpiForWindowMissing=false, metricForDpiMissing=false;
  int launchedPid=0, hideQueued=0, hideKind=0, pendX, pendY, cleaned=0, anchorL, anchorT, anchorR, anchorB;
  string lastHookMsg; DateTime lastLaunch=DateTime.MinValue; SynchronizationContext sync;
  static void Log(string s){ try{
    if(string.IsNullOrEmpty(Dir)) return;
    var path=Path.Combine(Dir,"helper.log");
    var fi=new FileInfo(path);
    if(fi.Exists && fi.Length>MaxLogBytes){ var bak=path+".old"; try{ if(File.Exists(bak)) File.Delete(bak); }catch{} try{ File.Move(path,bak); }catch{} }
    File.AppendAllText(path,DateTime.Now.ToString("s")+" "+s+"\r\n");
  }catch{} }
  static bool TryDpiContext(int ctx,string name){ try{ if(SetProcessDpiAwarenessContext(new IntPtr(ctx))){ dpiMode=name; return true; } }catch(EntryPointNotFoundException){} return false; }
  static bool TryDpiAwareness(int level,string name){ try{ if(SetProcessDpiAwareness(level)==0){ dpiMode=name; return true; } }catch(EntryPointNotFoundException){} catch(DllNotFoundException){} return false; }
  // Must run before any WinForms window. Static init must not touch Application.* or DPI locks to unaware.
  static void SetDpi(){
    if(TryDpiContext(-4,"per-monitor-v2")) return;
    if(TryDpiContext(-3,"per-monitor")) return;
    if(TryDpiAwareness(2,"per-monitor")) return;
    if(TryDpiContext(-2,"system")) return;
    if(TryDpiAwareness(1,"system")) return;
    try{ if(SetProcessDPIAware()){ dpiMode="system"; return; } }catch(EntryPointNotFoundException){}
    try{
      int a=GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
      if(a==2) dpiMode="per-monitor"; else if(a==1) dpiMode="system"; else dpiMode="unaware";
    }catch(EntryPointNotFoundException){ dpiMode="unaware"; } catch(DllNotFoundException){ dpiMode="unaware"; }
  }
  GrokBotTray(){
    startedAt=DateTime.Now;
    tray=new NotifyIcon();
    try{ tray.Icon=Icon.ExtractAssociatedIcon(ExePath); ownIcon=tray.Icon!=null; }catch{ ownIcon=false; }
    if(tray.Icon==null){ tray.Icon=SystemIcons.Application; ownIcon=false; }
    tray.Text=AppName.Length>63?AppName.Substring(0,63):AppName;
    menu=new ContextMenuStrip();
    menu.Items.Add("Show "+AppName,null,(s,e)=>ShowGrok());
    menu.Items.Add("Quit "+AppName,null,(s,e)=>QuitGrok());
    menu.Items.Add("Exit helper",null,(s,e)=>ExitHelper());
    tray.ContextMenuStrip=menu; tray.MouseClick+=(s,e)=>{ if(e.Button==MouseButtons.Left) ShowGrok(); }; tray.Visible=true;
    mProc=MouseHook; kProc=KeyHook;
    timer=new System.Windows.Forms.Timer(); timer.Interval=1000; timer.Tick+=(s,e)=>Tick(); timer.Start();
    Application.ApplicationExit+=delegate{ Cleanup(false); };
    AppDomain.CurrentDomain.ProcessExit+=delegate{ try{ UnhookOnly(); }catch{} };
  }
  void Boot(){
    try{ ui=new UiWin(this); ui.Open(); uiHwnd=ui.Handle; }
    catch(Exception ex){ Log("ui window "+ex.Message); uiHwnd=IntPtr.Zero; }
    sync=SynchronizationContext.Current;
    Log("started dpi="+dpiMode);
  }
  bool IsGrokPid(uint pid){ try{ using(var p=Process.GetProcessById((int)pid)) return p.ProcessName==ProcName; }catch{ return false; } }
  IntPtr FindMain(){ IntPtr found=IntPtr.Zero; EnumWindows((h,l)=>{ uint pid; GetWindowThreadProcessId(h,out pid);
      var t=new StringBuilder(256); GetWindowText(h,t,256); var c=new StringBuilder(256); GetClassName(h,c,256);
      if(t.ToString()==WindowTitle && c.ToString()==WinClass && IsGrokPid(pid)){ found=h; return false;} return true; },IntPtr.Zero); return found; }
  static void DisposeProcesses(Process[] ps){ if(ps==null) return; for(int i=0;i<ps.Length;i++){ try{ ps[i].Dispose(); }catch{} } }
  bool LaunchedAlive(){
    if(launchedPid<=0) return false;
    try{ using(var p=Process.GetProcessById(launchedPid)) return p!=null && !p.HasExited; }
    catch(ArgumentException){ launchedPid=0; return false; }
    catch{ return true; }
  }
  // Top-level target-class window owned by ProcName. Windowless leftover children do not match.
  bool ClassWindowAlive(){ bool found=false; EnumWindows((h,l)=>{
      var c=new StringBuilder(256); GetClassName(h,c,256); if(c.ToString()!=WinClass) return true;
      uint pid; GetWindowThreadProcessId(h,out pid); if(IsGrokPid(pid)){ found=true; return false; } return true; },IntPtr.Zero); return found; }
  bool BlocksRelaunch(){
    if(LaunchedAlive()||ClassWindowAlive()) return true;
    // A process younger than 20s is probably still creating its window. Older windowless
    // processes are leftover children (they share ProcName) and must not block relaunch.
    Process[] ps=null; try{
      ps=Process.GetProcessesByName(ProcName); var now=DateTime.Now;
      for(int i=0;i<ps.Length;i++){ var p=ps[i]; try{
          if(p.Id==MyPid||p.HasExited) continue;
          try{ if((now-p.StartTime).TotalSeconds<20) return true; }catch{}
        }catch{} }
    }catch{} finally{ DisposeProcesses(ps); }
    return false;
  }
  bool StartTarget(){
    var psi=new ProcessStartInfo(); psi.FileName=ExePath; psi.UseShellExecute=true;
    var wd=Path.GetDirectoryName(ExePath); if(!string.IsNullOrEmpty(wd)) psi.WorkingDirectory=wd;
    Process p=null; try{
      p=Process.Start(psi); if(p==null) return false;
      try{ launchedPid=p.Id; }catch{}
      return true;
    }finally{ if(p!=null) p.Dispose(); }
  }
  void Tick(){
    if(exiting||inTick) return; inTick=true; var tickSw=Stopwatch.StartNew();
    try{
      if(!booted){ booted=true; try{ Boot(); }catch(Exception ex){ Log("boot "+ex.Message); hooksSuspect=true; } }
      EnsureHooks();
      if(hideQueued!=0) OnUiHide();
      if(hwnd==IntPtr.Zero||!IsWindow(hwnd)){ var h=FindMain(); if(h!=hwnd){ hwnd=h; if(h==IntPtr.Zero) PublishClose(0,0,0,0,false); Log("attached hwnd="+h); } }
      bool running=hwnd!=IntPtr.Zero;
      if(running){ seenRunning=true; goneSince=DateTime.MaxValue;
        if(userQuit&&quitSawGone&&IsWindowVisible(hwnd)){ userQuit=false; quitSawGone=false; Log("window back after quit"); } }
      else{ if(goneSince==DateTime.MaxValue) goneSince=seenRunning?DateTime.Now:startedAt; if(userQuit) quitSawGone=true; }
      if(running&&hideNew&&IsWindowVisible(hwnd)){ ShowWindow(hwnd,0); hideNew=false; PublishClose(0,0,0,0,false); Log("hid relaunched window"); }
      bool visible=running&&IsWindowVisible(hwnd);
      if(!running&&!userQuit&&(DateTime.Now-goneSince).TotalSeconds>=2&&(DateTime.Now-lastLaunch).TotalSeconds>15){
        if(BlocksRelaunch()) lastLaunch=DateTime.Now.AddSeconds(-12);
        else{ hideNew=false; lastLaunch=DateTime.Now; var sw=Stopwatch.StartNew(); try{
            if(StartTarget()){ hideNew=true; Log("watchdog relaunched"); }
            else Log("launch fail");
          }catch(Exception ex){ hideNew=false; Log("launch fail "+ex.Message); }
          if(sw.ElapsedMilliseconds>250) hooksSuspect=true; } }
      // userQuit stays set while this window is still closing. Clearing it on sight relaunched the app.
      if(running&&visible&&!userQuit) UpdateCloseRect();
      else PublishClose(0,0,0,0,false);
      if(tickSw.ElapsedMilliseconds>250) hooksSuspect=true;
      if(hooksSuspect||hookFault) EnsureHooks();
      WriteStatus(running,visible);
    }catch(Exception ex){ Log("tick "+ex.Message); hooksSuspect=true; try{ EnsureHooks(); }catch{} }
    finally{ inTick=false; }
  }
  void WriteStatus(bool running,bool visible){ try{
    if(string.IsNullOrEmpty(Dir)) return;
    var rect=haveClose?(closeRc.l+","+closeRc.t+","+closeRc.r+","+closeRc.b):"none";
    File.WriteAllText(Path.Combine(Dir,"status.txt"),string.Format("time={0}\r\nhelperPid={1}\r\nhwnd={2}\r\nvisible={3}\r\nuserQuit={4}\r\nmouseHook={5}\r\nkeyHook={6}\r\ncloseRect={7}\r\ndpi={8}\r\n",DateTime.Now.ToString("s"),MyPid,hwnd,running&&visible,userQuit,mHook,kHook,rect,dpiMode));
  }catch{} }
  uint WindowDpi(IntPtr h){
    if(dpiForWindowMissing||h==IntPtr.Zero) return 96;
    try{ uint d=GetDpiForWindow(h); return d==0?96:d; }
    catch(EntryPointNotFoundException){ dpiForWindowMissing=true; return 96; }
  }
  int Metric(int idx,uint dpi){
    if(!metricForDpiMissing&&dpi!=0){ try{ return GetSystemMetricsForDpi(idx,dpi); }catch(EntryPointNotFoundException){ metricForDpiMissing=true; } }
    return GetSystemMetrics(idx);
  }
  static bool Fits16(int v){ return v>=short.MinValue&&v<=short.MaxValue; }
  static IntPtr PackPoint(int x,int y){ uint v=((uint)x&0xFFFFu)|(((uint)y&0xFFFFu)<<16); unchecked{ return new IntPtr((int)v); } }
  int Hit(int x,int y){ IntPtr r; var ok=SendMessageTimeout(hwnd,0x84,IntPtr.Zero,PackPoint(x,y),2,15,out r); return ok!=IntPtr.Zero?(int)r:-1; }
  void PublishClose(int l,int t,int r,int b,bool on){
    var prev=closeSnap;
    bool same=on?(prev!=null&&prev.l==l&&prev.t==t&&prev.r==r&&prev.b==b):(prev==null);
    if(same) return;
    if(!on){ closeSnap=null; haveClose=false; anchorL=anchorT=anchorR=anchorB=0; Log("closeRect=none"); return; }
    closeSnap=new CloseSnap(l,t,r,b,true); closeRc.l=l; closeRc.t=t; closeRc.r=r; closeRc.b=b; haveClose=true;
    Log("closeRect="+l+","+t+","+r+","+b);
  }
  void UpdateCloseRect(){
    RECT rc; if(hwnd==IntPtr.Zero||!GetWindowRect(hwnd,out rc)||rc.r<=rc.l||rc.b<=rc.t){ PublishClose(0,0,0,0,false); return; }
    uint dpi=WindowDpi(hwnd);
    int btnW=Math.Max(1,Metric(SM_CXSIZE,dpi)), btnH=Math.Max(1,Metric(SM_CYSIZE,dpi));
    int frameX=Math.Max(0,Metric(SM_CXFRAME,dpi)), frameY=Math.Max(0,Metric(SM_CYFRAME,dpi)), pad=Math.Max(0,Metric(SM_CXPADDEDBORDER,dpi));
    int step=Math.Max(2,btnW/8), reach=Math.Max(btnW*3+frameX,step*4);
    int yA=rc.t+Math.Max(1,btnH/3), yB=rc.t+frameY+pad+Math.Max(1,btnH/2);
    int hitL=int.MaxValue, hitR=int.MinValue, hitY=yA, probes=0; bool sawFit=false;
    var sw=Stopwatch.StartNew();
    for(int pass=0;pass<2&&hitR==int.MinValue;pass++){
      int y=pass==0?yA:yB; if(pass==1&&y==yA) break;
      for(int x=rc.r-2;x>rc.l&&x>rc.r-reach;x-=step){
        if(probes>=18||sw.ElapsedMilliseconds>=40) break;
        if(!Fits16(x)||!Fits16(y)) continue;
        sawFit=true; probes++;
        if(Hit(x,y)==20){ if(x<hitL) hitL=x; if(x>hitR) hitR=x; hitY=y; }
        else if(hitR!=int.MinValue) break;
      }
    }
    if(sw.ElapsedMilliseconds>250) hooksSuspect=true;
    if(hitR!=int.MinValue){
      int mid=(hitL+hitR)/2, btm=hitY;
      for(int yy=hitY;yy<hitY+btnH+frameY+pad;yy+=step){
        if(probes>=18||sw.ElapsedMilliseconds>=40) break;
        if(!Fits16(mid)||!Fits16(yy)) break;
        probes++; if(Hit(mid,yy)==20) btm=yy; else break;
      }
      anchorL=rc.l; anchorT=rc.t; anchorR=rc.r; anchorB=rc.b;
      PublishClose(hitL-2,rc.t,rc.r,btm+3,true); return;
    }
    // WM_NCHITTEST's lParam is a 16-bit point. Off-screen virtual coordinates cannot be probed.
    if(!sawFit){
      int bw=Math.Max(btnW,20*(int)dpi/96), bh=Math.Max(btnH+frameY+pad,16*(int)dpi/96);
      anchorL=rc.l; anchorT=rc.t; anchorR=rc.r; anchorB=rc.b;
      PublishClose(rc.r-bw-frameX,rc.t,rc.r,rc.t+bh,true); return;
    }
    // Probe missed and the window moved: drop the stale rect instead of swallowing clicks at the old spot.
    if(anchorR!=anchorL&&(rc.l!=anchorL||rc.t!=anchorT||rc.r!=anchorR||rc.b!=anchorB)) PublishClose(0,0,0,0,false);
  }
  bool InClose(int x,int y){ var s=closeSnap; return s!=null&&s.on&&x>=s.l&&x<s.r&&y>=s.t&&y<s.b; }
  // Hooks run on the input path. No logging, file IO, or WindowFromPoint here.
  void PostHide(int kind,int x,int y){
    pendHwnd=hwnd; hideKind=kind; pendX=x; pendY=y;
    if(Interlocked.CompareExchange(ref hideQueued,1,0)!=0) return;
    if(uiHwnd!=IntPtr.Zero&&PostMessage(uiHwnd,(uint)WM_HIDE,IntPtr.Zero,IntPtr.Zero)) return;
    uiHwnd=IntPtr.Zero;
    if(sync!=null) sync.Post(delegate{ OnUiHide(); },null); else hideQueued=0;
  }
  IntPtr MouseHook(int n,IntPtr w,IntPtr l){
    try{
      if(n<0||l==IntPtr.Zero) return CallNextHookEx(mHook,n,w,l);
      int msg=(int)w; if(msg!=0x201&&msg!=0x202) return CallNextHookEx(mHook,n,w,l);
      int x=Marshal.ReadInt32(l,0), y=Marshal.ReadInt32(l,4);
      if(msg==0x201){ if(InClose(x,y)){ swallowUp=true; return (IntPtr)1; } }
      else if(swallowUp){ swallowUp=false; if(InClose(x,y)) PostHide(1,x,y); return (IntPtr)1; }
    }catch{ hookFault=true; }
    return CallNextHookEx(mHook,n,w,l);
  }
  IntPtr KeyHook(int n,IntPtr w,IntPtr l){
    try{
      if(n<0||l==IntPtr.Zero) return CallNextHookEx(kHook,n,w,l);
      int msg=(int)w; if(msg!=0x100&&msg!=0x104&&msg!=0x101&&msg!=0x105) return CallNextHookEx(kHook,n,w,l);
      int vk=Marshal.ReadInt32(l,0), flags=Marshal.ReadInt32(l,8);
      if(vk!=0x73) return CallNextHookEx(kHook,n,w,l); // VK_F4
      bool up=(flags&0x80)!=0, alt=(flags&0x20)!=0; // LLKHF_UP, LLKHF_ALTDOWN
      if(up){ if(altF4Held){ altF4Held=false; return (IntPtr)1; } }
      else if(alt&&hwnd!=IntPtr.Zero&&GetForegroundWindow()==hwnd){
        if(!altF4Held){ altF4Held=true; PostHide(2,0,0); } // first down only; repeats are swallowed below
        return (IntPtr)1;
      }else if(altF4Held) altF4Held=false;
    }catch{ hookFault=true; }
    return CallNextHookEx(kHook,n,w,l);
  }
  void OnUiHide(){
    int kind=hideKind, x=pendX, y=pendY; IntPtr h=pendHwnd;
    hideKind=0; Interlocked.Exchange(ref hideQueued,0);
    if(exiting||kind==0) return;
    if(kind==2){ DoHide(h,"intercept alt+f4"); return; }
    if(kind!=1||!InClose(x,y)) return;
    if(h==IntPtr.Zero||!IsWindow(h)||!IsWindowVisible(h)) return;
    var p=new POINT(); p.x=x; p.y=y;
    var under=WindowFromPoint(p); if(under==IntPtr.Zero||GetAncestor(under,2)!=h) return;
    DoHide(h,"hid on close");
  }
  void DoHide(IntPtr h,string why){ if(h!=IntPtr.Zero&&IsWindow(h)) ShowWindow(h,0); PublishClose(0,0,0,0,false); Log(why); }
  void ShowGrok(){ userQuit=false; quitSawGone=false; hideNew=false; if(hwnd==IntPtr.Zero||!IsWindow(hwnd)) hwnd=FindMain();
    if(hwnd!=IntPtr.Zero){ ShowWindow(hwnd,5); ShowWindow(hwnd,9); SetForegroundWindow(hwnd);}
    else{ try{ lastLaunch=DateTime.Now; if(!StartTarget()) Log("launch fail"); }catch(Exception ex){ Log("launch fail "+ex.Message); } } }
  void QuitGrok(){ userQuit=true; quitSawGone=false; PublishClose(0,0,0,0,false); Log("user quit");
    if(hwnd!=IntPtr.Zero&&IsWindow(hwnd)) PostMessage(hwnd,0x10,IntPtr.Zero,IntPtr.Zero); }
  void EnsureHooks(){
    if(exiting) return;
    if(!hookFault&&!hooksSuspect&&mHook!=IntPtr.Zero&&kHook!=IntPtr.Zero) return;
    bool again=hookFault||hooksSuspect; hookFault=false; hooksSuspect=false;
    UnhookOnly();
    var mod=GetModuleHandle(null);
    mHook=SetWindowsHookEx(14,mProc,mod,0); kHook=SetWindowsHookEx(13,kProc,mod,0);
    string msg=(mHook==IntPtr.Zero||kHook==IntPtr.Zero)?("hook install failed mouse="+mHook+" key="+kHook):(again?"hooks reinstalled":"hooks installed");
    if(msg!=lastHookMsg||(DateTime.Now-lastHookLog).TotalSeconds>30){ lastHookLog=DateTime.Now; lastHookMsg=msg; Log(msg); }
  }
  void UnhookOnly(){
    if(mHook!=IntPtr.Zero){ UnhookWindowsHookEx(mHook); mHook=IntPtr.Zero; }
    if(kHook!=IntPtr.Zero){ UnhookWindowsHookEx(kHook); kHook=IntPtr.Zero; }
  }
  void Cleanup(bool fromProcessExit){
    if(Interlocked.Exchange(ref cleaned,1)==1) return;
    exiting=true;
    try{ if(timer!=null){ timer.Stop(); timer.Dispose(); timer=null; } }catch{}
    try{ UnhookOnly(); }catch{}
    if(fromProcessExit) return;
    try{ if(ui!=null){ ui.DestroyHandle(); ui=null; uiHwnd=IntPtr.Zero; } }catch{}
    try{
      if(tray!=null){
        var icon=tray.Icon; tray.Visible=false; tray.Icon=null;
        if(ownIcon&&icon!=null&&icon!=SystemIcons.Application) icon.Dispose();
        tray.ContextMenuStrip=null; if(menu!=null){ menu.Dispose(); menu=null; }
        tray.Dispose(); tray=null;
      }
    }catch{}
  }
  void ExitHelper(){ if(hwnd!=IntPtr.Zero&&IsWindow(hwnd)) ShowWindow(hwnd,5); Log("helper exit");
    // Defer so the context menu is not disposed from inside its own click.
    var later=sync??SynchronizationContext.Current;
    if(later!=null) later.Post(delegate{ Cleanup(false); ExitThread(); },null);
    else{ Cleanup(false); ExitThread(); } }
  [STAThread] static void Main(){
    SetDpi();
    MyPid=Process.GetCurrentProcess().Id;
    try{ Dir=Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName); }catch{}
    if(string.IsNullOrEmpty(Dir)) Dir=Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
    LoadConfig();
    bool created; var mx=new Mutex(true,"Local\\BgHelper_"+ProcName.Replace(" ","_"),out created);
    if(!created){ mx.Dispose(); return; }
    try{
      Application.EnableVisualStyles();
      Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
      Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e){ try{ Log("ui "+e.Exception.Message); }catch{} };
      Application.Run(new GrokBotTray());
    }finally{ mx.Dispose(); }
  }
}
