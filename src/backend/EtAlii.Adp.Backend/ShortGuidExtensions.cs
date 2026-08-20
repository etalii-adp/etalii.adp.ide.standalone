using Google.Protobuf;
using ShortGuidContract = EtAlii.Adp.Contracts.ShortGuid;

namespace EtAlii.Adp.Backend;

// C# doesn't allow a user-defined conversion operator to be declared in an
// extension block (CS9282) - only the type itself may declare one, and we
// don't own the generated ShortGuidContract type. These extension members are
// the closest equivalent: an explicit, discoverable conversion at the call site.
public static class ShortGuidExtensions
{
    extension(ShortGuidContract contract)
    {
        public ShortGuid ToShortGuid() => new Guid(contract.Value.Span);
    }

    extension(ShortGuid shortGuid)
    {
        public ShortGuidContract ToContract() => new ShortGuidContract { Value = ByteString.CopyFrom(shortGuid.Guid.ToByteArray()) };
    }
}
