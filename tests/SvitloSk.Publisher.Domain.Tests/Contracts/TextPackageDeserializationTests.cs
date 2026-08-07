using System;
using System.Text.Json;
using SvitloSk.Publisher.Domain.Contracts;
using Xunit;

namespace SvitloSk.Publisher.Domain.Tests.Contracts;

public class TextPackageDeserializationTests
{
    [Fact]
    public void Should_Deserialize_Valid_Text_JSON_To_DTO()
    {
        string json = @"
        {
          ""metadata"": {
            ""package_id"": ""a4d8b67b-1175-4d7a-8fbb-5a4c9a87352f"",
            ""generation_timestamp"": ""2026-08-06T15:00:00Z"",
            ""source_identifier"": ""dso-text-v1"",
            ""package_state"": ""NEW""
          },
          ""territorial_scope"": ""hmel"",
          ""events"": [
            {
              ""settlement"": ""Starokostiantyniv"",
              ""streets"": [""Myru"", ""Zarichna""],
              ""intervals"": [
                {
                  ""start_time"": ""2026-08-06T18:00:00Z"",
                  ""end_time"": ""2026-08-06T20:00:00Z""
                }
              ]
            }
          ]
        }";

        var dto = JsonSerializer.Deserialize<TextInputPackageDto>(json);
        
        Assert.NotNull(dto);
        Assert.Equal(Guid.Parse("a4d8b67b-1175-4d7a-8fbb-5a4c9a87352f"), dto.Metadata.PackageId);
        Assert.Equal("dso-text-v1", dto.Metadata.SourceIdentifier);
        Assert.Equal(PackageState.NEW, dto.Metadata.PackageState);
        Assert.Equal("hmel", dto.TerritorialScope);
        Assert.Single(dto.Events);
        Assert.Equal("Starokostiantyniv", dto.Events[0].Settlement);
        Assert.Equal(2, dto.Events[0].Streets.Count);
        Assert.Single(dto.Events[0].Intervals);
        Assert.Equal(DateTimeOffset.Parse("2026-08-06T18:00:00Z"), dto.Events[0].Intervals[0].StartTime);
    }
    
    [Fact]
    public void Should_Deserialize_Deterministically()
    {
        var dto1 = new TextInputPackageDto
        {
            Metadata = new TextMetadataDto { PackageId = Guid.NewGuid(), GenerationTimestamp = DateTimeOffset.UtcNow, SourceIdentifier = "123", PackageState = PackageState.UPDATE },
            TerritorialScope = "hmel"
        };
        var json = JsonSerializer.Serialize(dto1);
        var dto2 = JsonSerializer.Deserialize<TextInputPackageDto>(json);
        
        Assert.Equal(dto1.Metadata.PackageId, dto2!.Metadata.PackageId);
        Assert.Equal(dto1.Metadata.PackageState, dto2.Metadata.PackageState);
        Assert.Equal(dto1.TerritorialScope, dto2.TerritorialScope);
    }
}
