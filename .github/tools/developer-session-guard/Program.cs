using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

// Refuses a published build that carries the developer sign-in bypass
// (developer-sign-in-bypass Requirement 2.3).
//
// WHY THIS IS A PROGRAM AND NOT A TEST
//
// A unit test cannot answer this question. The test assembly compiles Debug, where the bypass
// exists by design, so a test asserting its absence would either assert the wrong thing or
// assert a configuration default - and would pass whether or not a release were safe. This is
// the only check that reads a different input: the Release-compiled artifact a user actually
// runs.
//
// WHY IT DOES NOT SEARCH FOR THE TEXT "DeveloperSession"
//
// That string is in every build, correct ones included. A service's methods cannot be split
// across proto files, so the RPC is declared in authentication.proto unconditionally: the
// serialized file descriptor carries the method name, and the generated service base carries a
// virtual method of that name which answers Unimplemented. Neither is the bypass. A grep would
// fail on a correct release, and the natural way to "fix" a guard that cries wolf is to loosen
// it until it stops - which is how a guard dies.
//
// WHAT IT ACTUALLY CHECKS
//
// The two things that ARE the bypass, both compiled only into a Debug build:
//
//   1. An override of DeveloperSession declared on the concrete AuthenticationService. Without
//      it nothing can mint a token, whatever else survives.
//   2. The literal "/DeveloperSession" in the metadata's user-string heap, which is
//      SessionInterceptor's exemption. Without it the call is refused for want of a session
//      before reaching any handler.
//
// Either alone is harmless; the guard reports both so that a partial survival is still loud.
//
// WHICH ASSEMBLY
//
// Both live in EtAlii.Adp.Authentication.dll, beside the service in the publish folder. They
// moved there from EtAlii.Adp.Backend.dll; pointed at the old assembly, the guard finds no
// AuthenticationService and refuses, rather than passing on an assembly that holds nothing.

const string ServiceTypeName = "AuthenticationService";
const string ServiceNamespace = "EtAlii.Adp.Authentication";
const string HandlerName = "DeveloperSession";
const string ExemptionLiteral = "/DeveloperSession";

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: DeveloperSessionGuard <path-to-assembly>");
    return 2;
}

var path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"{path} does not exist. The guard has not inspected anything, which is not the same as finding it clean.");
    return 2;
}

var findings = new List<string>();

var bytes = File.ReadAllBytes(path);
using (var stream = new MemoryStream(bytes, writable: false))
using (var peReader = new PEReader(stream))
{
    if (!peReader.HasMetadata)
    {
        Console.Error.WriteLine($"{path} carries no .NET metadata; the guard cannot inspect it.");
        return 2;
    }

    var metadata = peReader.GetMetadataReader();

    var serviceType = metadata.TypeDefinitions
        .Select(metadata.GetTypeDefinition)
        .Where(type => metadata.GetString(type.Name) == ServiceTypeName)
        .Where(type => metadata.GetString(type.Namespace) == ServiceNamespace)
        .ToArray();

    if (serviceType.Length != 1)
    {
        // The guard must not pass because it looked in the wrong place. If the type is renamed
        // or moved, this fails and says so, rather than reporting a clean bill of health for a
        // type it never found.
        Console.Error.WriteLine(
            $"Expected exactly one {ServiceNamespace}.{ServiceTypeName} in {Path.GetFileName(path)}, found {serviceType.Length}. " +
            "This guard has stopped reaching its subject rather than found it sound - repoint it before trusting a green run.");
        return 2;
    }

    var declared = serviceType[0].GetMethods()
        .Select(metadata.GetMethodDefinition)
        .Select(method => metadata.GetString(method.Name))
        .ToArray();

    if (declared.Contains(HandlerName))
    {
        findings.Add(
            $"{ServiceNamespace}.{ServiceTypeName} declares {HandlerName}. This is the handler that mints a session " +
            "without a credential; it is supposed to exist only inside #if DEBUG.");
    }
}

// The exemption, as a user string. Metadata stores user strings UTF-16, so this cannot collide
// with the UTF-8 method name inside the serialized proto descriptor - which is exactly the
// collision that makes a plain text search useless here.
if (IndexOf(bytes, Encoding.Unicode.GetBytes(ExemptionLiteral)) >= 0)
{
    findings.Add(
        $"The literal \"{ExemptionLiteral}\" is present. This is SessionInterceptor's exemption, which lets the " +
        "call through without a session token; it is supposed to exist only inside #if DEBUG.");
}

if (findings.Count > 0)
{
    Console.Error.WriteLine($"{Path.GetFileName(path)} carries the developer sign-in bypass. This build must not be released.");
    foreach (var finding in findings)
    {
        Console.Error.WriteLine($"  - {finding}");
    }

    Console.Error.WriteLine("Publish with -c Release, which compiles both away.");
    return 1;
}

Console.WriteLine($"{Path.GetFileName(path)} carries no developer sign-in bypass: no {HandlerName} handler and no \"{ExemptionLiteral}\" exemption.");
return 0;

static int IndexOf(byte[] haystack, byte[] needle)
{
    for (var start = 0; start <= haystack.Length - needle.Length; start++)
    {
        var matched = true;
        for (var offset = 0; offset < needle.Length; offset++)
        {
            if (haystack[start + offset] != needle[offset])
            {
                matched = false;
                break;
            }
        }

        if (matched)
        {
            return start;
        }
    }

    return -1;
}
