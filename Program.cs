using System.Management;
using System.Runtime.InteropServices;


const int maxCheckDelay = 1500;
const int minCheckDelay = 100;
const string offsetArg = "-o";

var lastBrightness = -1;
var delayTime = 1;
var offset = -10;

foreach (var arg in args)
{
    if (arg.Contains(offsetArg))
    {
        var amount = new string(arg.Skip(offsetArg.Length).ToArray());
        Console.WriteLine($"Offset: {amount}");
        if (!int.TryParse(amount, out offset))
        {
            Console.WriteLine("Expected offset ex \"-o100\"");
            return;
        }
    }
}

var scope = new ManagementScope("root\\WMI");
var query = new SelectQuery("SELECT * FROM WmiMonitorBrightness");
using var searcher = new ManagementObjectSearcher(scope, query);
while (true)
{
    Thread.Sleep(delayTime);
    using var objectCollection = searcher.Get();
    foreach (var o in objectCollection)
    {
        var mObj = (ManagementObject)o;
        foreach (var item in mObj.Properties)
        {
            if (item.Name == "CurrentBrightness")
            {
                var v = double.Parse(item.Value.ToString());
                // First time
                if (lastBrightness < 0)
                {
                    lastBrightness = (int)v;
                    continue;
                }

                // If brightness is not changed, add more delay time to reduce CPU usage
                if (lastBrightness == (int)v)
                {
                    if (delayTime < maxCheckDelay) delayTime += 10;
                    continue;
                }

                var newBright = offset + v;
                newBright = Math.Clamp(newBright, 0, 100);
                // Console.WriteLine($"{item.Name} {item.Value} -> {newBright}");
                BrightnessControllerDllInterop.SetBrightness(newBright);
                delayTime = minCheckDelay;
                lastBrightness = (int)v;
                break;
            }
        }
    }
}


public static class BrightnessControllerDllInterop
{
    private delegate bool InitAtkacpiDeviceFunc();

    private delegate bool UninitAtkacpiDeficeFunc();

    private delegate bool TwoWmiMethodIntFunction(ulong szMethod, int iArg0, int iArg1, out ulong pulReturnValue);

    private static IntPtr dllWmi;

    private static void LoadWmi()
    {
        var (dllPath, err) = FindDllPath();
        if (dllPath == null)
        {
            Console.WriteLine(err);
            return;
        }

        dllWmi = LoadLibrary(dllPath);

        if (dllWmi == IntPtr.Zero)
        {
            Console.WriteLine($"Failed to load {dllPath}");
            return;
        }

        _initFunc = (InitAtkacpiDeviceFunc)Marshal.GetDelegateForFunctionPointer(GetProcAddress(dllWmi, "InitializeATKACPIDevice"), typeof(InitAtkacpiDeviceFunc));
        _unInitFunc = (UninitAtkacpiDeficeFunc)Marshal.GetDelegateForFunctionPointer(GetProcAddress(dllWmi, "UninitializeATKACPIDevice"), typeof(UninitAtkacpiDeficeFunc));
        _setValueFunc = (TwoWmiMethodIntFunction)Marshal.GetDelegateForFunctionPointer(GetProcAddress(dllWmi, "Two_WMIMethod_INT"), typeof(TwoWmiMethodIntFunction));
    }

    private static InitAtkacpiDeviceFunc? _initFunc;
    private static UninitAtkacpiDeficeFunc? _unInitFunc;
    private static TwoWmiMethodIntFunction? _setValueFunc;

    private static (string? path, string? err) FindDllPath()
    {
        const string baseSearchDir = @"C:\Program Files\WindowsApps\";
        const string appName = "ScreenPadMaster";
        const string subDir = "AsusScreenXpert";
        const string dllName = "AsusXCommonLib.dll";

        var dirs = Directory.GetDirectories(baseSearchDir, $"*{appName}*", SearchOption.TopDirectoryOnly);
        var dir = dirs.FirstOrDefault(d => Directory.Exists(Path.Combine(d, subDir)));
        if (dir == null) return (null, $"Could not find {appName} in {baseSearchDir}");
        var dllPath = Path.Combine(dir, subDir, dllName);
        if (!File.Exists(dllPath)) return (null, $"Could not find {dllName} in {dir}");
        return (dllPath, null);
    }


    public static void SetBrightness(double brightnessPercent)
    {
        if (dllWmi == nint.Zero) LoadWmi();
        if (dllWmi == nint.Zero || _initFunc == null || _unInitFunc == null || _setValueFunc == null)
        {
            Console.WriteLine("Failed to load ATKACPI device");
            return;
        }

        if (!_initFunc())
        {
            Console.WriteLine("Failed to initialize ATKACPI device");
            return;
        }


        var newBrightness = (int)(brightnessPercent * 2.54 + 1.0); // 0-100 -> 1-255
        _setValueFunc(1398162756uL, 327730, newBrightness, out _);
        _unInitFunc();
    }

    [DllImport("kernel32.dll")]
    private static extern nint LoadLibrary(string dllToLoad);

    [DllImport("kernel32.dll")]
    private static extern nint GetProcAddress(IntPtr hModule, string procedureName);
}