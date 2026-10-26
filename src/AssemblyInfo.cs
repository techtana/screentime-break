using System.Reflection;
using System.Runtime.InteropServices;

// P/Invoke DLLs (user32, kernel32, xinput1_4) are only ever loaded from System32.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

[assembly: AssemblyTitle("ScreenBreak")]
[assembly: AssemblyDescription("Screen-time break reminder")]
[assembly: AssemblyProduct("ScreenBreak")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
