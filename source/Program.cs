using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Zg1Control {
    static class Program {
        static int count;
        static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;}
        static void MustThrow(Action a,string name){bool thrown=false;try{a();}catch{thrown=true;}Check(thrown,name);}
        static void SelfTest(string path) {
            Snapshot s=Parameters.Demo();List<Field> fields=Parameters.Create(true);
            Check(fields.Select(f=>f.Key).Distinct().Count()==fields.Count,"unique fields");
            Check(fields.First(f=>f.Key=="dac.digital").Value(s)==-12,"verified -12 dB decode");
            Plan p=Parameters.Build(s,new Dictionary<string,decimal>{{"dac.digital",-18}});
            Check(p.Writes.Count==1&&p.Writes[0x66]==0x2DC,"gain only, reserved bytes preserved");
            using(MemoryDevice d=new MemoryDevice(s)){int backups=0;Snapshot after=DeviceOps.Apply(d,s,p,delegate{backups++;},delegate{});Check(backups==1&&d.Writes==1&&d.Commits==0,"apply backup and no auto commit");foreach(int a in Parameters.ConfigAddresses)if(a!=0x66)Check(after.Get(a)==s.Get(a),"gain leaves 0x"+a.ToString("X2"));DeviceOps.Commit(d,after,delegate{backups++;});Check(d.Commits==1&&backups==2,"explicit commit with backup");}
            Snapshot reserved=s.Copy();reserved.Put(0x27,0xA5F80384);p=Parameters.Build(reserved,new Dictionary<string,decimal>{{"dac.band0.q",.707m},{"dac.band0.type",3}});Check(p.Writes[0x27]==0xA5FB02C3,"EQ type/Q preserve unrelated bits");
            p=Parameters.Build(s,new Dictionary<string,decimal>{{"dac.band0.gain",-2.3m},{"dac.band0.freq",77}});Check(p.Writes[0x26]==0x004DFFE9,"signed EQ gain and frequency merge");
            Snapshot n=s.Copy();n.Put(0x71,0x5AC4D8CE);p=Parameters.Build(n,new Dictionary<string,decimal>{{"ng.on",1},{"ng.low",-55}});Check((p.Writes[0x71]&0x7FFFFF00)==(n.Get(0x71)&0x7FFFFF00),"noise gate preserves flags and unchanged thresholds");
            Snapshot lim=s.Copy();lim.Put(0x78,0x153344FA);p=Parameters.Build(lim,new Dictionary<string,decimal>{{"lim.soft",1}});Check(p.Writes[0x78]==0x553344FA,"limiter preserves reserved bits");
            using(MemoryDevice d=new MemoryDevice(s)){d.State.Put(0x3B,12);MustThrow(delegate{DeviceOps.Apply(d,s,Parameters.Build(s,new Dictionary<string,decimal>{{"dac.digital",-18}}),delegate{},delegate{});},"stale-device edit rejected");Check(d.Writes==0,"stale state causes zero writes");}
            using(MemoryDevice d=new MemoryDevice(s)){MustThrow(delegate{DeviceOps.Apply(d,s,Parameters.Build(s,new Dictionary<string,decimal>{{"dac.digital",-18}}),delegate{throw new IOException("disk full");},delegate{});},"backup failure prevents write");Check(d.Writes==0,"failed backup zero writes");}
            using(MemoryDevice d=new MemoryDevice(s)){d.BadReadback=true;MustThrow(delegate{DeviceOps.Apply(d,s,Parameters.Build(s,new Dictionary<string,decimal>{{"dac.digital",-18}}),delegate{},delegate{});},"bad readback stops");Check(d.Commits==0,"bad readback never commits");}
            using(MemoryDevice d=new MemoryDevice(s)){MustThrow(delegate{d.Write(0x5B,0);},"identity write denied");Check(d.Writes==0,"identity protected");}
            MustThrow(delegate{Parameters.Build(s,new Dictionary<string,decimal>{{"dac.digital",-.25m}});},"half dB step required");
            MustThrow(delegate{Parameters.Build(s,new Dictionary<string,decimal>{{"dac.band0.gain",13}});},"EQ gain range");
            MustThrow(delegate{Parameters.Build(s,new Dictionary<string,decimal>{{"dac.band0.freq",5}});},"EQ frequency range");
            MustThrow(delegate{Parameters.Build(s,new Dictionary<string,decimal>{{"dac.band0.q",21}});},"EQ Q range");
            MustThrow(delegate{Parameters.Build(s,new Dictionary<string,decimal>{{"dac.right",-12}});},"single DAC does not expose reserved right byte");
            Check(Parameters.Build(s,new Dictionary<string,decimal>{{"dac.digital",-12}}).Writes.Count==0,"unchanged values never write");
            Check(Json.Read(Json.Write(s)).Get(0x66)==s.Get(0x66),"profile JSON round trip");
            Snapshot other=s.Copy();other.Firmware="V9.9";MustThrow(delegate{DeviceOps.ValidateFirmware(other);},"unknown firmware readonly");
            Snapshot custom=s.Copy();custom.Put(0x66,0xA55A02E8);custom.Put(0x27,0xA5F80384);
            using(MemoryDevice d=new MemoryDevice(custom)){List<Snapshot> backups=new List<Snapshot>();Snapshot restored=DeviceOps.RestoreGain(d,custom,x=>backups.Add(x.Copy()),delegate{});Check(restored.Get(0x66)==0xA55A0200,"restore gain preserves upper three bytes");Check(d.Writes==1&&d.Commits==1&&backups.Count==2,"restore applies once, backs up, then commits once");Check(backups[0].Get(0x66)==custom.Get(0x66),"restore backup retains prior gain");foreach(int a in Parameters.ConfigAddresses)if(a!=0x66)Check(restored.Get(a)==custom.Get(a),"restore preserves custom config 0x"+a.ToString("X2"));}
            using(MemoryDevice d=new MemoryDevice(custom)){d.State.Put(0x26,1);MustThrow(delegate{DeviceOps.RestoreGain(d,custom,delegate{},delegate{});},"restore rejects concurrent EQ edits");Check(d.Writes==0&&d.Commits==0,"stale restore no write or commit");}
            using(MemoryDevice d=new MemoryDevice(custom)){MustThrow(delegate{DeviceOps.RestoreGain(d,custom,delegate{throw new IOException("disk full");},delegate{});},"restore rejects failed initial backup");Check(d.Writes==0&&d.Commits==0,"failed restore backup no write or commit");}
            using(MemoryDevice d=new MemoryDevice(custom)){int backups=0;MustThrow(delegate{DeviceOps.RestoreGain(d,custom,delegate{if(++backups==2)throw new IOException("disk full");},delegate{});},"restore commit backup failure surfaced");Check(d.Writes==1&&d.Commits==0,"commit backup failure leaves applied gain unsaved");}
            using(MemoryDevice d=new MemoryDevice(custom)){d.BadReadback=true;MustThrow(delegate{DeviceOps.RestoreGain(d,custom,delegate{},delegate{});},"restore bad readback rejected");Check(d.Commits==0,"restore bad readback never commits");}
            using(MemoryDevice d=new MemoryDevice(other)){MustThrow(delegate{DeviceOps.RestoreGain(d,other,delegate{},delegate{});},"restore unknown firmware rejected");Check(d.Writes==0&&d.Commits==0,"unknown firmware restore no writes");}
            Snapshot zero=custom.Copy();zero.Put(0x66,0xA55A0200);using(MemoryDevice d=new MemoryDevice(zero)){int backups=0;DeviceOps.RestoreGain(d,zero,delegate{backups++;},delegate{});Check(d.Writes==0&&d.Commits==1&&backups==1,"already zero gain saves once without redundant write");}
            foreach(Field f in fields){uint original=s.Get(f.Address);foreach(decimal v in new decimal[]{f.Min,f.Max}){uint merged=f.Merge(original,v);Snapshot c=s.Copy();c.Put(f.Address,merged);Check(f.Value(c)==v,"encoding round trip "+f.Key+" "+v);Check((merged&~f.Mask)==(original&~f.Mask),"mask preservation "+f.Key);}}
            File.WriteAllText(path,"PASS: "+count+" checks. Simulated device only; no hardware I/O.\r\n",System.Text.Encoding.UTF8);
        }
        [STAThread]static int Main(string[] args) {
            try{
                if(args.Length>0&&args[0]=="--self-test"){SelfTest(args.Length>1?args[1]:"self-test.txt");return 0;}
                if(args.Length>0&&args[0]=="--probe"){List<string> lines=new List<string>();using(IDevice d=new NativeDevice(lines.Add)){Snapshot s=DeviceOps.Read(d);Json.Save(args.Length>1?args[1]:"probe.json",s);}return 0;}
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                if(args.Length>0&&args[0]=="--render"){string path=args.Length>1?args[1]:"preview.png";int page=args.Length>2?int.Parse(args[2]):0;using(MainForm form=new MainForm(true))form.Render(page,path);return 0;}
                bool demo=args.Length>0&&args[0]=="--demo";Application.Run(new MainForm(demo));return 0;
            }catch(Exception ex){if(args.Length>0){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"command-error.txt");try{File.WriteAllText(path,ex.ToString());}catch{}return 1;}MessageBox.Show(ex.Message,"ZG1 Control");return 1;}
        }
    }
}
