using Google.Protobuf;
using ShortGuidContract = EtAlii.Adp.Common.Wire.ShortGuid;

// ReSharper disable once CheckNamespace
namespace EtAlii.Adp.Common.Wire;

// C# doesn't allow a user-defined conversion operator to be declared in an
// extension block (CS9282) - only the type itself may declare one, and we
// don't own the generated ShortGuidContract type. These extension members are
// the closest equivalent: an explicit, discoverable conversion at the call site.
public sealed partial class ShortGuid
{
    public static implicit operator ShortGuidContract(Adp.ShortGuid shortGuid)
    {
        return new ShortGuidContract { Value = ByteString.CopyFrom(shortGuid.Guid.ToByteArray()) };
    }
    public static implicit operator Adp.ShortGuid(ShortGuidContract contract)
    {
        return new Adp.ShortGuid(new Guid(contract.Value.Span));
    }
}