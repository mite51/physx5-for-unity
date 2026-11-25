using System.Runtime.CompilerServices;

// UNDPWR.Tests is its own assembly under Unity and exercises internal rollback types
// such as SimAuthoritativeEventBuffer. The .NET test project compiles the package and
// its tests into one assembly, so it never needed this and is unaffected by it.
[assembly: InternalsVisibleTo("UNDPWR.Tests")]
