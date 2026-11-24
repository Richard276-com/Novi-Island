NOVA Island - Core Foundation (initial scaffold)

Files:
- NovaCore.cs: shared NOVA states and state-change events.
- NovaMascot.cs: lightweight WPF vector placeholder with state expressions.
- NovaExperience.cs: coordinator API for activity, thinking, permission, success, error and notifications.

Integration:
1. Copy the three .cs files into the repository root.
2. In build.bat, append NovaCore.cs NovaMascot.cs NovaExperience.cs to the csc.exe source-file list.
3. Build using the existing build.bat on Windows.

Compatibility:
- C# 5 syntax
- .NET Framework 4.8
- WPF
- Namespace DynIsland

This is a first foundation layer, not yet wired into IslandWindow. The mascot is temporary vector artwork; the final original NOVA character and animation assets will be designed separately.
