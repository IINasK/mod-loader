param([ValidateSet('projects','general','header','menus')][string[]]$Suite=@('projects','general'))

$ErrorActionPreference='Stop'
$project=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe=(Resolve-Path -LiteralPath (Join-Path $project 'T/Dev/LoaderDev.exe')).Path
$previousTemp=$env:TEMP
$previousTmp=$env:TMP

try {
    foreach($test in $Suite){
        do { $fixture=Join-Path $project ('T/'+[Guid]::NewGuid().ToString('N').Substring(0,3)) } while(Test-Path -LiteralPath $fixture)
        New-Item -ItemType Directory $fixture | Out-Null
        Copy-Item -LiteralPath (Join-Path $project 'Instalador/Online/FixtureGame.exe') -Destination $fixture
        $env:TEMP=$fixture
        $env:TMP=$fixture
        if(!('DevTestDesktop' -as [type])){
            Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class DevTestDesktop {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct StartupInfo {
        internal uint Size;
        internal string Reserved, Desktop, Title;
        internal uint X, Y, Width, Height, XCount, YCount, Fill, Flags;
        internal ushort ShowWindow, ReservedSize;
        internal IntPtr ReservedBytes, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo {
        internal IntPtr Process, Thread;
        internal uint ProcessId, ThreadId;
    }

    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity,
        IntPtr threadSecurity, bool inheritHandles, uint flags, IntPtr environment, string directory,
        ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr handle, uint code);

    public static uint Run(string exe, string test, string fixture) {
        const uint GenericAll = 0x10000000;
        const uint WaitTimeout = 258;
        string desktopName = "ModLoaderTests-" + Guid.NewGuid().ToString("N");
        IntPtr desktop = CreateDesktop(desktopName, IntPtr.Zero, IntPtr.Zero, 0, GenericAll, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception();
        try {
            var startup = new StartupInfo { Size=(uint)Marshal.SizeOf(typeof(StartupInfo)), Desktop=desktopName };
            var command = new StringBuilder("\"" + exe + "\" --self-test-" + test + " \"" + fixture + "\"");
            ProcessInfo process;
            if (!CreateProcess(exe, command, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, fixture, ref startup, out process))
                throw new Win32Exception();
            try {
                uint wait = WaitForSingleObject(process.Process, 240000);
                if (wait == WaitTimeout) {
                    TerminateProcess(process.Process, 1);
                    WaitForSingleObject(process.Process, 5000);
                    throw new TimeoutException("O processo de teste excedeu o tempo limite.");
                }
                uint code;
                if (wait != 0 || !GetExitCodeProcess(process.Process, out code)) throw new Win32Exception();
                return code;
            } finally {
                CloseHandle(process.Thread);
                CloseHandle(process.Process);
            }
        } finally {
            CloseDesktop(desktop);
        }
    }
}
"@
        }
        $result=[DevTestDesktop]::Run($exe,$test,$fixture)
        Write-Output "$test $result $fixture"
        if($result -ne 0){
            $diagnostic=Join-Path $fixture 'CyberpunkCoopOnline-erro.txt'
            if(Test-Path -LiteralPath $diagnostic){ Get-Content -LiteralPath $diagnostic }
            throw "O teste $test falhou."
        }
    }
} finally {
    $env:TEMP=$previousTemp
    $env:TMP=$previousTmp
}
