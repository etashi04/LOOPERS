using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.ComponentModel;

internal static class Program {
 const string ExeHash="07ba133e2cbc57eda85c184475fb9cb4f6228cc5dd864b9f1693bff4872e870b";
 const string Marker="_LOOPERS_KR_PATCH_v1.0.1";
 sealed class Row { public string Path, Before, After; }
 static List<Row> Rows=new List<Row>();
 static string LogPath;
 static Action<string> Progress;
 static void Log(string message){try{if(LogPath==null){string dir=Path.Combine(Path.GetTempPath(),"LOOPERS_KR_Patch");Directory.CreateDirectory(dir);LogPath=Path.Combine(dir,"install_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N")+".log");}File.AppendAllText(LogPath,DateTime.Now.ToString("o")+" "+message+Environment.NewLine);}catch{}}
 static void Step(string message){Log(message);if(Progress!=null)Progress(message);}
 static string Failure(Exception e){Log(e.ToString());string m=e.Message;if(e is UnauthorizedAccessException)m="게임 폴더에 쓸 권한이 없습니다. 설치기를 관리자 권한으로 다시 실행해 주세요. 읽기 전용 파일도 확인해 주세요.";else if(e is IOException && ((e.HResult&65535)==32 || (e.HResult&65535)==33))m="게임 파일을 다른 프로그램이 사용 중입니다. 게임과 다른 설치기를 종료한 뒤 다시 시도해 주세요.";return m+"\r\n\r\n"+(LogPath==null?"로그 파일을 저장하지 못했습니다.":"진단 로그: "+LogPath);}
 [STAThread] static void Main(string[] args) {
  try {
   using(var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("manifest.tsv"))) {
    string l;while((l=r.ReadLine())!=null){var v=l.Split('\t');Rows.Add(new Row{Path=v[0],Before=v[1],After=v[2]});}
   }
   if(args.Length==2 && (args[0]=="/install" || args[0]=="/restore")) { Run(args[1],args[0]=="/restore");return; }
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Window());
  } catch(Exception e) { string m=Failure(e);if(args.Length==2){Console.Error.WriteLine(m);Environment.ExitCode=1;}else MessageBox.Show(m,"작업 실패"); }
 }
 static string Hash(string p) { if(!File.Exists(p))return "-";using(var s=File.OpenRead(p))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
 static string Safe(string root,string rel) {
  if(Path.IsPathRooted(rel))throw new IOException("잘못된 파일 경로입니다.");
  string full=Path.GetFullPath(Path.Combine(root,rel.Replace('/',Path.DirectorySeparatorChar)));
  if(!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("잘못된 파일 경로입니다.");
  string part=full;while(part.Length>=root.Length){if((File.Exists(part)||Directory.Exists(part)) && (File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("연결된 폴더에는 설치할 수 없습니다.");part=Path.GetDirectoryName(part);if(part==null)break;}
  return full;
 }
 static void Copy(string a,string b){Directory.CreateDirectory(Path.GetDirectoryName(b));File.Copy(a,b,true);}
 static void Expect(string p,string h){string actual=Hash(p);if(actual!=h){Log("Hash mismatch: "+p+" expected="+h+" actual="+actual);throw new IOException("파일이 지원 원본 또는 설치 기록과 다릅니다: "+Path.GetFileName(p)+"\r\n"+(actual=="-"?"필요한 파일이 없습니다. 선택한 게임 폴더와 Steam 무결성 검사를 확인해 주세요.":"이전 패치·수동 패치·게임 업데이트 여부를 확인해 주세요. 이전 자동 패치는 해당 설치기로 복구하고, 수동 패치는 원본 복원 및 추가된 version.dll 제거 후 설치하세요. 백업은 보존해 주세요."));}}
 static void UpgradeRuntime(string root,Row row,string previous){
  Step("기존 v1.0.1의 번역 DLL을 수정본으로 갱신하고 있습니다.");
  string transaction=Safe(root,"_LOOPERS_KR_TEMP_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(transaction);
  string staged=Safe(transaction,"version.new"),undo=Safe(transaction,"version.previous"),target=Safe(root,row.Path);bool replaced=false,success=false;
  try{
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
    var entry=zip.GetEntry("version.dll");if(entry==null)throw new IOException("내장 번역 DLL이 없습니다.");
    using(var a=entry.Open())using(var b=File.Create(staged))a.CopyTo(b);
   }
   Expect(staged,row.After);Expect(target,previous);
   File.Replace(staged,target,undo);replaced=true;Expect(target,row.After);success=true;Step("번역 DLL 갱신 완료");
  }catch{if(replaced){Copy(undo,target);Expect(target,previous);}throw;}
  finally{if(success)try{Directory.Delete(transaction,true);}catch(Exception e){Log("갱신 완료 후 임시 폴더 정리 실패: "+e.Message);}}
 }
 static void Run(string path,bool restore) {
  Step("v1.0.1 installer revision 3 / "+(restore?"복구":"설치")+" / "+path);
  if(string.IsNullOrWhiteSpace(path))throw new IOException("게임 설치 폴더를 선택해 주세요.");
  string root=Path.GetFullPath(path),mark=Safe(root,Marker),backup=Safe(mark,"원본백업"),state=Safe(mark,"완료.txt");
  if(!File.Exists(Safe(root,"LOOPERS.exe")))throw new IOException("선택한 폴더에 LOOPERS.exe가 없습니다. Steam의 관리 → 로컬 파일 탐색으로 게임 폴더를 확인해 주세요.");
  string actualExe=Hash(Safe(root,"LOOPERS.exe"));Log("EXE SHA256="+actualExe);
  if(actualExe!=ExeHash)throw new IOException("지원하는 LOOPERS.exe가 아닙니다. 이 설치기는 Steam 빌드 25823497용입니다. 게임과 패치의 지원 버전을 확인해 주세요. 무결성 검사 후에도 같으면 새 게임 빌드용 패치가 필요할 수 있습니다.");
  if(Process.GetProcessesByName("LOOPERS").Length!=0)throw new IOException("LOOPERS를 종료한 뒤 다시 시도해 주세요.");
  foreach(var row in Rows)Safe(root,row.Path);
  Step("원본 파일과 설치 기록을 검사하고 있습니다.");
  if(restore) {
   if(!File.Exists(state))throw new IOException("완료된 설치 기록이 없습니다. 불완전한 작업 폴더는 임의로 삭제하지 마세요.");
   foreach(var row in Rows){Expect(Safe(root,row.Path),row.After);Expect(Safe(backup,row.Path),row.Before);}
  } else {
   if(Directory.Exists(mark) && File.Exists(Safe(mark,"복구완료.txt"))) {
    foreach(var row in Rows)Expect(Safe(root,row.Path),row.Before);
    Directory.Move(mark,Safe(root,Marker+"_복구기록_"+Guid.NewGuid().ToString("N")));
   }
   if(Directory.Exists(mark)) {
    if(!File.Exists(state))throw new IOException("이전 작업 기록이 남아 있습니다. 백업 폴더를 보존하고 문의해 주세요.");
    Row upgrade=null;const string PreviousRuntime="7e4a5312536afedafffe81f146774a8c8b4f32cc996e6bfd3ffdfe0c5e49eae5";
    foreach(var row in Rows){if(row.Path=="version.dll" && Hash(Safe(root,row.Path))==PreviousRuntime)upgrade=row;else Expect(Safe(root,row.Path),row.After);Expect(Safe(backup,row.Path),row.Before);}
    if(upgrade!=null)UpgradeRuntime(root,upgrade,PreviousRuntime);return;
   }
   foreach(var row in Rows)Expect(Safe(root,row.Path),row.Before);
  }
  foreach(var row in Rows){string p=Safe(root,row.Path);if(File.Exists(p))using(var probe=new FileStream(p,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} }
  long required=64L*1024*1024;foreach(var row in Rows){string p=Safe(root,row.Path);if(File.Exists(p))required+=new FileInfo(p).Length*(restore?1:2);}
  if(!restore)using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){foreach(var e in zip.Entries)required+=e.Length;}
  var drive=new DriveInfo(Path.GetPathRoot(root));if(drive.IsReady && drive.AvailableFreeSpace<required)throw new IOException("게임 드라이브의 여유 공간이 부족합니다. 약 "+Math.Ceiling(required/1073741824.0)+" GB 이상 확보한 뒤 다시 시도해 주세요.");
  string transaction=Safe(root,"_LOOPERS_KR_TEMP_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(transaction);Log("Transaction="+transaction);
  var touched=new List<Row>();bool success=false;
  try {
   string stage=Safe(transaction,"stage"),undo=Safe(transaction,"undo");
   if(!restore) {
    Step("패치 파일의 압축을 풀고 검증하고 있습니다.");
    using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)) {
     var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
     foreach(var e in zip.Entries){if(string.IsNullOrEmpty(e.Name))continue;if(!seen.Add(e.FullName))throw new IOException("중복 패치 파일입니다.");string dest=Safe(stage,e.FullName);Directory.CreateDirectory(Path.GetDirectoryName(dest));using(var a=e.Open())using(var b=File.Create(dest))a.CopyTo(b);}
     if(seen.Count!=Rows.Count)throw new IOException("내장 파일 목록이 다릅니다.");
    }
    foreach(var row in Rows)Expect(Safe(stage,row.Path),row.After);
   }
   // Complete and verify every undo/backup copy before changing any target.
   Step("복구에 필요한 원본을 백업하고 있습니다.");
   foreach(var row in Rows) {
    Step("백업 중: "+row.Path);
    string current=Safe(root,row.Path);if(File.Exists(current))Copy(current,Safe(undo,row.Path));
    Expect(Safe(undo,row.Path),restore?row.After:row.Before);
   }
   if(!restore){Directory.CreateDirectory(backup);foreach(var row in Rows)if(row.Before!="-"){Copy(Safe(undo,row.Path),Safe(backup,row.Path));Expect(Safe(backup,row.Path),row.Before);}}
   foreach(var row in Rows) {
    Step((restore?"복구 중: ":"설치 중: ")+row.Path);
    string target=Safe(root,row.Path);Expect(target,restore?row.After:row.Before);touched.Add(row);
    if(restore && row.Before=="-")File.Delete(target);
    else Copy(Safe(restore?backup:stage,row.Path),target);
    Expect(target,restore?row.Before:row.After);
   }
   if(!restore)File.WriteAllText(state,"LOOPERS 한국어 패치 v1.0.1");
   else File.Move(state,Safe(mark,"복구완료.txt"));
   success=true;
   Step(restore?"원본 복구 완료":"설치 완료");
  } catch {
   bool rollback=true;
   for(int i=touched.Count-1;i>=0;i--){var row=touched[i];try {string target=Safe(root,row.Path),old=Safe(Safe(transaction,"undo"),row.Path);if(File.Exists(old))Copy(old,target);else if(File.Exists(target))File.Delete(target);Expect(target,restore?row.After:row.Before);}catch{rollback=false;}}
   if(!rollback)throw new IOException("복구 중 오류가 발생했습니다. 임시 작업 폴더와 원본백업을 보존하고 문의해 주세요.");
   throw;
  } finally {
   // Keep recovery data on failures; remove only this verified transaction on success.
   if(success)try{Directory.Delete(transaction,true);}catch(Exception cleanup){Log("작업은 완료되었으나 임시 폴더 정리 실패: "+cleanup.Message);}
  }
 }
 sealed class Window:Form {
  TextBox path=new TextBox();Label status=new Label();Button install=new Button(),restore=new Button(),browse;bool busy;
  public Window(){
   Text="LOOPERS 한국어 패치 v1.0.1";ClientSize=new Size(640,285);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(245,245,245);Font=new Font("Malgun Gothic",9);
   using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("InstallerIcon")){if(s!=null)Icon=new Icon(s);}
   var title=new Label{Text="LOOPERS\r\n비공식 한국어 패치",Left=28,Top=22,Width=420,Height=58,Font=new Font("Malgun Gothic",15,FontStyle.Bold)};
   var logoPanel=new Panel{Left=465,Top=15,Width=150,Height=68,BackColor=Color.FromArgb(245,245,245)};
   var logo=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom};
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("InstallerLogo")){if(stream!=null)using(var image=Image.FromStream(stream))logo.Image=new Bitmap(image);}
   logoPanel.Padding=new Padding(4);logoPanel.Controls.Add(logo);
   var info=new Label{Text="지원 게임: Steam 빌드 25823497 · 원본 파일은 자동 백업됩니다.",Left=28,Top=91,Width=585,Height=24};
   var label=new Label{Text="게임 설치 폴더",Left=28,Top=122,Width=180};path.SetBounds(28,148,475,25);
   browse=new Button{Text="찾아보기",Left=513,Top=146,Width=100,Height=29};browse.Click+=delegate{using(var d=new FolderBrowserDialog()){if(d.ShowDialog()==DialogResult.OK)path.Text=d.SelectedPath;}};
   install.Text="한국어 패치 설치";install.SetBounds(28,197,178,38);restore.Text="원본 복구";restore.SetBounds(218,197,128,38);
   install.Click+=delegate{Action(false);};restore.Click+=delegate{Action(true);};status.SetBounds(28,245,585,30);
   Controls.AddRange(new Control[]{title,logoPanel,info,label,path,browse,install,restore,status});
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;MessageBox.Show("설치·복구가 진행 중입니다. 완료될 때까지 기다려 주세요.");}};
  }
  void Action(bool r){if(busy)return;if(MessageBox.Show(path.Text+"\r\n"+(r?"원본으로 복구할까요?":"한국어 패치를 설치할까요?"),"확인",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;string selected=path.Text;busy=true;install.Enabled=restore.Enabled=browse.Enabled=path.Enabled=false;UseWaitCursor=true;status.Text="파일을 확인하고 있습니다.";var worker=new BackgroundWorker();worker.WorkerReportsProgress=true;Progress=delegate(string s){worker.ReportProgress(0,s);};worker.ProgressChanged+=delegate(object sender,ProgressChangedEventArgs e){status.Text=(string)e.UserState;};worker.DoWork+=delegate{Run(selected,r);};worker.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e){Progress=null;busy=false;install.Enabled=restore.Enabled=browse.Enabled=path.Enabled=true;UseWaitCursor=false;if(e.Error!=null){status.Text="작업 실패 — 오류 안내와 로그를 확인해 주세요.";MessageBox.Show(Failure(e.Error),"작업 실패");}else status.Text=r?"원본 복구가 완료되었습니다.":"설치 완료. 게임 언어를 일본어로 설정해 주세요.";worker.Dispose();};worker.RunWorkerAsync();}
 }
}
