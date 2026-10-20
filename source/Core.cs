// ZG1 Control. Parameter mapping adapted from gxcreator/ktmicro-tools (BSD-3-Clause).
// Commit framing from jeromeof/devicePEQ (0BSD). See bundled notices.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Zg1Control {
    public sealed class Snapshot {
        public string Format = "zg1-control/v1";
        public string DeviceId = "31C2:1102";
        public string Firmware;
        public string CapturedAt;
        public uint Flags;
        public Dictionary<string,uint> Registers = new Dictionary<string,uint>();
        public uint Get(int a) { uint v; if(!Registers.TryGetValue(a.ToString("X2"),out v)) throw new InvalidDataException("缺少参数 0x"+a.ToString("X2")); return v; }
        public void Put(int a,uint v) { Registers[a.ToString("X2")]=v; }
        public Snapshot Copy() { return Json.Read(Json.Write(this)); }
    }
    public static class Json {
        public static string Write(object o) { return new JavaScriptSerializer().Serialize(o); }
        public static Snapshot Read(string text) { return new JavaScriptSerializer().Deserialize<Snapshot>(text); }
        public static void Save(string path,object o) { File.WriteAllText(path,Write(o),new UTF8Encoding(true)); }
    }
    public sealed class Field {
        public string Key,Label,Unit,Group;
        public int Address,Shift;
        public uint Mask;
        public decimal Min,Max,Step;
        public int Decimals;
        public string[] Choices;
        public bool Experimental;
        public Func<uint,decimal> Decode;
        public Func<decimal,uint> Encode;
        public decimal Value(Snapshot s) { return Decode(s.Get(Address)); }
        public uint Merge(uint original,decimal value) {
            if(value<Min || value>Max || (value-Min)%Step!=0) throw new ArgumentException(Label+" 超出范围或步进不正确。");
            uint bits=Encode(value);
            if((bits & ~Mask)!=0) throw new ArgumentException(Label+" 编码越界。");
            return (original & ~Mask) | bits;
        }
    }
    public sealed class Change {
        public string Key,Label;
        public decimal Before,After;
        public int Address;
    }
    public sealed class Plan {
        public List<Change> Changes = new List<Change>();
        public SortedDictionary<int,uint> Writes = new SortedDictionary<int,uint>();
    }
    public static class Parameters {
        public static readonly int[] ConfigAddresses = new int[]{0x18,0x1A,0x1B,0x1C,0x1D,0x1E,0x1F,0x20,0x21,0x22,0x23,0x24,0x26,0x27,0x28,0x29,0x2A,0x2B,0x2C,0x2D,0x2E,0x2F,0x3A,0x3B,0x65,0x66,0x71,0x72,0x73,0x78,0x79};
        public static readonly int[] ReadAddresses = new int[]{0xE1,0x5B,0x01,0x04,0x05}.Concat(ConfigAddresses).ToArray();
        public static bool Writable(int a) { return Array.IndexOf(ConfigAddresses,a)>=0; }
        static Field Number(string key,string label,string group,int addr,int shift,int bits,decimal min,decimal max,decimal step,decimal scale,string unit,bool signed,bool threshold) {
            uint part=bits==32 ? uint.MaxValue : (uint)((1UL<<bits)-1);
            uint mask=part<<shift;
            return new Field {Key=key,Label=label,Group=group,Address=addr,Shift=shift,Mask=mask,Min=min,Max=max,Step=step,Decimals=step<0.01m?3:step<0.1m?2:step<1?1:0,Unit=unit,Experimental=group!="play",
                Decode=delegate(uint raw){long n=(raw>>shift)&part; if(threshold)n-=256;else if(signed && (n & (1L<<(bits-1)))!=0)n-=(1L<<bits);return n/scale;},
                Encode=delegate(decimal d){long n=decimal.ToInt64(decimal.Round(d*scale,0,MidpointRounding.AwayFromZero));if(threshold)n+=256;return ((uint)n & part)<<shift;}};
        }
        static Field Choice(string key,string label,string group,int addr,int shift,int bits,string[] choices) {
            Field f=Number(key,label,group,addr,shift,bits,0,choices.Length-1,1,1,"",false,false);f.Choices=choices;return f;
        }
        public static List<Field> Create(bool single) {
            List<Field> list=new List<Field>();
            list.Add(Number("dac.digital","全局数字增益","play",0x66,0,8,-64,63.5m,.5m,2,"dB",true,false));
            if(!single) list.Add(Number("dac.right","右声道数字增益","play",0x66,8,8,-64,63.5m,.5m,2,"dB",true,false));
            string[] pga=new string[16];pga[0]="静音";for(int i=1;i<16;i++)pga[i]=((decimal)(i-1)*1.5m-18).ToString("+0.0;-0.0;0.0",CultureInfo.InvariantCulture)+" dB";
            Field analog=Choice("dac.analog","播放模拟增益","play",0x3B,0,8,pga);analog.Experimental=true;list.Add(analog);
            list.Add(Number("adc.digital","麦克风数字增益","mic",0x65,0,8,-64,63.5m,.5m,2,"dB",true,false));
            list.Add(Choice("adc.analog","麦克风模拟增益","mic",0x3A,0,8,new string[]{"0 dB","−6 dB","+8 dB","+14 dB","+20 dB","+26 dB","+32 dB","+44 dB"}));
            foreach(string channel in new string[]{"dac","adc"}) {
                string group=channel=="dac"?"dacEQ":"adcEQ";
                int en=channel=="dac"?0x24:0x18,b=channel=="dac"?0x26:0x1A;
                list.Add(Choice(channel+".eq","EQ 开关",group,en,0,1,new string[]{"关闭","开启"}));
                for(int i=0;i<5;i++) {
                    string prefix=channel+".band"+i+".",label="第 "+(i+1)+" 段 ";
                    list.Add(Number(prefix+"freq",label+"频率",group,b+i*2,16,16,0,65535,1,1,"Hz",false,false));
                    list.Add(Number(prefix+"gain",label+"增益",group,b+i*2,0,16,-3276.8m,3276.7m,.1m,10,"dB",true,false));
                    list.Add(Number(prefix+"q",label+"Q",group,b+i*2+1,0,16,0,65.535m,.001m,1000,"",false,false));
                    list.Add(Choice(prefix+"type",label+"类型",group,b+i*2+1,16,3,new string[]{"峰值 PK","低通 LPF","高通 HPF","低架 LS","高架 HS"}));
                }
            }
            list.Add(Choice("ng.on","噪声门开关","ng",0x71,31,1,new string[]{"关闭","开启"}));
            list.Add(Number("ng.floor","关门后电平","ng",0x71,16,8,-256,-1,1,1,"dB",false,true));
            list.Add(Number("ng.high","开启阈值","ng",0x71,8,8,-256,-1,1,1,"dB",false,true));
            list.Add(Number("ng.low","关闭阈值","ng",0x71,0,8,-256,-1,1,1,"dB",false,true));
            list.Add(Number("ng.attack","启动时间","ng",0x72,0,16,0,65535,1,1,"ms",false,false));
            list.Add(Number("ng.release","释放时间","ng",0x72,16,16,0,65535,1,1,"ms",false,false));
            list.Add(Number("ng.noise","噪声判定时间","ng",0x73,16,16,0,65535,1,1,"ms",false,false));
            list.Add(Choice("lim.on","限幅器开关","lim",0x78,31,1,new string[]{"关闭","开启"}));
            list.Add(Choice("lim.soft","软拐点","lim",0x78,30,1,new string[]{"关闭","开启"}));
            list.Add(Number("lim.threshold","限幅阈值","lim",0x78,0,8,-256,-1,1,1,"dB",false,true));
            list.Add(Number("lim.attack","启动时间","lim",0x79,0,16,0,65535,1,1,"ms",false,false));
            list.Add(Number("lim.release","释放时间","lim",0x79,16,16,0,65535,1,1,"ms",false,false));
            return list;
        }
        public static string Display(Field f,decimal value) {
            if(f.Choices!=null && value>=0 && value<f.Choices.Length)return f.Choices[(int)value];
            return value.ToString("0.###",CultureInfo.InvariantCulture)+(f.Unit.Length>0?" "+f.Unit:"");
        }
        public static Plan Build(Snapshot original,Dictionary<string,decimal> values) {
            Plan plan=new Plan();
            List<Field> fields=Create((original.Flags&0x200)!=0);
            foreach(string key in values.Keys) if(!fields.Any(f=>f.Key==key)) throw new ArgumentException("不支持的参数："+key);
            foreach(Field f in fields) {
                decimal value;if(!values.TryGetValue(f.Key,out value) || value==f.Value(original))continue;
                if(f.Key.Contains(".band")) {
                    if(f.Key.EndsWith(".freq") && (value<20 || value>20000))throw new ArgumentException(f.Label+" 请输入 20–20000 Hz。");
                    if(f.Key.EndsWith(".gain") && (value< -12 || value>12))throw new ArgumentException(f.Label+" 请输入 −12 至 +12 dB。");
                    if(f.Key.EndsWith(".q") && (value<.05m || value>20))throw new ArgumentException(f.Label+" 请输入 0.05–20。");
                }
                uint prior; if(!plan.Writes.TryGetValue(f.Address,out prior))prior=original.Get(f.Address);
                plan.Writes[f.Address]=f.Merge(prior,value);
                plan.Changes.Add(new Change{Key=f.Key,Label=f.Label,Before=f.Value(original),After=value,Address=f.Address});
            }
            foreach(var pair in plan.Writes.ToArray())if(pair.Value==original.Get(pair.Key))plan.Writes.Remove(pair.Key);
            Snapshot result=original.Copy();foreach(var pair in plan.Writes)result.Put(pair.Key,pair.Value);
            if((result.Get(0x71)&0x80000000)!=0 && plan.Changes.Any(c=>c.Key.StartsWith("ng."))) {
                Field high=fields.First(f=>f.Key=="ng.high"),low=fields.First(f=>f.Key=="ng.low");
                if(high.Value(result)<low.Value(result))throw new ArgumentException("噪声门开启阈值应不低于关闭阈值。");
            }
            return plan;
        }
        public static Snapshot Demo() {
            Snapshot s=new Snapshot{Firmware="V1.0.2",Flags=0x200,CapturedAt=DateTimeOffset.Now.ToString("o")};
            foreach(int a in ReadAddresses)s.Put(a,0);
            s.Put(0xE1,0x12345678);s.Put(0x5B,0x110231C2);s.Put(1,0x200);s.Put(4,0x302E3156);s.Put(5,0x322E);
            s.Put(0x66,0x2E8);s.Put(0x3B,13);s.Put(0x24,3);s.Put(0x18,2);
            int[] freq={60,200,1000,3000,8000};int[] gain={-10,-10,0,10,10};int[] q={900,1100,1000,1000,900};
            for(int i=0;i<5;i++){s.Put(0x26+i*2,((uint)freq[i]<<16)|(ushort)gain[i]);s.Put(0x27+i*2,(uint)q[i]);s.Put(0x1A+i*2,(uint)freq[i]<<16);s.Put(0x1B+i*2,1000);}
            s.Put(0x71,0x3CC4D8CE);s.Put(0x72,0x0064000A);s.Put(0x73,0x0032000A);s.Put(0x78,0x000000FA);s.Put(0x79,0x00640002);
            return s;
        }
    }
    public interface IDevice : IDisposable { uint Read(int address);void Write(int address,uint value);void Commit(); }
    public sealed class MemoryDevice : IDevice {
        public Snapshot State;public int Writes,Commits;public bool BadReadback;
        public MemoryDevice(Snapshot s){State=s.Copy();}
        public uint Read(int a){uint v=State.Get(a);return BadReadback && Writes>0 && a==0x66 ? v^1 : v;}
        public void Write(int a,uint v){if(!Parameters.Writable(a))throw new InvalidOperationException("禁止写入身份或未知寄存器。");Writes++;State.Put(a,v);}
        public void Commit(){Commits++;}
        public void Dispose(){}
    }
    public static class DeviceOps {
        public static Snapshot Read(IDevice d) {
            if(d.Read(0xE1)!=0x12345678 || d.Read(0x5B)!=0x110231C2)throw new InvalidOperationException("设备协议或标识不匹配，已停止。");
            Snapshot s=new Snapshot{CapturedAt=DateTimeOffset.Now.ToString("o")};
            foreach(int a in Parameters.ReadAddresses)s.Put(a,d.Read(a));
            s.Flags=s.Get(1);List<byte> b=new List<byte>();b.AddRange(BitConverter.GetBytes(s.Get(4)));b.AddRange(BitConverter.GetBytes(s.Get(5)));s.Firmware=Encoding.ASCII.GetString(b.ToArray()).TrimEnd('\0');return s;
        }
        public static void ValidateFirmware(Snapshot s) {
            if(s.DeviceId!="31C2:1102" || s.Firmware!="V1.0.2" || (s.Flags&0x200)==0)throw new InvalidOperationException("此版本的写入仅适配已核对的 ZG1 Ultra / V1.0.2。当前仅可查看。");
        }
        public static Snapshot Apply(IDevice d,Snapshot baseline,Plan plan,Action<Snapshot> backup,Action<string> log) {
            ValidateFirmware(baseline);Snapshot fresh=Read(d);ValidateFirmware(fresh);
            if(fresh.Flags!=baseline.Flags)throw new InvalidOperationException("设备控制模式改变，请重新读取。");
            foreach(int a in Parameters.ConfigAddresses)if(fresh.Get(a)!=baseline.Get(a))throw new InvalidOperationException("设备参数已被其他程序改动，请重新读取后再编辑。");
            if(plan.Writes.Count==0)return fresh;
            foreach(var p in plan.Writes)if(!Parameters.Writable(p.Key))throw new InvalidOperationException("禁止写入身份或未知寄存器。");
            backup(fresh); // A failed backup stops BEFORE the first device write.
            foreach(var p in plan.Writes){log("写入 0x"+p.Key.ToString("X2")+" = 0x"+p.Value.ToString("X8"));d.Write(p.Key,p.Value);if(d.Read(p.Key)!=p.Value)throw new InvalidOperationException("回读不一致，已停止。可能已部分生效；未发送保存指令，请重新读取。");}
            Snapshot after=Read(d);
            foreach(int a in Parameters.ConfigAddresses){uint expected; if(!plan.Writes.TryGetValue(a,out expected))expected=fresh.Get(a);if(after.Get(a)!=expected)throw new InvalidOperationException("参数核对不一致，未发送保存指令，请重新读取。");}
            return after;
        }
        public static void Commit(IDevice d,Snapshot expected,Action<Snapshot> backup) {
            ValidateFirmware(expected);Snapshot fresh=Read(d);ValidateFirmware(fresh);
            foreach(int a in Parameters.ConfigAddresses)if(fresh.Get(a)!=expected.Get(a))throw new InvalidOperationException("设备状态已改变，请重新读取；本次未保存。");
            backup(fresh);d.Commit();
        }
    }
    public sealed class NativeDevice : IDevice {
        [StructLayout(LayoutKind.Sequential)]struct Ov {public IntPtr Internal,InternalHigh;public uint Offset,OffsetHigh;public IntPtr Event;}
        [DllImport("hid.dll")]static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern uint CM_Get_Device_Interface_List_SizeW(out uint size,ref Guid g,string id,uint flags);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern uint CM_Get_Device_Interface_ListW(ref Guid g,string id,[Out]byte[] list,uint size,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFileW(string n,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool ReadFile(SafeFileHandle h,IntPtr b,uint size,out uint n,IntPtr ov);
        [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool WriteFile(SafeFileHandle h,IntPtr b,uint size,out uint n,IntPtr ov);
        [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool GetOverlappedResult(SafeFileHandle h,IntPtr ov,out uint n,[MarshalAs(UnmanagedType.Bool)]bool wait);
        [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool CancelIoEx(SafeFileHandle h,IntPtr ov);
        SafeFileHandle handle;Action<string> logger;
        public NativeDevice(Action<string> log) {
            logger=log;
            Zg1MetadataOnly.Result[] meta=Zg1MetadataOnly.Inspect();
            if(meta.Length!=1)throw new InvalidOperationException("未找到唯一的 ZG1 Ultra。请接回这台电脑，并关闭其他调音程序。");
            var m=meta[0];if(m.Error!=null || m.InputBytes!=11 || m.OutputBytes!=11 || m.FeatureBytes!=0)throw new InvalidOperationException("HID 接口格式不匹配。");
            foreach(string dir in new string[]{"Input","Output"})if(m.Items.Count(i=>i.Direction==dir && i.ReportID=="0x4B" && i.UsagePage=="0xFF01" && i.BitSize==8 && i.ReportCount==10)!=1)throw new InvalidOperationException("缺少所需的厂商控制报告。");
            Guid g;HidD_GetHidGuid(out g);uint size;
            if(CM_Get_Device_Interface_List_SizeW(out size,ref g,null,0)!=0 || size>1048576)throw new IOException("无法读取设备列表。");
            byte[] list=new byte[checked((int)size*2)];if(CM_Get_Device_Interface_ListW(ref g,null,list,size,0)!=0)throw new IOException("设备列表改变，请重试。");
            string[] paths=Encoding.Unicode.GetString(list).Split('\0').Where(p=>p.IndexOf("vid_31c2&pid_1102&mi_03",StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            if(paths.Length!=1)throw new IOException("目标设备数量不匹配。");
            handle=CreateFileW(paths[0],0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
            if(handle.IsInvalid){handle.Dispose();throw new IOException("设备无法打开（"+Marshal.GetLastWin32Error()+"）。请关闭其他调音软件。");}
        }
        int Transfer(byte[] b,bool send) {
            if(send){if(b.Length!=11 || b[0]!=0x4B || b[6]!=0)throw new InvalidOperationException("无效报告。");uint a=BitConverter.ToUInt32(b,1),v=BitConverter.ToUInt32(b,7);if(!((b[5]==0x52 && a<=255 && v==0)||(b[5]==0x57 && a<=255 && Parameters.Writable((int)a))||(b[5]==0x53 && a==0 && v==0)))throw new InvalidOperationException("命令被限制。");logger("TX "+BitConverter.ToString(b).Replace('-',' '));}
            using(ManualResetEvent ev=new ManualResetEvent(false)) {
                Ov ov=new Ov();ov.Event=ev.SafeWaitHandle.DangerousGetHandle();IntPtr p=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Ov)));GCHandle pin=GCHandle.Alloc(b,GCHandleType.Pinned);bool pending=false;
                try {Marshal.StructureToPtr(ov,p,false);uint n;bool ok=send?WriteFile(handle,pin.AddrOfPinnedObject(),(uint)b.Length,out n,p):ReadFile(handle,pin.AddrOfPinnedObject(),(uint)b.Length,out n,p);
                    if(!ok){int err=Marshal.GetLastWin32Error();if(err!=997)throw new IOException("USB 连接中断或访问失败（"+err+"）。");pending=true;if(!ev.WaitOne(1200))throw new TimeoutException("设备响应超时。操作已停止，请重新读取。");if(!GetOverlappedResult(handle,p,out n,false))throw new IOException("USB 操作未完成（"+Marshal.GetLastWin32Error()+"）。");pending=false;}
                    if(!send)logger("RX "+BitConverter.ToString(b,0,(int)n).Replace('-',' '));return (int)n;
                }finally{if(pending){CancelIoEx(handle,p);uint ignored;GetOverlappedResult(handle,p,out ignored,true);}pin.Free();Marshal.FreeHGlobal(p);}
            }
        }
        static byte[] Packet(int a,byte command,uint value){byte[] b=new byte[11];b[0]=0x4B;Array.Copy(BitConverter.GetBytes((uint)a),0,b,1,4);b[5]=command;Array.Copy(BitConverter.GetBytes(value),0,b,7,4);return b;}
        public uint Read(int a) {
            if(a<0 || a>255)throw new ArgumentOutOfRangeException("a");
            if(Transfer(Packet(a,0x52,0),true)!=11)throw new IOException("查询发送不完整。");Thread.Sleep(8);
            for(int attempt=0;attempt<12;attempt++){byte[] rx=new byte[11];int n=Transfer(rx,false);if(n==11 && rx[0]==0x4B && rx[6]==0 && rx[5]==0x52 && BitConverter.ToUInt32(rx,1)==a)return BitConverter.ToUInt32(rx,7);if(n>0 && rx[0]==1)continue;if(n==11 && rx[0]==0x4B && rx[5]==0x57 && Parameters.Writable((int)BitConverter.ToUInt32(rx,1)) && BitConverter.ToUInt32(rx,7)==3)continue;throw new IOException("收到不匹配的设备响应，已停止。");}throw new IOException("未收到匹配的查询响应。");
        }
        public void Write(int a,uint v){if(!Parameters.Writable(a))throw new InvalidOperationException("寄存器不可写。");if(Transfer(Packet(a,0x57,v),true)!=11)throw new IOException("参数发送不完整。");Thread.Sleep(15);}
        public void Commit(){if(Transfer(Packet(0,0x53,0),true)!=11)throw new IOException("保存指令未完整发送，请勿自动重试。");}
        public void Dispose(){if(handle!=null)handle.Dispose();}
    }
}
