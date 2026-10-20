using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zg1Control {
    public sealed class EqGraph : Control {
        public Snapshot State;public Dictionary<string,decimal> Values;public string Channel="dac";
        public EqGraph(){DoubleBuffered=true;Height=166;Dock=DockStyle.Top;BackColor=Color.FromArgb(247,250,249);}
        decimal V(string k){decimal v;if(Values!=null && Values.TryGetValue(k,out v))return v;var f=Parameters.Create(true).First(x=>x.Key==k);return State==null?0:f.Value(State);}
        // Standard RBJ display preview at 48 kHz; not a hardware measurement.
        double Magnitude(int i,double hz) {
            string p=Channel+".band"+i+".";double f=(double)V(p+"freq"),g=(double)V(p+"gain"),q=(double)V(p+"q");int t=(int)V(p+"type");
            if(f<=0 || f>=24000 || q<=0 || t<0 || t>4)return 0;
            double a=Math.Pow(10,g/40),w=2*Math.PI*f/48000,c=Math.Cos(w),alpha=Math.Sin(w)/(2*q),beta=2*Math.Sqrt(a)*alpha;
            double b0,b1,b2,a0,a1,a2;
            if(t==0){b0=1+alpha*a;b1=-2*c;b2=1-alpha*a;a0=1+alpha/a;a1=-2*c;a2=1-alpha/a;}
            else if(t==1){b0=(1-c)/2;b1=1-c;b2=b0;a0=1+alpha;a1=-2*c;a2=1-alpha;}
            else if(t==2){b0=(1+c)/2;b1=-(1+c);b2=b0;a0=1+alpha;a1=-2*c;a2=1-alpha;}
            else if(t==3){b0=a*((a+1)-(a-1)*c+beta);b1=2*a*((a-1)-(a+1)*c);b2=a*((a+1)-(a-1)*c-beta);a0=(a+1)+(a-1)*c+beta;a1=-2*((a-1)+(a+1)*c);a2=(a+1)+(a-1)*c-beta;}
            else {b0=a*((a+1)+(a-1)*c+beta);b1=-2*a*((a-1)+(a+1)*c);b2=a*((a+1)+(a-1)*c-beta);a0=(a+1)-(a-1)*c+beta;a1=2*((a-1)-(a+1)*c);a2=(a+1)-(a-1)*c-beta;}
            double z=2*Math.PI*hz/48000,x=Math.Cos(z),y=-Math.Sin(z),xx=Math.Cos(2*z),yy=-Math.Sin(2*z);
            double nr=b0+b1*x+b2*xx,ni=b1*y+b2*yy,dr=a0+a1*x+a2*xx,di=a1*y+a2*yy;
            return 10*Math.Log10(Math.Max(1e-20,(nr*nr+ni*ni)/Math.Max(1e-20,dr*dr+di*di)));
        }
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;Rectangle r=new Rectangle(42,18,Math.Max(10,Width-64),Height-45);
            using(Pen p=new Pen(Color.FromArgb(222,230,227)))using(Brush text=new SolidBrush(Color.FromArgb(112,126,122))){foreach(int db in new int[]{-12,0,12}){float y=r.Top+r.Height*(12-db)/24f;g.DrawLine(p,r.Left,y,r.Right,y);g.DrawString(db.ToString(),Font,text,6,y-8);}foreach(int hz in new int[]{20,100,1000,10000,20000}){float x=r.Left+(float)(Math.Log10(hz/20.0)/3)*r.Width;g.DrawLine(p,x,r.Top,x,r.Bottom);g.DrawString(hz>=1000?(hz/1000)+"k":hz.ToString(),Font,text,x-10,r.Bottom+6);}}
            if(State==null)return;List<PointF> pts=new List<PointF>();bool enabled=V(Channel+".eq")>0;
            for(int x=0;x<=r.Width;x++){double hz=20*Math.Pow(1000,x/(double)r.Width),db=0;if(enabled)for(int i=0;i<5;i++)db+=Magnitude(i,hz);db=Math.Max(-12,Math.Min(12,db));pts.Add(new PointF(r.Left+x,r.Top+(float)((12-db)/24)*r.Height));}
            using(Pen p=new Pen(Color.FromArgb(19,132,113),2.5f))if(pts.Count>1)g.DrawLines(p,pts.ToArray());
        }
    }
    public sealed class MainForm : Form {
        static readonly Color Ink=Color.FromArgb(25,45,48),Muted=Color.FromArgb(103,119,121),Accent=Color.FromArgb(18,126,108),Bg=Color.FromArgb(242,245,243);
        readonly List<Field> fields=Parameters.Create(true);
        readonly Dictionary<string,Control> editors=new Dictionary<string,Control>();
        readonly Dictionary<string,decimal> staged=new Dictionary<string,decimal>();
        readonly List<Panel> pages=new List<Panel>();readonly List<Button> nav=new List<Button>();readonly List<EqGraph> graphs=new List<EqGraph>();
        readonly Panel content=new Panel(),footer=new Panel();readonly TextBox logs=new TextBox();readonly Label status=new Label(),identity=new Label(),pending=new Label(),gainDisplay=new Label();
        readonly Button connect=new Button(),apply=new Button(),save=new Button();readonly DataGridView raw=new DataGridView();
        readonly object logLock=new object();readonly List<string> logLines=new List<string>();
        Snapshot snapshot;MemoryDevice memory;bool demo,loading,busy,unsaved,verified,closing;int activePage;
        string dataRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
        public MainForm(bool demoMode) {
            Text="ZG1 Control · 竹林鸟调音工具";Font=new Font("Microsoft YaHei UI",9F);BackColor=Bg;ForeColor=Ink;ClientSize=new Size(1160,790);MinimumSize=new Size(1020,740);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
            try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
            Panel header=new Panel{Dock=DockStyle.Top,Height=94,BackColor=Ink};Controls.Add(header);
            Label title=new Label{Text="ZG1  /  CONTROL",Font=new Font("Segoe UI",23,FontStyle.Bold),ForeColor=Color.White,AutoSize=true,Location=new Point(25,14)};header.Controls.Add(title);
            identity.Text="竹林鸟 ZG1 Ultra    ·    连接后读取真实参数";identity.ForeColor=Color.FromArgb(181,204,198);identity.AutoSize=true;identity.Location=new Point(28,62);header.Controls.Add(identity);
            connect.Text="连接并读取";StyleButton(connect,true);connect.Size=new Size(128,39);connect.Location=new Point(ClientSize.Width-154,27);connect.Anchor=AnchorStyles.Top|AnchorStyles.Right;connect.Click+=async delegate{await ReadDevice();};header.Controls.Add(connect);
            Button demoButton=new Button{Text="演示",Size=new Size(66,36),Location=new Point(ClientSize.Width-232,29),Anchor=AnchorStyles.Top|AnchorStyles.Right};StyleButton(demoButton,false);demoButton.Click+=delegate{if(!busy&&ConfirmDiscard())LoadDemo();};header.Controls.Add(demoButton);
            footer.Dock=DockStyle.Bottom;footer.Height=92;footer.BackColor=Color.White;Controls.Add(footer);
            status.SetBounds(24,12,600,23);status.ForeColor=Muted;footer.Controls.Add(status);pending.SetBounds(24,43,570,24);pending.Font=new Font(Font,FontStyle.Bold);pending.ForeColor=Accent;footer.Controls.Add(pending);
            apply.Text="应用修改";StyleButton(apply,true);apply.Size=new Size(126,42);apply.Location=new Point(ClientSize.Width-294,25);apply.Anchor=AnchorStyles.Top|AnchorStyles.Right;apply.Click+=async delegate{await ApplyChanges();};footer.Controls.Add(apply);
            save.Text="保存到耳机";StyleButton(save,false);save.Size=new Size(126,42);save.Location=new Point(ClientSize.Width-154,25);save.Anchor=AnchorStyles.Top|AnchorStyles.Right;save.Click+=async delegate{await SaveDevice();};footer.Controls.Add(save);
            Panel body=new Panel{Dock=DockStyle.Fill};Controls.Add(body);body.BringToFront();
            Panel sidebar=new Panel{Dock=DockStyle.Left,Width=174,Padding=new Padding(13,22,13,15),BackColor=Color.FromArgb(231,238,234)};body.Controls.Add(sidebar);
            content.Dock=DockStyle.Fill;content.Padding=new Padding(25,20,25,20);body.Controls.Add(content);content.BringToFront();
            string[] names={"总览与增益","播放 EQ","麦克风 EQ","动态处理","备份与导入","寄存器与日志"};
            for(int i=0;i<names.Length;i++){int ix=i;Button b=new Button{Text=names[i],Bounds=new Rectangle(13,22+i*53,148,43),TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(12,0,0,0)};StyleButton(b,false);b.Click+=delegate{ShowPage(ix);};sidebar.Controls.Add(b);nav.Add(b);Panel page=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(25,20,25,20),BackColor=Bg,Visible=false};content.Controls.Add(page);pages.Add(page);}
            Label version=new Label{Text="便携版 1.0\n31C2 : 1102\n本地直连 · 无需联网",ForeColor=Muted,AutoSize=true,Location=new Point(24,body.Height-105),Anchor=AnchorStyles.Left|AnchorStyles.Bottom};sidebar.Controls.Add(version);
            body.PerformLayout();content.PerformLayout();foreach(Panel page in pages)page.Size=new Size(content.ClientSize.Width-50,content.ClientSize.Height-40);
            BuildOverview();BuildEq(1,"dac");BuildEq(2,"adc");BuildDynamics();BuildBackup();BuildRaw();ShowPage(0);
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;return;}if(!closing && !ConfirmDiscard())e.Cancel=true;};
            status.Text="尚未连接。打开工具和编辑控件都不会自动写入。";UpdateState();if(demoMode)LoadDemo();
        }
        void StyleButton(Button b,bool primary){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=primary?Accent:Color.FromArgb(201,215,208);b.FlatAppearance.BorderSize=primary?0:1;b.BackColor=primary?Accent:Color.White;b.ForeColor=primary?Color.White:Ink;b.Cursor=Cursors.Hand;b.UseVisualStyleBackColor=false;}
        Label LabelAt(Control parent,string text,int x,int y,int width,int height,float size,bool bold){Label l=new Label{Text=text,Bounds=new Rectangle(x,y,width,height),Font=new Font(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=bold?Ink:Muted};parent.Controls.Add(l);return l;}
        Panel Card(Panel page,string title,string subtitle,int y,int height){Panel card=new Panel{BackColor=Color.White,Bounds=new Rectangle(0,y,870,height),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};page.Controls.Add(card);card.Width=Math.Max(700,page.ClientSize.Width-50);LabelAt(card,title,20,16,720,29,13,true);if(subtitle!=null)LabelAt(card,subtitle,20,48,card.Width-40,40,9,false);return card;}
        void Heading(Panel page,string title,string subtitle){LabelAt(page,title,0,0,800,44,18,true);LabelAt(page,subtitle,1,46,820,38,9,false);}
        Control Editor(Field field,Control parent,int x,int y,int width) {
            Control c;
            if(field.Choices!=null){ComboBox combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat};combo.Items.AddRange(field.Choices);combo.SelectedIndexChanged+=delegate{if(!loading && combo.SelectedIndex>=0){staged[field.Key]=combo.SelectedIndex;Changed();}};c=combo;}
            else {NumericUpDown num=new NumericUpDown{Minimum=field.Min,Maximum=field.Max,Increment=field.Step,DecimalPlaces=field.Decimals,ThousandsSeparator=false,TextAlign=HorizontalAlignment.Right};num.ValueChanged+=delegate{if(!loading){staged[field.Key]=num.Value;Changed();}};c=num;}
            c.Bounds=new Rectangle(x,y,width,31);c.Font=new Font(Font.FontFamily,10);c.Enabled=false;parent.Controls.Add(c);editors.Add(field.Key,c);return c;
        }
        Field F(string key){return fields.First(f=>f.Key==key);}
        void Row(Control card,string key,int y){Field f=F(key);LabelAt(card,f.Label,22,y+4,195,27,10,false);Editor(f,card,230,y,174);LabelAt(card,f.Unit,414,y+5,45,24,9,false);}
        void BuildOverview(){Panel p=pages[0];Heading(p,"音量，留出更多余地","整体数字增益独立于五段 EQ。修改先暂存，点击“应用修改”才会生效。");
            Panel card=Card(p,"全局数字增益","已在这副耳机上验证：降低整体播放电平，保留原有 EQ。",88,227);
            gainDisplay.Text="— dB";gainDisplay.SetBounds(23,92,285,60);gainDisplay.Font=new Font("Segoe UI",32,FontStyle.Bold);gainDisplay.ForeColor=Accent;card.Controls.Add(gainDisplay);
            Editor(F("dac.digital"),card,320,112,155);LabelAt(card,"dB  /  0.5 dB 步进",487,118,220,25,9,false);
            int x=23;foreach(decimal db in new decimal[]{-24,-18,-12,-6,0}){decimal value=db;Button b=new Button{Text=db+" dB",Bounds=new Rectangle(x,174,94,33)};StyleButton(b,db==-12);b.Click+=delegate{if(snapshot!=null && !busy){((NumericUpDown)editors["dac.digital"]).Value=value;}};card.Controls.Add(b);x+=106;}
            Panel analog=Card(p,"模拟增益与设备状态","模拟 PGA 使用芯片的离散档位；正数字增益会减少削波余量。",331,165);Row(analog,"dac.analog",98);
            Panel note=Card(p,"你的工作方式","① 连接并读取    →    ② 修改参数    →    ③ 应用并回读    →    ④ 保存到耳机",512,132);LabelAt(note,"每次写入前自动备份。保存后拔插验证；不会自动更换驱动或刷固件。",21,88,780,27,9,false);
        }
        void BuildEq(int index,string channel){Panel p=pages[index];bool dac=channel=="dac";Heading(p,dac?"播放五段参数均衡":"麦克风五段参数均衡",dac?"保留你的音色，只在需要时修改某一段。下图为标准滤波器预览，不是实测频响。":"开源协议提供的麦克风通道参数；这副耳机尚未逐项验证麦克风与滤波器效果。");
            int gridY=88;
            if(!dac){Panel mic=Card(p,"麦克风增益","模拟与数字增益分别调节。",88,186);Row(mic,"adc.digital",83);Row(mic,"adc.analog",130);gridY=290;}
            Panel card=Card(p,dac?"播放 EQ":"麦克风 EQ","编辑频率 20–20000 Hz，增益 −12 至 +12 dB，Q 为 0.05–20。",gridY,355);
            LabelAt(card,"EQ",22,91,65,25,10,true);Editor(F(channel+".eq"),card,82,85,135);
            string[] headers={"段","滤波类型","频率 / Hz","增益 / dB","Q"};int[] xs={23,70,257,425,590};for(int i=0;i<5;i++)LabelAt(card,headers[i],xs[i],135,170,26,9,true);
            for(int i=0;i<5;i++){int y=169+i*34;string pre=channel+".band"+i+".";LabelAt(card,(i+1).ToString("00"),25,y+4,35,25,10,true);Editor(F(pre+"type"),card,70,y,170);Editor(F(pre+"freq"),card,257,y,150);Editor(F(pre+"gain"),card,425,y,145);Editor(F(pre+"q"),card,590,y,145);}
            Panel graphCard=Card(p,"曲线预览 · 48 kHz","",gridY+371,218);EqGraph graph=new EqGraph{Channel=channel,Bounds=new Rectangle(13,51,graphCard.Width-26,154),Dock=DockStyle.None,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};graphCard.Controls.Add(graph);graphs.Add(graph);
        }
        void BuildDynamics(){Panel p=pages[3];Heading(p,"动态处理","噪声门和限幅器来自开源参数映射，未在本机逐项听测；默认保持读取到的状态。");
            Panel ng=Card(p,"噪声门","用于压低低于阈值的信号；不等同于恒定的全局衰减。",88,390);int y=86;foreach(Field f in fields.Where(f=>f.Group=="ng")){Row(ng,f.Key,y);y+=41;}
            Panel lim=Card(p,"限幅器","限制超过阈值的峰值。",494,309);y=86;foreach(Field f in fields.Where(f=>f.Group=="lim")){Row(lim,f.Key,y);y+=41;}
        }
        Button ActionButton(Control p,string text,int x,int y,int width,Action action){Button b=new Button{Text=text,Bounds=new Rectangle(x,y,width,38)};StyleButton(b,false);b.Click+=delegate{if(!busy)action();};p.Controls.Add(b);return b;}
        void BuildBackup(){Panel p=pages[4];Heading(p,"备份与导入","导入只填入编辑区，仍需点击“应用修改”；不会打开文件后立即写入耳机。");
            Panel c=Card(p,"配置文件","JSON 保存当前已读取的参数、设备标识与版本。只适用于同一型号和固件。",88,200);
            ActionButton(c,"导出当前设备配置",22,106,190,Export);ActionButton(c,"导入配置到编辑区",228,106,190,Import);ActionButton(c,"打开备份文件夹",434,106,178,OpenData);
            Panel help=Card(p,"自动备份与操作记录","每次应用或保存之前，先将当时的设备参数写入 data 文件夹。",304,195);
            LabelAt(help,"应用：即时生效，并回读核对；未改动的参数不会重写。\n保存：把已经应用的当前配置提交到耳机，可能短暂断开 USB。\n导出文件覆盖工具已知的音频参数，不是固件备份。",23,91,790,88,10,false);
            Panel sources=Card(p,"开源来源","参数：gxcreator / ktmicro-tools    ·    保存：jeromeof / devicePEQ",515,122);LabelAt(sources,"原项目许可证、协议说明和本工具源代码随压缩包附带。",23,83,790,27,9,false);
        }
        void BuildRaw(){Panel p=pages[5];Heading(p,"寄存器与操作日志","用于核对与排查。此页不提供任意寄存器写入，不修改设备标识。");
            raw.SetBounds(0,88,850,253);raw.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;raw.ReadOnly=true;raw.AllowUserToAddRows=false;raw.AllowUserToDeleteRows=false;raw.RowHeadersVisible=false;raw.BackgroundColor=Color.White;raw.BorderStyle=BorderStyle.None;raw.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;raw.Columns.Add("address","地址");raw.Columns.Add("raw","原始值");raw.Columns.Add("meaning","用途");p.Controls.Add(raw);
            LabelAt(p,"本次操作日志",1,359,700,28,13,true);logs.Multiline=true;logs.ReadOnly=true;logs.ScrollBars=ScrollBars.Both;logs.WordWrap=false;logs.Font=new Font("Consolas",9);logs.BackColor=Ink;logs.ForeColor=Color.FromArgb(210,232,222);logs.SetBounds(0,399,850,225);logs.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;p.Controls.Add(logs);
            ActionButton(p,"导出日志",1,641,140,delegate{using(SaveFileDialog d=new SaveFileDialog{Filter="文本文件|*.txt",FileName="ZG1-log.txt"})if(d.ShowDialog(this)==DialogResult.OK){lock(logLock)File.WriteAllLines(d.FileName,logLines.ToArray(),Encoding.UTF8);}});
        }
        void OpenData(){try{Directory.CreateDirectory(dataRoot);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{FileName=dataRoot,UseShellExecute=true});}catch(Exception ex){Error(ex);}}
        void ShowPage(int index){activePage=index;for(int i=0;i<pages.Count;i++){pages[i].Visible=i==index;nav[i].BackColor=i==index?Accent:Color.White;nav[i].ForeColor=i==index?Color.White:Ink;}pages[index].BringToFront();}
        bool ConfirmDiscard(){if(snapshot!=null && (HasChanges()||unsaved))return MessageBox.Show(this,HasChanges()?"编辑区仍有未应用的修改。继续将丢弃这些编辑？":"有已应用但未保存到耳机的修改。继续？","保留当前修改",MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes;return true;}
        bool HasChanges(){return snapshot!=null && fields.Any(f=>staged.ContainsKey(f.Key)&&staged[f.Key]!=f.Value(snapshot));}
        void LoadDemo(){demo=true;memory=new MemoryDevice(Parameters.Demo());snapshot=memory.State.Copy();unsaved=false;LoadControls();Log("已进入演示模式；所有操作只作用于内存。");status.Text="演示模式 · 不连接或写入真实耳机";}
        void LoadControls(){loading=true;verified=snapshot!=null && snapshot.Firmware=="V1.0.2" && (snapshot.Flags&0x200)!=0;staged.Clear();
            if(snapshot!=null){foreach(Field f in fields){decimal value=f.Value(snapshot);Control c=editors[f.Key];NumericUpDown n=c as NumericUpDown;ComboBox combo=c as ComboBox;if(n!=null)n.Value=Math.Max(n.Minimum,Math.Min(n.Maximum,value));else if(combo!=null)combo.SelectedIndex=value>=0 && value<combo.Items.Count?(int)value:-1;staged[f.Key]=value;}
                identity.Text=(demo?"演示 · ":"")+"ZG1 Ultra    ·    "+snapshot.Firmware+"    ·    "+snapshot.DeviceId;raw.Rows.Clear();foreach(var p in snapshot.Registers.OrderBy(x=>Convert.ToInt32(x.Key,16))){int address=Convert.ToInt32(p.Key,16);string meaning=string.Join(" / ",fields.Where(f=>f.Address==address).Select(f=>f.Label).Distinct().ToArray());raw.Rows.Add("0x"+p.Key,"0x"+p.Value.ToString("X8"),meaning.Length==0?"设备信息":meaning);}}
            loading=false;Changed();}
        void Changed(){if(snapshot!=null){decimal db=staged["dac.digital"];gainDisplay.Text=db.ToString("0.0",CultureInfo.InvariantCulture)+" dB";foreach(EqGraph graph in graphs){graph.State=snapshot;graph.Values=staged;graph.Invalidate();}}UpdateState();}
        void UpdateState(){bool ready=snapshot!=null&&!busy;foreach(var p in editors)p.Value.Enabled=ready&&verified;connect.Enabled=!busy;apply.Enabled=ready&&verified&&HasChanges();save.Enabled=ready&&verified&&!HasChanges();pending.Text=busy?"正在操作设备，请稍候…":snapshot==null?"连接后加载参数":HasChanges()?"有待应用的修改 · 尚未写入耳机":unsaved?"已应用并核对 · 尚未保存到耳机":demo?"演示数据 · 不影响真实耳机":"设备参数已读取 · 编辑不会自动生效";connect.Text=busy?"操作中…":"连接并读取";}
        void Log(string line){lock(logLock){logLines.Add(DateTime.Now.ToString("HH:mm:ss.fff")+"  "+line);} }
        void FlushLog(){lock(logLock)logs.Lines=logLines.ToArray();logs.SelectionStart=logs.TextLength;logs.ScrollToCaret();}
        IDevice Open(){return demo?(IDevice)memory:new NativeDevice(Log);}
        void Backup(Snapshot s){string folder=Path.Combine(dataRoot,demo?"demo-backups":"backups");Directory.CreateDirectory(folder);Json.Save(Path.Combine(folder,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,5)+".json"),s);}
        void SaveLog(){try{Directory.CreateDirectory(dataRoot);lock(logLock)File.WriteAllLines(Path.Combine(dataRoot,"last-session.log"),logLines.ToArray(),Encoding.UTF8);}catch{}}
        async Task Run(string message,Func<Snapshot> operation,Action<Snapshot> done){busy=true;status.Text=message;UpdateState();try{Snapshot result=await Task.Run(operation);done(result);}catch(Exception ex){Log("操作停止："+ex.Message);if(snapshot!=null){verified=false;pending.Text="请重新读取设备状态";}Error(ex);}finally{busy=false;FlushLog();SaveLog();UpdateState();}}
        async Task ReadDevice(){if(!ConfirmDiscard())return;demo=false;await Run("正在读取音频参数；不会写入…",delegate{using(IDevice d=new NativeDevice(Log))return DeviceOps.Read(d);},delegate(Snapshot s){snapshot=s;unsaved=false;LoadControls();status.Text=verified?"已读取 "+s.Registers.Count+" 项寄存器。修改前请先暂停播放。":"已读取，但当前固件尚未适配写入。";});}
        async Task ApplyChanges(){if(snapshot==null)return;try{ValidateChildren();Plan plan=Parameters.Build(snapshot,staged);if(plan.Writes.Count==0)return;
                string details=string.Join("\r\n",plan.Changes.Select(c=>{Field f=F(c.Key);return c.Label+"："+Parameters.Display(f,c.Before)+" → "+Parameters.Display(f,c.After);}).ToArray());
                using(Form dialog=new Form{Text=demo?"演示：应用修改":"应用以下修改",Size=new Size(650,480),StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false,Font=Font}){TextBox preview=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Text=details,BackColor=Color.White,Font=new Font(Font.FontFamily,10)};dialog.Controls.Add(preview);FlowLayoutPanel buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=58,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(10)};Button ok=new Button{Text="应用并回读",DialogResult=DialogResult.OK,Size=new Size(140,34)},cancel=new Button{Text="返回编辑",DialogResult=DialogResult.Cancel,Size=new Size(110,34)};StyleButton(ok,true);StyleButton(cancel,false);buttons.Controls.Add(ok);buttons.Controls.Add(cancel);dialog.Controls.Add(buttons);dialog.AcceptButton=ok;dialog.CancelButton=cancel;if(dialog.ShowDialog(this)!=DialogResult.OK)return;}
                Snapshot baseline=snapshot;await Run("正在备份、应用并回读核对…",delegate{using(IDevice d=Open())return DeviceOps.Apply(d,baseline,plan,Backup,Log);},delegate(Snapshot result){snapshot=result;unsaved=true;LoadControls();status.Text="已应用 "+plan.Changes.Count+" 项修改，其他已知参数核对一致。需要断电保留时点击保存。";});
            }catch(Exception ex){Error(ex);}}
        async Task SaveDevice(){if(snapshot==null || HasChanges())return;Snapshot expected=snapshot;
            await Run("正在提交当前配置，设备可能短暂重新连接…",delegate{using(IDevice d=Open())DeviceOps.Commit(d,expected,Backup);if(demo)return DeviceOps.Read(memory);System.Threading.Thread.Sleep(1600);Exception last=null;for(int i=0;i<3;i++){try{using(IDevice d=new NativeDevice(Log)){Snapshot after=DeviceOps.Read(d);foreach(int a in Parameters.ConfigAddresses)if(after.Get(a)!=expected.Get(a))throw new IOException("保存后参数改变，请重新读取。");return after;}}catch(Exception ex){last=ex;System.Threading.Thread.Sleep(700);}}throw new IOException("保存指令已发送，但未能重新读取。请勿重复点击保存；拔插后重新读取核对。",last);},delegate(Snapshot s){snapshot=s;unsaved=false;LoadControls();status.Text="保存指令已发送，重新读取一致。拔插一次可验证断电保留。";});}
        void Export(){if(snapshot==null){MessageBox.Show(this,"请先连接并读取设备。","导出配置");return;}try{using(SaveFileDialog d=new SaveFileDialog{Filter="ZG1 配置|*.json",FileName="ZG1-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".json"})if(d.ShowDialog(this)==DialogResult.OK){Json.Save(d.FileName,snapshot);status.Text="已导出当前设备配置。未应用的编辑不包含在此文件。";}}catch(Exception ex){Error(ex);}}
        void Import(){if(snapshot==null){MessageBox.Show(this,"请先连接并读取设备，再导入配置。","导入配置");return;}try{using(OpenFileDialog d=new OpenFileDialog{Filter="ZG1 配置|*.json"})if(d.ShowDialog(this)==DialogResult.OK){FileInfo file=new FileInfo(d.FileName);if(file.Length>1024*1024)throw new InvalidDataException("配置文件过大。");Snapshot incoming=Json.Read(File.ReadAllText(d.FileName));if(incoming==null || incoming.Format!="zg1-control/v1" || incoming.DeviceId!=snapshot.DeviceId || incoming.Firmware!=snapshot.Firmware || incoming.Flags!=snapshot.Flags)throw new InvalidDataException("配置格式、型号、固件或控制模式不匹配。");Dictionary<string,decimal> proposed=new Dictionary<string,decimal>();foreach(Field f in fields)proposed[f.Key]=f.Value(incoming);Parameters.Build(snapshot,proposed);loading=true;try{foreach(Field f in fields){decimal value=proposed[f.Key];if(value<f.Min || value>f.Max)throw new InvalidDataException("配置参数超出范围。");NumericUpDown n=editors[f.Key] as NumericUpDown;if(n!=null)n.Value=value;else ((ComboBox)editors[f.Key]).SelectedIndex=(int)value;staged[f.Key]=value;}}finally{loading=false;}Changed();status.Text="配置已载入编辑区，尚未写入耳机。";}}catch(Exception ex){Error(ex);}}
        void Error(Exception ex){status.Text="已停止："+ex.Message;MessageBox.Show(this,ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        public void Render(int page,string path){LoadDemo();ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);Show();Application.DoEvents();ShowPage(page);Application.DoEvents();File.WriteAllLines(path+".layout.txt",Controls.Cast<Control>().Select(c=>c.GetType().Name+" "+c.Bounds+" dock="+c.Dock+" visible="+c.Visible).ToArray());using(Bitmap image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(Point.Empty,Size));image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}closing=true;Hide();}
    }
}
