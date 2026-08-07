using System;
using System.Text.Json;
using SvitloSk.Publisher.Domain.Contracts;
using Xunit;

namespace SvitloSk.Publisher.Domain.Tests.Contracts;

public class InputPackageValidationTests
{
    [Fact]
    public void Validator_Should_Reject_Missing_Required_Properties()
    {
        string json = @"
        {
          ""metadata"": {
            ""generation_timestamp"": ""2026-08-06T15:00:00Z"",
            ""package_state"": ""NEW""
          }
        }";
        // Missing PackageId, SourceIdentifier, TerritorialScope, Events
        // JsonSerializer will throw JsonException because of [JsonRequired] attributes.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TextInputPackageDto>(json));
    }

    [Fact]
    public void Validator_Should_Reject_Invalid_Enum_Values()
    {
        string json = @"
        {
          ""metadata"": {
            ""package_id"": ""a4d8b67b-1175-4d7a-8fbb-5a4c9a87352f"",
            ""generation_timestamp"": ""2026-08-06T15:00:00Z"",
            ""source_identifier"": ""dso-text-v1"",
            ""package_state"": ""UNKNOWN_STATE""
          },
          ""territorial_scope"": ""hmel"",
          ""events"": []
        }";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TextInputPackageDto>(json));
    }

    [Fact]
    public void Validator_Should_Reject_Malformed_HH_mm_Intervals()
    {
        var dto = new GraphicInputPackageDto
        {
            Metadata = new GraphicMetadataDto
            {
                PackageId = Guid.NewGuid(),
                GenerationTimestamp = DateTimeOffset.UtcNow,
                TargetDate = new DateOnly(2026, 8, 7),
                SourceIdentifier = "test"
            },
            TerritorialScope = "hmel"
        };
        dto.Queues.Add(new GraphicQueueDto
        {
            QueueId = "q1",
            Subqueues = new System.Collections.Generic.List<GraphicSubqueueDto>
            {
                new GraphicSubqueueDto
                {
                    SubqueueId = "q1.1",
                    Intervals = new System.Collections.Generic.List<GraphicIntervalDto>
                    {
                        new GraphicIntervalDto { StartTime = "8:00", EndTime = "12:0", Status = "R" } // Missing leading zeros
                    }
                }
            }
        });

        Assert.Throws<ArgumentException>(() => InputPackageValidator.Validate(dto));
    }

    [Fact]
    public void Validator_Should_Accept_Valid_PackageState()
    {
        var dto = new TextInputPackageDto
        {
            Metadata = new TextMetadataDto
            {
                PackageId = Guid.NewGuid(),
                GenerationTimestamp = DateTimeOffset.UtcNow,
                SourceIdentifier = "test",
                PackageState = PackageState.UPDATE // Valid enum
            },
            TerritorialScope = "hmel"
        };
        dto.Events.Add(new TextEventDto
        {
            Settlement = "test",
            Streets = new System.Collections.Generic.List<string> { "s1" },
            Intervals = new System.Collections.Generic.List<TextIntervalDto>
            {
                new TextIntervalDto { StartTime = DateTimeOffset.UtcNow, EndTime = DateTimeOffset.UtcNow.AddHours(2) }
            }
        });

        // Should not throw
        InputPackageValidator.Validate(dto);
    }
}
