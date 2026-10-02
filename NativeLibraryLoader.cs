using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LSLStreamViewer
{
    public static class NativeLibraryLoader
    {
        private static string _extractedDllPath;
        private static IntPtr _loadedLibHandle = IntPtr.Zero;

        public static void Initialize()
        {
            ExtractNativeDll();
            NativeLibrary.SetDllImportResolver(typeof(LSL.StreamInfo).Assembly, DllImportResolver);
        }

        private static void ExtractNativeDll()
        {
            var asm = Assembly.GetExecutingAssembly();
            var version = asm.GetName().Version?.ToString() ?? "0";
            var targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LSLStreamViewer", "native", version);

            Directory.CreateDirectory(targetDir);
            _extractedDllPath = Path.Combine(targetDir, "lsl.dll");

            if (!File.Exists(_extractedDllPath) || new FileInfo(_extractedDllPath).Length == 0)
            {
                using var stream = asm.GetManifestResourceStream("lsl.dll");
                if (stream == null)
                    throw new FileNotFoundException(
                        "Embedded lsl.dll resource not found.");

                using var fs = new FileStream(_extractedDllPath, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.CopyTo(fs);
            }
        }

        private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == "lsl")
            {
                if (_loadedLibHandle == IntPtr.Zero)
                {
                    if (!string.IsNullOrEmpty(_extractedDllPath) && File.Exists(_extractedDllPath))
                    {
                        if (NativeLibrary.TryLoad(_extractedDllPath, out _loadedLibHandle))
                            return _loadedLibHandle;
                    }

                    var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                    var sideBySidePath = Path.Combine(exeDir, "lsl.dll");
                    if (File.Exists(sideBySidePath))
                    {
                        if (NativeLibrary.TryLoad(sideBySidePath, out _loadedLibHandle))
                            return _loadedLibHandle;
                    }

                    NativeLibrary.TryLoad("lsl", assembly, searchPath, out _loadedLibHandle);
                }
                return _loadedLibHandle;
            }
            return IntPtr.Zero;
        }
    }
}
