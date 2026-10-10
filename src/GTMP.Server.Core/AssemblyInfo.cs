using System.Runtime.CompilerServices;

// The host drives the tick counter into the context; nothing else may.
[assembly: InternalsVisibleTo("GTMP.Server.Host")]
[assembly: InternalsVisibleTo("GTMP.Server.Tests")]