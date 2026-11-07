using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal sealed class P2PProcessJob : IDisposable {
    const uint KillOnClose=0x2000, Suspended=0x4, ExtendedStartup=0x80000, UnicodeEnvironment=0x400, NoWindow=0x8000000;
    const int ExtendedLimits=9, JobListAttribute=0x2000D;
    readonly SafeFileHandle job;

    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimits {
        internal long ProcessTime, JobTime;
        internal uint Flags;
        internal UIntPtr MinWorkingSet, MaxWorkingSet;
        internal uint ActiveProcesses;
        internal UIntPtr Affinity;
        internal uint Priority, Scheduling;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimitInformation {
        internal BasicLimits Basic;
        internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
        internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfo {
        internal int Size;
        internal IntPtr Reserved, Desktop, Title;
        internal int X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
        internal short ShowWindow, ReservedLength;
        internal IntPtr ReservedBuffer, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfoEx {
        internal StartupInfo Info;
        internal IntPtr Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation {
        internal IntPtr Process, Thread;
        internal int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll",SetLastError=true)]
    static extern SafeFileHandle CreateJobObjectW(IntPtr security,IntPtr name);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool SetInformationJobObject(SafeFileHandle handle,int information,ref ExtendedLimitInformation limits,int length);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,int flags,ref IntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr attribute,IntPtr value,IntPtr size,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")]
    static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    static extern bool CreateProcessW(string exe,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,uint flags,
        IntPtr environment,string directory,ref StartupInfoEx startup,out ProcessInformation process);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    internal P2PProcessJob(){
        job=CreateJobObjectW(IntPtr.Zero,IntPtr.Zero);
        if(job.IsInvalid)throw new Win32Exception();
        var limits=new ExtendedLimitInformation{Basic=new BasicLimits{Flags=KillOnClose}};
        if(!SetInformationJobObject(job,ExtendedLimits,ref limits,Marshal.SizeOf(typeof(ExtendedLimitInformation)))){
            var failure=new Win32Exception();
            job.Dispose();
            throw failure;
        }
    }

    internal Process Start(ProcessStartInfo start){
        IntPtr length=IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref length);
        if(length==IntPtr.Zero)throw new Win32Exception();
        IntPtr list=Marshal.AllocHGlobal(length), handle=IntPtr.Zero, environment=IntPtr.Zero;
        bool initialized=false;
        try{
            initialized=InitializeProcThreadAttributeList(list,1,0,ref length);
            if(!initialized)throw new Win32Exception();
            handle=Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(handle,job.DangerousGetHandle());
            // Assign before the helper can start its monitor child.
            if(!UpdateProcThreadAttribute(list,0,new IntPtr(JobListAttribute),handle,new IntPtr(IntPtr.Size),IntPtr.Zero,IntPtr.Zero))
                throw new Win32Exception();
            string variables=String.Join("\0",start.EnvironmentVariables.Keys.Cast<string>().OrderBy(key=>key,StringComparer.OrdinalIgnoreCase)
                .Select(key=>key+"="+start.EnvironmentVariables[key]))+"\0";
            environment=Marshal.StringToHGlobalUni(variables);
            var startup=new StartupInfoEx{Info=new StartupInfo{Size=Marshal.SizeOf(typeof(StartupInfoEx))},Attributes=list};
            var command=new StringBuilder("\""+start.FileName+"\" "+start.Arguments);
            ProcessInformation child;
            if(!CreateProcessW(start.FileName,command,IntPtr.Zero,IntPtr.Zero,false,ExtendedStartup|UnicodeEnvironment|NoWindow|Suspended,
                environment,start.WorkingDirectory,ref startup,out child))throw new Win32Exception();
            try{
                var process=Process.GetProcessById(child.ProcessId);
                try{
                    // Retain a handle so the exit code survives a fast startup failure.
                    if(process.Handle==IntPtr.Zero||ResumeThread(child.Thread)==UInt32.MaxValue)throw new Win32Exception();
                    return process;
                }catch{process.Dispose();throw;}
            }
            finally{CloseHandle(child.Thread);CloseHandle(child.Process);}
        }finally{
            if(initialized)DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(environment);
            Marshal.FreeHGlobal(handle);
            Marshal.FreeHGlobal(list);
        }
    }

    public void Dispose(){job.Dispose();}
}
