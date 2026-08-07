using System.Text.Json.Serialization;

namespace SvitloSk.Publisher.Domain.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PackageState
{
    NEW,
    UPDATE,
    CANCELLATION
}
