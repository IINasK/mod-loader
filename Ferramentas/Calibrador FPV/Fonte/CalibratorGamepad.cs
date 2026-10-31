using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// SDL3: wiki.libsdl.org/SDL3; ViGEm: nefarius/ViGEmClient include/ViGEm/{Client,Common}.h.
// Call from the UI thread. Construction and SelfTest never enumerate or create devices.
public sealed class CalibratorGamepad : IDisposable
{
    private const uint GamepadSubsystem = 0x00002000;
    private const uint VigemSuccess = 0x20000000;
    private readonly string sdlPath;
    private readonly string vigemPath;
    private IntPtr sdlLibrary;
    private IntPtr vigemLibrary;
    private IntPtr physical;
    private IntPtr client;
    private IntPtr target;
    private bool sdlInitialized;
    private bool busConnected;
    private bool targetAdded;
    private bool disposed;

    public CalibratorGamepad(string sdlPath, string vigemPath)
    {
        this.sdlPath = ValidatePath(sdlPath, "SDL3.dll");
        this.vigemPath = ValidatePath(vigemPath, "ViGEmClient.dll");
        PhysicalName = "";
    }

    public string PhysicalName { get; private set; }
    public bool Connected
    {
        get { return !disposed && physical != IntPtr.Zero && Sdl.SDL_GamepadConnected(physical); }
    }
    public bool VirtualConnected
    {
        get { return !disposed && targetAdded; }
    }

