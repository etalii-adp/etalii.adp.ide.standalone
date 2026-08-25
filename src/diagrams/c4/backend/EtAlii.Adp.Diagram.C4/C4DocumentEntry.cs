using System.Collections.Concurrent;
using Serilog;

namespace EtAlii.Adp.Diagram.C4;

internal sealed record C4DocumentEntry(C4Document Document, C4Workspace Workspace);
