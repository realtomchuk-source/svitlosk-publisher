// Source: PACKAGE_ASSEMBLY_SPECIFICATION.md
// Section: 01

using SvitloSk.Publisher.Core;

namespace SvitloSk.Publisher.Execution;

public record PackageArtifact(
    string PackageType,
    Classification Classification,
    string GraphContent,
    string ForecastContent,
    string TechnicalContent
);
