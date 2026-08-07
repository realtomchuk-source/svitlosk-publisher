// Source: GRAPHIC_ASSEMBLY_SPECIFICATION.md
// Section: 01

using SvitloSk.Publisher.Core;

namespace SvitloSk.Publisher.Execution;

public interface IGraphicAssembly
{
    GraphicArtifact Assemble(GraphicInputPackage inputPackage, EditorialDecision decision);
}
