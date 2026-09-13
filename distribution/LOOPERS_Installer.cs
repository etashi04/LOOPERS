using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

internal static class Program {
 const string ExeHash="c9726ffa200840648318bbee2c0ce451c27874b7fb81333170df811eafa5fefd";
 const string Marker="_LOOPERS_KR_PATCH_v1.0.0";
 sealed class Row { public string Path, Before, After; }
 static List<Row> Rows=new List<Row>();
 [STAThread] static void Main(string[] args) {
  try {
   using(var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("manifest.tsv"))) {
    string l;while((l=r.ReadLine())!=null){var v=l.Split('\t');Rows.Add(new Row{Path=v[0],Before=v[1],After=v[2]});}
   }
   if(args.Length==2 && (args[0]=="/install" || args[0]=="/restore")) { Run(args[1],args[0]=="/restore");return; }
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Window());
  } catch(Exception e) { if(args.Length==2){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}else MessageBox.Show(e.Message,"작업 실패"); }
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
 static void Expect(string p,string h){if(Hash(p)!=h)throw new IOException("파일이 지원 원본 또는 설치 기록과 다릅니다: "+Path.GetFileName(p));}
 static void Run(string path,bool restore) {
  if(string.IsNullOrWhiteSpace(path))throw new IOException("게임 설치 폴더를 선택해 주세요.");
  string root=Path.GetFullPath(path),mark=Safe(root,Marker),backup=Safe(mark,"원본백업"),state=Safe(mark,"완료.txt");
  Expect(Safe(root,"LOOPERS.exe"),ExeHash);
  if(Process.GetProcessesByName("LOOPERS").Length!=0)throw new IOException("LOOPERS를 종료한 뒤 다시 시도해 주세요.");
  foreach(var row in Rows)Safe(root,row.Path);
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
    foreach(var row in Rows){Expect(Safe(root,row.Path),row.After);Expect(Safe(backup,row.Path),row.Before);}return;
   }
   foreach(var row in Rows)Expect(Safe(root,row.Path),row.Before);
  }
  string transaction=Safe(root,"_LOOPERS_KR_TEMP_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(transaction);
  var touched=new List<Row>();bool success=false;
  try {
   string stage=Safe(transaction,"stage"),undo=Safe(transaction,"undo");
   if(!restore) {
    using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)) {
     var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
     foreach(var e in zip.Entries){if(string.IsNullOrEmpty(e.Name))continue;if(!seen.Add(e.FullName))throw new IOException("중복 패치 파일입니다.");string dest=Safe(stage,e.FullName);Directory.CreateDirectory(Path.GetDirectoryName(dest));using(var a=e.Open())using(var b=File.Create(dest))a.CopyTo(b);}
     if(seen.Count!=Rows.Count)throw new IOException("내장 파일 목록이 다릅니다.");
    }
    foreach(var row in Rows)Expect(Safe(stage,row.Path),row.After);
   }
   // Complete and verify every undo/backup copy before changing any target.
   foreach(var row in Rows) {
    string current=Safe(root,row.Path);if(File.Exists(current))Copy(current,Safe(undo,row.Path));
    Expect(Safe(undo,row.Path),restore?row.After:row.Before);
   }
   if(!restore){Directory.CreateDirectory(backup);foreach(var row in Rows)if(row.Before!="-"){Copy(Safe(undo,row.Path),Safe(backup,row.Path));Expect(Safe(backup,row.Path),row.Before);}}
   foreach(var row in Rows) {
    string target=Safe(root,row.Path);Expect(target,restore?row.After:row.Before);touched.Add(row);
    if(restore && row.Before=="-")File.Delete(target);
    else Copy(Safe(restore?backup:stage,row.Path),target);
    Expect(target,restore?row.Before:row.After);
   }
   if(!restore)File.WriteAllText(state,"LOOPERS 한국어 패치 v1.0.0");
   else File.Move(state,Safe(mark,"복구완료.txt"));
   success=true;
  } catch {
   bool rollback=true;
   for(int i=touched.Count-1;i>=0;i--){var row=touched[i];try {string target=Safe(root,row.Path),old=Safe(Safe(transaction,"undo"),row.Path);if(File.Exists(old))Copy(old,target);else if(File.Exists(target))File.Delete(target);Expect(target,restore?row.After:row.Before);}catch{rollback=false;}}
   if(!rollback)throw new IOException("복구 중 오류가 발생했습니다. 임시 작업 폴더와 원본백업을 보존하고 문의해 주세요.");
   throw;
  } finally {
   // Keep recovery data on failures; remove only this verified transaction on success.
   if(success)Directory.Delete(transaction,true);
  }
 }
 sealed class Window:Form {
  TextBox path=new TextBox();Label status=new Label();Button install=new Button(),restore=new Button();
  public Window(){
   Text="LOOPERS 한국어 패치 v1.0.0";ClientSize=new Size(640,285);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(245,245,245);Font=new Font("Malgun Gothic",9);
   using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("InstallerIcon")){if(s!=null)Icon=new Icon(s);}
   var title=new Label{Text="LOOPERS\r\n비공식 한국어 패치",Left=28,Top=22,Width=420,Height=58,Font=new Font("Malgun Gothic",15,FontStyle.Bold)};
   var logoPanel=new Panel{Left=465,Top=15,Width=150,Height=68,BackColor=Color.FromArgb(245,245,245)};
   var logo=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom};
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("InstallerLogo")){if(stream!=null)using(var image=Image.FromStream(stream))logo.Image=new Bitmap(image);}
   logoPanel.Padding=new Padding(4);logoPanel.Controls.Add(logo);
   var info=new Label{Text="지원 게임: Steam 빌드 14841524 · 원본 파일은 자동 백업됩니다.",Left=28,Top=91,Width=585,Height=24};
   var label=new Label{Text="게임 설치 폴더",Left=28,Top=122,Width=180};path.SetBounds(28,148,475,25);
   var browse=new Button{Text="찾아보기",Left=513,Top=146,Width=100,Height=29};browse.Click+=delegate{using(var d=new FolderBrowserDialog()){if(d.ShowDialog()==DialogResult.OK)path.Text=d.SelectedPath;}};
   install.Text="한국어 패치 설치";install.SetBounds(28,197,178,38);restore.Text="원본 복구";restore.SetBounds(218,197,128,38);
   install.Click+=delegate{Action(false);};restore.Click+=delegate{Action(true);};status.SetBounds(28,245,585,30);
   Controls.AddRange(new Control[]{title,logoPanel,info,label,path,browse,install,restore,status});
  }
  void Action(bool r){if(MessageBox.Show(path.Text+"\r\n"+(r?"원본으로 복구할까요?":"한국어 패치를 설치할까요?"),"확인",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;install.Enabled=restore.Enabled=false;UseWaitCursor=true;status.Text="처리 중입니다. 창을 닫지 마세요.";Refresh();try{Run(path.Text,r);status.Text=r?"원본 복구가 완료되었습니다.":"한국어 패치 설치가 완료되었습니다.";}catch(Exception e){status.Text="작업 실패";MessageBox.Show(e.Message,"작업 실패");}finally{install.Enabled=restore.Enabled=true;UseWaitCursor=false;}}
 }
}