    public void OpenPhysical()
    {
        CheckDisposed();
        if (Connected) return;
        ClosePhysical();
        if (sdlLibrary == IntPtr.Zero) sdlLibrary = LoadNative(sdlPath);
        if (!sdlInitialized)
        {
            if (!Sdl.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1"))
                throw SdlError("Não foi possível habilitar a leitura do controle em segundo plano");
            if (!Sdl.SDL_Init(GamepadSubsystem))
                throw SdlError("Não foi possível iniciar a leitura do controle");
            sdlInitialized = true;
        }
        Sdl.SDL_PumpEvents();
        Sdl.SDL_UpdateGamepads();
        int count;
        IntPtr ids = Sdl.SDL_GetJoysticks(out count);
        if (ids == IntPtr.Zero) throw SdlError("Não foi possível listar os controles");
        try
        {
            for (int i = 0; i < count; i++)
            {
                uint id = unchecked((uint)Marshal.ReadInt32(ids, checked(i * 4)));
                if (!Sdl.SDL_IsGamepad(id)) continue;
                IntPtr candidate = Sdl.SDL_OpenGamepad(id);
                if (candidate == IntPtr.Zero) continue;
                bool selected = false;
                try
                {
                    ushort vendor = Sdl.SDL_GetGamepadVendor(candidate);
                    ushort product = Sdl.SDL_GetGamepadProduct(candidate);
                    string name = Utf8(Sdl.SDL_GetGamepadName(candidate));
                    if (!IsDualSense(vendor, product, name)) continue;
                    physical = candidate;
                    PhysicalName = String.IsNullOrEmpty(name) ? "Sony DualSense" : name;
                    selected = true;
                    return;
                }
                finally { if (!selected) Sdl.SDL_CloseGamepad(candidate); }
            }
        }
        finally { Sdl.SDL_free(ids); }
        throw new InvalidOperationException("DualSense físico não encontrado. Conecte o controle Sony por USB ou Bluetooth e tente novamente.");
    }

    public ControlSample ReadPhysical()
    {
        CheckDisposed();
        if (physical == IntPtr.Zero)
            throw new InvalidOperationException("O DualSense físico ainda não foi aberto.");
        Sdl.SDL_PumpEvents();
        Sdl.SDL_UpdateGamepads();
        if (!Connected)
        {
            ClosePhysical();
            DisconnectVirtual();
            throw new InvalidOperationException("O DualSense foi desconectado. Reconecte o controle antes de continuar.");
        }
        ControlSample sample = FromAxes(Sdl.SDL_GetGamepadAxis(physical, 0),
            Sdl.SDL_GetGamepadAxis(physical, 1), Sdl.SDL_GetGamepadAxis(physical, 2),
            Sdl.SDL_GetGamepadAxis(physical, 3));
        uint buttons = 0;
        for (int i = 0; i <= 14; i++)
            if (Sdl.SDL_GetGamepadButton(physical, i)) buttons |= (1u << i);
        sample.Buttons = ConvertButtons(buttons);
        sample.ContinuePressed = (buttons & (1u << 8)) != 0;
        sample.LeftTrigger = ConvertTrigger(Sdl.SDL_GetGamepadAxis(physical, 4));
        sample.RightTrigger = ConvertTrigger(Sdl.SDL_GetGamepadAxis(physical, 5));
        return sample;
    }

    public void ConnectVirtual()
    {
        CheckDisposed();
        if (VirtualConnected) return;
        if (vigemLibrary == IntPtr.Zero) vigemLibrary = LoadNative(vigemPath);
        try
        {
            client = Vigem.vigem_alloc();
            if (client == IntPtr.Zero) throw new InvalidOperationException("Não foi possível reservar o cliente ViGEm.");
            CheckVigem(Vigem.vigem_connect(client), "Conectar ao ViGEmBus");
            busConnected = true;
            target = Vigem.vigem_target_x360_alloc();
            if (target == IntPtr.Zero) throw new InvalidOperationException("Não foi possível reservar o controle Xbox virtual.");
            CheckVigem(Vigem.vigem_target_add(client, target), "Criar o controle Xbox virtual");
            targetAdded = true;
            Send(new ControlSample { Throttle = 0 });
        }
        catch
        {
            DisconnectVirtual();
            throw;
        }
    }

    public void ProbeBusOnly()
    {
        CheckDisposed();
        if (VirtualConnected) throw new InvalidOperationException("Desconecte o controle virtual antes do diagnóstico.");
        if (vigemLibrary == IntPtr.Zero) vigemLibrary = LoadNative(vigemPath);
        IntPtr probe = Vigem.vigem_alloc();
        if (probe == IntPtr.Zero) throw new InvalidOperationException("Não foi possível reservar o diagnóstico ViGEm.");
        bool connected = false;
        try { CheckVigem(Vigem.vigem_connect(probe), "Conferir ViGEmBus"); connected = true; }
        finally { if (connected) Vigem.vigem_disconnect(probe); Vigem.vigem_free(probe); }
    }

    public void Send(ControlSample sample)
    {
        CheckDisposed();
        if (!VirtualConnected) throw new InvalidOperationException("O controle Xbox virtual não está conectado.");
        try { CheckVigem(Vigem.vigem_target_x360_update(client, target, ToReport(sample)), "Atualizar o controle Xbox virtual"); }
        catch
        {
            DisconnectVirtual();
            throw;
        }
    }

    public void DisconnectVirtual()
    {
        // Cleanup must continue even after a device/driver error.
        if (target != IntPtr.Zero)
        {
            if (targetAdded && client != IntPtr.Zero)
            {
                try { Vigem.vigem_target_x360_update(client, target, ToReport(new ControlSample { Throttle = 0 })); } catch { }
                try { Vigem.vigem_target_remove(client, target); } catch { }
            }
            try { Vigem.vigem_target_free(target); } catch { }
            target = IntPtr.Zero;
        }
        targetAdded = false;
        if (client != IntPtr.Zero)
        {
            if (busConnected) { try { Vigem.vigem_disconnect(client); } catch { } }
            try { Vigem.vigem_free(client); } catch { }
            client = IntPtr.Zero;
        }
        busConnected = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        DisconnectVirtual();
        ClosePhysical();
        if (sdlInitialized)
        {
            try { Sdl.SDL_QuitSubSystem(GamepadSubsystem); } catch { }
            sdlInitialized = false;
        }
        if (sdlLibrary != IntPtr.Zero) { try { FreeLibrary(sdlLibrary); } catch { } sdlLibrary = IntPtr.Zero; }
        if (vigemLibrary != IntPtr.Zero) { try { FreeLibrary(vigemLibrary); } catch { } vigemLibrary = IntPtr.Zero; }
        disposed = true;
    }

    private void ClosePhysical()
    {
        if (physical != IntPtr.Zero)
        {
            try { Sdl.SDL_CloseGamepad(physical); } catch { }
            physical = IntPtr.Zero;
        }
        PhysicalName = "";
    }

    private void CheckDisposed()
    {
        if (disposed) throw new ObjectDisposedException("CalibratorGamepad");
    }

    private static bool IsDualSense(ushort vendor, ushort product, string name)
    {
        // Reject the virtual Xbox explicitly, then require Sony PS5 identity.
        if (vendor == 0x045e || vendor != 0x054c) return false;
        return product == 0x0ce6 || product == 0x0df2 ||
            (!String.IsNullOrEmpty(name) && (name.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("PS5", StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private static ControlSample FromAxes(short lx, short ly, short rx, short ry)
    {
        return new ControlSample { Yaw = Normalize(lx), Throttle = (1 - Normalize(ly)) / 2,
            Roll = Normalize(rx), Pitch = -Normalize(ry) };
    }

    private static double Normalize(short value) { return value < 0 ? value / 32768.0 : value / 32767.0; }
    private static byte ConvertTrigger(short value) { return (byte)Math.Round(Math.Max(0, (int)value) * 255.0 / 32767.0); }

    private static ushort ConvertButtons(uint sdlButtons)
    {
        // SDL south/east/west/north/back/guide/start/L3/R3/LB/RB/up/down/left/right.
        ushort[] mapping = { 0x1000, 0x2000, 0x4000, 0x8000, 0x0020, 0x0400, 0x0010,
            0x0040, 0x0080, 0x0100, 0x0200, 0x0001, 0x0002, 0x0004, 0x0008 };
        ushort result = 0;
        for (int i = 0; i < mapping.Length; i++)
            if ((sdlButtons & (1u << i)) != 0) result |= mapping[i];
        return result;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct XusbReport
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }

    private static XusbReport ToReport(ControlSample sample)
    {
        if (sample == null) throw new ArgumentNullException("sample");
        return new XusbReport { Buttons = sample.Buttons,
            LeftTrigger = sample.LeftTrigger, RightTrigger = sample.RightTrigger,
            LeftX = Stick(sample.Yaw), LeftY = Stick(2 * Clamp(sample.Throttle, 0, 1) - 1),
            RightX = Stick(sample.Roll), RightY = Stick(sample.Pitch) };
    }

    private static short Stick(double value) { return (short)Math.Round(Clamp(value, -1, 1) * 32767); }
    private static double Clamp(double value, double min, double max)
    {
        if (Double.IsNaN(value) || Double.IsInfinity(value))
            throw new ArgumentException("A amostra do controle contém um valor inválido.");
        return Math.Max(min, Math.Min(max, value));
    }

    private static InvalidOperationException SdlError(string operation)
    {
        return new InvalidOperationException(operation + ": " + Utf8(Sdl.SDL_GetError()));
    }

    private static string Utf8(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return "";
        int length = 0;
        while (Marshal.ReadByte(pointer, length) != 0) length++;
        byte[] bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string ValidatePath(string path, string fileName)
    {
        if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ||
            !String.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Informe o caminho absoluto de " + fileName + ".");
        return Path.GetFullPath(path);
    }

    private static IntPtr LoadNative(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Biblioteca necessária não encontrada: " + Path.GetFileName(path), path);
        IntPtr library = LoadLibraryEx(path, IntPtr.Zero, 0x00000100 | 0x00001000);
        if (library == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException("Não foi possível carregar " + Path.GetFileName(path) +
                ". Verifique se a biblioteca tem a mesma arquitetura do aplicativo. " + new Win32Exception(error).Message);
        }
        return library;
    }

    private static void CheckVigem(uint code, string operation)
    {
        if (code == VigemSuccess) return;
        string reason;
        switch (code)
        {
            case 0xE0000001: reason = "ViGEmBus não encontrado"; break;
            case 0xE0000002: reason = "não há vaga para outro controle virtual"; break;
            case 0xE0000003: reason = "controle virtual inválido"; break;
            case 0xE0000004: reason = "falha ao remover o controle virtual"; break;
            case 0xE0000005: reason = "controle virtual já conectado"; break;
            case 0xE0000006: reason = "controle virtual não inicializado"; break;
            case 0xE0000007: reason = "controle virtual desconectado"; break;
            case 0xE0000008: reason = "versão do ViGEmBus incompatível"; break;
            case 0xE0000009: reason = "não foi possível acessar o ViGEmBus"; break;
            case 0xE0000012: reason = "cliente já conectado ao ViGEmBus"; break;
            case 0xE0000013: reason = "conexão ao ViGEmBus inválida"; break;
            case 0xE0000015: reason = "parâmetro inválido"; break;
            case 0xE0000016: reason = "operação não suportada pelo ViGEmBus"; break;
            case 0xE0000017: reason = "erro do Windows ao acessar o ViGEmBus"; break;
            case 0xE0000018: reason = "tempo de resposta esgotado"; break;
            case 0xE0000019: reason = "cliente ViGEm sendo encerrado"; break;
            default: reason = "erro do ViGEm"; break;
        }
        throw new InvalidOperationException(operation + ": " + reason + " (0x" + code.ToString("X8") + ").");
    }

    public static int SelfTest()
    {
        int passed = 0;
        Assert(Marshal.SizeOf(typeof(XusbReport)) == 12, ref passed);
        ControlSample sample = FromAxes(-32768, -32768, 32767, 32767);
        Assert(sample.Yaw == -1 && sample.Throttle == 1 && sample.Roll == 1 && sample.Pitch == -1, ref passed);
        sample = FromAxes(0, 32767, 0, -32768);
        Assert(sample.Throttle == 0 && sample.Pitch == 1, ref passed);
        sample = FromAxes(0, 0, 0, 0);
        Assert(sample.Throttle == 0.5 && sample.Yaw == 0 && sample.Roll == 0 && sample.Pitch == 0, ref passed);
        XusbReport report = ToReport(new ControlSample { Throttle = 0, Yaw = -1, Roll = 1, Pitch = -1,
            Buttons = 0xFFFF, LeftTrigger = 13, RightTrigger = 255 });
        Assert(report.LeftY == -32767 && report.LeftX == -32767 && report.RightX == 32767 && report.RightY == -32767, ref passed);
        Assert(report.Buttons == 0xFFFF && report.LeftTrigger == 13 && report.RightTrigger == 255, ref passed);
        Assert(ToReport(new ControlSample { Throttle = 1 }).LeftY == 32767, ref passed);
        Assert(ToReport(new ControlSample { Throttle = 0.5 }).LeftY == 0, ref passed);
        Assert(Stick(2) == 32767 && Stick(-2) == -32767, ref passed);
        Assert(ConvertTrigger(-32768) == 0 && ConvertTrigger(0) == 0 && ConvertTrigger(32767) == 255, ref passed);
        Assert(ConvertButtons(1u << 8) == 0x0080 && ConvertButtons(1u << 7) == 0x0040, ref passed);
        Assert(ToReport(new ControlSample { Buttons = ConvertButtons(1u << 8) }).Buttons == 0x0080, ref passed);
        Assert(ConvertButtons(0x7FFF) == 0xF7FF, ref passed);
        Assert(ConvertButtons((1u << 0) | (1u << 1) | (1u << 2) | (1u << 3)) == 0xF000, ref passed);
        Assert(!IsDualSense(0x045e, 0x028e, "DualSense") && !IsDualSense(0x054c, 0x09cc, "PS4 Controller"), ref passed);
        Assert(IsDualSense(0x054c, 0x0ce6, "Wireless Controller") && IsDualSense(0x054c, 0x0df2, "DualSense Edge"), ref passed);
        bool rejected = false;
        try { Stick(Double.NaN); } catch (ArgumentException) { rejected = true; }
        Assert(rejected, ref passed);
        return passed;
    }

    private static void Assert(bool condition, ref int passed)
    {
        if (!condition) throw new InvalidOperationException("Falha no teste interno do controle: " + (passed + 1));
        passed++;
    }

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    private static class Sdl
    {
        private const string Dll = "SDL3.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SDL_Init(uint flags);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SDL_QuitSubSystem(uint flags);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SDL_SetHint(string name, string value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SDL_PumpEvents();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SDL_UpdateGamepads();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SDL_GetJoysticks(out int count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SDL_free(IntPtr memory);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SDL_IsGamepad(uint id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SDL_OpenGamepad(uint id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SDL_CloseGamepad(IntPtr pad);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SDL_GamepadConnected(IntPtr pad);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SDL_GetGamepadName(IntPtr pad);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ushort SDL_GetGamepadVendor(IntPtr pad);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ushort SDL_GetGamepadProduct(IntPtr pad);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern short SDL_GetGamepadAxis(IntPtr pad, int axis);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SDL_GetGamepadButton(IntPtr pad, int button);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SDL_GetError();
    }

    private static class Vigem
    {
        private const string Dll = "ViGEmClient.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr vigem_alloc();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void vigem_free(IntPtr client);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint vigem_connect(IntPtr client);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void vigem_disconnect(IntPtr client);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr vigem_target_x360_alloc();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void vigem_target_free(IntPtr target);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint vigem_target_add(IntPtr client, IntPtr target);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint vigem_target_remove(IntPtr client, IntPtr target);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint vigem_target_x360_update(IntPtr client, IntPtr target, XusbReport report);
    }
}
