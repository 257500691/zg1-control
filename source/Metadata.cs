using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class Zg1MetadataOnly {
    [StructLayout(LayoutKind.Sequential)]
    public struct Attributes {
        public int Size;
        public ushort VendorID, ProductID, VersionNumber;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Caps {
        public ushort Usage, UsagePage, InputReportByteLength;
        public ushort OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    public class Item {
        public string Direction, Kind, ReportID, UsagePage, Usage;
        public int BitSize, ReportCount;
    }
    public class Result {
        public string Interface, VendorID, ProductID, DeviceRelease;
        public string Manufacturer, Product, UsagePage, Usage, Error;
        public int InputBytes, OutputBytes, FeatureBytes;
        public List<Item> Items = new List<Item>();
    }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode)]
    static extern uint CM_Get_Device_Interface_List_SizeW(out uint size, ref Guid guid, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode)]
    static extern uint CM_Get_Device_Interface_ListW(ref Guid guid, string id, [Out] byte[] buffer, uint size, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetManufacturerString(SafeFileHandle handle, [Out] byte[] buffer, uint bytes);
    [DllImport("hid.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetProductString(SafeFileHandle handle, [Out] byte[] buffer, uint bytes);
    [DllImport("hid.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, [Out] byte[] caps, ref ushort count, IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, [Out] byte[] caps, ref ushort count, IntPtr data);

    static string Hex(ushort n) { return "0x" + n.ToString("X4"); }
    static string GetString(SafeFileHandle h, bool product) {
        byte[] b = new byte[512];
        bool ok = product ? HidD_GetProductString(h,b,512) : HidD_GetManufacturerString(h,b,512);
        return ok ? Encoding.Unicode.GetString(b).TrimEnd('\0') : null;
    }
    static void ReadCaps(IntPtr data, int type, ushort count, bool values, List<Item> items) {
        if (count == 0) return;
        byte[] b = new byte[checked(count * 72)];
        int status = values ? HidP_GetValueCaps(type,b,ref count,data) : HidP_GetButtonCaps(type,b,ref count,data);
        if (status != 0x00110000) throw new InvalidOperationException("HID parser status 0x" + status.ToString("X8"));
        string[] directions = {"Input", "Output", "Feature"};
        for (int i=0;i<count;i++) {
            int o=i*72;
            ushort usage=BitConverter.ToUInt16(b,o+56);
            string usageText=Hex(usage);
            if(b[o+12] != 0) usageText += ".." + Hex(BitConverter.ToUInt16(b,o+58));
            items.Add(new Item {
                Direction=directions[type], Kind=values ? "Value" : "Button",
                ReportID="0x" + b[o+2].ToString("X2"),
                UsagePage=Hex(BitConverter.ToUInt16(b,o)), Usage=usageText,
                BitSize=values ? BitConverter.ToUInt16(b,o+18) : 1,
                ReportCount=values ? BitConverter.ToUInt16(b,o+20) : 0
            });
        }
    }
    public static Result[] Inspect() {
        if (Marshal.SizeOf(typeof(Caps)) != 64) throw new InvalidOperationException("Unexpected HIDP_CAPS layout");
        Guid guid; HidD_GetHidGuid(out guid);
        byte[] paths=null;
        for(int attempt=0;attempt<3;attempt++) {
            uint size;
            uint code=CM_Get_Device_Interface_List_SizeW(out size,ref guid,null,0);
            if(code!=0) throw new InvalidOperationException("CM list-size status 0x"+code.ToString("X"));
            if(size>1048576) throw new InvalidOperationException("Unexpected device-list size");
            paths=new byte[checked((int)size*2)];
            code=CM_Get_Device_Interface_ListW(ref guid,null,paths,size,0);
            if(code==0) break;
            if(code!=0x1A || attempt==2) throw new InvalidOperationException("CM list status 0x"+code.ToString("X"));
        }
        List<Result> results=new List<Result>();
        foreach(string path in Encoding.Unicode.GetString(paths).Split('\0')) {
            if(path.IndexOf("vid_31c2&pid_1102",StringComparison.OrdinalIgnoreCase)<0) continue;
            // Do not include serial numbers / full device paths in the report.
            string[] segments=path.Split('#');
            Result r=new Result { Interface=segments.Length>1 ? segments[1] : "31C2:1102" };
            results.Add(r);
            using(SafeFileHandle h=CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                if(h.IsInvalid) { r.Error="Metadata open failed: "+Marshal.GetLastWin32Error(); continue; }
                Attributes a=new Attributes(); a.Size=Marshal.SizeOf(typeof(Attributes));
                if(HidD_GetAttributes(h,ref a)) {
                    r.VendorID=Hex(a.VendorID);r.ProductID=Hex(a.ProductID);r.DeviceRelease=Hex(a.VersionNumber);
                }
                r.Manufacturer=GetString(h,false);r.Product=GetString(h,true);
                IntPtr data;
                if(!HidD_GetPreparsedData(h,out data)) {r.Error="Preparsed data failed: "+Marshal.GetLastWin32Error();continue;}
                try {
                    Caps c;int status=HidP_GetCaps(data,out c);
                    if(status!=0x00110000) {r.Error="HidP_GetCaps status 0x"+status.ToString("X8");continue;}
                    r.UsagePage=Hex(c.UsagePage);r.Usage=Hex(c.Usage);
                    r.InputBytes=c.InputReportByteLength;r.OutputBytes=c.OutputReportByteLength;r.FeatureBytes=c.FeatureReportByteLength;
                    ReadCaps(data,0,c.NumberInputButtonCaps,false,r.Items);ReadCaps(data,0,c.NumberInputValueCaps,true,r.Items);
                    ReadCaps(data,1,c.NumberOutputButtonCaps,false,r.Items);ReadCaps(data,1,c.NumberOutputValueCaps,true,r.Items);
                    ReadCaps(data,2,c.NumberFeatureButtonCaps,false,r.Items);ReadCaps(data,2,c.NumberFeatureValueCaps,true,r.Items);
                } finally {HidD_FreePreparsedData(data);}
            }
        }
        return results.ToArray();
    }
}
