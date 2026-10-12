// GrokBot Background Helper (Windows) - MIT License. Unofficial; not affiliated with SpaceXAI/xAI.
using System; using System.Diagnostics; using System.Drawing; using System.IO; using System.Runtime.InteropServices; using System.Text; using System.Threading; using System.Windows.Forms;
class GrokBotTray : ApplicationContext {
  // Config (helper.ini next to the exe): AppName, ExePath, WindowTitle, ProcessName, WindowClass
  static string AppName="Grok Bot", ExePath, WindowTitle="Grok Bot", ProcName="Grok Bot", WinClass="Chrome_WidgetWin_1";
  static void LoadConfig(){
    ExePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Grok Bot","Grok Bot.exe");
    var f=Path.Combine(Dir,"helper.ini"); if(!File.Exists(f)) return;
    foreach(var raw in File.ReadAllLines(f)){ var line=raw.Trim(); if(line.Length==0||line[0]==';'||line[0]=='#'||line[0]=='[') continue;
      int i=line.IndexOf('='); if(i<1) continue; var k=line.Substring(0,i).Trim().ToLowerInvariant(); var v=Environment.ExpandEnvironmentVariables(line.Substring(i+1).Trim());
      if(v.Length==0) continue;
      if(k=="appname") AppName=v; else if(k=="exepath") ExePath=v; else if(k=="windowtitle") WindowTitle=v; else if(k=="processname") ProcName=v; else if(k=="windowclass") WinClass=v; } }
  static readonly string Dir=Path.GetDirectoryName(Application.ExecutablePath);
  delegate IntPtr HookProc(int n, IntPtr w, IntPtr l); delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32")] static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr m, uint t);
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
  [DllImport("user32")] static extern short GetAsyncKeyState(int k);
  [DllImport("user32")] static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  RECT closeRc; bool haveClose=false; DateTime goneSince=DateTime.MaxValue;
  [StructLayout(LayoutKind.Sequential)] struct POINT{public int x,y;}
  [StructLayout(LayoutKind.Sequential)] struct RECT{public int l,t,r,b;}
  [StructLayout(LayoutKind.Sequential)] struct MSLL{public POINT pt;public uint data,flags,time;public IntPtr extra;}
  [StructLayout(LayoutKind.Sequential)] struct KBLL{public uint vk,scan,flags,time;public IntPtr extra;}
  NotifyIcon tray; System.Windows.Forms.Timer timer; IntPtr hwnd=IntPtr.Zero, mHook, kHook; HookProc mProc,kProc;
  bool userQuit=false, swallowUp=false, hideNew=false; DateTime lastLaunch=DateTime.MinValue;
  static void Log(string s){ try{ File.AppendAllText(Path.Combine(Dir,"helper.log"),DateTime.Now.ToString("s")+" "+s+"\r\n"); }catch{} }
  GrokBotTray(){
    tray=new NotifyIcon(); try{ tray.Icon=Icon.ExtractAssociatedIcon(ExePath);}catch{ tray.Icon=SystemIcons.Application; }
    tray.Text=AppName.Length>63?AppName.Substring(0,63):AppName; var m=new ContextMenuStrip();
    m.Items.Add("Show "+AppName,null,(s,e)=>ShowGrok());
    m.Items.Add("Quit "+AppName,null,(s,e)=>QuitGrok());
    m.Items.Add("Exit helper",null,(s,e)=>ExitHelper());
    tray.ContextMenuStrip=m; tray.MouseClick+=(s,e)=>{ if(e.Button==MouseButtons.Left) ShowGrok(); }; tray.Visible=true;
    mProc=MouseHook; kProc=KeyHook; IntPtr mod=GetModuleHandle(null);
    mHook=SetWindowsHookEx(14,mProc,mod,0); kHook=SetWindowsHookEx(13,kProc,mod,0);
    timer=new System.Windows.Forms.Timer(); timer.Interval=1000; timer.Tick+=(s,e)=>Tick(); timer.Start(); Log("started"); Tick();
  }
  bool IsGrokPid(uint pid){ try{ using(var p=Process.GetProcessById((int)pid)) return p.ProcessName==ProcName; }catch{ return false; } }
  static readonly int MyPid=Process.GetCurrentProcess().Id;
  IntPtr FindMain(){ IntPtr found=IntPtr.Zero; EnumWindows((h,l)=>{ uint pid; GetWindowThreadProcessId(h,out pid);
      var t=new StringBuilder(256); GetWindowText(h,t,256); var c=new StringBuilder(256); GetClassName(h,c,256);
      if(t.ToString()==WindowTitle && c.ToString()==WinClass && IsGrokPid(pid)){ found=h; return false;} return true; },IntPtr.Zero); return found; }
  void Tick(){
    if(hwnd==IntPtr.Zero || !IsWindow(hwnd)){ var h=FindMain(); if(h!=hwnd){ hwnd=h; haveClose=false; Log("attached hwnd="+h); } }
    bool running=hwnd!=IntPtr.Zero;
    if(running){ goneSince=DateTime.MaxValue; if(IsWindowVisible(hwnd)){ if(userQuit) userQuit=false; UpdateCloseRect(); } }
    else if(goneSince==DateTime.MaxValue) goneSince=DateTime.Now;
    if(running && hideNew && IsWindowVisible(hwnd)){ ShowWindow(hwnd,0); hideNew=false; Log("hid relaunched window"); }
    if(!running && !userQuit && (DateTime.Now-goneSince).TotalSeconds>=2 && (DateTime.Now-lastLaunch).TotalSeconds>15){ try{ lastLaunch=DateTime.Now; hideNew=true; Process.Start(ExePath); Log("watchdog relaunched"); }catch(Exception ex){ Log("launch fail "+ex.Message);} }
    try{ File.WriteAllText(Path.Combine(Dir,"status.txt"),string.Format("time={0}\r\nhelperPid={1}\r\nhwnd={2}\r\nvisible={3}\r\nuserQuit={4}\r\nmouseHook={5}\r\nkeyHook={6}\r\ncloseRect={7}\r\n",DateTime.Now.ToString("s"),MyPid,hwnd,running&&IsWindowVisible(hwnd),userQuit,mHook,kHook,haveClose?(closeRc.l+","+closeRc.t+","+closeRc.r+","+closeRc.b):"none")); }catch{}
  }
  int Hit(int x,int y){ IntPtr r; IntPtr lp=(IntPtr)(((y&0xFFFF)<<16)|(x&0xFFFF)); return SendMessageTimeout(hwnd,0x84,IntPtr.Zero,lp,2,100,out r)!=IntPtr.Zero?(int)r:-1; }
  void UpdateCloseRect(){ RECT rc; if(!GetWindowRect(hwnd,out rc)) return; int y=Math.Max(rc.t,0)+12; int l=int.MaxValue,r=int.MinValue;
    for(int x=rc.r-2;x>rc.r-400;x-=3){ if(Hit(x,y)==20){ l=Math.Min(l,x); r=Math.Max(r,x);} else if(r!=int.MinValue) break; }
    if(r==int.MinValue){ haveClose=false; return; } int b=y; for(int yy=y;yy<y+150;yy+=3){ if(Hit((l+r)/2,yy)==20) b=yy; else break; }
    var n=new RECT{l=l-2,t=rc.t,r=rc.r,b=b+3}; if(!haveClose||n.l!=closeRc.l||n.b!=closeRc.b||n.r!=closeRc.r){ closeRc=n; haveClose=true; Log("closeRect="+n.l+","+n.t+","+n.r+","+n.b); } }
  bool IsCloseHit(POINT p){ if(hwnd==IntPtr.Zero||!haveClose||!IsWindowVisible(hwnd)) return false;
    if(p.x<closeRc.l||p.x>=closeRc.r||p.y<closeRc.t||p.y>=closeRc.b) return false; return GetAncestor(WindowFromPoint(p),2)==hwnd; }
  IntPtr MouseHook(int n,IntPtr w,IntPtr l){
    if(n>=0){ int msg=(int)w; var d=(MSLL)Marshal.PtrToStructure(l,typeof(MSLL));
      if(msg==0x201 && IsCloseHit(d.pt)){ swallowUp=true; Log("intercept down "+d.pt.x+","+d.pt.y); return (IntPtr)1; }
      else if(msg==0x201 && haveClose && d.pt.y<closeRc.b+60 && d.pt.x>closeRc.l-300 && d.pt.x<closeRc.r+10 && d.pt.y>closeRc.t-10) Log("pass-through down "+d.pt.x+","+d.pt.y);
      if(msg==0x202 && swallowUp){ swallowUp=false; bool hit=IsCloseHit(d.pt); Log("intercept up "+d.pt.x+","+d.pt.y+" hide="+hit); if(hit) BeginHide(); return (IntPtr)1; } }
    return CallNextHookEx(mHook,n,w,l); }
  IntPtr KeyHook(int n,IntPtr w,IntPtr l){
    if(n>=0 && ((int)w==0x104||(int)w==0x100)){ var k=(KBLL)Marshal.PtrToStructure(l,typeof(KBLL));
      if(k.vk==0x73 && (GetAsyncKeyState(0x12)&0x8000)!=0 && hwnd!=IntPtr.Zero && GetForegroundWindow()==hwnd){ Log("intercept alt+f4"); BeginHide(); return (IntPtr)1; } }
    return CallNextHookEx(kHook,n,w,l); }
  void BeginHide(){ var h=hwnd; timer.Stop(); new System.Windows.Forms.Timer{Interval=1}.With(t=>{ t.Tick+=(s,e)=>{t.Stop(); t.Dispose(); ShowWindow(h,0); Log("hid on close"); timer.Start();}; t.Start(); }); }
  void ShowGrok(){ userQuit=false; hideNew=false; if(hwnd==IntPtr.Zero||!IsWindow(hwnd)) hwnd=FindMain();
    if(hwnd!=IntPtr.Zero){ ShowWindow(hwnd,5); ShowWindow(hwnd,9); SetForegroundWindow(hwnd);} else { try{ lastLaunch=DateTime.Now; Process.Start(ExePath); }catch{} } }
  void QuitGrok(){ userQuit=true; haveClose=false; Log("user quit"); if(hwnd!=IntPtr.Zero){ PostMessage(hwnd,0x10,IntPtr.Zero,IntPtr.Zero); } }
  void ExitHelper(){ if(hwnd!=IntPtr.Zero && IsWindow(hwnd)) ShowWindow(hwnd,5); UnhookWindowsHookEx(mHook); UnhookWindowsHookEx(kHook); tray.Visible=false; tray.Dispose(); Log("helper exit"); ExitThread(); }
  [STAThread] static void Main(){ try{ SetProcessDpiAwarenessContext((IntPtr)(-4)); }catch(EntryPointNotFoundException){} LoadConfig(); bool created; var mx=new Mutex(true,"Local\\BgHelper_"+ProcName.Replace(" ","_"),out created); if(!created) return;
    Application.EnableVisualStyles(); Application.Run(new GrokBotTray()); GC.KeepAlive(mx); }
}
static class Ext{ public static T With<T>(this T o,Action<T> a){ a(o); return o; } }
