using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Problems;

/// <summary>The cache's own shape - flat, so the abstract location needs no JSON polymorphism.</summary>
internal sealed record ProblemCacheFile(int Version, string RootPath, IReadOnlyList<CachedProblem> Problems);
