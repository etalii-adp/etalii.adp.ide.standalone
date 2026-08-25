using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Problems;

internal sealed class CachedProjectProblems(string rootPath)
{
    public object Gate { get; } = new();
    public string RootPath { get; } = rootPath;
    public List<StoredProblem> Problems { get; } = [];
    public ProjectProblemSetState State { get; set; } = ProjectProblemSetState.NeverValidated;
    public Timer? WriteTimer { get; set; }
}
