// Source: GRAPHIC_ASSEMBLY_SPECIFICATION.md
// Section: 01

namespace SvitloSk.Publisher.Execution;

public record GraphicArtifact(
    byte[] Payload,
    string Format
);
