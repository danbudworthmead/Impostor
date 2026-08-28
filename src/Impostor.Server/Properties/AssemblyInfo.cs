using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Impostor.Benchmarks")]
[assembly: InternalsVisibleTo("Impostor.Tests")]
[assembly: InternalsVisibleTo("Impostor.Tools.ServerReplay")]

// skeld.net.plugins lives in a sibling repo (net.skeld.plugins) but needs the same
// server-hosted-game test harness Impostor.Tests uses - a server-hosted lobby with real
// (test) clients joining/rejoining across rounds isn't reachable through IGame alone.
[assembly: InternalsVisibleTo("skeld.net.Tests")]
